using System.Diagnostics;
using System.Windows.Forms;

namespace MonederoArcade
{
    public enum FaseSesion { EsperandoPrimerAviso, OverlayVisible, TimerVisible, Terminada }

    /// <summary>
    /// Ciclo de una partida: [fast-forward opcional] -> espera -> overlay + countdown + pausa ->
    /// reanudar con crédito (loop) o cerrar el emulador por abandono. La primera espera depende del modo
    /// (entrada: tiempo_overlay; diferido/silencioso: minutos_libres). En modo silencioso no hay overlay ni pausa:
    /// aparece un timer chico y, sin crédito, se cierra el juego.
    /// </summary>
    public class GameSession : IDisposable
    {
        public int ProcessId { get; }
        public string Emulador { get; }
        public string Sistema { get; }
        public string NombreArchivo { get; }
        public GameConfig Config { get; }

        private readonly ControlEmulador _control;
        private readonly OverlayForm _overlay;
        private readonly TimerForm _timerForm;
        private readonly string _baseDir;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
        private readonly CreditHook _hook;
        private readonly GamepadCredit _pad;

        private FaseSesion _fase = FaseSesion.EsperandoPrimerAviso;
        private int _segundosObjetivo;
        private int _segundosTranscurridos;
        private int _countdownRestante;
        private bool _primerPeriodo = true;
        private bool _ffActivo;
        private bool _ffTerminado;
        private bool _disposed;
        private int _pulsos;
        private DateTime _primerPulso = DateTime.MinValue;

        public event Action<int>? SesionTerminada;

        public GameSession(int pid, string emulador, string sistema, string nombreArchivo, GameConfig config,
                           EmuladorConfig emuCfg, CreditHook hookCompartido, GamepadCredit padCompartido, string baseDir)
        {
            _baseDir = baseDir;
            _overlay = new OverlayForm(baseDir);
            _timerForm = new TimerForm(baseDir);
            ProcessId = pid;
            Emulador = emulador;
            Sistema = sistema;
            NombreArchivo = nombreArchivo;
            Config = config;
            _hook = hookCompartido;
            _pad = padCompartido;
            _control = new ControlEmulador(pid, emulador, emuCfg) { SinMinimizar = config.Aviso == "banner" };

            _segundosObjetivo = PrimeraEspera();
            _timer.Tick += (_, _) => Tick();
            Logger.Log($"Sesión {emulador} [{sistema}] {nombreArchivo} pid={pid} overlay={config.TiempoOverlay}s " +
                       $"countdown={config.TiempoCountdown}s loop={config.IntervaloLoop}s ff={config.FastForward}/{config.TiempoFastForward}s " +
                       $"pulsos={config.PulsosPorCredito} pausa={emuCfg.MetodoPausa} aviso={config.Aviso} modo={config.Modo}" +
                       (config.Modo != "entrada" ? $" libres={config.MinutosLibres}min (sorteo {_segundosObjetivo}s)" : "") +
                       (config.Modo == "silencioso" ? $" timer={config.TiempoTimer}s" : ""));
        }

        public void Iniciar() => _timer.Start();

        /// <summary>Primera espera según el modo: entrada = tiempo_overlay; diferido y silencioso = minutos libres.</summary>
        private int PrimeraEspera() => Config.Modo == "entrada" ? Config.TiempoOverlay : Config.SorteoSegundosLibres();

        public void DevolverFoco() => _control.DevolverFoco();

        /// <summary>Aplica valores editados desde la interfaz a la sesión en curso.</summary>
        public void AplicarConfig(GameConfig nuevo)
        {
            Config.TiempoOverlay = nuevo.TiempoOverlay;
            Config.IntervaloLoop = nuevo.IntervaloLoop;
            Config.TiempoCountdown = nuevo.TiempoCountdown;
            Config.FastForward = nuevo.FastForward;
            Config.TiempoFastForward = nuevo.TiempoFastForward;
            Config.Aviso = nuevo.Aviso;
            Config.Modo = nuevo.Modo;
            Config.MinutosLibres = nuevo.MinutosLibres;
            Config.TiempoTimer = nuevo.TiempoTimer;
            _control.SinMinimizar = nuevo.Aviso == "banner";
            if (_fase == FaseSesion.EsperandoPrimerAviso)
                _segundosObjetivo = _primerPeriodo || Config.Modo == "silencioso" ? PrimeraEspera() : Config.IntervaloLoop;
            Logger.Log("Config de la sesión actualizada desde la interfaz");
        }

