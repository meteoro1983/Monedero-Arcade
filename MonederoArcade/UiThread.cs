using System.Windows.Forms;

namespace MonederoArcade
{
    /// <summary>Permite ejecutar acciones en el hilo de UI desde cualquier hilo (tareas, hook, etc.).</summary>
    public static class UiThread
    {
        private static Control? _anchor;

        public static void Init()
        {
            _anchor = new Control();
            _ = _anchor.Handle; // fuerza la creación del handle en el hilo de UI
        }

        public static void Post(Action accion)
        {
            var c = _anchor;
            if (c == null || c.IsDisposed) { accion(); return; }
            try { c.BeginInvoke(accion); }
            catch (Exception ex) { Logger.Log("UiThread.Post: " + ex.Message); }
        }
    }
}
