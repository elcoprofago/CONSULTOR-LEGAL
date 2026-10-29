namespace ConsultorLegal
{
    public class AppSettings
    {
        public string Endpoint { get; set; } = McpClient.EndpointPorDefecto;
        public string Tema { get; set; } = "Oscuro";            // Claro | Oscuro | Textura
        public string? RutaTextura { get; set; }
        public string FormatoExportacion { get; set; } = "txt";  // txt | docx
        public string? CarpetaExportacionDefault { get; set; }
        public double TamanoFuenteResultados { get; set; } = 13;
        public string? UltimaHerramienta { get; set; }
        public bool LogDesacoplado { get; set; }
        public PosicionVentana? VentanaLog { get; set; }
    }

    public class PosicionVentana
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximizada { get; set; }
    }

    // Una llamada ya hecha: sirve para "Atrás", para pedir el fragmento o la
    // página siguiente (misma herramienta, un argumento cambiado) y para
    // exportar lo que se está viendo.
    public class Llamada
    {
        public string Herramienta { get; set; } = "";
        public System.Text.Json.Nodes.JsonObject Argumentos { get; set; } = new();
        public string Respuesta { get; set; } = "";
        public bool EsError { get; set; }
    }
}
