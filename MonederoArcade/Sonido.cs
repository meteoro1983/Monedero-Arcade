using System.Runtime.InteropServices;
using System.Text;

namespace MonederoArcade
{
    /// <summary>Reproduce mp3/wav con MCI (winmm), sin dependencias externas. Llamar desde el hilo de UI.</summary>
    public static class Sonido
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string comando, StringBuilder? retorno, int largo, IntPtr callback);

        private const string Alias = "monedero_coin";

        public static void Reproducir(string baseDir, string archivo, int volumen)
        {
            if (string.IsNullOrWhiteSpace(archivo)) return;
            try
            {
                var path = Path.IsPathRooted(archivo) ? archivo : Path.Combine(baseDir, archivo);
                if (!File.Exists(path)) { Logger.Log($"Sonido no encontrado: {path}"); return; }

                mciSendString($"close {Alias}", null, 0, IntPtr.Zero);
                int err = mciSendString($"open \"{path}\" type mpegvideo alias {Alias}", null, 0, IntPtr.Zero);
                if (err != 0) { Logger.Log($"Sonido: MCI open error {err}"); return; }
                mciSendString($"setaudio {Alias} volume to {Math.Clamp(volumen, 0, 100) * 10}", null, 0, IntPtr.Zero);
                mciSendString($"play {Alias} from 0", null, 0, IntPtr.Zero);
            }
            catch (Exception ex) { Logger.Log("Sonido: " + ex.Message); }
        }
    }
}
