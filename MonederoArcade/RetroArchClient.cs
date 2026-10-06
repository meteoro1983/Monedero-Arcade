using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MonederoArcade
{
    /// <summary>
    /// Comandos UDP a la Network Command Interface de RetroArch
    /// (requiere network_cmd_enable = "true"). Puerto por defecto: 55355.
    /// </summary>
    public class RetroArchClient
    {
        private readonly int _port;
        public RetroArchClient(int port = 55355) => _port = port;

        private void Send(string command)
        {
            try
            {
                using var udp = new UdpClient();
                var bytes = Encoding.ASCII.GetBytes(command + "\n");
                udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, _port));
                Logger.Log($"UDP -> {command}");
            }
            catch (Exception ex) { Logger.Log($"UDP error ({command}): {ex.Message}"); }
        }

        private string? Query(string command, int timeoutMs = 1000)
        {
            try
            {
                using var udp = new UdpClient(0);
                udp.Client.ReceiveTimeout = timeoutMs;
                var bytes = Encoding.ASCII.GetBytes(command + "\n");
                udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, _port));
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var data = udp.Receive(ref remote);
                var resp = Encoding.ASCII.GetString(data).Trim();
                Logger.Log($"UDP <- {resp}");
                return resp;
            }
            catch { return null; }
        }

        /// <summary>true = pausado, false = corriendo, null = no se pudo saber.</summary>
        public bool? EstaPausado()
        {
            var r = Query("GET_STATUS");
            if (r == null) return null;
            if (r.StartsWith("GET_STATUS PAUSED", StringComparison.OrdinalIgnoreCase)) return true;
            if (r.StartsWith("GET_STATUS PLAYING", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        /// <summary>
        /// Consulta el estado reintentando hasta maxMs: RetroArch abre el puerto de red recién cuando
        /// terminó de cargar el contenido (y tras el cambio de resolución CRT), así que en los primeros
        /// segundos GET_STATUS no responde.
        /// </summary>
        public bool? EsperarEstado(int maxMs)
        {
            var fin = DateTime.UtcNow.AddMilliseconds(maxMs);
            while (true)
            {
                var st = EstaPausado();
                if (st != null || DateTime.UtcNow >= fin) return st;
                Thread.Sleep(250);
            }
        }

        /// <summary>Pausa y verifica por GET_STATUS; si no quedó pausado, reintenta una vez.</summary>
        public void PausarSiHaceFalta()
        {
            var inicial = EsperarEstado(8000);
            if (inicial == true) { Logger.Log("Ya estaba pausado"); return; }
            if (inicial == null)
            {
                // Un toggle a ciegas puede quedar desfasado y terminar pausando al reanudar.
                Logger.Log("RetroArch no responde por red (¿network_cmd_enable = \"true\"?): no se pausa");
                return;
            }
            Send("PAUSE_TOGGLE");
            Thread.Sleep(400);
            var st = EstaPausado();
            Logger.Log($"Pausa verificada: {(st.HasValue ? st.Value.ToString() : "sin respuesta")}");
            if (st == false)
            {
                Send("PAUSE_TOGGLE");
                Thread.Sleep(400);
                var st2 = EstaPausado();
                Logger.Log($"Pausa tras reintento: {(st2.HasValue ? st2.Value.ToString() : "sin respuesta")}");
            }
        }

        public void ReanudarSiHaceFalta()
        {
            // Solo se despausa si consta que está pausado: con estado desconocido, un toggle podría pausar.
            var st = EsperarEstado(3000);
            if (st == true) Send("PAUSE_TOGGLE");
            else Logger.Log($"Reanudar: sin toggle (estado {(st.HasValue ? "PLAYING" : "desconocido")})");
        }

        public void FastForward() => Send("FAST_FORWARD");
        public void Quit() => Send("QUIT");
        public void ShowMsg(string msg) => Send("SHOW_MSG " + msg);
    }
}
