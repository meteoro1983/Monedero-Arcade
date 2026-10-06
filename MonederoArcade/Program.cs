using System.Windows.Forms;

namespace MonederoArcade
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            ApplicationConfiguration.Initialize();
            UiThread.Init();

            // Red de seguridad: si algo falla, nunca dejar un emulador suspendido.
            Application.ThreadException += (_, e) =>
            {
                Logger.Log("ThreadException: " + e.Exception);
                ProcessControl.ReanudarTodos();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Logger.Log("UnhandledException: " + e.ExceptionObject);
                ProcessControl.ReanudarTodos();
            };
            Application.ApplicationExit += (_, _) => ProcessControl.ReanudarTodos();
            ProcessControl.RecuperarAlInicio();

            // Carpeta base: junto al .exe (RetroBat\monedero\), según la spec.
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configManager = new ConfigManager(baseDir);
            Logger.Log("=== Monedero Arcade 3.6 iniciado ===");

            var hook = new CreditHook();
            hook.Instalar();
            var pad = new GamepadCredit();

            var watcher = new ProcessWatcher();
            var sesiones = new Dictionary<int, GameSession>();

            ConfigForm? configForm = null;
            void AbrirConfig()
            {
                if (configForm != null && !configForm.IsDisposed)
                {
                    configForm.Activate();
                    return;
                }
                var pin = new IniFile(configManager.GeneralIniPath).GetString("general", "pin_config", "");
                if (!string.IsNullOrEmpty(pin) && !PinPrompt.Verificar(pin)) return;

                configForm = new ConfigForm(configManager, () => sesiones.Values.ToList());
                configForm.FormClosed += (_, _) =>
                {
                    foreach (var s in sesiones.Values) s.DevolverFoco();
                };
                configForm.Show();
                configForm.Activate();
            }

            var notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "Monedero Arcade 3.6 (beta)",
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Monedero Arcade 3.6 (beta) corriendo...").Enabled = false;
            menu.Items.Add("Configuración (Ctrl+Alt+F5)", null, (_, _) => AbrirConfig());
            menu.Items.Add("Configurador completo (MonederoConfig)", null, (_, _) =>
            {
                var exe = Path.Combine(configManager.BaseDir, "MonederoConfig.exe");
                if (File.Exists(exe)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                else MessageBox.Show("No se encontró MonederoConfig.exe junto a monedero.exe", "Monedero");
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (_, _) =>
            {
                ProcessControl.ReanudarTodos();
                hook.Dispose();
                pad.Dispose();
                notifyIcon.Visible = false;
                Application.Exit();
            });
            notifyIcon.ContextMenuStrip = menu;

            hook.ConfigSolicitada += () => UiThread.Post(AbrirConfig);

            watcher.JuegoLanzado += launched =>
            {
                if (configManager.EsSistemaExento(launched.Sistema))
                {
                    Logger.Log($"Sistema exento ({launched.Sistema}): no se controla {launched.NombreArchivo}");
                    return;
                }
                if (sesiones.ContainsKey(launched.ProcessId)) return;

                var cfg = configManager.Resolve(launched.Sistema, launched.NombreArchivo);
                var emuCfg = configManager.ResolveEmulador(launched.Emulador);
                var sesion = new GameSession(launched.ProcessId, launched.Emulador, launched.Sistema,
                                             launched.NombreArchivo, cfg, emuCfg, hook, pad, configManager.BaseDir);
                sesion.SesionTerminada += pid =>
                {
                    if (sesiones.Remove(pid, out var s)) s.Dispose();
                };
                sesiones[launched.ProcessId] = sesion;
                sesion.Iniciar();

                notifyIcon.ShowBalloonTip(2000, "Monedero Arcade",
                    $"Detectado: {launched.Emulador} [{launched.Sistema}] {launched.NombreArchivo}", ToolTipIcon.Info);
            };

            watcher.JuegoFinalizado += pid =>
            {
                if (sesiones.Remove(pid, out var s))
                {
                    s.NotificarProcesoFinalizado();
                    s.Dispose();
                }
            };

            var pollTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            pollTimer.Tick += (_, _) => watcher.Tick();
            pollTimer.Start();

            Application.Run();
        }
    }
}
