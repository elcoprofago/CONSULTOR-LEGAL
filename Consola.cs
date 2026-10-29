using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ConsultorLegal
{
    // Consola de texto enriquecido con la paleta y los marcadores del
    // Actualizador Warlords (Terminal.cs): [*] info, [+] ok, [!] aviso,
    // [-] error, títulos ━━━ y encabezados ┌─[ ]. Cada tramo lleva su color.
    public class Consola
    {
        public static readonly Color Fondo = Rgb(0x0E1016);
        public static readonly Color Texto = Rgb(0xD8DEE9);
        public static readonly Color Tenue = Rgb(0x6B7289);
        public static readonly Color Rojo = Rgb(0xFF5555);
        public static readonly Color Verde = Rgb(0x50FA7B);
        public static readonly Color Amarillo = Rgb(0xF1FA8C);
        public static readonly Color Naranja = Rgb(0xFFB86C);
        public static readonly Color Azul = Rgb(0x6CB6FF);
        public static readonly Color Cian = Rgb(0x8BE9FD);
        public static readonly Color Magenta = Rgb(0xFF79C6);
        public static readonly Color Violeta = Rgb(0xBD93F9);
        public static readonly Color FondoAviso = Rgb(0x4A141A);

        public record Tramo(string Texto, Color Color, bool Negrita = false, Color? FondoTramo = null);

        private readonly RichTextBox _rtb;
        public bool AutoScroll { get; set; } = true;

        public Consola(RichTextBox rtb)
        {
            _rtb = rtb;
            _rtb.Background = new SolidColorBrush(Fondo);
            _rtb.Foreground = new SolidColorBrush(Texto);
            _rtb.Document.Blocks.Clear();
            _rtb.Document.PagePadding = new Thickness(4);
        }

        public void Info(string t) => Marcado("[*]", Azul, t, Texto);
        public void Ok(string t) => Marcado("[+]", Verde, t, Verde);
        public void Aviso(string t) => Marcado("[!]", Amarillo, t, Amarillo);
        public void Error(string t) => LineaConHora(Hora(), new Tramo("  [-] ", Rojo, true), new Tramo(t, Rojo, false, FondoAviso));
        public void Tenues(string t) => LineaConHora(Hora(), new Tramo("      " + t, Tenue));

        public void Titulo(string t)
        {
            string s = $"  ━━━ {t.ToUpperInvariant()} ";
            Linea(new Tramo(s + new string('━', Math.Max(4, 62 - s.Length)), Magenta, true));
        }

        public void Encabezado(string t)
        {
            Linea(new Tramo("  ┌─", Tenue), new Tramo("[ ", Tenue), new Tramo(t, Amarillo, true), new Tramo(" ]", Tenue));
            Linea(new Tramo("  │", Tenue));
        }

        public void Item(string clave, string valor, Color? color = null) =>
            Linea(new Tramo("  │ ", Tenue), new Tramo(clave + ": ", Tenue), new Tramo(valor, color ?? Cian));

        public void Cierre() => Linea(new Tramo("  └" + new string('─', 60), Tenue));

        // Letras de bloque con degradé "fuego" (FF4D4D → FFBA30), como el
        // banner del Actualizador.
        public void Banner(string version)
        {
            string[] arte =
            {
                "  ██╗     ███████╗ ██████╗  █████╗ ██╗     ",
                "  ██║     ██╔════╝██╔════╝ ██╔══██╗██║     ",
                "  ██║     █████╗  ██║  ███╗███████║██║     ",
                "  ██║     ██╔══╝  ██║   ██║██╔══██║██║     ",
                "  ███████╗███████╗╚██████╔╝██║  ██║███████╗",
                "  ╚══════╝╚══════╝ ╚═════╝ ╚═╝  ╚═╝╚══════╝",
            };
            Color a = Rgb(0xFF4D4D), b = Rgb(0xFFBA30);
            for (int i = 0; i < arte.Length; i++)
            {
                double f = (double)i / (arte.Length - 1);
                var c = Color.FromRgb((byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
                Linea(new Tramo(arte[i], c, true));
            }
            Linea(new Tramo("  CONSULTOR-LEGAL ", Naranja, true), new Tramo("v" + version, Cian));
            Linea(new Tramo("  consultas avanzadas a bravo47 (MCP)", Tenue));
            Linea(new Tramo("", Texto));
        }

        public void Linea(params Tramo[] tramos)
        {
            if (!_rtb.Dispatcher.CheckAccess())
            {
                _rtb.Dispatcher.BeginInvoke(new Action(() => Linea(tramos)));
                return;
            }
            var p = new Paragraph { Margin = new Thickness(0), LineHeight = 15 };
            foreach (var t in tramos)
            {
                var run = new Run(t.Texto) { Foreground = new SolidColorBrush(t.Color) };
                if (t.Negrita) run.FontWeight = FontWeights.Bold;
                if (t.FondoTramo is Color bg) run.Background = new SolidColorBrush(bg);
                p.Inlines.Add(run);
            }
            _rtb.Document.Blocks.Add(p);
            if (AutoScroll) _rtb.ScrollToEnd();
        }

        public string TextoPlano() =>
            new TextRange(_rtb.Document.ContentStart, _rtb.Document.ContentEnd).Text;

        private void Marcado(string marca, Color colorMarca, string t, Color colorTexto) =>
            LineaConHora(Hora(), new Tramo($"  {marca} ", colorMarca, true), new Tramo(t, colorTexto));

        private static Tramo Hora() => new($"{DateTime.Now:HH:mm:ss}", Tenue);

        private static Color Rgb(int v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);

        private void LineaConHora(Tramo hora, params Tramo[] resto) => Linea(new[] { hora }.Concat(resto).ToArray());
    }
}
