using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MonederoArcade
{
    /// <summary>Aspecto visual del overlay, leído de la sección [general] de config.ini.</summary>
    public class EstiloOverlay
    {
        public string Fuente { get; set; } = "PressStart2P.ttf";
        public string Aspecto { get; set; } = "auto";          // auto | 4:3 | real
        public string Linea1 { get; set; } = "INSERT COIN";
        public string Linea2 { get; set; } = "TO CONTINUE";
        public string GameOver1 { get; set; } = "GAME OVER";
        public string GameOver2 { get; set; } = "RETURNING TO MENU";
        public string Sonido { get; set; } = "coin-sound.mp3";
        public int Volumen { get; set; } = 80;                 // 0-100

        public static EstiloOverlay Cargar(string iniPath)
        {
            var g = new IniFile(iniPath);
            var d = new EstiloOverlay();
            return new EstiloOverlay
            {
                Fuente = g.GetString("general", "fuente", d.Fuente),
                Aspecto = g.GetString("general", "aspecto_pantalla", d.Aspecto).Trim().ToLowerInvariant(),
                Linea1 = g.GetString("general", "texto_linea1", d.Linea1),
                Linea2 = g.GetString("general", "texto_linea2", d.Linea2),
                GameOver1 = g.GetString("general", "texto_gameover1", d.GameOver1),
                GameOver2 = g.GetString("general", "texto_gameover2", d.GameOver2),
                Sonido = g.GetString("general", "sonido_credito", d.Sonido),
                Volumen = Math.Clamp(g.GetInt("general", "volumen_sonido", d.Volumen), 0, 100),
            };
        }
    }

    /// <summary>
    /// Dibuja el overlay sobre un canvas virtual pensado para CRT.
    /// - Alto virtual: el alto del modo dividido por un factor entero (224, 240, 256; 448 -> 224 x2,
    ///   480 -> 240 x2, 768 -> 256 x3), así cada línea del canvas cae en líneas reales enteras.
    /// - Ancho lógico: el que tendría ese alto con píxeles cuadrados en 4:3 (ej. 240 -> 320).
    /// - En CRT el texto se rasteriza directo al ancho real del modo (2560, 1280, 256...) con una
    ///   escala horizontal, en vez de estirar un bitmap: no se deforma ni pierde nitidez.
    /// </summary>
    public static class OverlayRenderer
    {
        private static readonly Dictionary<string, PrivateFontCollection> _fuentes = new(StringComparer.OrdinalIgnoreCase);

        public static FontFamily Familia(string baseDir, string archivo)
        {
            try
            {
                var path = Path.IsPathRooted(archivo) ? archivo : Path.Combine(baseDir, archivo);
                if (!File.Exists(path)) return new FontFamily("Consolas");
                if (!_fuentes.TryGetValue(path, out var pfc))
                {
                    pfc = new PrivateFontCollection();
                    pfc.AddFontFile(path);
                    _fuentes[path] = pfc;
                }
                return pfc.Families[0];
            }
            catch { return new FontFamily("Consolas"); }
        }

        /// <summary>
        /// true = la pantalla física es un CRT 4:3 (cualquier modo, incluidas súper resoluciones).
        /// En "auto" solo se considera monitor moderno (píxeles cuadrados) un modo de 720+ líneas
        /// con proporción de monitor común (5:4 a 16:9).
        /// </summary>
        public static bool EsCrt(Size modo, string aspecto)
        {
            if (aspecto == "4:3") return true;
            if (aspecto == "real") return false;
            double r = (double)modo.Width / modo.Height;
            bool monitorModerno = modo.Height >= 720 && r >= 1.2 && r <= 1.85;
            return !monitorModerno;
        }

        public static void Dibujar(Graphics destino, Rectangle rectDestino, Size modo, EstiloOverlay estilo,
                                   string baseDir, string countdown, bool gameOver)
        {
            if (modo.Width <= 0 || modo.Height <= 0) return;

            int k = Math.Max(1, (int)Math.Round(modo.Height / 240.0));
            int vh = (int)Math.Ceiling(modo.Height / (double)k);
            bool crt = EsCrt(modo, estilo.Aspecto);
            int logico = crt ? (int)Math.Round(vh * 4.0 / 3.0)
                             : (int)Math.Round(vh * (double)modo.Width / modo.Height);
            int anchoBmp = crt ? modo.Width : logico;

            using var bmp = new Bitmap(anchoBmp, vh);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.ScaleTransform(anchoBmp / (float)logico, 1f);
                var fam = Familia(baseDir, estilo.Fuente);

                if (gameOver)
                {
                    Linea(g, fam, estilo.GameOver1, 24, Brushes.Yellow, logico, vh * 0.36f);
                    Linea(g, fam, estilo.GameOver2, 8, Brushes.White, logico, vh * 0.58f);
                }
                else
                {
                    Linea(g, fam, estilo.Linea1, 16, Brushes.White, logico, vh * 0.14f);
                    Linea(g, fam, estilo.Linea2, 8, Brushes.White, logico, vh * 0.27f);
                    Linea(g, fam, countdown, 64, Brushes.Yellow, logico, vh * 0.62f);
                }
            }

            destino.InterpolationMode = InterpolationMode.NearestNeighbor;
            destino.PixelOffsetMode = PixelOffsetMode.Half;
            destino.DrawImage(bmp, rectDestino);
        }

        /// <summary>
        /// Texto centrado en (ancho/2, centroY). Los tamaños son múltiplos de 8 (grilla de las
        /// fuentes pixel tipo Press Start 2P); si no entra a lo ancho, baja de a 8.
        /// </summary>
        private static void Linea(Graphics g, FontFamily fam, string texto, int tam, Brush color, int ancho, float centroY)
        {
            if (string.IsNullOrEmpty(texto)) return;
            Font? f = null;
            try
            {
                while (true)
                {
                    f?.Dispose();
                    f = new Font(fam, tam, FontStyle.Regular, GraphicsUnit.Pixel);
                    var med = g.MeasureString(texto, f, PointF.Empty, StringFormat.GenericTypographic);
                    if (med.Width <= ancho - 8 || tam <= 8) break;
                    tam -= 8;
                }
                var fmt = new StringFormat(StringFormat.GenericTypographic)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(texto, f, color, new PointF(ancho / 2f, (float)Math.Round(centroY)), fmt);
            }
            finally { f?.Dispose(); }
        }
    }
}
