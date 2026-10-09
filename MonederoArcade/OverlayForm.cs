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
            if (!Visible) _topmostTimer.Stop();
        }

        private void ForzarTopMost()
        {
            if (IsHandleCreated && Visible)
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            OverlayRenderer.Dibujar(e.Graphics, ClientRectangle, ClientSize, _estilo, _baseDir, _countdown, _gameOver);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _topmostTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
