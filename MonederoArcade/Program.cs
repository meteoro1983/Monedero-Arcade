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
            Logger.Log("=== Monedero Arcade 3.10 iniciado ===");

            var hook = new CreditHook();
            hook.Instalar();
            var pad = new GamepadCredit();

            var watcher = new ProcessWatcher { EsSistemaPc = configManager.EsSistemaPc };
            var sesiones = new Dictionary<int, GameSession>();
            var salida = new SalidaUniversal();

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
                Text = "Monedero Arcade 3.9 (beta)",
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Monedero Arcade 3.9 (beta) corriendo...").Enabled = false;
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
                salida.Dispose();
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
                if (configManager.EsJuegoExento(launched.Sistema, launched.NombreArchivo))
                {
                    Logger.Log($"Juego exento ({launched.NombreArchivo}): maneja sus propios créditos");
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

            // Salida universal (Select+Start) para juegos de PC: independiente del crédito (vale también en
            // sistemas exentos y con la sesión pausada).
            watcher.LauncherIniciado += (pid, sistema, rom) =>
            {
                if (!configManager.UsaSalidaUniversal(sistema, rom)) return;
                salida.Configurar(configManager.SalidaCombo, configManager.SalidaMs);
                salida.Agregar(pid, sistema, rom);
            };
            watcher.LauncherFinalizado += salida.LauncherTerminado;
            salida.SalidaPedida += juego =>
            {
                Action reanudar = () => { };
                if (sesiones.Remove(juego.LauncherPid, out var s))
                {
                    reanudar = s.PrepararSalida();
                    s.Dispose();
                }
                var espera = configManager.ResolveEmulador("pc").EsperaCierreMs;
                var resolucion = configManager.SalidaRestaurarResolucion;
                Task.Run(() =>
                {
                    reanudar();
                    salida.Cerrar(juego, espera, resolucion);
                });
            };

            var pollTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            pollTimer.Tick += (_, _) => { watcher.Tick(); salida.Refrescar(); };
            pollTimer.Start();

            Application.Run();
        }
    }
}
