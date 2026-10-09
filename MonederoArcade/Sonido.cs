using System.Runtime.InteropServices;
using System.Text;

namespace MonederoArcade
{
    /// <summary>
    /// Reproduce el sonido de crédito con MCI (winmm), sin dependencias externas. Llamar desde el hilo de UI.
    /// WAV usa el dispositivo waveaudio (anda siempre); MP3 usa mpegvideo, que necesita los códecs de Windows
    /// Media: en Windows "N" sin Media Feature Pack falla (error 277), así que si hay un .wav con el mismo
    /// nombre se usa ese.
    /// </summary>
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
                var wav = Path.ChangeExtension(path, ".wav");
                if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && File.Exists(wav)) path = wav;
                if (!File.Exists(path)) { Logger.Log($"Sonido no encontrado: {path}"); return; }

                bool esWav = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
                mciSendString($"close {Alias}", null, 0, IntPtr.Zero);
                int err = mciSendString($"open \"{path}\" type {(esWav ? "waveaudio" : "mpegvideo")} alias {Alias}",
                                        null, 0, IntPtr.Zero);
                if (err != 0)
                {
                    Logger.Log($"Sonido: MCI open error {err}" +
                               (esWav ? "" : " (MP3 sin códec: poner un .wav con el mismo nombre)"));
                    return;
                }
                // waveaudio no admite volumen: el .wav se exporta ya con el nivel deseado.
                if (!esWav)
                    mciSendString($"setaudio {Alias} volume to {Math.Clamp(volumen, 0, 100) * 10}", null, 0, IntPtr.Zero);
                mciSendString($"play {Alias} from 0", null, 0, IntPtr.Zero);
            }
            catch (Exception ex) { Logger.Log("Sonido: " + ex.Message); }
        }
    }
}
