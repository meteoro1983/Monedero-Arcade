using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MonederoArcade
{
    /// <summary>Pide un PIN simple antes de abrir la configuración (si pin_config está definido).</summary>
    public static class PinPrompt
    {
        public static bool Verificar(string pinEsperado)
        {
            using var f = new Form
            {
                Text = "Monedero - PIN",
                Width = 270,
                Height = 140,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };
            var tb = new TextBox { UseSystemPasswordChar = true, Left = 12, Top = 12, Width = 230 };
            var ok = new Button { Text = "OK", Left = 12, Top = 48, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Left = 100, Top = 48, DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[] { tb, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            f.Shown += (_, _) => { f.Activate(); tb.Focus(); };
            return f.ShowDialog() == DialogResult.OK && tb.Text == pinEsperado;
        }
    }

    /// <summary>
    /// Interfaz de configuración (Ctrl+Alt+F5 o menú de la bandeja): defaults por sistema,
    /// alta de sistemas, escaneo de carpetas de ROMs y edición del juego en curso.
    /// </summary>
    public class ConfigForm : Form
    {
        private readonly ConfigManager _cfg;
        private readonly Func<IReadOnlyList<GameSession>> _getSesiones;

        private readonly DataGridView _grid = new();
        private readonly ComboBox _cmbSesion = new();
        private readonly Label _lblInfo = new();
        private readonly NumericUpDown _nOverlay = NuevoNum();
        private readonly NumericUpDown _nLoop = NuevoNum();
        private readonly NumericUpDown _nCountdown = NuevoNum();
        private readonly NumericUpDown _nFf = NuevoNum();
        private readonly CheckBox _chkFf = new();
        private List<GameSession> _sesiones = new();

        private static NumericUpDown NuevoNum() => new NumericUpDown { Minimum = 0, Maximum = 86400, Width = 90 };

        public ConfigForm(ConfigManager cfg, Func<IReadOnlyList<GameSession>> getSesiones)
        {
            _cfg = cfg;
            _getSesiones = getSesiones;

            Text = "Monedero Arcade - Configuración";
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            Width = Math.Min(780, area.Width - 20);
            Height = Math.Min(520, area.Height - 20);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabSis = new TabPage("Sistemas");
            var tabJuego = new TabPage("Juego actual");
            tabs.TabPages.Add(tabSis);
            tabs.TabPages.Add(tabJuego);
            Controls.Add(tabs);

            ConstruirTabSistemas(tabSis);
            ConstruirTabJuego(tabJuego);
            CargarSistemas();
            CargarSesiones();
        }

        private void Msg(string texto) => MessageBox.Show(this, texto, "Monedero");

        private static Button Boton(string texto, EventHandler onClick)
        {
            var b = new Button { Text = texto, AutoSize = true, Margin = new Padding(4) };
            b.Click += onClick;
            return b;
        }

        // ---------------- Pestaña Sistemas ----------------

        private void ConstruirTabSistemas(TabPage tab)
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.Columns.Add("sistema", "Sistema");
            _grid.Columns["sistema"]!.ReadOnly = true;
            _grid.Columns.Add("tiempo_overlay", "Aviso (s)");
            _grid.Columns.Add("intervalo_loop", "Loop (s)");
            _grid.Columns.Add("tiempo_countdown", "Countdown (s)");
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "fast_forward", HeaderText = "Fast-forward" });
            _grid.Columns.Add("tiempo_fast_forward", "FF hasta (s)");

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight
            };
            panel.Controls.Add(Boton("Guardar", (_, _) => GuardarSistemas()));
            panel.Controls.Add(Boton("Agregar sistema", (_, _) => AgregarSistema()));
            panel.Controls.Add(Boton("Escanear ROMs del seleccionado", (_, _) => EscanearSeleccionado()));
            panel.Controls.Add(Boton("Recargar", (_, _) => CargarSistemas()));

            tab.Controls.Add(_grid);
            tab.Controls.Add(panel);
            _grid.BringToFront();
        }

        private void CargarSistemas()
        {
            _grid.Rows.Clear();
            foreach (var s in _cfg.ListarSistemas())
            {
                var c = _cfg.LeerSistema(s);
                _grid.Rows.Add(s, c.TiempoOverlay, c.IntervaloLoop, c.TiempoCountdown, c.FastForward, c.TiempoFastForward);
            }
        }

        private static bool TryInt(object? v, out int n) => int.TryParse(Convert.ToString(v), out n);

        private void GuardarSistemas()
        {
            var nuevos = new List<(string Sis, GameConfig Cfg)>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var sis = Convert.ToString(row.Cells["sistema"].Value) ?? "";
                if (!TryInt(row.Cells["tiempo_overlay"].Value, out var ov) ||
                    !TryInt(row.Cells["intervalo_loop"].Value, out var lp) ||
                    !TryInt(row.Cells["tiempo_countdown"].Value, out var cd) ||
                    !TryInt(row.Cells["tiempo_fast_forward"].Value, out var ff))
                {
                    Msg($"Valores inválidos en '{sis}': deben ser números enteros.");
                    return;
                }
                bool ffOn = row.Cells["fast_forward"].Value is true;
                nuevos.Add((sis, new GameConfig
                {
                    TiempoOverlay = ov,
                    IntervaloLoop = lp,
                    TiempoCountdown = cd,
                    FastForward = ffOn,
                    TiempoFastForward = ff
                }));
            }
            foreach (var (sis, c) in nuevos) _cfg.GuardarSistema(sis, c);
            Msg("Guardado. Se aplica a los próximos juegos que se lancen.");
        }

        private void AgregarSistema()
        {
            using var dlg = new Form
            {
                Text = "Agregar sistema",
                Width = 370,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };
            var lbl = new Label { Text = "Nombre del sistema (igual que en RetroBat):", Left = 10, Top = 10, Width = 340 };
            var cmb = new ComboBox { Left = 10, Top = 35, Width = 340, DropDownStyle = ComboBoxStyle.DropDown };
            var romsDir = Path.GetFullPath(Path.Combine(_cfg.BaseDir, "..", "roms"));
            if (Directory.Exists(romsDir))
                foreach (var d in Directory.GetDirectories(romsDir))
                    cmb.Items.Add(new DirectoryInfo(d).Name);
            var ok = new Button { Text = "Agregar", Left = 10, Top = 70, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Left = 100, Top = 70, DialogResult = DialogResult.Cancel };
            dlg.Controls.AddRange(new Control[] { lbl, cmb, ok, cancel });
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var nombre = cmb.Text.Trim().ToLowerInvariant();
            if (nombre.Length == 0) return;
            _cfg.AgregarSistema(nombre);
            CargarSistemas();
        }

        private void EscanearSeleccionado()
        {
            if (_grid.CurrentRow == null) return;
            var sis = Convert.ToString(_grid.CurrentRow.Cells["sistema"].Value) ?? "";
            if (sis.Length == 0 || sis.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                Msg("Elegí un sistema (no 'default').");
                return;
            }

            var carpeta = Path.GetFullPath(Path.Combine(_cfg.BaseDir, "..", "roms", sis));
            if (!Directory.Exists(carpeta))
            {
                using var fb = new FolderBrowserDialog { Description = $"Carpeta de ROMs de '{sis}'" };
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                carpeta = fb.SelectedPath;
            }

            var n = _cfg.EscanearCarpeta(sis, carpeta);
            Msg($"Listo: {n} archivos .ini nuevos creados en monedero\\{sis} (nacen con activo=0).");
        }

        // ---------------- Pestaña Juego actual ----------------

        private static int Fila(TabPage tab, string texto, NumericUpDown num, int y)
        {
            var l = new Label { Text = texto, Left = 12, Top = y + 3, Width = 200 };
            num.Left = 220;
            num.Top = y;
            tab.Controls.Add(l);
            tab.Controls.Add(num);
            return y + 30;
        }

        private void ConstruirTabJuego(TabPage tab)
        {
            tab.AutoScroll = true;
            int y = 12;

            _lblInfo.Left = 12; _lblInfo.Top = y; _lblInfo.Width = 700; _lblInfo.Height = 24;
            tab.Controls.Add(_lblInfo);
            y += 28;

            _cmbSesion.Left = 12; _cmbSesion.Top = y; _cmbSesion.Width = 700;
            _cmbSesion.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSesion.SelectedIndexChanged += (_, _) => MostrarSesion();
            tab.Controls.Add(_cmbSesion);
            y += 36;

            y = Fila(tab, "Primer aviso (s)", _nOverlay, y);
            y = Fila(tab, "Loop entre avisos (s)", _nLoop, y);
            y = Fila(tab, "Countdown (s)", _nCountdown, y);

            _chkFf.Text = "Fast-forward al inicio (solo RetroArch)";
            _chkFf.Left = 12; _chkFf.Top = y; _chkFf.Width = 340;
            tab.Controls.Add(_chkFf);
            y += 30;

            y = Fila(tab, "Fast-forward hasta (s)", _nFf, y);

            var bGuardar = Boton("Guardar (activa este juego)", (_, _) => GuardarJuego());
            bGuardar.Left = 12; bGuardar.Top = y + 8;
            var bAbrir = Boton("Abrir .ini", (_, _) => AbrirIni());
            bAbrir.Left = 240; bAbrir.Top = y + 8;
            tab.Controls.Add(bGuardar);
            tab.Controls.Add(bAbrir);
        }

        private void CargarSesiones()
        {
            _sesiones = _getSesiones().ToList();
            _cmbSesion.Items.Clear();
            foreach (var s in _sesiones)
                _cmbSesion.Items.Add($"[{s.Sistema}] {s.NombreArchivo} ({s.Emulador}, pid {s.ProcessId})");

            if (_sesiones.Count == 0)
            {
                _lblInfo.Text = "No hay ningún juego controlado en este momento (abrí esta ventana con un juego en marcha).";
                return;
            }
            _lblInfo.Text = "Juego en curso:";
            _cmbSesion.SelectedIndex = 0;
        }

        private GameSession? Seleccionada() =>
            _cmbSesion.SelectedIndex >= 0 && _cmbSesion.SelectedIndex < _sesiones.Count
                ? _sesiones[_cmbSesion.SelectedIndex]
                : null;

        private static decimal Acotar(int v) => Math.Max(0, Math.Min(86400, v));

        private void MostrarSesion()
        {
            var s = Seleccionada();
            if (s == null) return;
            var c = s.Config;
            _nOverlay.Value = Acotar(c.TiempoOverlay);
            _nLoop.Value = Acotar(c.IntervaloLoop);
            _nCountdown.Value = Acotar(c.TiempoCountdown);
            _chkFf.Checked = c.FastForward;
            _nFf.Value = Acotar(c.TiempoFastForward);
        }

        private void GuardarJuego()
        {
            var s = Seleccionada();
            if (s == null) { Msg("No hay juego en curso."); return; }

            var c = new GameConfig
            {
                TiempoOverlay = (int)_nOverlay.Value,
                IntervaloLoop = (int)_nLoop.Value,
                TiempoCountdown = (int)_nCountdown.Value,
                FastForward = _chkFf.Checked,
                TiempoFastForward = (int)_nFf.Value,
                CreditKeyName = s.Config.CreditKeyName,
                CreditPadName = s.Config.CreditPadName,
                PulsosPorCredito = s.Config.PulsosPorCredito,
                OsdRetroArch = s.Config.OsdRetroArch,
                Aviso = s.Config.Aviso
            };
            _cfg.ActivarYGuardar(s.Sistema, s.NombreArchivo, c);
            s.AplicarConfig(c);
            Msg("Guardado. Este juego quedó con activo=1 y valores propios.");
        }

        private void AbrirIni()
        {
            var s = Seleccionada();
            if (s == null) return;
            var path = _cfg.RomIniPath(s.Sistema, s.NombreArchivo);
            if (!File.Exists(path)) { Msg("Todavía no existe el .ini de este juego."); return; }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Msg(ex.Message); }
        }
    }
}
