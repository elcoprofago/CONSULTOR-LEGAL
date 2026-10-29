using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ConsultorLegal
{
    public partial class MainWindow : Window
    {
        // La app solo arma pedidos y muestra respuestas: el trabajo pesado lo
        // hace el servidor. 1 GB alcanza de sobra; si el círculo se pone en
        // rojo es que algo anda mal (por ejemplo, una respuesta enorme).
        private const double RamCapMb = 1000;

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions JsonIndentado = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private AppSettings _settings = new();
        private readonly Consola _log;
        private McpClient? _cliente;
        private readonly Dictionary<string, Herramienta> _herramientas = new();
        private Herramienta? _herramientaActual;
        private readonly Dictionary<string, (Campo campo, FrameworkElement control)> _controles = new();

        // Llamadas hechas, para "Atrás". La última es la que se está viendo.
        private readonly List<Llamada> _historial = new();
        private bool _consultando;

        private DispatcherTimer? _perfTimer;
        private TimeSpan _lastCpuTime = TimeSpan.Zero;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private ulong _lastGpuRaw = 0;
        private ulong _lastGpuTimeStamp = 0;

        private GridLength? _colLogGuardado;
        private GridLength? _colFormGuardado;
        private GridLength _colLogAntesDeDesacoplar = new(1, GridUnitType.Star);
        private Window? _ventanaLog;
        private bool _cerrandoApp;

        private BitmapImage? _iconoConfigNormal;
        private BitmapImage? _iconoConfigHover;

        public MainWindow()
        {
            InitializeComponent();
            _settings = CargarSettings();
            _log = new Consola(TxtLog);

            CargarIconosConfiguracion();
            AplicarTema();
            AplicarFormatoExportacion();
            TxtEndpoint.Text = _settings.Endpoint;
            TxtComando.Text = McpClient.ComandoClaudeCode(_settings.Endpoint);

            foreach (var c in new[] { CircleRam, CircleCpu, CircleGpu }) { c.SetSize(56); c.SetCircle(0, "–"); }
            SetupPerfTimer();

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && BtnConsultar.IsEnabled)
                {
                    e.Handled = true;
                    BtnConsultar_Click(BtnConsultar, new RoutedEventArgs());
                }
            };

            Loaded += async (_, _) =>
            {
                _log.Banner(VersionApp);
                if (_settings.LogDesacoplado) DesacoplarLog();
                await ConectarAsync();
            };
        }

        private static string VersionApp =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

        // ===================== Configuración =====================

        private static string GetConfigPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CONSULTOR-LEGAL");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "config.json");
        }

        private static AppSettings CargarSettings()
        {
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path)) return new AppSettings();
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
            catch
            {
                // Config corrupta o ilegible: seguir con los valores por
                // defecto en vez de impedir que la app arranque.
                return new AppSettings();
            }
        }

        private void GuardarSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetConfigPath(), json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _log.Aviso($"No se pudo guardar la configuración: {ex.Message}");
            }
        }

        private void CargarIconosConfiguracion()
        {
            _iconoConfigNormal = new BitmapImage(new Uri("pack://application:,,,/Assets/rueda1.png"));
            _iconoConfigHover = new BitmapImage(new Uri("pack://application:,,,/Assets/rueda2.png"));
            ImgConfiguracion.Source = _iconoConfigNormal;
            BtnConfiguracion.MouseEnter += (s, e) => ImgConfiguracion.Source = _iconoConfigHover;
            BtnConfiguracion.MouseLeave += (s, e) => ImgConfiguracion.Source = _iconoConfigNormal;
        }

        // El tema "Con textura" pinta la ventana y los paneles de conexión,
        // formulario, rendimiento y botones. Resultados y Log quedan opacos:
        // una textura detrás de texto denso perjudica la lectura.
        private void AplicarTema()
        {
            Brush fondoPaneles = (Brush)FindResource("PanelBg");

            switch (_settings.Tema)
            {
                case "Claro":
                    Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC));
                    break;
                case "Textura":
                    Brush? fondoMosaico = null;
                    if (!string.IsNullOrEmpty(_settings.RutaTextura) && File.Exists(_settings.RutaTextura))
                    {
                        try
                        {
                            var bmp = new BitmapImage(new Uri(_settings.RutaTextura));
                            // Mosaico a tamaño nativo: la textura no se recorta
                            // ni se deforma sea cual sea la forma del panel.
                            fondoMosaico = new ImageBrush(bmp)
                            {
                                TileMode = TileMode.Tile,
                                Viewport = new Rect(0, 0, bmp.Width, bmp.Height),
                                ViewportUnits = BrushMappingMode.Absolute,
                            };
                        }
                        catch (Exception ex)
                        {
                            _log.Aviso($"No se pudo cargar la imagen de textura: {ex.Message}");
                        }
                    }
                    fondoMosaico ??= new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    Background = fondoMosaico;
                    fondoPaneles = fondoMosaico;
                    break;
                default: // "Oscuro"
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    break;
            }

            BorderConexion.Background = fondoPaneles;
            BorderPerf.Background = fondoPaneles;
            BorderForm.Background = fondoPaneles;
            BorderBotones.Background = fondoPaneles;
        }

        private void AplicarFormatoExportacion()
        {
            RbDocx.IsChecked = _settings.FormatoExportacion == "docx";
            RbTxt.IsChecked = !RbDocx.IsChecked;
        }

        private void BtnConfiguracion_Click(object sender, RoutedEventArgs e)
        {
            _settings.FormatoExportacion = RbDocx.IsChecked == true ? "docx" : "txt";
            var wnd = new SettingsWindow(_settings) { Owner = this };
            if (wnd.ShowDialog() != true || wnd.Result == null) return;

            _settings = wnd.Result;
            GuardarSettings();
            AplicarTema();
            AplicarFormatoExportacion();
            if (_historial.Count > 0) Renderizar(_historial[^1]);
            _log.Ok("Configuración guardada.");
        }

        // ===================== Conexión =====================

        private void TxtEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtComando != null) TxtComando.Text = McpClient.ComandoClaudeCode(TxtEndpoint.Text);
        }

        private async void TxtEndpoint_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await ConectarAsync();
        }

        private async void BtnConectar_Click(object sender, RoutedEventArgs e) => await ConectarAsync();

        private void BtnRestaurarEndpoint_Click(object sender, RoutedEventArgs e) =>
            TxtEndpoint.Text = McpClient.EndpointPorDefecto;

        private void BtnCopiarComando_Click(object sender, RoutedEventArgs e)
        {
            CopiarAlPortapapeles(TxtComando.Text);
            _log.Ok("Comando copiado. Pegalo una vez en una terminal (o en la de VSCode) y Claude Code podrá usar bravo47.");
        }

        private async Task ConectarAsync()
        {
            string endpoint = TxtEndpoint.Text.Trim();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                _log.Error("La dirección del servidor no es válida (tiene que empezar con https://).");
                return;
            }

            BtnConectar.IsEnabled = false;
            BtnConsultar.IsEnabled = false;
            TxtEstadoServidor.Text = "Conectando…";
            _log.Titulo("Conexión");
            _log.Info($"Conectando con {endpoint}");
            var reloj = Stopwatch.StartNew();
            try
            {
                var cliente = new McpClient(endpoint);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                await cliente.InicializarAsync(cts.Token);
                var tools = await cliente.ListarHerramientasAsync(cts.Token);

                _cliente = cliente;
                _herramientas.Clear();
                foreach (var t in tools.OfType<JsonObject>())
                {
                    var h = Herramientas.Desde(t);
                    _herramientas[h.Nombre] = h;
                }
                foreach (var h in JubaTcp.Herramientas()) _herramientas[h.Nombre] = h;

                string nombre = cliente.InfoServidor?["name"]?.ToString() ?? "?";
                string version = cliente.InfoServidor?["version"]?.ToString() ?? "?";
                TxtEstadoServidor.Text = $"Conectado: {nombre} {version} · {_herramientas.Count} herramientas · {reloj.ElapsedMilliseconds} ms";
                _log.Ok($"Conectado a {nombre} {version} en {reloj.ElapsedMilliseconds} ms: {_herramientas.Count} herramientas.");

                if (_settings.Endpoint != endpoint)
                {
                    _settings.Endpoint = endpoint;
                    GuardarSettings();
                }
                CargarGrupos();
            }
            catch (Exception ex)
            {
                TxtEstadoServidor.Text = "No se pudo conectar: " + ex.Message;
                _log.Error($"No se pudo conectar: {ex.Message}");
                _log.Tenues("Revisá la conexión a internet y la dirección del servidor; después pulsá Conectar.");
            }
            finally
            {
                BtnConectar.IsEnabled = true;
            }
        }

        private void BtnReglas_Click(object sender, RoutedEventArgs e)
        {
            string? texto = _cliente?.Instrucciones;
            if (string.IsNullOrWhiteSpace(texto))
            {
                _log.Aviso("Todavía no hay conexión con el servidor: no se recibieron sus reglas de uso.");
                return;
            }
            var caja = new TextBox
            {
                Text = texto, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8),
            };
            new Window
            {
                Title = "Reglas de uso de bravo47", Content = caja, Owner = this, Icon = Icon,
                Width = 720, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            }.Show();
        }

        private void BtnSitio_Click(object sender, RoutedEventArgs e) => AbrirUrl("https://mcp.bravo47.com");

        // ===================== Formulario =====================

        private void CargarGrupos()
        {
            string? recordar = _herramientaActual?.Nombre ?? _settings.UltimaHerramienta;
            var grupos = Herramientas.OrdenGrupos.Where(g => _herramientas.Values.Any(h => h.Grupo == g)).ToList();
            CmbGrupo.ItemsSource = grupos;
            if (recordar != null && _herramientas.TryGetValue(recordar, out var h))
                SeleccionarHerramienta(h.Nombre, null);
            else if (grupos.Count > 0)
                CmbGrupo.SelectedIndex = 0;
        }

        private void CmbGrupo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbGrupo.SelectedItem is not string grupo) return;
            var lista = _herramientas.Values.Where(h => h.Grupo == grupo).ToList();
            CmbHerramienta.ItemsSource = lista;
            if (_herramientaActual == null || !lista.Contains(_herramientaActual))
                CmbHerramienta.SelectedIndex = lista.Count > 0 ? 0 : -1;
            else
                CmbHerramienta.SelectedItem = _herramientaActual;
        }

        private void CmbHerramienta_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbHerramienta.SelectedItem is not Herramienta h || h == _herramientaActual) return;
            _herramientaActual = h;
            ConstruirFormulario(h);
            _settings.UltimaHerramienta = h.Nombre;
        }

        // Selecciona la herramienta en los combos y, si vienen, carga esos
        // valores en los campos (los demás quedan con su valor por defecto).
        private void SeleccionarHerramienta(string nombre, JsonObject? valores)
        {
            if (!_herramientas.TryGetValue(nombre, out var h)) return;
            _herramientaActual = null;
            CmbGrupo.SelectedItem = h.Grupo;
            CmbGrupo_SelectionChanged(CmbGrupo, null!);
            CmbHerramienta.SelectedItem = h;
            if (_herramientaActual != h) { _herramientaActual = h; ConstruirFormulario(h); }
            if (valores != null) CargarValores(valores, "");
        }

        private void ConstruirFormulario(Herramienta h)
        {
            PanelCampos.Children.Clear();
            _controles.Clear();
            LblConsulta.Text = "Consulta · " + h.Titulo;
            TxtDescripcion.Text = h.Descripcion;

            if (!h.Campos.Any(c => c.ValorFijo == null))
                PanelCampos.Children.Add(new TextBlock
                {
                    Text = "Esta herramienta no necesita datos: pulsá Consultar.",
                    Foreground = (Brush)FindResource("TextDim"), TextWrapping = TextWrapping.Wrap,
                });

            foreach (var c in h.Campos)
            {
                if (c.ValorFijo != null) continue;

                FrameworkElement control;
                switch (c.Tipo)
                {
                    case TipoCampo.Booleano:
                        control = new CheckBox { Content = c.Etiqueta + (c.Requerido ? " *" : ""), IsChecked = c.PorDefecto?.GetValue<bool>() ?? false };
                        break;
                    case TipoCampo.Opciones:
                        var cmb = new ComboBox();
                        if (!c.Requerido || c.PorDefecto == null) cmb.Items.Add("");
                        foreach (var o in c.Opciones) cmb.Items.Add(o);
                        cmb.SelectedItem = c.PorDefecto?.ToString() ?? "";
                        control = cmb;
                        break;
                    case TipoCampo.TextoLargo:
                    case TipoCampo.Cuestiones:
                        control = new TextBox
                        {
                            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                            Height = c.Tipo == TipoCampo.Cuestiones ? 120 : 80,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        };
                        break;
                    default:
                        string inicial = c.PorDefecto is JsonValue v && v.ToString() != "" ? v.ToString() : "";
                        if (c.Sugerencias.Count > 0)
                        {
                            var ed = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, Text = inicial };
                            foreach (var s in c.Sugerencias) ed.Items.Add(s);
                            control = ed;
                        }
                        else
                        {
                            control = new TextBox { Text = inicial };
                            if (c.EsFecha) AgregarMenuFecha((TextBox)control);
                        }
                        break;
                }

                if (c.Ayuda != null) control.ToolTip = c.Ayuda;
                control.Margin = new Thickness(0, 0, 0, 8);

                if (c.Tipo != TipoCampo.Booleano)
                {
                    var etiqueta = new TextBlock
                    {
                        Text = c.Etiqueta + (c.Requerido ? " *" : "") + (c.EsFecha ? "  (AAAA-MM-DD)" : ""),
                        Foreground = c.Requerido ? (Brush)FindResource("TextMain") : (Brush)FindResource("TextDim"),
                        Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap,
                    };
                    if (c.Ayuda != null) etiqueta.ToolTip = c.Ayuda;
                    PanelCampos.Children.Add(etiqueta);
                }
                PanelCampos.Children.Add(control);
                _controles[c.Nombre] = (c, control);
            }

            BtnConsultar.IsEnabled = _cliente != null && !_consultando;
        }

        // Clic derecho en un campo de fecha: atajos para no tipear el formato.
        private static void AgregarMenuFecha(TextBox t)
        {
            var menu = new ContextMenu();
            void Opcion(string texto, Func<DateTime> fecha)
            {
                var mi = new MenuItem { Header = texto };
                mi.Click += (_, _) => t.Text = fecha().ToString("yyyy-MM-dd");
                menu.Items.Add(mi);
            }
            Opcion("Hoy", () => DateTime.Today);
            Opcion("Hace un mes", () => DateTime.Today.AddMonths(-1));
            Opcion("Hace un año", () => DateTime.Today.AddYears(-1));
            Opcion("1 de enero de este año", () => new DateTime(DateTime.Today.Year, 1, 1));
            t.ContextMenu = menu;
        }

        private void CargarValores(JsonObject valores, string prefijo)
        {
            foreach (var (k, v) in valores)
            {
                string ruta = prefijo + k;
                if (v is JsonObject sub && !_controles.ContainsKey(ruta)) { CargarValores(sub, ruta + "."); continue; }
                if (!_controles.TryGetValue(ruta, out var par)) continue;
                switch (par.control)
                {
                    case CheckBox cb: cb.IsChecked = v?.GetValue<bool>() ?? false; break;
                    case ComboBox { IsEditable: false } cmb: cmb.SelectedItem = v?.ToString() ?? ""; break;
                    case ComboBox cmb: cmb.Text = v?.ToString() ?? ""; break;
                    case TextBox tb when par.campo.Tipo == TipoCampo.Cuestiones: tb.Text = CuestionesATexto(v as JsonArray); break;
                    case TextBox tb: tb.Text = v?.ToString() ?? ""; break;
                }
            }
        }

        private void BtnLimpiar_Click(object sender, RoutedEventArgs e)
        {
            if (_herramientaActual != null) ConstruirFormulario(_herramientaActual);
        }

        private void BtnToggleForm_Click(object sender, RoutedEventArgs e)
        {
            ToggleSectorColumna(ColForm, FormContenido, BtnToggleForm, ref _colFormGuardado);
            LblConsulta.Visibility = FormContenido.Visibility;
        }

        // Arma los argumentos desde los campos. Devuelve null (y avisa en el
        // Log) si falta un dato obligatorio o uno no tiene el formato correcto.
        private JsonObject? ArmarArgumentos(Herramienta h)
        {
            var args = new JsonObject();
            var errores = new List<string>();

            foreach (var c in h.Campos)
            {
                JsonNode? valor;
                if (c.ValorFijo != null) valor = c.ValorFijo;
                else
                {
                    var control = _controles[c.Nombre].control;
                    string texto = control switch
                    {
                        ComboBox { IsEditable: false } cmb => cmb.SelectedItem?.ToString() ?? "",
                        ComboBox cmb => cmb.Text,
                        TextBox tb => tb.Text,
                        _ => "",
                    };
                    texto = texto.Trim();

                    if (control is CheckBox cb)
                    {
                        bool marcado = cb.IsChecked == true;
                        bool porDefecto = c.PorDefecto?.GetValue<bool>() ?? false;
                        if (marcado == porDefecto && !c.Requerido) continue;
                        valor = marcado;
                    }
                    else if (texto.Length == 0)
                    {
                        if (c.Requerido) errores.Add($"Falta «{c.Etiqueta}».");
                        continue;
                    }
                    else if (c.Tipo == TipoCampo.Entero)
                    {
                        if (!long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                        { errores.Add($"«{c.Etiqueta}» tiene que ser un número entero."); continue; }
                        if (c.PorDefecto is JsonValue d && d.ToString() == texto && !c.Requerido) continue;
                        valor = n;
                    }
                    else if (c.Tipo == TipoCampo.Cuestiones)
                    {
                        var (lista, error) = TextoACuestiones(texto);
                        if (error != null) { errores.Add(error); continue; }
                        valor = lista;
                    }
                    else if (c.EsFecha)
                    {
                        string? iso = NormalizarFecha(texto);
                        if (iso == null) { errores.Add($"«{c.Etiqueta}»: la fecha tiene que ser AAAA-MM-DD (o DD/MM/AAAA)."); continue; }
                        valor = iso;
                    }
                    else
                    {
                        if (c.PorDefecto is JsonValue d && d.ToString() == texto && !c.Requerido) continue;
                        valor = texto;
                    }
                }

                // "a.ref" va dentro de args["a"].
                var partes = c.Nombre.Split('.');
                JsonObject destino = args;
                for (int i = 0; i < partes.Length - 1; i++)
                {
                    if (destino[partes[i]] is not JsonObject sub) { sub = new JsonObject(); destino[partes[i]] = sub; }
                    destino = sub;
                }
                destino[partes[^1]] = valor;
            }

            if (h.Nombre == "comparar_textos")
                foreach (var lado in new[] { "a", "b" })
                    if (args[lado] is JsonObject o && o["fecha"] != null && o["documento"] != null)
                        errores.Add($"Texto {lado.ToUpper()}: indicá una fecha o una versión del texto, no las dos.");

            if (errores.Count == 0) return args;
            foreach (var e in errores) _log.Error(e);
            return null;
        }

        private static string? NormalizarFecha(string s)
        {
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                return f.ToString("yyyy-MM-dd");
            return Regex.IsMatch(s, @"^\d{4}$") ? s + "-01-01" : null;
        }

        // "etiqueta | query | voces | normas, separadas, por coma"
        private static (JsonArray? lista, string? error) TextoACuestiones(string texto)
        {
            var lista = new JsonArray();
            int n = 0;
            foreach (string linea in texto.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                n++;
                var p = linea.Split('|').Select(x => x.Trim()).ToArray();
                if (p.Length < 2 || p[0].Length == 0 || p[1].Length == 0)
                    return (null, $"Cuestión {n}: hacen falta al menos «etiqueta | texto a buscar».");
                var o = new JsonObject { ["etiqueta"] = p[0], ["query"] = p[1] };
                if (p.Length > 2 && p[2].Length > 0) o["voces"] = p[2];
                if (p.Length > 3 && p[3].Length > 0)
                    o["normas"] = new JsonArray(p[3].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
                                                     .Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
                lista.Add(o);
            }
            return n == 0 ? (null, "Hace falta al menos una cuestión.") : (lista, null);
        }

        private static string CuestionesATexto(JsonArray? a) =>
            a == null ? "" : string.Join("\n", a.OfType<JsonObject>().Select(o =>
            {
                string normas = o["normas"] is JsonArray n ? string.Join(", ", n.Select(x => x?.ToString())) : "";
                return $"{o["etiqueta"]} | {o["query"]} | {o["voces"]} | {normas}".TrimEnd(' ', '|');
            }));

        // ===================== Consultar =====================

        private async void BtnConsultar_Click(object sender, RoutedEventArgs e)
        {
            if (_herramientaActual == null) return;
            var args = ArmarArgumentos(_herramientaActual);
            if (args == null) return;
            await EjecutarAsync(new Llamada { Herramienta = _herramientaActual.Nombre, Argumentos = args });
        }

        private async Task EjecutarAsync(Llamada llamada, bool reemplazarActual = false)
        {
            if (_cliente == null) { _log.Aviso("Primero hay que conectarse con el servidor."); return; }
            if (_consultando) return;

            _consultando = true;
            BtnConsultar.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.AppStarting;
            string titulo = _herramientas.TryGetValue(llamada.Herramienta, out var h) ? h.Titulo : llamada.Herramienta;
            _log.Encabezado(titulo);
            foreach (var (k, v) in llamada.Argumentos)
                _log.Item(k, v is JsonValue ? v.ToString() : v?.ToJsonString() ?? "");
            var reloj = Stopwatch.StartNew();
            try
            {
                var (texto, esError) = await LlamarAsync(llamada.Herramienta, llamada.Argumentos);
                llamada.Respuesta = texto;
                llamada.EsError = esError;
                if (esError) _log.Error($"El servidor rechazó la consulta ({reloj.ElapsedMilliseconds} ms): {Resumir(texto, 400)}");
                else _log.Ok($"Respuesta en {reloj.ElapsedMilliseconds} ms · {ResumenRespuesta(texto)}");

                if (reemplazarActual && _historial.Count > 0) _historial[^1] = llamada;
                else _historial.Add(llamada);
                Renderizar(llamada);
            }
            catch (Exception ex)
            {
                _log.Error($"No se pudo completar la consulta: {ex.Message}");
            }
            finally
            {
                _log.Cierre();
                _consultando = false;
                BtnConsultar.IsEnabled = _herramientaActual != null;
                Mouse.OverrideCursor = null;
            }
        }

        // Las herramientas del Tribunal de Casación las resuelve la app contra
        // JUBA; el resto va al servidor.
        private Task<(string texto, bool esError)> LlamarAsync(string herramienta, JsonObject args, CancellationToken ct = default) =>
            JubaTcp.EsPropia(herramienta) ? JubaTcp.LlamarAsync(herramienta, args, ct) : _cliente!.LlamarAsync(herramienta, args, ct);

        private static string ResumenRespuesta(string texto)
        {
            try
            {
                var n = JsonNode.Parse(texto);
                if (n is JsonObject o)
                {
                    var lista = o.FirstOrDefault(kv => kv.Value is JsonArray a && a.Any(x => x is JsonObject));
                    if (lista.Value is JsonArray arr)
                        return $"{arr.Count} {lista.Key}" + (o["total"] is JsonNode t ? $" de {t}" : "");
                    if (o["resultado"] is JsonNode r) return r.ToString();
                }
            }
            catch { }
            return $"{texto.Length:N0} caracteres";
        }

        private void BtnAtras_Click(object sender, RoutedEventArgs e)
        {
            if (_historial.Count < 2) return;
            _historial.RemoveAt(_historial.Count - 1);
            var anterior = _historial[^1];
            SeleccionarHerramienta(anterior.Herramienta, anterior.Argumentos);
            Renderizar(anterior);
        }

        // Fragmento y página: repiten la llamada actual cambiando un argumento.
        private async Task RepetirConAsync(string argumento, long valor)
        {
            if (_historial.Count == 0) return;
            var actual = _historial[^1];
            var args = (JsonObject)actual.Argumentos.DeepClone();
            args[argumento] = valor;
            SeleccionarHerramienta(actual.Herramienta, args);
            await EjecutarAsync(new Llamada { Herramienta = actual.Herramienta, Argumentos = args }, reemplazarActual: true);
        }

        private async void BtnFragAnt_Click(object sender, RoutedEventArgs e) => await RepetirConAsync("fragmento", _fragmento - 1);
        private async void BtnFragSig_Click(object sender, RoutedEventArgs e) => await RepetirConAsync("fragmento", _fragmento + 1);
        private async void BtnPagAnt_Click(object sender, RoutedEventArgs e) => await RepetirConAsync("offset", Math.Max(0, _offset - _limite));
        private async void BtnPagSig_Click(object sender, RoutedEventArgs e) => await RepetirConAsync("offset", _offset + _limite);

        private async void BtnVerificarSeleccion_Click(object sender, RoutedEventArgs e)
        {
            string texto = _seleccion.Trim();
            if (texto.Length == 0)
            {
                _log.Aviso("Seleccioná con el mouse un pasaje dentro de un resultado y volvé a pulsar el botón.");
                return;
            }
            if (!_herramientas.ContainsKey("verificar_cita")) return;
            var args = new JsonObject { ["texto"] = texto };
            if (_seleccionRef != null) args["ref"] = _seleccionRef;
            if (_seleccionArticulo != null) args["articulo"] = _seleccionArticulo;
            SeleccionarHerramienta("verificar_cita", args);
            await EjecutarAsync(new Llamada { Herramienta = "verificar_cita", Argumentos = args });
        }

        private void TglJson_Click(object sender, RoutedEventArgs e)
        {
            bool json = TglJson.IsChecked == true;
            TxtJson.Visibility = json ? Visibility.Visible : Visibility.Collapsed;
            ScrollResultados.Visibility = json ? Visibility.Collapsed : Visibility.Visible;
        }

        // ===================== Rendimiento (semáforo RAM/CPU/GPU) =====================

        private void SetupPerfTimer()
        {
            _perfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _perfTimer.Tick += PerfTimer_Tick;
            _perfTimer.Start();
        }

        // Mide el árbol de procesos de esta misma aplicación (WebView o
        // procesos auxiliares de .NET incluidos, si los hubiera).
        private void PerfTimer_Tick(object? sender, EventArgs e)
        {
            List<int> pids;
            try
            {
                pids = GetProcessTreePids(Environment.ProcessId);
            }
            catch (ManagementException)
            {
                // WMI puede fallar transitoriamente; se reintenta en el próximo tick.
                return;
            }

            double totalMb = 0;
            TimeSpan totalCpu = TimeSpan.Zero;
            foreach (int pid in pids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.Refresh();
                    totalMb += p.WorkingSet64 / 1024.0 / 1024.0;
                    totalCpu += p.TotalProcessorTime;
                }
                catch { }
            }

            double ramPct = Math.Min(totalMb / RamCapMb * 100.0, 100.0);
            CircleRam.SetCircle(ramPct, $"RAM {totalMb:F0} MB");

            DateTime now = DateTime.UtcNow;
            if (_lastSampleTime != DateTime.MinValue)
            {
                double elapsedMs = (now - _lastSampleTime).TotalMilliseconds;
                double cpuMs = (totalCpu - _lastCpuTime).TotalMilliseconds;
                // 100% = mitad de los núcleos lógicos saturados (mismo
                // criterio que los otros consultores).
                double cpuCapCores = Math.Max(1.0, Environment.ProcessorCount / 2.0);
                double raw = elapsedMs > 0 ? cpuMs / elapsedMs * 100.0 / cpuCapCores : 0.0;
                CircleCpu.SetCircle(Math.Clamp(raw, 0.0, 100.0), $"CPU {raw:F1}%");
            }
            _lastCpuTime = totalCpu;
            _lastSampleTime = now;

            try
            {
                double gpu = GetGpuPercent(pids);
                CircleGpu.SetCircle(Math.Clamp(gpu, 0.0, 100.0), $"GPU {gpu:F1}%");
            }
            catch
            {
                CircleGpu.SetCircle(0, "GPU --");
            }
        }

        // Recorre Win32_Process una sola vez y arma el árbol para encontrar todos
        // los descendientes de rootPid (incluido él mismo).
        private static List<int> GetProcessTreePids(int rootPid)
        {
            var childrenByParent = new Dictionary<int, List<int>>();
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    int pid = Convert.ToInt32(mo["ProcessId"]);
                    int ppid = Convert.ToInt32(mo["ParentProcessId"]);
                    if (!childrenByParent.TryGetValue(ppid, out var list))
                    {
                        list = new List<int>();
                        childrenByParent[ppid] = list;
                    }
                    list.Add(pid);
                }
            }

            var pids = new List<int> { rootPid };
            var queue = new Queue<int>();
            queue.Enqueue(rootPid);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!childrenByParent.TryGetValue(current, out var kids)) continue;
                foreach (int kid in kids)
                {
                    if (pids.Contains(kid)) continue;
                    pids.Add(kid);
                    queue.Enqueue(kid);
                }
            }
            return pids;
        }

        // Win32_PerfRawData_GPUPerformanceCounters_GPUEngine expone el uso de GPU
        // por proceso; el campo "Name" tiene formato pid_XXXX_luid_..._engtype_3D.
        private double GetGpuPercent(List<int> pids)
        {
            ulong sumRunningTime = 0;
            ulong sumTimeStamp = 0;
            int count = 0;

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, RunningTime, TimeStamp_Sys100NS FROM Win32_PerfRawData_GPUPerformanceCounters_GPUEngine");

            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "";
                if (!pids.Contains(ExtractPidFromName(name))) continue;

                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase))
                    continue;

                sumRunningTime += Convert.ToUInt64(obj["RunningTime"]);
                sumTimeStamp = Math.Max(sumTimeStamp, Convert.ToUInt64(obj["TimeStamp_Sys100NS"]));
                count++;
            }

            if (count == 0) return 0;

            double gpuPercent = 0;
            if (_lastGpuTimeStamp > 0 && sumTimeStamp > _lastGpuTimeStamp)
            {
                double deltaRunning = sumRunningTime - _lastGpuRaw;
                double deltaTimestamp = sumTimeStamp - _lastGpuTimeStamp;
                gpuPercent = Math.Clamp(deltaRunning / deltaTimestamp * 100.0, 0.0, 100.0);
            }
            _lastGpuRaw = sumRunningTime;
            _lastGpuTimeStamp = sumTimeStamp;
            return gpuPercent;
        }

        private static int ExtractPidFromName(string name)
        {
            const string prefix = "pid_";
            int start = name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return -1;
            start += prefix.Length;
            int end = name.IndexOf('_', start);
            if (end < 0) end = name.Length;
            return int.TryParse(name[start..end], out int pid) ? pid : -1;
        }

        // ===================== Paneles colapsables =====================

        private void BtnToggleLog_Click(object sender, RoutedEventArgs e) =>
            ToggleSectorColumna(ColLog, TxtLog, BtnToggleLog, ref _colLogGuardado);

        // Al colapsar se recuerda el ancho de la columna para restaurarlo.
        private static void ToggleSectorColumna(ColumnDefinition columna, FrameworkElement contenido, Button boton, ref GridLength? guardado)
        {
            bool colapsar = contenido.Visibility == Visibility.Visible;
            if (colapsar)
            {
                guardado = columna.Width;
                contenido.Visibility = Visibility.Collapsed;
                columna.Width = GridLength.Auto;
                boton.Content = "▶";
            }
            else
            {
                contenido.Visibility = Visibility.Visible;
                columna.Width = guardado ?? new GridLength(1, GridUnitType.Star);
                boton.Content = "▼";
            }
        }

        private void BtnLimpiarLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Document.Blocks.Clear();
            _log.Banner(VersionApp);
        }

        // ===================== Log desacoplado (otra ventana / otro monitor) =====================

        // El mismo TxtLog se mueve de PanelLog a una ventana aparte y vuelve al
        // cerrarla: la consola sigue escribiendo en él y no se pierde lo escrito.

        private void BtnDesacoplarLog_Click(object sender, RoutedEventArgs e) => DesacoplarLog();

        private void DesacoplarLog()
        {
            if (_ventanaLog != null) { _ventanaLog.Activate(); return; }

            if (TxtLog.Visibility != Visibility.Visible)
                ToggleSectorColumna(ColLog, TxtLog, BtnToggleLog, ref _colLogGuardado);

            _colLogAntesDeDesacoplar = ColLog.Width;
            PanelLog.Children.Remove(TxtLog);
            BorderLog.Visibility = Visibility.Collapsed;
            SplitterLog.Visibility = Visibility.Collapsed;
            ColSplitterLog.Width = new GridLength(0);
            ColLog.Width = new GridLength(0);

            var btnAcoplar = new Button
            {
                Content = "Acoplar",
                FontSize = 11,
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Devolver el Log a la ventana principal",
            };
            var barra = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 4) };
            var titulo = new TextBlock { Text = "Log", Style = (Style)FindResource("SectionHeader") };
            DockPanel.SetDock(titulo, Dock.Left);
            DockPanel.SetDock(btnAcoplar, Dock.Right);
            barra.Children.Add(titulo);
            barra.Children.Add(btnAcoplar);

            var panel = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(barra, Dock.Top);
            panel.Children.Add(barra);
            panel.Children.Add(TxtLog);

            var ventana = new Window
            {
                Title = "Log — " + Title,
                Content = panel,
                Background = new SolidColorBrush(Consola.Fondo),
                Owner = this,
                Icon = Icon,
                Width = 640,
                Height = 760,
                MinWidth = 300,
                MinHeight = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            UbicarVentanaLog(ventana, _settings.VentanaLog);

            btnAcoplar.Click += (_, _) => ventana.Close();
            ventana.Closing += (_, _) =>
            {
                // Al cerrar la app las ventanas propias se cierran solas: ahí
                // queda registrado que estaba desacoplado (Window_Closing).
                if (_cerrandoApp) return;
                _settings.VentanaLog = PosicionDe(ventana);
                _settings.LogDesacoplado = false;
                GuardarSettings();
            };
            ventana.Closed += (_, _) => AcoplarLog(panel);

            _ventanaLog = ventana;
            _settings.LogDesacoplado = true;
            GuardarSettings();
            ventana.Show();
            if (_settings.VentanaLog?.Maximizada == true) ventana.WindowState = WindowState.Maximized;
            TxtLog.ScrollToEnd();
        }

        private void AcoplarLog(DockPanel panelVentana)
        {
            panelVentana.Children.Remove(TxtLog);
            PanelLog.Children.Add(TxtLog);
            BorderLog.Visibility = Visibility.Visible;
            SplitterLog.Visibility = Visibility.Visible;
            ColSplitterLog.Width = new GridLength(6);
            ColLog.Width = _colLogAntesDeDesacoplar;
            _ventanaLog = null;
            TxtLog.ScrollToEnd();
        }

        // Tamaño inicial: el del XAML si entra en el área de trabajo del monitor
        // donde abre (el del cursor); si no, el 90 % de esa área, centrada.
        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            var fuente = PresentationSource.FromVisual(this);
            if (fuente?.CompositionTarget == null) return;
            var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            Point arribaIzq = fuente.CompositionTarget.TransformFromDevice.Transform(new Point(area.Left, area.Top));
            Point abajoDer = fuente.CompositionTarget.TransformFromDevice.Transform(new Point(area.Right, area.Bottom));
            var trabajo = new Rect(arribaIzq, abajoDer);

            MinWidth = Math.Min(MinWidth, trabajo.Width);
            MinHeight = Math.Min(MinHeight, trabajo.Height);
            Width = Math.Max(MinWidth, Math.Min(Width, trabajo.Width * 0.9));
            Height = Math.Max(MinHeight, Math.Min(Height, trabajo.Height * 0.9));
            Left = trabajo.Left + (trabajo.Width - Width) / 2;
            Top = trabajo.Top + (trabajo.Height - Height) / 2;
        }

        private static PosicionVentana PosicionDe(Window w)
        {
            Rect r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds;
            return new PosicionVentana
            {
                Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height,
                Maximizada = w.WindowState == WindowState.Maximized,
            };
        }

        // Sólo se usa la posición guardada si cae dentro del escritorio actual.
        private static void UbicarVentanaLog(Window ventana, PosicionVentana? pos)
        {
            if (pos == null || pos.Width < 200 || pos.Height < 150) return;
            var escritorio = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var barraTitulo = new Rect(pos.Left, pos.Top, pos.Width, 40);
            barraTitulo.Intersect(escritorio);
            if (barraTitulo.IsEmpty || barraTitulo.Width < 100 || barraTitulo.Height < 20) return;

            ventana.WindowStartupLocation = WindowStartupLocation.Manual;
            ventana.Left = pos.Left;
            ventana.Top = pos.Top;
            ventana.Width = pos.Width;
            ventana.Height = pos.Height;
        }

        // ===================== Utilidades =====================

        private void CopiarAlPortapapeles(string texto)
        {
            try { Clipboard.SetText(texto); }
            catch (Exception ex) { _log.Aviso($"No se pudo copiar al portapapeles: {ex.Message}"); }
        }

        private void AbrirUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { _log.Error($"No se pudo abrir {url}: {ex.Message}"); }
        }

        private static string Resumir(string s, int max) => s.Length > max ? s[..max] + "…" : s;

        // ===================== Cierre =====================

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _cerrandoApp = true;
            if (_ventanaLog != null)
            {
                _settings.VentanaLog = PosicionDe(_ventanaLog);
                _settings.LogDesacoplado = true;
            }
            _settings.FormatoExportacion = RbDocx.IsChecked == true ? "docx" : "txt";
            GuardarSettings();
            _perfTimer?.Stop();
        }
    }
}
