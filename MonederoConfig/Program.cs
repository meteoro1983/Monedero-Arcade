using System.Windows.Forms;

namespace MonederoConfig
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            // Vive junto a monedero.exe (RetroBat\monedero\) y edita el mismo config.ini.
            Application.Run(new MainForm(AppDomain.CurrentDomain.BaseDirectory));
        }
    }
}
