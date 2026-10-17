using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonederoArcade
{
    /// <summary>
    /// Modo silencioso (free play limitado): timer chico abajo a la derecha. No pausa, no toma el foco y no
    /// recibe clics: el juego sigue normal. Se dibuja en un lienzo con el aspecto que se va a VER en el tubo
    /// (4:3 aunque el modo sea 2560x240) y se estira al tamaño real, así no sale deformado.
    /// </summary>
    public class TimerForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        private const double AnchoRel = 0.30, AltoRel = 0.16, Margen = 0.03;

        private readonly string _baseDir;
        private readonly System.Windows.Forms.Timer _topmost = new() { Interval = 250 };
        private EstiloOverlay _estilo = new();
        private Size _pantalla;
        private int _segundos;

        public TimerForm(string baseDir)
        {
            _baseDir = baseDir;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Opacity = 0.88;
            _topmost.Tick += (_, _) =>
            {
                if (!IsHandleCreated || !Visible) return;
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                if (_segundos <= 10) Invalidate();   // parpadeo de los últimos segundos
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/ | 0x20 /*TRANSPARENT: sin clics*/;
                return cp;
            }
        }

        public void Mostrar(Rectangle pantalla, int segundos)
        {
            _estilo = EstiloOverlay.Cargar(Path.Combine(_baseDir, "config.ini"));
            _pantalla = pantalla.Size;
            _segundos = segundos;
            int w = (int)(pantalla.Width * AnchoRel), h = (int)(pantalla.Height * AltoRel);
            Bounds = new Rectangle(pantalla.Right - w - (int)(pantalla.Width * Margen), pantalla.Bottom - h - (int)(pantalla.Height * Margen), w, h);
            if (!Visible) Show();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _topmost.Start();
            Invalidate();
            Logger.Log($"Timer silencioso {w}x{h} en {pantalla.Width}x{pantalla.Height} ({segundos}s)");
        }

        public void Actualizar(int segundos)
        {
            if (InvokeRequired) { BeginInvoke(() => Actualizar(segundos)); return; }
            _segundos = segundos;
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) _topmost.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            // Aspecto con que se ve la caja: en un CRT 4:3 cualquier modo llena el tubo, así que la caja se ve
            // (AnchoRel*4) : (AltoRel*3); en un monitor moderno, con píxeles cuadrados.
            bool crt = OverlayRenderer.EsCrt(_pantalla, _estilo.Aspecto);
            double aspecto = crt ? AnchoRel * 4 / (AltoRel * 3) : (double)Width / Height;
            int vh = 120, vw = (int)Math.Round(vh * aspecto);
            using var lienzo = new Bitmap(vw, vh);
            using (var g = Graphics.FromImage(lienzo))
            {
                g.Clear(Color.Black);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                var fam = OverlayRenderer.Familia(_baseDir, _estilo.Fuente);
                var urgente = _segundos <= 10;
                var parpadeo = urgente && DateTime.Now.Millisecond < 500;
                using var borde = new Pen(urgente ? Color.FromArgb(230, 40, 40) : Color.FromArgb(255, 210, 40), 4);
                g.DrawRectangle(borde, 2, 2, vw - 5, vh - 5);
                var centro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var f1 = new Font(fam, 15, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var amarillo = new SolidBrush(Color.FromArgb(255, 210, 40)))
                    g.DrawString(_estilo.Linea1, f1, amarillo, new RectangleF(0, 8, vw, 34), centro);
                var t = $"{_segundos / 60}:{_segundos % 60:00}";
                using (var f2 = new Font(fam, 44, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var color = new SolidBrush(urgente ? (parpadeo ? Color.White : Color.FromArgb(240, 50, 50)) : Color.White))
                    g.DrawString(t, f2, color, new RectangleF(0, 40, vw, 76), centro);
            }
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(lienzo, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _topmost.Dispose();
            base.Dispose(disposing);
        }
    }
}