        private void Tick()
        {
            switch (_fase)
            {
                case FaseSesion.EsperandoPrimerAviso:
                    _segundosTranscurridos++;
                    ManejarFastForward();
                    if (_segundosTranscurridos >= _segundosObjetivo)
                    {
                        if (Config.Modo == "silencioso") MostrarTimer();
                        else MostrarOverlay();
                    }
                    break;

                case FaseSesion.TimerVisible:
                    _countdownRestante--;
                    _timerForm.Actualizar(Math.Max(_countdownRestante, 0));
                    if (_countdownRestante <= 0) AbandonoSinCredito();
                    break;

                case FaseSesion.OverlayVisible:
                    _countdownRestante--;
                    _overlay.ActualizarCountdown(Math.Max(_countdownRestante, 0));
                    if (Config.UsaOsd && _countdownRestante > 0)
                    {
                        var n = _countdownRestante;
                        Task.Run(() => _control.ShowMsg($"INSERT COIN - {n}"));
                    }
                    if (_countdownRestante <= 0) AbandonoSinCredito();
                    break;
            }
        }

        /// <summary>Fast-forward solo en el primer período (hasta el primer aviso) y solo RetroArch.</summary>
        private void ManejarFastForward()
        {
            if (!_primerPeriodo || !Config.FastForward || !_control.EsRetroArch || _ffTerminado) return;

            int fin = Math.Max(1, Math.Min(Config.TiempoFastForward, _segundosObjetivo - 1));
            if (!_ffActivo && _segundosTranscurridos >= 1 && _segundosTranscurridos < fin)
            {
                _ffActivo = true;
                Logger.Log("Fast-forward ON");
                Task.Run(() => _control.ToggleFastForward());
            }
            else if (_ffActivo && _segundosTranscurridos >= fin)
            {
                _ffActivo = false;
                _ffTerminado = true;
                Logger.Log("Fast-forward OFF");
                Task.Run(() => _control.ToggleFastForward());
            }
        }

