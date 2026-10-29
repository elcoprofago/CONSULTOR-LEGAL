using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Win32;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ConsultorLegal
{
    // Muestra la respuesta como tarjetas sobre el fondo "papel": una por
    // resultado, con botones que encadenan la consulta siguiente a partir de
    // los ids que trae cada uno (fallo → texto, norma → artículos, etc.).
    public partial class MainWindow
    {
        private long _fragmento = 1, _totalFragmentos;
        private long _offset, _limite = 20;

        // Último pasaje seleccionado en una tarjeta, y la norma de esa tarjeta
        // (si tiene) para verificar la cita contra ella.
        private string _seleccion = "";
        private string? _seleccionRef, _seleccionArticulo;

        // Texto plano de lo que se está viendo, para copiar y exportar.
        private readonly List<(string titulo, string cuerpo)> _textoActual = new();

        private static readonly Brush TintaPapel = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly Brush TintaTenue = new SolidColorBrush(Color.FromRgb(0x6A, 0x63, 0x55));
        private static readonly Brush TituloPapel = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x63));
        private static readonly Brush BordeTarjeta = new SolidColorBrush(Color.FromRgb(0xD6, 0xCC, 0xB4));

        // Claves que se muestran como título y no se repiten en el cuerpo.
        private static readonly string[] ClavesTitulo = { "titulo", "caratula", "nombre", "nombre_completo", "organismo_nombre" };

        // Claves que solo meten ruido en la tarjeta (siguen en la vista JSON).
        private static readonly HashSet<string> ClavesOcultas = new() { "sha256", "ficha_id_opaco", "matcheo" };

        private void BtnLimpiarResultados_Click(object sender, RoutedEventArgs e)
        {
            _historial.Clear();
            PanelResultados.Children.Clear();
            _textoActual.Clear();
            _seleccion = "";
            TxtJson.Text = "";
            TxtTituloResultado.Text = "";
            TxtFragmento.Text = "Fragmento –";
            TxtPagina.Text = "Página –";
            TxtBuscarPanel.Text = TxtBuscarEstado.Text = "";
            _cacheFragmentos.Clear();
            BtnAtras.IsEnabled = BtnFragAnt.IsEnabled = BtnFragSig.IsEnabled = BtnPagAnt.IsEnabled = BtnPagSig.IsEnabled = false;
            _log.Info("Resultados limpiados.");
        }

        private void Renderizar(Llamada llamada)
        {
            PanelResultados.Children.Clear();
            _textoActual.Clear();
            _seleccion = "";
            _resaltar = TerminosBuscados(llamada.Argumentos);
            string filtro = TxtBuscarPanel.Text.Trim();
            if (filtro.Length > 0) _resaltar.Insert(0, filtro);
            TxtBuscarEstado.Text = "";
            string tituloHerr = _herramientas.TryGetValue(llamada.Herramienta, out var h) ? h.Titulo : llamada.Herramienta;
            BtnAtras.IsEnabled = _historial.Count > 1;

            JsonNode? raiz = null;
            try { raiz = JsonNode.Parse(llamada.Respuesta); } catch { }
            TxtJson.Text = raiz != null ? raiz.ToJsonString(JsonIndentado) : llamada.Respuesta;

            ActualizarNavegacion(llamada, raiz, h);

            if (llamada.EsError || raiz == null)
            {
                TxtTituloResultado.Text = tituloHerr + (llamada.EsError ? " · el servidor rechazó la consulta" : "");
                AgregarTarjeta(llamada.EsError ? "Error" : "Respuesta", llamada.Respuesta, null, llamada.EsError);
                return;
            }

            if (raiz is JsonObject o)
            {
                var lista = o.FirstOrDefault(kv => kv.Value is JsonArray a && a.Any(x => x is JsonObject));
                if (lista.Value is JsonArray items)
                {
                    var meta = new JsonObject();
                    foreach (var (k, v) in o) if (k != lista.Key) meta[k] = v?.DeepClone();
                    string total = o["total"] is JsonNode t ? $" de {t}" : "";
                    TxtTituloResultado.Text = $"{tituloHerr} · {items.Count} {Bonito(lista.Key).ToLower()}{total}";
                    if (meta.Count > 0) TarjetaObjeto(meta, llamada.Herramienta, "Información de la consulta", esMeta: true);
                    int i = _offset > 0 ? (int)_offset : 0;
                    foreach (var item in items)
                    {
                        i++;
                        if (item is JsonObject io) TarjetaObjeto(io, llamada.Herramienta, null, numero: i);
                        else AgregarTarjeta($"{i}.", item?.ToString() ?? "", null);
                    }
                    if (items.Count == 0) AgregarTarjeta("Sin resultados", "La consulta no devolvió resultados. Probá con menos filtros o con otras palabras.", null);
                }
                else
                {
                    TxtTituloResultado.Text = tituloHerr;
                    TarjetaObjeto(o, llamada.Herramienta, null);
                }
            }
            else if (raiz is JsonArray arr)
            {
                TxtTituloResultado.Text = $"{tituloHerr} · {arr.Count} resultados";
                int i = 0;
                foreach (var item in arr)
                    if (item is JsonObject io) TarjetaObjeto(io, llamada.Herramienta, null, numero: ++i);
            }
            else
            {
                TxtTituloResultado.Text = tituloHerr;
                AgregarTarjeta("Respuesta", raiz.ToString(), null);
            }
            if (_totalFragmentos == 0) FiltrarTarjetas(filtro);
            ScrollResultados.ScrollToTop();
        }

        // ================= Buscar dentro del resultado =================

        private static readonly System.Globalization.CompareInfo CompEs = System.Globalization.CultureInfo.GetCultureInfo("es-AR").CompareInfo;
        private const System.Globalization.CompareOptions SinTildesNiMayus =
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;

        // Respuestas de fragmentos ya pedidas, para no repetir la descarga
        // cuando se busca otra palabra en el mismo texto.
        private readonly Dictionary<string, string> _cacheFragmentos = new();
        private CancellationTokenSource? _cancelarBusqueda;

        private void TxtBuscarPanel_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) BtnBuscarPanel_Click(sender, e);
        }

        private async void BtnBuscarPanel_Click(object sender, RoutedEventArgs e)
        {
            if (_cancelarBusqueda != null) { _cancelarBusqueda.Cancel(); return; }
            if (_historial.Count == 0) { TxtBuscarEstado.Text = "No hay resultado donde buscar."; return; }
            var actual = _historial[^1];
            string filtro = TxtBuscarPanel.Text.Trim();

            // Sin fragmentos, o con el campo vacío: se filtran las tarjetas que ya están.
            if (_totalFragmentos == 0 || filtro.Length == 0 || _cliente == null)
            {
                Renderizar(actual);
                return;
            }
            if (_consultando) return;

            _consultando = true;
            _cancelarBusqueda = new CancellationTokenSource();
            BtnBuscarPanel.Content = "Cancelar";
            BtnConsultar.IsEnabled = false;
            long total = _totalFragmentos, actualFrag = _fragmento;
            var hallazgos = new List<(long fragmento, int cantidad, List<string> contextos)>();
            try
            {
                for (long f = 1; f <= total; f++)
                {
                    TxtBuscarEstado.Text = $"Buscando… fragmento {f}/{total}";
                    var args = (JsonObject)actual.Argumentos.DeepClone();
                    args["fragmento"] = f;
                    string clave = actual.Herramienta + args.ToJsonString();
                    if (!_cacheFragmentos.TryGetValue(clave, out var texto))
                    {
                        if (f == actualFrag && !actual.EsError) texto = actual.Respuesta;
                        else
                        {
                            var (t, esError) = await LlamarAsync(actual.Herramienta, args, _cancelarBusqueda.Token);
                            if (esError) { _log.Aviso($"El fragmento {f} no se pudo leer: {Resumir(t, 200)}"); continue; }
                            texto = t;
                        }
                        _cacheFragmentos[clave] = texto;
                    }
                    var (cantidad, contextos) = Coincidencias(TextoPlano(texto), filtro);
                    if (cantidad > 0) hallazgos.Add((f, cantidad, contextos));
                }
            }
            catch (OperationCanceledException) { TxtBuscarEstado.Text = "Búsqueda cancelada."; return; }
            catch (Exception ex) { _log.Error($"No se pudo completar la búsqueda: {ex.Message}"); TxtBuscarEstado.Text = "La búsqueda falló (ver Log)."; return; }
            finally
            {
                _cancelarBusqueda = null;
                _consultando = false;
                BtnBuscarPanel.Content = "Buscar en el resultado";
                BtnConsultar.IsEnabled = _herramientaActual != null;
            }

            MostrarHallazgos(actual, filtro, total, hallazgos);
        }

        private void MostrarHallazgos(Llamada actual, string filtro, long total, List<(long fragmento, int cantidad, List<string> contextos)> hallazgos)
        {
            PanelResultados.Children.Clear();
            _textoActual.Clear();
            _resaltar = new List<string> { filtro };
            int suma = hallazgos.Sum(h => h.cantidad);
            TxtTituloResultado.Text = $"\"{filtro}\" · {suma} coincidencias en {hallazgos.Count} de {total} fragmentos";
            TxtBuscarEstado.Text = "";
            foreach (var (f, cantidad, contextos) in hallazgos)
            {
                string cuerpo = string.Join("\n\n", contextos.Select(c => "… " + c + " …"));
                if (cantidad > contextos.Count) cuerpo += $"\n\n(y {cantidad - contextos.Count} más en este fragmento)";
                long destino = f;
                AgregarTarjeta($"Fragmento {f} · {cantidad} coincidencia{(cantidad == 1 ? "" : "s")}", cuerpo,
                    new() { ("Ir al fragmento", $"Abrir el fragmento {f} con \"{filtro}\" resaltado", async () => await RepetirConAsync("fragmento", destino)) });
            }
            if (hallazgos.Count == 0)
                AgregarTarjeta("Sin coincidencias", $"\"{filtro}\" no aparece en ninguno de los {total} fragmentos.", null);
            var volver = new List<(string, string, Action)> { ("Volver al resultado", "Ver de nuevo el fragmento que estaba abierto", () => Renderizar(actual)) };
            AgregarTarjeta("Búsqueda en todos los fragmentos", "Vaciá el campo de búsqueda y pulsá el botón para dejar de resaltar.", volver, esMeta: true);
            ScrollResultados.ScrollToTop();
        }

        // Sin fragmentos: se ocultan las tarjetas que no contienen el texto
        // (la de "Información de la consulta" queda siempre).
        private void FiltrarTarjetas(string filtro)
        {
            if (filtro.Length == 0) return;
            int visibles = 0, total = 0;
            for (int i = 0; i < PanelResultados.Children.Count && i < _textoActual.Count; i++)
            {
                var (t, c) = _textoActual[i];
                if (t == "Información de la consulta") continue;
                total++;
                bool hay = CompEs.IndexOf(t + "\n" + c, filtro, SinTildesNiMayus) >= 0;
                PanelResultados.Children[i].Visibility = hay ? Visibility.Visible : Visibility.Collapsed;
                if (hay) visibles++;
            }
            TxtBuscarEstado.Text = $"\"{filtro}\": {visibles} de {total} tarjetas";
        }

        // Todo el texto de una respuesta JSON (los valores, no las claves).
        private static string TextoPlano(string respuesta)
        {
            JsonNode? raiz;
            try { raiz = JsonNode.Parse(respuesta); } catch { return respuesta; }
            var sb = new StringBuilder();
            void Juntar(JsonNode? n)
            {
                switch (n)
                {
                    case JsonObject o: foreach (var (_, v) in o) Juntar(v); break;
                    case JsonArray a: foreach (var v in a) Juntar(v); break;
                    case JsonValue v: sb.AppendLine(v.ToString()); break;
                }
            }
            Juntar(raiz);
            return sb.ToString();
        }

        private static (int cantidad, List<string> contextos) Coincidencias(string texto, string filtro, int maxContextos = 5)
        {
            var contextos = new List<string>();
            int cantidad = 0, pos = 0;
            while (pos < texto.Length)
            {
                int i = CompEs.IndexOf(texto, filtro, pos, SinTildesNiMayus);
                if (i < 0) break;
                cantidad++;
                if (contextos.Count < maxContextos)
                {
                    int ini = Math.Max(0, i - 120), fin = Math.Min(texto.Length, i + filtro.Length + 120);
                    contextos.Add(texto[ini..fin].Replace('\n', ' ').Replace('\r', ' ').Trim());
                }
                pos = i + Math.Max(1, filtro.Length);
            }
            return (cantidad, contextos);
        }

        private void ActualizarNavegacion(Llamada llamada, JsonNode? raiz, Herramienta? h)
        {
            // Fragmentos: la respuesta trae "fragmento" y "total_fragmentos"
            // (o "fragmentos_total"), arriba o dentro de "texto".
            _fragmento = 1; _totalFragmentos = 0;
            if (h?.Campos.Any(c => c.Nombre == "fragmento") == true && BuscarFragmentos(raiz) is (long f, long t))
            {
                _fragmento = f; _totalFragmentos = t;
            }
            TxtFragmento.Text = _totalFragmentos > 0 ? $"Fragmento {_fragmento}/{_totalFragmentos}" : "Fragmento –";
            BtnFragAnt.IsEnabled = _totalFragmentos > 0 && _fragmento > 1;
            BtnFragSig.IsEnabled = _totalFragmentos > 0 && _fragmento < _totalFragmentos;

            // Páginas: solo en las herramientas que aceptan "offset".
            bool pagina = h?.Campos.Any(c => c.Nombre == "offset") == true;
            _offset = Entero(llamada.Argumentos["offset"]) ?? 0;
            _limite = Entero(llamada.Argumentos["limit"]) ?? Entero(h?.Campos.FirstOrDefault(c => c.Nombre == "limit")?.PorDefecto) ?? 20;
            bool hayMas = raiz?["hay_mas"]?.ToString() is "true" or "True";
            TxtPagina.Text = pagina ? $"Página {_offset / Math.Max(1, _limite) + 1}" : "Página –";
            BtnPagAnt.IsEnabled = pagina && _offset > 0;
            BtnPagSig.IsEnabled = pagina && hayMas;
        }

        private static (long, long)? BuscarFragmentos(JsonNode? n)
        {
            if (n is not JsonObject o) return null;
            long? total = Entero(o["total_fragmentos"]) ?? Entero(o["fragmentos_total"]);
            if (total != null) return (Entero(o["fragmento"]) ?? 1, total.Value);
            foreach (var (_, v) in o)
                if (BuscarFragmentos(v) is (long, long) r) return r;
            return null;
        }

        private static long? Entero(JsonNode? n) =>
            n != null && long.TryParse(n.ToString(), out long v) ? v : null;

        // ----------------------------------------------------------------

        private void TarjetaObjeto(JsonObject o, string herramienta, string? titulo, bool esMeta = false, int numero = 0)
        {
            var fuente = o["fuente"] as JsonObject;
            titulo ??= TituloDe(o, fuente);
            if (numero > 0) titulo = $"{numero}. {titulo}";

            var cuerpo = new StringBuilder();
            var largos = new StringBuilder();
            Volcar(o, "", cuerpo, largos, esRaiz: true);
            string texto = cuerpo.ToString().TrimEnd();
            if (largos.Length > 0) texto += (texto.Length > 0 ? "\n\n" : "") + largos.ToString().TrimEnd();

            var acciones = esMeta ? new List<(string, string, Action)>() : Acciones(o, fuente, herramienta);
            string? refNorma = RefNorma(o, fuente);
            string? articulo = (o["articulo"] ?? fuente?["articulo"])?.ToString();
            AgregarTarjeta(titulo, texto, acciones, esMeta: esMeta, refNorma: refNorma, articulo: articulo);
        }

        private static string TituloDe(JsonObject o, JsonObject? fuente)
        {
            foreach (var k in ClavesTitulo)
                if (o[k] is JsonValue v && v.ToString().Length > 0)
                {
                    // Normas: "Ley 20744 — título".
                    if (k == "titulo" && o["tipo"] is JsonValue tipo && o["numero"] is JsonValue num)
                        return $"{tipo} {num} — {v}";
                    return v.ToString();
                }
            if (fuente?["caratula"] is JsonValue c) return c.ToString();
            if (o["tipo"] is JsonValue t2 && o["numero"] is JsonValue n2) return $"{t2} {n2}";
            if (o["resultado"] is JsonValue r) return r.ToString();
            if (o["articulo"] is JsonValue a) return "Artículo " + a;
            return "Resultado";
        }

        // Escribe "Clave: valor" para lo corto y deja los textos largos aparte,
        // como párrafos al final de la tarjeta.
        private void Volcar(JsonNode? n, string prefijo, StringBuilder cuerpo, StringBuilder largos, bool esRaiz = false)
        {
            if (n is not JsonObject o) return;
            foreach (var (k, v) in o)
            {
                if (v == null || ClavesOcultas.Contains(k)) continue;
                if (esRaiz && ClavesTitulo.Contains(k)) continue;
                string etiqueta = prefijo + Bonito(k);
                switch (v)
                {
                    case JsonValue val:
                        string s = val.ToString();
                        if (s.Length == 0) continue;
                        if (s.Length > 220 || s.Contains('\n'))
                            largos.Append(etiqueta.ToUpperInvariant()).Append('\n').Append(s.Trim()).Append("\n\n");
                        else
                            cuerpo.Append(etiqueta).Append(": ").Append(s).Append('\n');
                        break;
                    case JsonArray arr when arr.All(x => x is JsonValue):
                        if (arr.Count > 0)
                            cuerpo.Append(etiqueta).Append(": ").Append(string.Join(", ", arr.Select(x => x?.ToString()))).Append('\n');
                        break;
                    case JsonArray arr:
                        largos.Append(etiqueta.ToUpperInvariant()).Append($" ({arr.Count})\n");
                        int i = 0;
                        foreach (var item in arr)
                        {
                            i++;
                            if (item is JsonObject io)
                            {
                                var c2 = new StringBuilder();
                                var l2 = new StringBuilder();
                                Volcar(io, "", c2, l2);
                                largos.Append($"  {i}) ").Append(c2.ToString().TrimEnd().Replace("\n", " · "));
                                if (l2.Length > 0) largos.Append('\n').Append(Sangrar(l2.ToString().TrimEnd()));
                                largos.Append("\n");
                            }
                            else largos.Append($"  {i}) {item}\n");
                        }
                        largos.Append('\n');
                        break;
                    case JsonObject sub when k == "fuente":
                        // De la fuente solo interesa lo que no está ya arriba.
                        foreach (var (fk, fv) in sub)
                            if (fv is JsonValue fval && !ClavesOcultas.Contains(fk) && o[fk] == null && !ClavesTitulo.Contains(fk))
                                cuerpo.Append("Fuente · ").Append(Bonito(fk)).Append(": ").Append(fval).Append('\n');
                        break;
                    case JsonObject sub:
                        Volcar(sub, etiqueta + " · ", cuerpo, largos);
                        break;
                }
            }
        }

        private static string Sangrar(string s) => string.Join("\n", s.Split('\n').Select(l => "     " + l));

        private static string Bonito(string clave)
        {
            string s = clave.Replace('_', ' ');
            return s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
        }

        private static string? RefNorma(JsonObject o, JsonObject? fuente)
        {
            foreach (var src in new[] { o, fuente })
            {
                if (src == null) continue;
                foreach (var k in new[] { "norma_id", "id_norma", "norma_id_canonico" })
                    if (src[k] is JsonValue v && v.ToString() is { Length: > 0 } s && s != "null") return s;
            }
            return null;
        }

        // Botones de encadenamiento según los ids que trae el resultado. "Ir"
        // ejecuta directamente; "cargar" deja el formulario listo para que se
        // complete lo que falta (por ejemplo, la fecha de una vigencia).
        private List<(string texto, string ayuda, Action accion)> Acciones(JsonObject o, JsonObject? fuente, string herramienta)
        {
            var lista = new List<(string, string, Action)>();
            string? Valor(params string[] claves)
            {
                foreach (var src in new[] { o, fuente })
                    foreach (var k in claves)
                        if (src?[k] is JsonValue v && v.ToString() is { Length: > 0 } s && s != "null") return s;
                return null;
            }
            void Ir(string texto, string ayuda, string tool, JsonObject args)
            {
                if (_herramientas.ContainsKey(tool))
                    lista.Add((texto, ayuda, async () =>
                    {
                        SeleccionarHerramienta(tool, args);
                        await EjecutarAsync(new Llamada { Herramienta = tool, Argumentos = (JsonObject)args.DeepClone() });
                    }));
            }
            void Cargar(string texto, string ayuda, string tool, JsonObject args)
            {
                if (_herramientas.ContainsKey(tool))
                    lista.Add((texto, ayuda, () =>
                    {
                        SeleccionarHerramienta(tool, args);
                        _log.Info($"Formulario listo: completá los datos que faltan y pulsá Consultar.");
                    }));
            }

            if (Valor("id_texto_tcpba") is string tcp && long.TryParse(tcp, out long idt))
            {
                if (herramienta != JubaTcp.Leer) Ir("Leer texto", "Texto completo del fallo en JUBA", JubaTcp.Leer, new JsonObject { ["id_juba"] = idt });
            }
            else if (Valor("id_juba") is string juba)
            {
                if (herramienta != "obtener_fallo_scba") Ir("Ficha", "Ficha completa del fallo", "obtener_fallo_scba", new JsonObject { ["id_juba"] = juba });
                if (herramienta != "leer_fallo_scba") Ir("Leer texto", "Texto completo del fallo", "leer_fallo_scba", new JsonObject { ["id_juba"] = juba });
            }
            else if (Valor("id_fallo") is string fallo && long.TryParse(fallo, out long idf))
            {
                if (herramienta != "obtener_fallo") Ir("Ficha", "Ficha completa del fallo", "obtener_fallo", new JsonObject { ["id_fallo"] = idf });
                if (herramienta != "leer_fallo") Ir("Leer texto", "Texto completo del fallo", "leer_fallo", new JsonObject { ["id_fallo"] = idf });
                Ir("Precedentes", "Fallos que cita y fallos que lo citan", "red_precedentes", new JsonObject { ["id_fallo"] = idf });
                Ir("Relacionados", "Fallos de temática parecida", "fallos_relacionados", new JsonObject { ["id_fallo"] = idf });
            }

            if (Valor("ficha_id_opaco", "id_tratado") is string trat && herramienta.Contains("tratado"))
            {
                var a = new JsonObject { ["ref"] = Valor("id_tratado") ?? trat };
                if (Valor("catalogo") is string cat) a["catalogo"] = cat;
                if (herramienta != "obtener_tratado") Ir("Ficha", "Ficha del tratado", "obtener_tratado", (JsonObject)a.DeepClone());
                if (herramienta != "leer_tratado") Ir("Leer texto", "Texto del tratado", "leer_tratado", (JsonObject)a.DeepClone());
            }

            if (RefNorma(o, fuente) is string norma && !herramienta.Contains("tratado"))
            {
                string? art = Valor("articulo");
                if (herramienta != "obtener_norma") Ir("Ficha", "Ficha de la norma", "obtener_norma", new JsonObject { ["ref"] = norma });
                if (art != null && herramienta != "leer_articulos")
                    Ir($"Leer art. {art}", "Texto del artículo", "leer_articulos", new JsonObject { ["ref"] = norma, ["articulos"] = art });
                else if (herramienta != "leer_articulos")
                    Cargar("Artículos…", "Prepara el formulario para leer artículos de esta norma", "leer_articulos", new JsonObject { ["ref"] = norma });
                if (herramienta != "modificaciones") Ir("Modificaciones", "Qué normas la modifican y cuáles modifica", "modificaciones", new JsonObject { ["ref"] = norma });
                if (herramienta != "fallos_que_citan")
                {
                    var a = new JsonObject { ["ref"] = norma };
                    if (art != null) a["articulo"] = art;
                    Ir("Fallos que la citan", "Fallos de la Corte que citan esta norma", "fallos_que_citan", a);
                }
                var vig = new JsonObject { ["ref"] = norma, ["fecha"] = DateTime.Today.ToString("yyyy-MM-dd") };
                if (art != null) vig["articulo"] = art;
                Cargar("Vigencia…", "Prepara el formulario para ver el texto vigente a una fecha", "vigencia", vig);
            }

            if (herramienta == "buscar_bora" && Valor("id") is string bora)
                Ir("Leer aviso", "Texto completo del aviso", "leer_aviso_bora", new JsonObject { ["id_publicacion"] = bora });
            if (Valor("aviso_id") is string aviso && herramienta != "leer_aviso_boletin")
                Ir("Leer aviso", "Texto completo del aviso", "leer_aviso_boletin", new JsonObject { ["aviso_id"] = aviso });
            if (Valor("organismo_slug") is string slug && herramienta != "obtener_tribunal")
                Ir("Ficha", "Datos completos del tribunal", "obtener_tribunal", new JsonObject { ["ref"] = slug });

            foreach (var k in new[] { "url", "url_infoleg", "url_juba", "url_pdf", "url_texto" })
                if (Valor(k) is string url && url.StartsWith("http"))
                    lista.Add(($"Abrir fuente ({Bonito(k)})", url, () => AbrirUrl(url)));
            return lista;
        }

        // Voces de la consulta que se resaltan en los resultados: las frases
        // de "voces" enteras y las palabras de "query" (sin las cortas, que
        // son artículos y conectores: de, la, o, y...).
        private List<string> _resaltar = new();
        private static readonly Brush FondoResalte = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));

        private static List<string> TerminosBuscados(JsonObject args)
        {
            var lista = new List<string>();
            void Frases(JsonNode? n)
            {
                if (n is JsonArray a) foreach (var x in a) Frases(x);
                else if (n is JsonValue v && v.TryGetValue<string>(out var t))
                    foreach (var f in t.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        lista.Add(f);
            }
            Frases(args["voces"]);
            if (args["query"] is JsonValue q && q.TryGetValue<string>(out var query))
                foreach (var w in query.Split(new[] { ' ', ',', ';', '"', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                    if (w.Length >= 4 && !w.StartsWith("-")) lista.Add(w.Trim('*'));
            return lista.Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(t => t.Length).ToList();
        }

        // Parte el texto en tramos normales y resaltados, sin distinguir
        // mayúsculas ni tildes ("sentencia" encuentra "SENTENCIA" y "sentencía").
        private static void AgregarConResalte(InlineCollection destino, string texto, List<string> terminos)
        {
            var ci = System.Globalization.CultureInfo.GetCultureInfo("es-AR").CompareInfo;
            const System.Globalization.CompareOptions op = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
            int pos = 0;
            while (pos < texto.Length && terminos.Count > 0)
            {
                int mejor = -1, largo = 0;
                foreach (var t in terminos)
                {
                    int i = ci.IndexOf(texto.AsSpan(pos), t.AsSpan(), op, out int len);
                    if (i >= 0) i += pos;
                    if (i >= 0 && (mejor < 0 || i < mejor || (i == mejor && len > largo))) { mejor = i; largo = len; }
                }
                if (mejor < 0) break;
                if (mejor > pos) destino.Add(new Run(texto[pos..mejor]));
                destino.Add(new Run(texto.Substring(mejor, largo)) { Background = FondoResalte, FontWeight = FontWeights.SemiBold });
                pos = mejor + largo;
            }
            if (pos < texto.Length) destino.Add(new Run(texto[pos..]));
        }

        private void AgregarTarjeta(string titulo, string cuerpo, List<(string texto, string ayuda, Action accion)>? acciones,
                                    bool esError = false, bool esMeta = false, string? refNorma = null, string? articulo = null)
        {
            _textoActual.Add((titulo, cuerpo));
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = titulo, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                FontSize = _settings.TamanoFuenteResultados + 1,
                Foreground = esError ? Brushes.DarkRed : esMeta ? TintaTenue : TituloPapel,
                Margin = new Thickness(0, 0, 0, 4),
            });

            // RichTextBox de solo lectura: el texto se puede seleccionar y copiar,
            // la selección alimenta "Verificar cita seleccionada", y las voces
            // buscadas se resaltan.
            var parrafo = new Paragraph { Margin = new Thickness(0) };
            AgregarConResalte(parrafo.Inlines, cuerpo, esMeta ? new List<string>() : _resaltar);
            var caja = new RichTextBox
            {
                Document = new FlowDocument(parrafo) { PagePadding = new Thickness(0) },
                IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = esMeta ? TintaTenue : TintaPapel,
                FontSize = esMeta ? _settings.TamanoFuenteResultados - 1 : _settings.TamanoFuenteResultados,
                FontFamily = new FontFamily("Segoe UI"), Padding = new Thickness(0), CaretBrush = TintaPapel,
                SelectionBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)),
            };
            caja.SelectionChanged += (_, _) =>
            {
                if (caja.Selection.IsEmpty) return;
                _seleccion = caja.Selection.Text;
                _seleccionRef = refNorma;
                _seleccionArticulo = articulo;
            };
            panel.Children.Add(caja);

            if (acciones is { Count: > 0 } || !esMeta)
            {
                var botones = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var (texto, ayuda, accion) in acciones ?? new())
                {
                    var b = new Button { Content = texto, ToolTip = ayuda, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3) };
                    b.Click += (_, _) => accion();
                    botones.Children.Add(b);
                }
                var copiar = new Button { Content = "Copiar", ToolTip = "Copiar el texto de esta tarjeta", Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3) };
                copiar.Click += (_, _) => { CopiarAlPortapapeles(titulo + "\n" + cuerpo); _log.Ok("Tarjeta copiada."); };
                botones.Children.Add(copiar);
                panel.Children.Add(botones);
            }

            PanelResultados.Children.Add(new Border
            {
                Child = panel, Padding = new Thickness(12, 10, 12, 8), Margin = new Thickness(0, 0, 0, 10),
                CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                BorderBrush = esError ? Brushes.IndianRed : BordeTarjeta,
                Background = esError ? new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE7))
                           : esMeta ? new SolidColorBrush(Color.FromRgb(0xE9, 0xE2, 0xD2))
                           : new SolidColorBrush(Color.FromRgb(0xFB, 0xF8, 0xF1)),
            });
        }

        // ===================== Copiar / exportar =====================

        private string TextoResultado()
        {
            if (_historial.Count == 0) return "";
            var llamada = _historial[^1];
            string titulo = _herramientas.TryGetValue(llamada.Herramienta, out var h) ? h.Titulo : llamada.Herramienta;
            var sb = new StringBuilder();
            sb.AppendLine($"CONSULTOR-LEGAL · {titulo} ({llamada.Herramienta})");
            sb.AppendLine($"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm} · Servidor: {_cliente?.Endpoint}");
            sb.AppendLine("Consulta: " + llamada.Argumentos.ToJsonString(JsonIndentado).Replace("\n", " ").Replace("  ", ""));
            sb.AppendLine(new string('=', 70));
            foreach (var (t, c) in _textoActual)
            {
                sb.AppendLine();
                sb.AppendLine(t);
                sb.AppendLine(new string('-', Math.Min(70, Math.Max(10, t.Length))));
                sb.AppendLine(c);
            }
            return sb.ToString();
        }

        private void BtnCopiarResultado_Click(object sender, RoutedEventArgs e)
        {
            if (_historial.Count == 0) { _log.Aviso("Todavía no hay un resultado para copiar."); return; }
            CopiarAlPortapapeles(TxtJson.Visibility == Visibility.Visible ? TxtJson.Text : TextoResultado());
            _log.Ok("Resultado copiado al portapapeles.");
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (_historial.Count == 0) { _log.Aviso("Todavía no hay un resultado para exportar."); return; }
            bool docx = RbDocx.IsChecked == true;
            var llamada = _historial[^1];
            var dlg = new SaveFileDialog
            {
                Title = "Exportar resultado",
                Filter = docx ? "Documento de Word (*.docx)|*.docx" : "Texto (*.txt)|*.txt",
                FileName = $"{llamada.Herramienta}_{DateTime.Now:yyyyMMdd_HHmm}",
            };
            if (!string.IsNullOrEmpty(_settings.CarpetaExportacionDefault) && Directory.Exists(_settings.CarpetaExportacionDefault))
                dlg.InitialDirectory = _settings.CarpetaExportacionDefault;
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                if (docx) ExportarDocx(dlg.FileName);
                else File.WriteAllText(dlg.FileName, TextoResultado(), new UTF8Encoding(true));
                _log.Ok($"Exportado: {dlg.FileName}");
            }
            catch (Exception ex)
            {
                _log.Error($"No se pudo exportar: {ex.Message}");
            }
        }

        private void ExportarDocx(string ruta)
        {
            var llamada = _historial[^1];
            string titulo = _herramientas.TryGetValue(llamada.Herramienta, out var h) ? h.Titulo : llamada.Herramienta;
            using var doc = WordprocessingDocument.Create(ruta, WordprocessingDocumentType.Document);
            var main = doc.AddMainDocumentPart();
            var body = new W.Body();
            main.Document = new W.Document(body);

            W.Paragraph Parrafo(string texto, bool negrita = false, int tamanoMedioPuntos = 22, string? color = null)
            {
                var props = new W.RunProperties(new W.FontSize { Val = tamanoMedioPuntos.ToString() },
                                                new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
                if (negrita) props.Append(new W.Bold());
                if (color != null) props.Append(new W.Color { Val = color });
                var run = new W.Run(props);
                var lineas = texto.Replace("\r", "").Split('\n');
                for (int i = 0; i < lineas.Length; i++)
                {
                    if (i > 0) run.Append(new W.Break());
                    run.Append(new W.Text(lineas[i]) { Space = SpaceProcessingModeValues.Preserve });
                }
                return new W.Paragraph(run);
            }

            body.Append(Parrafo($"{titulo}", true, 32, "1F4E63"));
            body.Append(Parrafo($"{DateTime.Now:dd/MM/yyyy HH:mm} · {_cliente?.Endpoint}", false, 18, "6A6355"));
            body.Append(Parrafo("Consulta: " + llamada.Argumentos.ToJsonString(JsonIndentado).Replace("\n", " ").Replace("  ", ""), false, 18, "6A6355"));
            foreach (var (t, c) in _textoActual)
            {
                body.Append(Parrafo(t, true, 24, "1F4E63"));
                body.Append(Parrafo(c));
            }
            main.Document.Save();
        }
    }
}
