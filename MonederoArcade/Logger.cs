namespace MonederoArcade
{
    /// <summary>Log simple a monedero.log (junto al .exe) para diagnosticar pruebas.</summary>
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string _path =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monedero.log");

        public static void Log(string msg)
        {
            try
            {
                lock (_lock)
                    File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {msg}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