        /// <summary>Pantalla donde está la ventana del emulador (o la principal si no se encuentra).</summary>
        private Rectangle ObtenerPantallaObjetivo()
        {
            try
            {
                using var p = Process.GetProcessById(ProcessId);
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero)
                    return Screen.FromHandle(p.MainWindowHandle).Bounds;
            }
            catch { }
            return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 640, 480);
        }

        private void Armar()
        {
            _hook.TeclaCredito = Enum.TryParse<Keys>(Config.CreditKeyName, true, out var k) ? k : Keys.D5;
            _hook.CreditoInsertado -= OnCreditoInsertado;
            _hook.CreditoInsertado += OnCreditoInsertado;
            _hook.Armado = true;

            _pad.Configurar(Config.CreditPadName);
            _pad.CreditoInsertado -= OnCreditoInsertado;
            _pad.CreditoInsertado += OnCreditoInsertado;
            _pad.Armado = true;
        }

        private void Desarmar()
        {
            _hook.Armado = false;
            _hook.CreditoInsertado -= OnCreditoInsertado;
            _pad.Armado = false;
            _pad.CreditoInsertado -= OnCreditoInsertado;
        }

        private void OcultarOverlay()
        {
            if (!_disposed && !_overlay.IsDisposed) _overlay.Hide();
            if (!_disposed && !_timerForm.IsDisposed) _timerForm.Hide();
        }

        /// <summary>Modo silencioso: timer chico en una esquina, el juego sigue corriendo (sin pausa).</summary>
        private void MostrarTimer()
        {
            _fase = FaseSesion.TimerVisible;
            _countdownRestante = Math.Max(5, Config.TiempoTimer);
            _pulsos = 0;
            _ffActivo = false;
            _ffTerminado = true;
            Logger.Log($"Modo silencioso: timer de {_countdownRestante}s (sin pausa)");
            Armar();
            _timerForm.Mostrar(ObtenerPantallaObjetivo(), _countdownRestante);
            if (Config.UsaOsd) Task.Run(() => _control.ShowMsg($"INSERT COIN - {_countdownRestante}s"));
        }

        /// <summary>Modo silencioso con crédito: se esconde el timer y empieza otro período libre.</summary>
        private void RenovarSilencioso()
        {
            if (_fase != FaseSesion.TimerVisible) return;
            var estilo = EstiloOverlay.Cargar(Path.Combine(_baseDir, "config.ini"));
            Sonido.Reproducir(_baseDir, estilo.Sonido, estilo.Volumen);
            Desarmar();
            OcultarOverlay();
            _fase = FaseSesion.EsperandoPrimerAviso;
            _primerPeriodo = false;
            _segundosObjetivo = Config.SorteoSegundosLibres();
            _segundosTranscurridos = 0;
            Logger.Log($"Crédito en modo silencioso: otro período de {_segundosObjetivo}s");
        }

        private void MostrarOverlay()
        {
            _fase = FaseSesion.OverlayVisible;
            _countdownRestante = Config.TiempoCountdown;
            _pulsos = 0;
            Logger.Log($"Mostrando overlay + pausa (countdown {_countdownRestante}s)");

            var apagarFf = _ffActivo;
            _ffActivo = false;
            _ffTerminado = true;
            Task.Run(() =>
            {
                if (apagarFf) _control.ToggleFastForward();
                _control.Pausar();
                // Al minimizar (Dolphin, juegos de PC) se sale de la pantalla exclusiva: cambia el modo de video
                // y Windows le da el foco a otra ventana (EmulationStation). Se reubica el overlay en la pantalla
                // real y se lo trae al frente, ahora y otra vez cuando el cambio de modo terminó de asentarse.
                if (Config.UsaOverlay && _control.MinimizaAlPausar)
                    foreach (var espera in new[] { 0, 700 })
                    {
                        Thread.Sleep(espera);
                        UiThread.Post(() =>
                        {
                            if (_fase == FaseSesion.OverlayVisible && !_disposed && !_overlay.IsDisposed)
                                _overlay.Reubicar();
                        });
                    }
            });

            Armar();
            if (Config.Aviso == "banner") _overlay.MostrarBanner(ObtenerPantallaObjetivo(), _countdownRestante);
            else if (Config.UsaOverlay) _overlay.MostrarEn(ObtenerPantallaObjetivo(), _countdownRestante);
            if (Config.UsaOsd) Task.Run(() => _control.ShowMsg($"INSERT COIN - {Config.TiempoCountdown}"));
        }

        // Puede llamarse desde el callback del hook: debe ser liviano, así que solo posteamos al hilo de UI.
        private void OnCreditoInsertado() => UiThread.Post(ContarPulso);

        /// <summary>Cuenta pulsos: se necesitan PulsosPorCredito pulsos dentro de una ventana de 2 s.</summary>
        private void ContarPulso()
        {
            if (_fase != FaseSesion.OverlayVisible && _fase != FaseSesion.TimerVisible) return;
            int requeridos = Math.Max(1, Config.PulsosPorCredito);
            var ahora = DateTime.UtcNow;
            if (_pulsos == 0 || (ahora - _primerPulso).TotalSeconds > 2)
            {
                _pulsos = 0;
                _primerPulso = ahora;
            }
            _pulsos++;
            Logger.Log($"Pulso de crédito {_pulsos}/{requeridos}");
            if (_pulsos >= requeridos)
            {
                _pulsos = 0;
                if (_fase == FaseSesion.TimerVisible) RenovarSilencioso();
                else Reanudar();
            }
        }

        private void Reanudar()
        {
            if (_fase != FaseSesion.OverlayVisible) return;
            Logger.Log("Crédito completo: reanudando");
            var estilo = EstiloOverlay.Cargar(Path.Combine(_baseDir, "config.ini"));
            Sonido.Reproducir(_baseDir, estilo.Sonido, estilo.Volumen);

            Desarmar();
            OcultarOverlay();
            Task.Run(() => _control.Reanudar());

            _fase = FaseSesion.EsperandoPrimerAviso;
            _primerPeriodo = false;
            _segundosObjetivo = Config.IntervaloLoop;
            _segundosTranscurridos = 0;
        }

        private void AbandonoSinCredito()
        {
            Logger.Log("Countdown agotado: cerrando emulador");
            _fase = FaseSesion.Terminada;
            Desarmar();
            _timer.Stop();

            // El overlay se queda tapando el juego durante el cierre (en modo silencioso todavía no estaba a la vista).
            if (!_timerForm.IsDisposed) _timerForm.Hide();
            if (Config.UsaOverlay)
            {
                if (!_overlay.Visible) _overlay.MostrarEn(ObtenerPantallaObjetivo(), 0);
                _overlay.MostrarGameOver();
            }
            if (Config.UsaOsd) Task.Run(() => _control.ShowMsg("GAME OVER"));

            Task.Run(() =>
            {
                try { _control.Cerrar(); }
                catch (Exception ex) { Logger.Log("Error al cerrar: " + ex.Message); }
                finally
                {
                    UiThread.Post(() =>
                    {
                        OcultarOverlay();
                        SesionTerminada?.Invoke(ProcessId);
                    });
                }
            });
        }

        /// <summary>
        /// Salida universal (Select+Start): corta la sesión sin cartel de GAME OVER. Hilo de UI. Devuelve una
        /// acción bloqueante (para Task.Run) que reanuda el juego si estaba pausado, para que pueda cerrarse.
        /// </summary>
        public Action PrepararSalida()
        {
            bool pausado = _fase == FaseSesion.OverlayVisible;
            _fase = FaseSesion.Terminada;
            _timer.Stop();
            Desarmar();
            OcultarOverlay();
            Logger.Log($"Sesión cortada por el combo de salida (pausada={pausado})");
            return () => { if (pausado) _control.Reanudar(); };
        }

        public void NotificarProcesoFinalizado()
        {
            _fase = FaseSesion.Terminada;
            _timer.Stop();
            Desarmar();
            OcultarOverlay();
            Logger.Log($"Proceso {ProcessId} finalizó");
            SesionTerminada?.Invoke(ProcessId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Desarmar();
            _timer.Dispose();
            _overlay.Dispose();
            _timerForm.Dispose();
        }
    }
}
