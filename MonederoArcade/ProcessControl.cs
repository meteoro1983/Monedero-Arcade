namespace MonederoArcade
{
    /// <summary>
    /// Suspende/reanuda procesos completos (NtSuspendProcess). Es la pausa "universal" para
    /// emuladores standalone y juegos de PC sin hotkey de pausa. Guarda los PID suspendidos en
    /// suspendidos.txt para poder reanudarlos si monedero.exe se cae o se reinicia.
    /// </summary>
    public static class ProcessControl
    {
        private static readonly string _archivo =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "suspendidos.txt");
        private static readonly HashSet<int> _suspendidos = new();
        private static readonly object _lock = new();

        public static void Suspender(int pid)
        {
            lock (_lock)
            {
                if (_suspendidos.Contains(pid)) return;
                var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, pid);
                if (h == IntPtr.Zero) { Logger.Log($"OpenProcess falló para pid={pid}"); return; }
                try
                {
                    var r = NativeMethods.NtSuspendProcess(h);
                    Logger.Log($"NtSuspendProcess pid={pid} -> {r}");
                    if (r == 0) { _suspendidos.Add(pid); Guardar(); }
                }
                finally { NativeMethods.CloseHandle(h); }
            }
        }

        public static void Reanudar(int pid)
        {
            lock (_lock)
            {
                if (!_suspendidos.Contains(pid)) return;
                ReanudarNativo(pid);
                _suspendidos.Remove(pid);
                Guardar();
            }
        }

        public static void ReanudarTodos()
        {
            lock (_lock)
            {
                foreach (var pid in _suspendidos.ToList()) ReanudarNativo(pid);
                _suspendidos.Clear();
                Guardar();
            }
        }

        /// <summary>Al arrancar: reanuda cualquier PID que haya quedado suspendido por una caída anterior.</summary>
        public static void RecuperarAlInicio()
        {
            try
            {
                if (!File.Exists(_archivo)) return;
                foreach (var linea in File.ReadAllLines(_archivo))
                    if (int.TryParse(linea.Trim(), out var pid)) ReanudarNativo(pid);
                File.Delete(_archivo);
                Logger.Log("Recuperación al inicio: PID suspendidos reanudados");
            }
            catch (Exception ex) { Logger.Log("RecuperarAlInicio: " + ex.Message); }
        }

        private static void ReanudarNativo(int pid)
        {
            var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, pid);
            if (h == IntPtr.Zero) return;
            try
            {
                var r = NativeMethods.NtResumeProcess(h);
                Logger.Log($"NtResumeProcess pid={pid} -> {r}");
            }
            finally { NativeMethods.CloseHandle(h); }
        }

        private static void Guardar()
        {
            try
            {
                if (_suspendidos.Count == 0) { if (File.Exists(_archivo)) File.Delete(_archivo); }
                else File.WriteAllLines(_archivo, _suspendidos.Select(p => p.ToString()));
            }
            catch { }
        }
    }
}
