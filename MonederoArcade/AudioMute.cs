using System.Runtime.InteropServices;

namespace MonederoArcade
{
    /// <summary>
    /// Silencia/desilencia las sesiones de audio (WASAPI) de un conjunto de procesos en el dispositivo
    /// de salida por defecto. Un juego con DirectSound suspendido deja su buffer en bucle (zumbido):
    /// se silencia antes de suspender y se restaura al reanudar.
    /// </summary>
    internal static class AudioMute
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCo { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int NotImpl1(); int NotImpl2();
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl (9 métodos) + GetSessionIdentifier + GetSessionInstanceIdentifier
            int N1(); int N2(); int N3(); int N4(); int N5(); int N6(); int N7(); int N8(); int N9();
            int N10(); int N11();
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            int SetMasterVolume(float level, ref Guid ctx);
            int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        /// <summary>Devuelve cuántas sesiones se tocaron. Nunca lanza excepciones.</summary>
        public static int Silenciar(IReadOnlyCollection<int> pids, bool silenciar)
        {
            int n = 0;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                foreach (var role in new[] { 1, 0 }) // eMultimedia, eConsole (suelen ser el mismo dispositivo)
                {
                    if (en.GetDefaultAudioEndpoint(0 /*eRender*/, role, out var dev) != 0 || dev == null) continue;
                    var iid = typeof(IAudioSessionManager2).GUID;
                    if (dev.Activate(ref iid, 1 /*CLSCTX_INPROC*/, IntPtr.Zero, out var o) != 0) continue;
                    var mgr = (IAudioSessionManager2)o;
                    if (mgr.GetSessionEnumerator(out var se) != 0) continue;
                    se.GetCount(out var count);
                    for (int i = 0; i < count; i++)
                    {
                        if (se.GetSession(i, out var s) != 0 || s == null) continue;
                        if (s.GetProcessId(out var pid) != 0 || !pids.Contains((int)pid)) continue;
                        var vol = (ISimpleAudioVolume)s;
                        var g = Guid.Empty;
                        if (vol.SetMute(silenciar, ref g) == 0) n++;
                    }
                    if (n > 0) break;
                }
            }
            catch (Exception ex) { Logger.Log("AudioMute: " + ex.Message); }
            Logger.Log($"Audio {(silenciar ? "silenciado" : "restaurado")} en {n} sesión(es)");
            return n;
        }
    }
}
