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
