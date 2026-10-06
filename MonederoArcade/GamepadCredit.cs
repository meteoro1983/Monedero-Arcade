using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonederoArcade
{
    /// <summary>
    /// Detecta el botón de crédito en controles XInput (ej. Select/Back) por polling
    /// (cada 50 ms, solo mientras está armado). XInput no necesita foco de ventana.
    /// </summary>
    public class GamepadCredit : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger, bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        private static readonly Dictionary<string, ushort> Mapa = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NONE"] = 0,
            ["UP"] = 0x0001, ["DOWN"] = 0x0002, ["LEFT"] = 0x0004, ["RIGHT"] = 0x0008,
            ["START"] = 0x0010, ["BACK"] = 0x0020, ["SELECT"] = 0x0020,
            ["L3"] = 0x0040, ["R3"] = 0x0080, ["LB"] = 0x0100, ["RB"] = 0x0200,
            ["A"] = 0x1000, ["B"] = 0x2000, ["X"] = 0x4000, ["Y"] = 0x8000
        };

        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
        private readonly ushort[] _previo = new ushort[4];
        private ushort _mascara = 0x0020; // BACK (Select) por defecto
        private bool _armado;

        public event Action? CreditoInsertado;

        public GamepadCredit() => _timer.Tick += (_, _) => Poll();

        public void Configurar(string? nombreBoton)
        {
            _mascara = nombreBoton != null && Mapa.TryGetValue(nombreBoton.Trim(), out var m) ? m : (ushort)0x0020;
        }

        public bool Armado
        {
            get => _armado;
            set
            {
                if (value == _armado) return;
                _armado = value;
                if (value) { Snapshot(); _timer.Start(); }
                else _timer.Stop();
            }
        }

        private static ushort Leer(uint i)
        {
            try { return XInputGetState(i, out var s) == 0 ? s.Gamepad.wButtons : (ushort)0; }
            catch { return 0; }
        }

        // Al armar, se toma el estado actual para que un botón ya presionado no cuente como crédito.
        private void Snapshot()
        {
            for (uint i = 0; i < 4; i++) _previo[i] = Leer(i);
        }

        private void Poll()
        {
            if (_mascara == 0) return;
            for (uint i = 0; i < 4; i++)
            {
                var cur = Leer(i);
                bool ahora = (cur & _mascara) == _mascara;
                bool antes = (_previo[i] & _mascara) == _mascara;
                _previo[i] = cur;
                if (ahora && !antes)
                {
                    CreditoInsertado?.Invoke();
                    return;
                }
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
