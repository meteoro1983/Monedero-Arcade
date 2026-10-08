using System.Runtime.InteropServices;

namespace MonederoArcade
{
    /// <summary>
    /// Descendientes de un proceso (snapshot de ToolHelp, sin WMI). Los juegos de PC que lanza
    /// emulatorLauncher son hijos suyos, y algunos abren a su vez otro exe (launchers de Unreal, FNA...).
    /// </summary>
    internal static class ProcessTree
    {
        private const uint TH32CS_SNAPPROCESS = 0x2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);

        /// <summary>PIDs de todos los descendientes de <paramref name="raiz"/> (sin incluirla), con su nombre.</summary>
        public static List<(int Pid, string Nombre)> Descendientes(int raiz)
        {
            var hijos = new Dictionary<uint, List<(uint, string)>>();
            var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return new();
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (!Process32FirstW(snap, ref e)) return new();
                do
                {
                    if (e.th32ProcessID == e.th32ParentProcessID) continue;
                    if (!hijos.TryGetValue(e.th32ParentProcessID, out var l)) hijos[e.th32ParentProcessID] = l = new();
                    l.Add((e.th32ProcessID, e.szExeFile));
                } while (Process32NextW(snap, ref e));
            }
            finally { NativeMethods.CloseHandle(snap); }

            var res = new List<(int, string)>();
            var pend = new Queue<uint>(); pend.Enqueue((uint)raiz);
            var vistos = new HashSet<uint> { (uint)raiz };
            while (pend.Count > 0)
            {
                if (!hijos.TryGetValue(pend.Dequeue(), out var l)) continue;
                foreach (var (pid, nombre) in l)
                {
                    if (!vistos.Add(pid)) continue;
                    // conhost acompaña a los .bat/.cmd: no es el juego
                    if (nombre.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    res.Add(((int)pid, nombre));
                    pend.Enqueue(pid);
                }
            }
            return res;
        }
    }
}
