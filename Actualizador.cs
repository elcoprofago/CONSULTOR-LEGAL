using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConsultorLegal
{
    // Actualización desde las releases de GitHub que arma publicar.ps1
    // (etiqueta v<Version>, con CONSULTOR-LEGAL.exe autocontenido como único
    // archivo). Lo descargado se verifica antes de instalarlo: tamaño y
    // SHA-256 contra lo que informa GitHub, y que la versión grabada en el
    // .exe sea la de la etiqueta.
    public static class Actualizador
    {
        public const string Repo = "elcoprofago/CONSULTOR-LEGAL";
        private const string NombreExe = "CONSULTOR-LEGAL.exe";

        public static Version VersionActual
        {
            get
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        // En la compilación de desarrollo el .exe es un lanzador de
        // CONSULTOR-LEGAL.dll: pisarlo con el publicado rompería esa carpeta.
        public static bool EsEjecutablePublicado =>
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "CONSULTOR-LEGAL.dll"));

        public static string ExeActual =>
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, NombreExe);

        public record Release(Version Version, string Etiqueta, string UrlExe, long Bytes, string? Sha256, string Notas, string UrlPagina);

        private static HttpClient NuevoCliente(TimeSpan timeout)
        {
            var http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"CONSULTOR-LEGAL/{VersionActual}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        public static async Task<Release> ConsultarUltimaAsync(CancellationToken ct = default)
        {
            using var http = NuevoCliente(TimeSpan.FromSeconds(30));
            using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException("Todavía no hay ninguna versión publicada en GitHub.");
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string etiqueta = root.GetProperty("tag_name").GetString() ?? "";
            var m = Regex.Match(etiqueta, @"^v(\d+)\.(\d+)\.(\d+)$");
            if (!m.Success) throw new InvalidDataException($"La etiqueta de la última versión ({etiqueta}) no tiene el formato vX.Y.Z.");
            var version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != NombreExe) continue;
                string? sha = null;
                if (asset.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String &&
                    dg.GetString() is string s && s.StartsWith("sha256:"))
                    sha = s["sha256:".Length..];
                return new Release(version, etiqueta,
                    asset.GetProperty("browser_download_url").GetString()!,
                    asset.GetProperty("size").GetInt64(), sha,
                    root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                    root.GetProperty("html_url").GetString() ?? "");
            }
            throw new InvalidDataException($"La versión {etiqueta} no trae el archivo {NombreExe}.");
        }

        // Descarga el .exe a una carpeta temporal propia y lo verifica.
        public static async Task<string> DescargarYVerificarAsync(Release r, IProgress<string>? progreso, CancellationToken ct = default)
        {
            string dir = Path.Combine(Path.GetTempPath(), "CONSULTOR-LEGAL-actualizacion", r.Etiqueta);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, NombreExe);

            using (var http = NuevoCliente(TimeSpan.FromMinutes(30)))
            using (var resp = await http.GetAsync(r.UrlExe, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var origen = await resp.Content.ReadAsStreamAsync(ct);
                await using var destino = File.Create(exe);
                var buffer = new byte[1 << 16];
                long total = 0;
                var reloj = Stopwatch.StartNew();
                int leidos;
                while ((leidos = await origen.ReadAsync(buffer, ct)) > 0)
                {
                    await destino.WriteAsync(buffer.AsMemory(0, leidos), ct);
                    total += leidos;
                    if (reloj.ElapsedMilliseconds > 250)
                    {
                        reloj.Restart();
                        progreso?.Report($"Descargando {total / 1048576.0:0.0} de {r.Bytes / 1048576.0:0.0} MB...");
                    }
                }
            }

            progreso?.Report("Verificando la descarga...");
            long largo = new FileInfo(exe).Length;
            if (largo != r.Bytes)
                throw new InvalidDataException($"La descarga quedó incompleta ({largo} de {r.Bytes} bytes).");
            if (r.Sha256 != null)
            {
                string sha;
                using (var fs = File.OpenRead(exe)) sha = Convert.ToHexString(SHA256.HashData(fs));
                if (!sha.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El archivo descargado no coincide con el publicado (SHA-256 distinto).");
            }

            string interna = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "";
            if (interna != $"{r.Version}.0")
                throw new InvalidDataException(
                    $"La versión grabada en el {NombreExe} descargado ({interna}) no coincide con la etiqueta {r.Etiqueta}. No se instala.");
            return exe;
        }

        // Windows permite renombrar un .exe en ejecución (no pisarlo): el
        // actual pasa a .anterior y el nuevo toma su nombre. Si el último
        // paso falla, se deshace el renombre. El .anterior se borra en el
        // próximo arranque, cuando la versión nueva ya abrió bien.
        public static void Instalar(string exeNuevo, string exeActual)
        {
            string nuevo = exeActual + ".nuevo";
            string anterior = exeActual + ".anterior";

            File.Copy(exeNuevo, nuevo, overwrite: true);
            if (!MismoContenido(exeNuevo, nuevo))
            {
                File.Delete(nuevo);
                throw new IOException($"La copia del nuevo {NombreExe} no coincide con la descarga.");
            }

            if (File.Exists(anterior)) File.Delete(anterior);
            File.Move(exeActual, anterior);
            try
            {
                File.Move(nuevo, exeActual);
            }
            catch
            {
                File.Move(anterior, exeActual);
                throw;
            }
        }

        private static bool MismoContenido(string a, string b)
        {
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            return fa.Length == fb.Length && SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
        }

        // Al arrancar: un .nuevo suelto es una instalación que no llegó a
        // completarse, y un .anterior ya no hace falta porque esta versión abrió.
        public static void LimpiarRestos()
        {
            if (!EsEjecutablePublicado) return;
            foreach (var sufijo in new[] { ".nuevo", ".anterior" })
            {
                try { if (File.Exists(ExeActual + sufijo)) File.Delete(ExeActual + sufijo); }
                catch { }
            }
        }
    }
}
