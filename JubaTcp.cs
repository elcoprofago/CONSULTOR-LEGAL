using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ConsultorLegal
{
    // Tribunal de Casación Penal de la Provincia de Buenos Aires, leído
    // directamente de JUBA (juba.scba.gov.ar), porque bravo47 solo trae los
    // fallos de la SCBA. Son herramientas locales con la misma forma que las
    // del servidor: se declaran con un inputSchema y responden JSON, así el
    // formulario, las tarjetas, los fragmentos y las páginas funcionan igual.
    //
    // JUBA es un formulario ASP.NET: hay que leer sus campos ocultos y
    // devolverlos en cada envío. La búsqueda usa "Búsqueda Integral" con el
    // atajo "Tribunal Emisor": TC* son todas las salas, TC0001 la Sala I, etc.
    // (probado contra JUBA el 29/10/2026).
    public static class JubaTcp
    {
        public const string Grupo = "Tribunal de Casación Penal PBA (TCPBA)";
        public const string Buscar = "buscar_fallos_tcpba";
        public const string Leer = "leer_fallo_tcpba";
        private const string Base = "https://juba.scba.gov.ar/";
        private const string P = "ctl00$cphMainContent$";
        private const int PorPagina = 20;            // fijo en JUBA
        private const int LargoFragmento = 12000;

        private static readonly Dictionary<string, string> Salas = new()
        {
            ["Todas"] = "TC*",
            ["Sala I"] = "TC0001", ["Sala II"] = "TC0002", ["Sala III"] = "TC0003",
            ["Sala IV"] = "TC0004", ["Sala V"] = "TC0005", ["Sala VI"] = "TC0006",
            ["Presidencia"] = "TC0000", ["Plenario"] = "TCPLEN",
        };

        public static bool EsPropia(string herramienta) => herramienta is Buscar or Leer;

        public static IEnumerable<Herramienta> Herramientas()
        {
            var buscar = ConsultorLegal.Herramientas.Desde(new JsonObject
            {
                ["name"] = Buscar,
                ["description"] = "Busca sumarios del Tribunal de Casación Penal bonaerense en JUBA (juba.scba.gov.ar). " +
                                  "Se busca en el texto del sumario y en las voces, o por número de sumario. JUBA admite los operadores \"y\", \"o\" y \"no\", y el comodín *.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string" },
                        ["sumario"] = new JsonObject { ["type"] = "string", ["description"] = "Número de sumario de JUBA, por ejemplo B3258273." },
                        ["sala"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Salas.Keys.Select(k => (JsonNode)k).ToArray()), ["default"] = "Todas" },
                        ["offset"] = new JsonObject { ["type"] = "integer", ["default"] = 0 },
                    },
                },
            });
            var leer = ConsultorLegal.Herramientas.Desde(new JsonObject
            {
                ["name"] = Leer,
                ["description"] = "Texto completo de un fallo del Tribunal de Casación Penal, tal como lo publica JUBA, en fragmentos.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("id_juba"),
                    ["properties"] = new JsonObject
                    {
                        ["id_juba"] = new JsonObject { ["type"] = "integer", ["description"] = "Número del texto completo en JUBA (idFallo)." },
                        ["fragmento"] = new JsonObject { ["type"] = "integer", ["default"] = 1 },
                    },
                },
            });
            buscar.Grupo = leer.Grupo = Grupo;
            buscar.Titulo = "Buscar fallos (JUBA)";
            leer.Titulo = "Leer texto del fallo";
            buscar.Campos.First(c => c.Nombre == "sala").Etiqueta = "Sala";
            buscar.Campos.First(c => c.Nombre == "sumario").Etiqueta = "Nro. de sumario";
            return new[] { buscar, leer };
        }

        public static async Task<(string texto, bool esError)> LlamarAsync(string herramienta, JsonObject args, CancellationToken ct = default)
        {
            try
            {
                return herramienta == Buscar ? (await BuscarAsync(args, ct), false) : (await LeerAsync(args, ct), false);
            }
            catch (OperationCanceledException) { throw; }
            catch (ArgumentException ex) { return (ex.Message, true); }
            catch (HttpRequestException ex) { return ($"JUBA no respondió: {ex.Message}", true); }
        }

        private static HttpClient NuevoCliente()
        {
            var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.All })
            { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CONSULTOR-LEGAL)");
            return http;
        }

        // ============================ Búsqueda ============================

        private static async Task<string> BuscarAsync(JsonObject args, CancellationToken ct)
        {
            string query = args["query"]?.ToString().Trim() ?? "";
            string nro = Regex.Replace(args["sumario"]?.ToString() ?? "", @"[\s""]", "").ToUpperInvariant();
            if (query.Length == 0 && nro.Length == 0) throw new ArgumentException("Falta el texto a buscar o el número de sumario.");
            string sala = args["sala"]?.ToString() ?? "Todas";
            if (!Salas.TryGetValue(sala, out var codigo)) throw new ArgumentException($"Sala desconocida: {sala}");
            long offset = Math.Max(0, args["offset"] is JsonNode o && long.TryParse(o.ToString(), out long v) ? v : 0);
            int pagina = (int)(offset / PorPagina) + 1;
            string expresion = codigo;
            if (nro.Length > 0) expresion += $" y Nro.Sumario:\"{nro}\"";
            if (query.Length > 0) expresion += $" y {query}";

            using var http = NuevoCliente();
            string formulario = await http.GetStringAsync(Base + "Busquedas.aspx", ct);
            var datos = CamposOcultos(formulario);
            datos[P + "TipoBusqueda"] = "rdbBusquedaIntegral";
            datos[P + "txtExpresionBusquedaIntegral"] = expresion;
            datos[P + "chkTextoSumario"] = "on";
            datos[P + "chkVoces"] = "on";
            datos[P + "chkTribunalEmisor"] = "on";
            datos[P + "btnRealizarBusqueda"] = "Buscar";
            string html = await EnviarAsync(http, datos, ct);

            if (pagina > 1 && Total(html) > (pagina - 1) * PorPagina)
            {
                var pag = CamposOcultos(html);
                pag["__EVENTTARGET"] = P + "lnkIrPagina";
                pag[P + "ddlPaginaResultados"] = (pagina - 1).ToString();   // el selector cuenta desde 0
                pag[P + "TipoBusqueda"] = "rdbBusquedaIntegral";
                pag[P + "txtExpresionBusquedaIntegral"] = expresion;
                html = await EnviarAsync(http, pag, ct);
            }

            int total = Total(html);
            var sumarios = new JsonArray();
            if (total > (pagina - 1) * PorPagina)
                foreach (var s in Sumarios(html)) sumarios.Add(s);

            return new JsonObject
            {
                ["fuente"] = "JUBA · Tribunal de Casación Penal PBA",
                ["expresion"] = expresion,
                ["total"] = total,
                ["hay_mas"] = offset + PorPagina < total,
                ["sumarios"] = sumarios,
            }.ToJsonString();
        }

        private static async Task<string> EnviarAsync(HttpClient http, Dictionary<string, string> datos, CancellationToken ct)
        {
            using var r = await http.PostAsync(Base + "Busquedas.aspx", new FormUrlEncodedContent(datos), ct);
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadAsStringAsync(ct);
        }

        private static Dictionary<string, string> CamposOcultos(string html)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(html, @"<input[^>]*type=""hidden""[^>]*>"))
            {
                var n = Regex.Match(m.Value, @"name=""([^""]+)""");
                var v = Regex.Match(m.Value, @"value=""([^""]*)""");
                if (n.Success) d[n.Groups[1].Value] = v.Success ? WebUtility.HtmlDecode(v.Groups[1].Value) : "";
            }
            return d;
        }

        private static int Total(string html) =>
            Regex.Match(html, @"Resultado: \d+ de (\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

        // Cada resultado de JUBA es un sumario: materia, número, voces, texto,
        // normas citadas y el fallo (o los fallos) de donde sale.
        private static IEnumerable<JsonObject> Sumarios(string html)
        {
            var partes = Regex.Split(html, @"<span id=""cphMainContent_RepeaterDatosResultados_lblCantidad_\d+"">");
            foreach (var parte in partes.Skip(1))
            {
                var celdas = Regex.Matches(parte, @"<td class=""tdFilaRepeater(?:Der)?""[^>]*>(.*?)</td>", RegexOptions.Singleline)
                                  .Select(m => Limpio(m.Groups[1].Value)).ToList();
                // celdas: materia, número, voces, texto, normas, ...
                var fallos = Regex.Matches(parte, @"<br>\s*((?:TC\w*) [A-Z]{2} .*?)(?=<br><br>|</p>)", RegexOptions.Singleline)
                                  .Select(m => m.Groups[1].Value).ToList();
                var s = new JsonObject
                {
                    ["materia"] = celdas.ElementAtOrDefault(0),
                    ["sumario"] = celdas.ElementAtOrDefault(1),
                    ["voces"] = celdas.ElementAtOrDefault(2)?.TrimEnd(' ', '|'),
                };
                if (fallos.Count > 0)
                {
                    var lineas = Regex.Split(fallos[0], @"<br\s*/?>").Select(Limpio).Where(l => l.Length > 0).ToList();
                    s["caratula"] = Dato(lineas, "Carátula:") ?? "(sin carátula)";
                    s["fallo"] = lineas.ElementAtOrDefault(0);
                    s["magistrados_votantes"] = Dato(lineas, "Magistrados Votantes:");
                    s["tribunal_origen"] = Dato(lineas, "Tribunal Origen:");
                    s["observaciones"] = Dato(lineas, "Observaciones:");
                    if (Regex.Match(fallos[0], @"VerTextoCompleto\.aspx\?idFallo=(\d+)") is { Success: true } id)
                        s["id_texto_tcpba"] = long.Parse(id.Groups[1].Value);
                    if (fallos.Count > 1)
                        s["otros_fallos"] = string.Join("\n", fallos.Skip(1).Select(f => Limpio(Regex.Split(f, @"<br\s*/?>")[0])));
                }
                s["texto"] = celdas.ElementAtOrDefault(3);
                s["normas"] = celdas.ElementAtOrDefault(4)?.TrimEnd(' ', '|');
                foreach (var k in s.Where(kv => kv.Value == null || kv.Value.ToString().Length == 0).Select(kv => kv.Key).ToList())
                    s.Remove(k);
                yield return s;
            }
        }

        private static string? Dato(List<string> lineas, string rotulo) =>
            lineas.FirstOrDefault(l => l.StartsWith(rotulo))?[rotulo.Length..].Trim() is { Length: > 0 } v ? v : null;

        private static string Limpio(string html)
        {
            string t = Regex.Replace(html, @"<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(t), @"\s+", " ").Trim();
        }

        // ========================= Texto completo =========================

        private static readonly Dictionary<long, (JsonObject datos, List<string> fragmentos)> Textos = new();

        private static async Task<string> LeerAsync(JsonObject args, CancellationToken ct)
        {
            if (args["id_juba"] is not JsonNode n || !long.TryParse(n.ToString(), out long id) || id <= 0)
                throw new ArgumentException("Falta el Id JUBA del fallo (el número que aparece como \"Id texto tcpba\" en los resultados de la búsqueda).");
            long fragmento = args["fragmento"] is JsonNode f && long.TryParse(f.ToString(), out long fv) ? fv : 1;

            if (!Textos.TryGetValue(id, out var leido))
            {
                using var http = NuevoCliente();
                string html = await http.GetStringAsync($"{Base}VerTextoCompleto.aspx?idFallo={id}", ct);
                leido = Desarmar(html);
                if (leido.fragmentos.Count == 0) throw new ArgumentException($"JUBA no tiene texto completo para el id {id}.");
                Textos[id] = leido;
            }
            int total = leido.fragmentos.Count;
            if (fragmento < 1 || fragmento > total) throw new ArgumentException($"El fallo tiene {total} fragmentos; pediste el {fragmento}.");

            var r = (JsonObject)leido.datos.DeepClone();
            r["id_texto_tcpba"] = id;
            r["fragmento"] = fragmento;
            r["total_fragmentos"] = total;
            r["texto"] = leido.fragmentos[(int)fragmento - 1];
            return r.ToJsonString();
        }

        private static readonly (string id, string clave)[] DatosFallo =
        {
            ("lblCaratulaPublica", "caratula"), ("lblMateria", "materia"), ("lblTipoDeFallo", "tipo_fallo"),
            ("lblTribunalEmisor", "tribunal"), ("lblCausa", "causa"), ("lblFecha", "fecha"),
            ("lblNroregistroInterno", "registro"), ("lblmagistradosVotantes", "magistrados_votantes"),
            ("lblTribunalOrigen", "tribunal_origen"), ("lblObservacionFallo", "observaciones"),
        };

        private static (JsonObject, List<string>) Desarmar(string html)
        {
            var datos = new JsonObject();
            foreach (var (id, clave) in DatosFallo)
            {
                // Los tribunales siguen después del span: "SALA I - LA PLATA ( TC0001 LP )".
                var m = Regex.Match(html, $@"id=""{id}"">(.*?)</span>(.*?)</p>", RegexOptions.Singleline);
                if (!m.Success) continue;
                string v = Limpio(m.Groups[1].Value + (clave.StartsWith("tribunal") ? " " + m.Groups[2].Value : ""));
                if (v.Length > 0) datos[clave] = v;
            }

            int ini = html.IndexOf("TEXTO COMPLETO</p>", StringComparison.Ordinal);
            if (ini < 0) return (datos, new List<string>());
            string cuerpo = html[(ini + "TEXTO COMPLETO</p>".Length)..];
            int fin = cuerpo.IndexOf("<footer", StringComparison.OrdinalIgnoreCase);
            if (fin > 0) cuerpo = cuerpo[..fin];

            // El texto es un documento de Word pegado como HTML: se descartan
            // sus comentarios condicionales, estilos y metadatos.
            cuerpo = Regex.Replace(cuerpo, @"<!--.*?-->|<head.*?</head>|<style.*?</style>|<script.*?</script>|<xml.*?</xml>", "",
                                   RegexOptions.Singleline | RegexOptions.IgnoreCase);
            // Word corta los renglones dentro de cada párrafo: esos saltos son espacios.
            cuerpo = Regex.Replace(cuerpo, @"\s*\r?\n\s*", " ");
            cuerpo = Regex.Replace(cuerpo, @"<br\s*/?>|</p>|</div>|</tr>|</h\d>", "\n", RegexOptions.IgnoreCase);
            cuerpo = WebUtility.HtmlDecode(Regex.Replace(cuerpo, @"<[^>]+>", ""));
            var parrafos = cuerpo.Replace("\r", "").Split('\n')
                                 .Select(l => Regex.Replace(l, @"[ \t ]+", " ").Trim())
                                 .Where(l => l.Length > 0).ToList();

            var fragmentos = new List<string>();
            var sb = new StringBuilder();
            foreach (var p in parrafos)
            {
                if (sb.Length > 0 && sb.Length + p.Length > LargoFragmento) { fragmentos.Add(sb.ToString().TrimEnd()); sb.Clear(); }
                sb.Append(p).Append("\n\n");
            }
            if (sb.Length > 0) fragmentos.Add(sb.ToString().TrimEnd());
            return (datos, fragmentos);
        }
    }
}
