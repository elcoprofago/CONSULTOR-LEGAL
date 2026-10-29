using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace ConsultorLegal
{
    // Cliente MCP mínimo sobre "Streamable HTTP": cada pedido es un POST con un
    // mensaje JSON-RPC y la respuesta llega como JSON o como SSE
    // ("event: message" / "data: {...}"). El servidor de bravo47 no pide sesión
    // ni autenticación, pero si devuelve Mcp-Session-Id se reenvía igual.
    public class McpClient
    {
        public const string EndpointPorDefecto = "https://mcp.bravo47.com/v5/mcp";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
        private int _id;
        private string? _sesion;

        public string Endpoint { get; }
        public JsonObject? InfoServidor { get; private set; }
        public string? Instrucciones { get; private set; }

        public McpClient(string endpoint) => Endpoint = endpoint.Trim();

        public static string ComandoClaudeCode(string endpoint) =>
            $"claude mcp add --transport http bravo47 {endpoint.Trim()}";

        public async Task InicializarAsync(CancellationToken ct = default)
        {
            var res = await PedirAsync("initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-03-26",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "CONSULTOR-LEGAL", ["version"] = "1.0" },
            }, ct);
            InfoServidor = res?["serverInfo"]?.DeepClone() as JsonObject;
            Instrucciones = res?["instructions"]?.GetValue<string>();
            await NotificarAsync("notifications/initialized", ct);
        }

        public async Task<JsonArray> ListarHerramientasAsync(CancellationToken ct = default)
        {
            var res = await PedirAsync("tools/list", new JsonObject(), ct);
            return res?["tools"]?.DeepClone() as JsonArray ?? new JsonArray();
        }

        // Devuelve el texto de los bloques "text" (el servidor manda ahí el
        // JSON del resultado) y si el servidor lo marcó como error.
        public async Task<(string texto, bool esError)> LlamarAsync(string herramienta, JsonObject argumentos, CancellationToken ct = default)
        {
            var res = await PedirAsync("tools/call",
                new JsonObject { ["name"] = herramienta, ["arguments"] = argumentos.DeepClone() }, ct);
            bool esError = res?["isError"]?.GetValue<bool>() ?? false;
            var sb = new StringBuilder();
            if (res?["content"] is JsonArray content)
                foreach (var c in content)
                    if (c?["type"]?.GetValue<string>() == "text")
                        sb.Append(c["text"]?.GetValue<string>());
            if (sb.Length == 0 && res?["structuredContent"] is JsonNode sc)
                sb.Append(sc.ToJsonString());
            return (sb.ToString(), esError);
        }

        private async Task NotificarAsync(string metodo, CancellationToken ct)
        {
            var msg = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = metodo };
            try { using var _ = await EnviarAsync(msg, ct); } catch { /* una notificación no tiene respuesta que importe */ }
        }

        private async Task<JsonNode?> PedirAsync(string metodo, JsonObject parametros, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref _id);
            var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = metodo, ["params"] = parametros };
            using var resp = await EnviarAsync(msg, ct);
            // Bytes decodificados como UTF-8 a mano: si el Content-Type no trae
            // charset, ReadAsStringAsync puede equivocarse con los acentos.
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            string cuerpo = Encoding.UTF8.GetString(bytes);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Recortar(cuerpo)}");

            JsonNode? respuesta = null;
            string tipo = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (tipo.Contains("event-stream"))
            {
                foreach (string datos in EventosSse(cuerpo))
                {
                    var n = JsonNode.Parse(datos);
                    if (n?["id"]?.ToString() == id.ToString()) { respuesta = n; break; }
                }
            }
            else if (!string.IsNullOrWhiteSpace(cuerpo))
            {
                respuesta = JsonNode.Parse(cuerpo);
            }

            if (respuesta == null) throw new InvalidOperationException("El servidor no devolvió respuesta.");
            if (respuesta["error"] is JsonNode err)
                throw new InvalidOperationException($"Error {err["code"]}: {err["message"]}");
            return respuesta["result"];
        }

        private async Task<HttpResponseMessage> EnviarAsync(JsonObject msg, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(msg.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Accept.ParseAdd("application/json");
            req.Headers.Accept.ParseAdd("text/event-stream");
            if (_sesion != null) req.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sesion);
            var resp = await Http.SendAsync(req, ct);
            if (resp.Headers.TryGetValues("Mcp-Session-Id", out var v)) _sesion = v.FirstOrDefault();
            return resp;
        }

        // Une las líneas "data:" de cada evento (separados por una línea vacía).
        private static IEnumerable<string> EventosSse(string cuerpo)
        {
            var datos = new StringBuilder();
            using var lector = new StringReader(cuerpo);
            string? linea;
            while ((linea = lector.ReadLine()) != null)
            {
                if (linea.Length == 0)
                {
                    if (datos.Length > 0) { yield return datos.ToString(); datos.Clear(); }
                }
                else if (linea.StartsWith("data:"))
                {
                    if (datos.Length > 0) datos.Append('\n');
                    datos.Append(linea[5..].TrimStart());
                }
            }
            if (datos.Length > 0) yield return datos.ToString();
        }

        private static string Recortar(string s) => s.Length > 300 ? s[..300] + "…" : s;
    }
}
