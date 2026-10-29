using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace ConsultorLegal
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _copia;
        public AppSettings? Result { get; private set; }

        public SettingsWindow(AppSettings actual)
        {
            InitializeComponent();
            TxtVersion.Text = $"Versión instalada: {Actualizador.VersionActual}";
            // Se trabaja sobre una copia: Cancelar no toca la configuración viva.
            _copia = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(actual)) ?? new AppSettings();

            RbTemaClaro.IsChecked = _copia.Tema == "Claro";
            RbTemaTextura.IsChecked = _copia.Tema == "Textura";
            RbTemaOscuro.IsChecked = _copia.Tema is not ("Claro" or "Textura");
            TxtRutaTextura.Text = _copia.RutaTextura ?? "";
            RbDocx.IsChecked = _copia.FormatoExportacion == "docx";
            RbTxt.IsChecked = RbDocx.IsChecked != true;
            TxtCarpeta.Text = _copia.CarpetaExportacionDefault ?? "";
            SldFuente.Value = _copia.TamanoFuenteResultados;
            TxtFuente.Text = SldFuente.Value.ToString("0");
            ActualizarTextura();
        }

        private void Tema_Changed(object sender, RoutedEventArgs e) => ActualizarTextura();

        private void ActualizarTextura()
        {
            if (BtnExaminarTextura == null) return;
            bool textura = RbTemaTextura.IsChecked == true;
            BtnExaminarTextura.IsEnabled = textura;
            TxtRutaTextura.IsEnabled = textura;
        }

        private void BtnExaminarTextura_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Elegir imagen de textura",
                Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            };
            if (File.Exists(TxtRutaTextura.Text)) dlg.InitialDirectory = Path.GetDirectoryName(TxtRutaTextura.Text);
            if (dlg.ShowDialog(this) == true) TxtRutaTextura.Text = dlg.FileName;
        }

        private void BtnExaminarCarpeta_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Carpeta para exportar resultados" };
            if (Directory.Exists(TxtCarpeta.Text)) dlg.SelectedPath = TxtCarpeta.Text;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) TxtCarpeta.Text = dlg.SelectedPath;
        }

        private void SldFuente_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtFuente != null) TxtFuente.Text = e.NewValue.ToString("0");
        }

        private async void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            BtnActualizar.IsEnabled = false;
            TxtEstadoActualizacion.Visibility = Visibility.Visible;
            var progreso = new Progress<string>(s => TxtEstadoActualizacion.Text = s);
            try
            {
                TxtEstadoActualizacion.Text = "Consultando GitHub...";
                var r = await Actualizador.ConsultarUltimaAsync();
                if (r.Version <= Actualizador.VersionActual)
                {
                    TxtEstadoActualizacion.Text = $"Ya tenés la última versión ({Actualizador.VersionActual}).";
                    return;
                }

                if (!Actualizador.EsEjecutablePublicado)
                {
                    TxtEstadoActualizacion.Text = $"Hay una versión nueva ({r.Version}), pero esta copia corre desde la compilación de desarrollo: " +
                                                  $"la actualización automática es sólo para el CONSULTOR-LEGAL.exe publicado. Descarga: {r.UrlPagina}";
                    return;
                }

                string notas = string.IsNullOrWhiteSpace(r.Notas) ? "" : $"\n\n{r.Notas.Trim()}";
                var ok = MessageBox.Show(this,
                    $"Hay una versión nueva: {r.Version} (instalada: {Actualizador.VersionActual}).{notas}\n\n" +
                    "¿Descargarla e instalarla? CONSULTOR-LEGAL se va a reiniciar; la configuración no se toca.",
                    "Actualizar CONSULTOR-LEGAL", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ok != MessageBoxResult.Yes)
                {
                    TxtEstadoActualizacion.Text = $"Versión {r.Version} disponible (no instalada).";
                    return;
                }

                string exeNuevo = await Actualizador.DescargarYVerificarAsync(r, progreso);
                TxtEstadoActualizacion.Text = "Instalando...";
                string exeActual = Actualizador.ExeActual;
                Actualizador.Instalar(exeNuevo, exeActual);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exeActual)
                {
                    WorkingDirectory = Path.GetDirectoryName(exeActual)!,
                    UseShellExecute = false,
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                TxtEstadoActualizacion.Text = $"No se pudo actualizar: {ex.Message}";
            }
            finally
            {
                BtnActualizar.IsEnabled = true;
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (RbTemaTextura.IsChecked == true && !File.Exists(TxtRutaTextura.Text))
            {
                MessageBox.Show(this, "Elegí una imagen para el fondo con textura.", "Configuración",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _copia.Tema = RbTemaClaro.IsChecked == true ? "Claro" : RbTemaTextura.IsChecked == true ? "Textura" : "Oscuro";
            _copia.RutaTextura = TxtRutaTextura.Text.Length > 0 ? TxtRutaTextura.Text : null;
            _copia.FormatoExportacion = RbDocx.IsChecked == true ? "docx" : "txt";
            _copia.CarpetaExportacionDefault = TxtCarpeta.Text.Trim().Length > 0 ? TxtCarpeta.Text.Trim() : null;
            _copia.TamanoFuenteResultados = SldFuente.Value;
            Result = _copia;
            DialogResult = true;
        }
    }
}
