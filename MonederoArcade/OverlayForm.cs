using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonederoArcade
{
    /// <summary>
    /// Ventana del overlay. El dibujo lo hace OverlayRenderer (canvas virtual CRT); acá solo se maneja
    /// la ventana: sin bordes, sin activar, y re-afirmada como topmost cada 250 ms mientras se ve,
    /// porque algunos emuladores (RetroArch con OpenGL, ej. parallel_n64) se vuelven a poner encima.
    /// </summary>
    public class OverlayForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        private readonly string _baseDir;
        private readonly System.Windows.Forms.Timer _topmostTimer = new() { Interval = 250 };
        private EstiloOverlay _estilo = new();
        private string _countdown = "";
        private bool _gameOver;
        // Modo banner: franja arriba que baja deslizándose, sin tapar el juego entero ni tocar el foco.
        private bool _banner;
        private Size _modo;
        private int _bannerYFinal;
        private readonly System.Windows.Forms.Timer _slideTimer = new() { Interval = 15 };

        public OverlayForm(string baseDir)
        {
            _baseDir = baseDir;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Opacity = 0.9;
            _topmostTimer.Tick += (_, _) => ForzarTopMost();
            _slideTimer.Tick += (_, _) => PasoSlide();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/;
                return cp;
            }
        }

        /// <summary>Se posiciona justo al mostrarse (la resolución puede haber cambiado tras el lanzamiento).</summary>
        public void MostrarEn(Rectangle bounds, int segundos)
        {
            _estilo = EstiloOverlay.Cargar(Path.Combine(_baseDir, "config.ini")); // cambios del configurador sin reiniciar
            Bounds = bounds;
            _banner = false;
            _modo = bounds.Size;
            _slideTimer.Stop();
            _gameOver = false;
            _countdown = segundos.ToString();
            if (!Visible) Show();
            ForzarTopMost();
            _topmostTimer.Start();
            Invalidate();
            Logger.Log($"Overlay en {bounds.Width}x{bounds.Height} @ {bounds.X},{bounds.Y} " +
                       $"(canvas {(OverlayRenderer.EsCrt(bounds.Size, _estilo.Aspecto) ? "CRT 4:3" : "píxel cuadrado")}, fuente {_estilo.Fuente})");
        }

        /// <summary>
        /// Banner: franja de ~30 % del alto arriba de la pantalla que entra deslizándose (~200 ms). No minimiza
        /// ni activa nada: el juego (en ventana sin bordes) sigue con el foco y el control.
        /// </summary>
        public void MostrarBanner(Rectangle pantalla, int segundos)
        {
            _estilo = EstiloOverlay.Cargar(Path.Combine(_baseDir, "config.ini"));
            _banner = true;
            _modo = pantalla.Size;
            _gameOver = false;
            _countdown = segundos.ToString();
            int alto = OverlayRenderer.AltoBannerVirtual(_modo) * OverlayRenderer.FactorLineas(_modo);
            _bannerYFinal = pantalla.Y;
            Bounds = new Rectangle(pantalla.X, pantalla.Y - alto, pantalla.Width, alto);
            if (!Visible) Show();
            ForzarTopMost();
            _topmostTimer.Start();
            _slideTimer.Start();
            Invalidate();
            Logger.Log($"Banner {pantalla.Width}x{alto} arriba de la pantalla {pantalla.Width}x{pantalla.Height} @ {pantalla.X},{pantalla.Y}");
        }

        private void PasoSlide()
        {
            if (!Visible || !_banner) { _slideTimer.Stop(); return; }
            int paso = Math.Max(1, Height / 12);
            int y = Math.Min(_bannerYFinal, Top + paso);
            SetWindowPos(Handle, HWND_TOPMOST, Left, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (y >= _bannerYFinal) _slideTimer.Stop();
        }

        /// <summary>
        /// Tras minimizar el emulador: la pantalla volvió al modo del escritorio, así que el overlay se ajusta a la
        /// pantalla donde está y se pone primero (topmost + primer plano, para ganarle a EmulationStation).
        /// </summary>
        public void Reubicar()
        {
            if (!IsHandleCreated || !Visible) return;
            var b = Screen.FromHandle(Handle).Bounds;
            if (b != Bounds)
            {
                Bounds = b;
                Logger.Log($"Overlay reubicado en {b.Width}x{b.Height} @ {b.X},{b.Y}");
            }
            ForzarTopMost();
            NativeMethods.TraerAlFrente(Handle);
            Invalidate();
        }

        /// <summary>Deja el overlay tapando el juego mientras se cierra el emulador.</summary>
        public void MostrarGameOver()
        {
            if (InvokeRequired) { BeginInvoke(MostrarGameOver); return; }
            _gameOver = true;
            ForzarTopMost();
            Invalidate();
        }

        public void ActualizarCountdown(int segundos)
        {
            if (InvokeRequired) { BeginInvoke(() => ActualizarCountdown(segundos)); return; }
            _countdown = segundos.ToString();
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { _topmostTimer.Stop(); _slideTimer.Stop(); }
        }

        private void ForzarTopMost()
        {
            if (IsHandleCreated && Visible)
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            OverlayRenderer.Dibujar(e.Graphics, ClientRectangle, _banner ? _modo : ClientSize, _estilo, _baseDir,
                                    _countdown, _gameOver, _banner);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _topmostTimer.Dispose(); _slideTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
