using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MonederoArcade;

namespace MonederoConfig
{
    /// <summary>
    /// Configurador de Monedero Arcade, aparte del monedero (estilo BatGui). Todo se guarda en
    /// config.ini y en los .ini por ROM, con edición quirúrgica (no pisa comentarios). El monedero
    /// relee config en cada lanzamiento y en cada overlay, así que no hace falta reiniciarlo.
    /// Pensado para entrar en un escritorio de 640x480.
    /// </summary>
    public class MainForm : Form
    {
        private readonly string _baseDir;
        private readonly string _ini;
        private readonly ConfigManager _cfg;
        private readonly string _romsDir;

        public MainForm(string baseDir)
        {
            _baseDir = baseDir;
            _cfg = new ConfigManager(baseDir); // crea config.ini si no existe
            _ini = _cfg.GeneralIniPath;
            _romsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "roms"));

            Text = "Monedero Arcade - Configurador";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            Size = new Size(Math.Min(700, area.Width), Math.Min(500, area.Height));
            MinimumSize = new Size(560, 380);
            if (area.Width <= 800) WindowState = FormWindowState.Maximized;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(TabGeneral());
            tabs.TabPages.Add(TabSistemas());
            tabs.TabPages.Add(TabEmuladores());
            tabs.TabPages.Add(TabJuegos());
            tabs.TabPages.Add(TabVistaPrevia());
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedIndex == 4) _preview.Invalidate(); };
            Controls.Add(tabs);
        }

        private IniFile Ini() => new IniFile(_ini);
        private void Ok(string msg) => MessageBox.Show(this, msg, "Monedero");

        private static Button Boton(string texto, Action accion)
        {
            var b = new Button { Text = texto, AutoSize = true, Margin = new Padding(3) };
            b.Click += (_, _) => accion();
            return b;
        }

        private static FlowLayoutPanel Fila(params Control[] cs)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            p.Controls.AddRange(cs);
            return p;
        }

        // =====================================================================================
        // General
        // =====================================================================================
        private readonly Label _lblTecla = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        private string _tecla = "D5";
        private readonly ComboBox _cmbPad = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly NumericUpDown _nPulsos = new() { Minimum = 1, Maximum = 10, Width = 60 };
        private readonly ComboBox _cmbSonido = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        private readonly NumericUpDown _nVolumen = new() { Minimum = 0, Maximum = 100, Width = 60 };
        private readonly ComboBox _cmbFuente = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        private readonly ComboBox _cmbAspecto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly TextBox _txtL1 = new() { Width = 200 }, _txtL2 = new() { Width = 200 };
        private readonly TextBox _txtG1 = new() { Width = 200 }, _txtG2 = new() { Width = 200 };
        private readonly TextBox _txtExentos = new() { Width = 330 };
        private readonly TextBox _txtPin = new() { Width = 90 };

        private static readonly string[] BotonesPad = { "BACK", "START", "A", "B", "X", "Y", "LB", "RB", "L3", "R3", "UP", "DOWN", "LEFT", "RIGHT", "NONE" };

        private TabPage TabGeneral()
        {
            var tab = new TabPage("General");
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(6) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            void Add(string etiqueta, Control c)
            {
                t.Controls.Add(new Label { Text = etiqueta, AutoSize = true, Margin = new Padding(3, 8, 8, 3) });
                t.Controls.Add(c);
            }

            _cmbPad.Items.AddRange(BotonesPad);
            _cmbAspecto.Items.AddRange(new object[] { "auto", "4:3", "real" });
            foreach (var f in Directory.GetFiles(_baseDir, "*.*").Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)))
                _cmbSonido.Items.Add(Path.GetFileName(f));
            _cmbSonido.Items.Add("(sin sonido)");
            foreach (var f in Directory.GetFiles(_baseDir, "*.ttf").Concat(Directory.GetFiles(_baseDir, "*.otf")))
                _cmbFuente.Items.Add(Path.GetFileName(f));
            _cmbFuente.Items.Add("Consolas");

            Add("Tecla de crédito:", Fila(_lblTecla, Boton("Asignar tecla...", AsignarTeclaCredito)));
            Add("Botón joystick (XInput):", Fila(_cmbPad, Boton("Detectar...", DetectarPad)));
            Add("Pulsos por crédito:", _nPulsos);
            Add("Sonido de moneda:", Fila(_cmbSonido, new Label { Text = "Vol.", AutoSize = true, Margin = new Padding(6, 8, 0, 0) }, _nVolumen,
                                         Boton("Probar", () => Sonido.Reproducir(_baseDir, SonidoElegido(), (int)_nVolumen.Value))));
            Add("Fuente del cartel:", _cmbFuente);
            Add("Aspecto de pantalla:", Fila(_cmbAspecto, new Label { Text = "auto = CRT 4:3 (también súper resoluciones)", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(6, 8, 0, 0) }));
            Add("Texto línea 1:", _txtL1);
            Add("Texto línea 2:", _txtL2);
            Add("Game over 1:", _txtG1);
            Add("Game over 2:", _txtG2);
            Add("Sistemas exentos:", _txtExentos);
            Add("PIN (Ctrl+Alt+F5):", _txtPin);
            Add("", Fila(Boton("Guardar", GuardarGeneral), Boton("Recargar", CargarGeneral)));

            tab.Controls.Add(t);
            CargarGeneral();
            return tab;
        }

        private string SonidoElegido() => _cmbSonido.SelectedItem as string is { } s && s != "(sin sonido)" ? s : "";

        private static void Elegir(ComboBox c, string valor)
        {
            int i = c.Items.Cast<object>().ToList().FindIndex(x => string.Equals(x.ToString(), valor, StringComparison.OrdinalIgnoreCase));
            if (i < 0 && !string.IsNullOrEmpty(valor) && c.DropDownStyle == ComboBoxStyle.DropDownList) { c.Items.Insert(0, valor); i = 0; }
            c.SelectedIndex = Math.Max(0, i);
        }

        private void CargarGeneral()
        {
            var g = Ini();
            var est = EstiloOverlay.Cargar(_ini);
            _tecla = g.GetString("default", "credit_key", "D5");
            _lblTecla.Text = NombreTecla(_tecla);
            Elegir(_cmbPad, g.GetString("default", "credit_pad", "BACK"));
            _nPulsos.Value = Math.Clamp(g.GetInt("default", "pulsos_por_credito", 1), 1, 10);
            if (string.IsNullOrEmpty(est.Sonido)) Elegir(_cmbSonido, "(sin sonido)"); else Elegir(_cmbSonido, est.Sonido);
            _nVolumen.Value = est.Volumen;
            Elegir(_cmbFuente, est.Fuente);
            Elegir(_cmbAspecto, est.Aspecto);
            _txtL1.Text = est.Linea1; _txtL2.Text = est.Linea2;
            _txtG1.Text = est.GameOver1; _txtG2.Text = est.GameOver2;
            _txtExentos.Text = g.GetString("general", "sistemas_exentos", ConfigManager.ExentosPorDefecto);
            _txtPin.Text = g.GetString("general", "pin_config", "");
        }

        private void GuardarGeneral()
        {
            var g = Ini();
            g.Set("default", "credit_key", _tecla);
            g.Set("default", "credit_pad", _cmbPad.SelectedItem?.ToString() ?? "BACK");
            g.Set("default", "pulsos_por_credito", ((int)_nPulsos.Value).ToString());
            g.Set("general", "sonido_credito", SonidoElegido());
            g.Set("general", "volumen_sonido", ((int)_nVolumen.Value).ToString());
            g.Set("general", "fuente", _cmbFuente.SelectedItem?.ToString() ?? "PressStart2P.ttf");
            g.Set("general", "aspecto_pantalla", _cmbAspecto.SelectedItem?.ToString() ?? "auto");
            g.Set("general", "texto_linea1", _txtL1.Text.Trim());
            g.Set("general", "texto_linea2", _txtL2.Text.Trim());
            g.Set("general", "texto_gameover1", _txtG1.Text.Trim());
            g.Set("general", "texto_gameover2", _txtG2.Text.Trim());
            g.Set("general", "sistemas_exentos", string.Join(",", _txtExentos.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            g.Set("general", "pin_config", _txtPin.Text.Trim());
            g.Save();
            CargarSistemas();
            _preview.Invalidate();
            Ok("Guardado. Se aplica en el próximo cartel / juego.");
        }

        private void AsignarTeclaCredito()
        {
            var k = CapturaTecla.Pedir(this, "Presioná la tecla de crédito (la que manda el monedero)");
            if (k == null) return;
            _tecla = k;
            _lblTecla.Text = NombreTecla(k);
        }

        private void DetectarPad()
        {
            var b = CapturaPad.Pedir(this);
            if (b != null) Elegir(_cmbPad, b);
        }

        private static string NombreTecla(string k) =>
            k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1]) ? $"{k[1]}  ({k})" : k;

        // =====================================================================================
        // Sistemas
        // =====================================================================================
        private readonly DataGridView _gridSis = new();
        private readonly ComboBox _cmbNuevoSis = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 120 };
        private static readonly string[] Avisos = { "overlay", "osd", "ambos", "banner" };

        private TabPage TabSistemas()
        {
            var tab = new TabPage("Sistemas");
            var g = _gridSis;
            g.Dock = DockStyle.Fill;
            g.AllowUserToAddRows = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "sistema", HeaderText = "Sistema", ReadOnly = true, FillWeight = 120 });
            g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "exento", HeaderText = "Exento", FillWeight = 55 });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "tiempo_overlay", HeaderText = "Aviso s", FillWeight = 60 });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "intervalo_loop", HeaderText = "Loop s", FillWeight = 60 });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "tiempo_countdown", HeaderText = "Cuenta s", FillWeight = 65 });
            g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "fast_forward", HeaderText = "FF", FillWeight = 35 });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "tiempo_fast_forward", HeaderText = "FF s", FillWeight = 45 });
            var colAviso = new DataGridViewComboBoxColumn { Name = "aviso", HeaderText = "Cartel", FillWeight = 75, FlatStyle = FlatStyle.Flat };
            colAviso.Items.AddRange(Avisos);
            g.Columns.Add(colAviso);
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "credit_key", HeaderText = "Tecla", FillWeight = 60 });
            var colModo = new DataGridViewComboBoxColumn { Name = "modo", HeaderText = "Modo", FillWeight = 80, FlatStyle = FlatStyle.Flat };
            colModo.Items.AddRange(GameConfig.Modos);
            g.Columns.Add(colModo);
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "minutos_libres", HeaderText = "Libres min", FillWeight = 60 });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "tiempo_timer", HeaderText = "Timer s", FillWeight = 55 });
            g.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && g.Columns[e.ColumnIndex].Name == "credit_key") AsignarTeclaSistema();
            };

            if (Directory.Exists(_romsDir))
                foreach (var d in Directory.GetDirectories(_romsDir).Select(Path.GetFileName).OrderBy(x => x))
                    _cmbNuevoSis.Items.Add(d!);

            var pie = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            pie.Controls.Add(Boton("Guardar", GuardarSistemas));
            pie.Controls.Add(_cmbNuevoSis);
            pie.Controls.Add(Boton("Agregar", AgregarSistema));
            pie.Controls.Add(Boton("Tecla del sistema...", AsignarTeclaSistema));
            pie.Controls.Add(new Label
            {
                Text = "Cartel: overlay = ventana; osd = mensaje de RetroArch; ambos = las dos (N64/OpenGL). Tecla vacía = la general. " +
                       "Modo: entrada = pide al arrancar y cada Loop; diferido = Libres min sin molestar y después el cartel; " +
                       "silencioso = free play limitado: cada Libres min un timer chico de Timer s, sin crédito cierra. Libres: 20 o 10-20 (al azar).",
                AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Color.DimGray
            });

            tab.Controls.Add(g);
            tab.Controls.Add(pie);
            CargarSistemas();
            return tab;
        }

        private List<string> Exentos() =>
            Ini().GetString("general", "sistemas_exentos", ConfigManager.ExentosPorDefecto)
                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private void CargarSistemas()
        {
            var g = Ini();
            var exentos = Exentos();
            var sistemas = _cfg.ListarSistemas().Where(s => !s.Equals("default", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var e in exentos) if (!sistemas.Contains(e, StringComparer.OrdinalIgnoreCase)) sistemas.Add(e);

            _gridSis.Rows.Clear();
            foreach (var s in new[] { "default" }.Concat(sistemas.OrderBy(x => x)))
            {
                var c = _cfg.LeerSistema(s);
                bool ex = exentos.Contains(s, StringComparer.OrdinalIgnoreCase);
                var aviso = Avisos.Contains(c.Aviso) ? c.Aviso : "overlay";
                _gridSis.Rows.Add(s, ex, c.TiempoOverlay, c.IntervaloLoop, c.TiempoCountdown, c.FastForward,
                                  c.TiempoFastForward, aviso, g.Get(s, "credit_key") ?? "", c.Modo, c.MinutosLibres, c.TiempoTimer);
            }
            if (_gridSis.Rows.Count > 0) _gridSis.Rows[0].DefaultCellStyle.BackColor = Color.LightYellow;
        }

        private void GuardarSistemas()
        {
            _gridSis.EndEdit();
            var g = Ini();
            var exentos = new List<string>();
            var errores = new List<string>();
            foreach (DataGridViewRow r in _gridSis.Rows)
            {
                var s = r.Cells["sistema"].Value?.ToString();
                if (string.IsNullOrEmpty(s)) continue;
                if (r.Cells["exento"].Value is true && s != "default") exentos.Add(s);

                foreach (var col in new[] { "tiempo_overlay", "intervalo_loop", "tiempo_countdown", "tiempo_fast_forward", "tiempo_timer" })
                {
                    var v = r.Cells[col].Value?.ToString()?.Trim();
                    if (int.TryParse(v, out var n) && n >= 0) g.Set(s, col, n.ToString());
                    else errores.Add($"{s}: {col} = '{v}'");
                }
                g.Set(s, "fast_forward", r.Cells["fast_forward"].Value is true ? "1" : "0");
                g.Set(s, "aviso", r.Cells["aviso"].Value?.ToString() ?? "overlay");
                g.Set(s, "modo", GameConfig.NormalizarModo(r.Cells["modo"].Value?.ToString()));
                var libres = r.Cells["minutos_libres"].Value?.ToString()?.Trim() ?? "";
                g.Set(s, "minutos_libres", libres.Length > 0 ? libres : "20");

                var tecla = r.Cells["credit_key"].Value?.ToString()?.Trim() ?? "";
                if (tecla.Length > 0) g.Set(s, "credit_key", tecla);
                else if (g.Get(s, "credit_key") != null)
                    g.Set(s, "credit_key", s == "default" ? "D5" : g.GetString("default", "credit_key", "D5"));
            }
            g.Set("general", "sistemas_exentos", string.Join(",", exentos));
            g.Save();
            _txtExentos.Text = string.Join(",", exentos);
            CargarSistemas();
            Ok(errores.Count == 0 ? "Sistemas guardados." : "Guardado, salvo valores inválidos:\n" + string.Join("\n", errores));
        }

        private void AgregarSistema()
        {
            var s = _cmbNuevoSis.Text.Trim().ToLowerInvariant();
            if (s.Length == 0) return;
            _cfg.AgregarSistema(s);
            CargarSistemas();
        }

        private void AsignarTeclaSistema()
        {
            var r = _gridSis.CurrentRow;
            if (r == null) return;
            var k = CapturaTecla.Pedir(this, $"Tecla de crédito para «{r.Cells["sistema"].Value}» (Esc = cancelar)");
            if (k != null) r.Cells["credit_key"].Value = k;
        }

        // =====================================================================================
        // Emuladores
        // =====================================================================================
        private readonly DataGridView _gridEmu = new();

        private TabPage TabEmuladores()
        {
            var tab = new TabPage("Emuladores");
            var g = _gridEmu;
            g.Dock = DockStyle.Fill;
            g.AllowUserToAddRows = false;
            g.RowHeadersVisible = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "emu", HeaderText = "Emulador", ReadOnly = true });
            var met = new DataGridViewComboBoxColumn { Name = "metodo", HeaderText = "Método de pausa", FlatStyle = FlatStyle.Flat };
            met.Items.AddRange("suspend", "hotkey");
            g.Columns.Add(met);
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "tecla", HeaderText = "Tecla de pausa (hotkey)" });
            g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "minimizar", HeaderText = "Minimizar al pausar" });
            g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 2) AsignarTeclaEmu(); };

            var pie = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            pie.Controls.Add(Boton("Guardar", GuardarEmuladores));
            pie.Controls.Add(Boton("Asignar tecla...", AsignarTeclaEmu));
            pie.Controls.Add(new Label
            {
                Text = "RetroArch se pausa siempre por red (network_cmd_enable = \"true\"). suspend = congela el proceso " +
                       "(sirve con cualquier emulador); hotkey = simula la tecla de pausa del emulador. Minimizar: para emuladores " +
                       "en pantalla completa exclusiva que tapan el cartel (Dolphin).",
                AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Color.DimGray
            });
            tab.Controls.Add(g);
            tab.Controls.Add(pie);

            var ini = Ini();
            foreach (var emu in new[] { "dolphin", "pcsx2", "duckstation", "redream", "ppsspp" })
            {
                var c = _cfg.ResolveEmulador(emu);
                g.Rows.Add(emu, c.MetodoPausa == "hotkey" ? "hotkey" : "suspend", c.TeclaPausa, c.MinimizarAlPausar);
            }
            return tab;
        }

        private void AsignarTeclaEmu()
        {
            var r = _gridEmu.CurrentRow;
            if (r == null) return;
            var k = CapturaTecla.Pedir(this, $"Tecla de pausa de {r.Cells["emu"].Value}");
            if (k != null) r.Cells["tecla"].Value = k;
        }

        private void GuardarEmuladores()
        {
            _gridEmu.EndEdit();
            var g = Ini();
            foreach (DataGridViewRow r in _gridEmu.Rows)
            {
                var sec = "emulador:" + r.Cells["emu"].Value;
                g.Set(sec, "metodo_pausa", r.Cells["metodo"].Value?.ToString() ?? "suspend");
                g.Set(sec, "tecla_pausa", r.Cells["tecla"].Value?.ToString() ?? "Space");
                g.Set(sec, "minimizar_al_pausar", r.Cells["minimizar"].Value is true ? "1" : "0");
            }
            g.Save();
            Ok("Emuladores guardados.");
        }

        // =====================================================================================
        // Juegos (.ini por ROM)
        // =====================================================================================
        private readonly ComboBox _cmbSisJuego = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly TextBox _txtFiltro = new() { Width = 140, PlaceholderText = "filtrar..." };
        private readonly ListBox _lstJuegos = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly CheckBox _chkActivo = new() { Text = "Usar valores propios (activo)", AutoSize = true };
        private readonly NumericUpDown _jOverlay = Num(), _jLoop = Num(), _jCount = Num(), _jFf = Num();
        private readonly CheckBox _jChkFf = new() { Text = "Fast-forward", AutoSize = true };
        private readonly ComboBox _jAviso = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly ComboBox _jModo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly TextBox _jLibres = new() { Width = 90 };
        private readonly NumericUpDown _jTimer = Num();
        private List<string> _juegos = new();

        private static NumericUpDown Num() => new() { Minimum = 0, Maximum = 86400, Width = 75 };

        private TabPage TabJuegos()
        {
            var tab = new TabPage("Juegos");
            var arriba = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            arriba.Controls.Add(new Label { Text = "Sistema:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            arriba.Controls.Add(_cmbSisJuego);
            arriba.Controls.Add(_txtFiltro);
            arriba.Controls.Add(Boton("Escanear ROMs", EscanearJuegos));

            _jAviso.Items.AddRange(Avisos);
            _jModo.Items.AddRange(GameConfig.Modos);
            var editor = new TableLayoutPanel { Dock = DockStyle.Right, Width = 250, ColumnCount = 2, Padding = new Padding(6, 0, 0, 0) };
            void Add(string e, Control c)
            {
                editor.Controls.Add(new Label { Text = e, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                editor.Controls.Add(c);
            }
            editor.Controls.Add(_chkActivo); editor.SetColumnSpan(_chkActivo, 2);
            Add("Aviso (s):", _jOverlay);
            Add("Loop (s):", _jLoop);
            Add("Countdown (s):", _jCount);
            editor.Controls.Add(_jChkFf); editor.SetColumnSpan(_jChkFf, 2);
            Add("FF hasta (s):", _jFf);
            Add("Cartel:", _jAviso);
            Add("Modo:", _jModo);
            Add("Libres (min):", _jLibres);
            Add("Timer (s):", _jTimer);
            var bGuardar = Boton("Guardar juego", GuardarJuego);
            editor.Controls.Add(bGuardar); editor.SetColumnSpan(bGuardar, 2);

            tab.Controls.Add(_lstJuegos);
            tab.Controls.Add(editor);
            tab.Controls.Add(arriba);

            foreach (var s in _cfg.ListarSistemas().Where(s => s != "default").Concat(
                         Directory.GetDirectories(_baseDir).Select(d => Path.GetFileName(d)!)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                _cmbSisJuego.Items.Add(s);
            _cmbSisJuego.SelectedIndexChanged += (_, _) => CargarJuegos();
            _txtFiltro.TextChanged += (_, _) => FiltrarJuegos();
            _lstJuegos.SelectedIndexChanged += (_, _) => CargarJuego();
            if (_cmbSisJuego.Items.Count > 0) _cmbSisJuego.SelectedIndex = 0;
            return tab;
        }

        private string? SisJuego => _cmbSisJuego.SelectedItem?.ToString();

        private void CargarJuegos()
        {
            _juegos.Clear();
            var dir = Path.Combine(_baseDir, SisJuego ?? "");
            if (SisJuego != null && Directory.Exists(dir))
                _juegos = Directory.GetFiles(dir, "*.ini").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(x => x).ToList();
            FiltrarJuegos();
        }

        private void FiltrarJuegos()
        {
            var f = _txtFiltro.Text.Trim();
            _lstJuegos.BeginUpdate();
            _lstJuegos.Items.Clear();
            foreach (var j in _juegos.Where(j => f.Length == 0 || j.Contains(f, StringComparison.OrdinalIgnoreCase)))
                _lstJuegos.Items.Add(j);
            _lstJuegos.EndUpdate();
        }

        private void EscanearJuegos()
        {
            if (SisJuego == null) return;
            var carpeta = Path.Combine(_romsDir, SisJuego);
            int n = _cfg.EscanearCarpeta(SisJuego, carpeta);
            if (n < 0) { Ok($"No existe la carpeta {carpeta}"); return; }
            CargarJuegos();
            Ok($"{n} juegos nuevos agregados (nacen sin valores propios: usan los del sistema).");
        }

        private void CargarJuego()
        {
            if (SisJuego == null || _lstJuegos.SelectedItem is not string rom) return;
            var cfg = _cfg.Resolve(SisJuego, rom);
            var ini = new IniFile(_cfg.RomIniPath(SisJuego, rom));
            _chkActivo.Checked = ini.GetBool("monedero", "activo", false);
            _jOverlay.Value = Math.Clamp(ini.GetInt("monedero", "tiempo_overlay", cfg.TiempoOverlay), 0, 86400);
            _jLoop.Value = Math.Clamp(ini.GetInt("monedero", "intervalo_loop", cfg.IntervaloLoop), 0, 86400);
            _jCount.Value = Math.Clamp(ini.GetInt("monedero", "tiempo_countdown", cfg.TiempoCountdown), 0, 86400);
            _jChkFf.Checked = ini.GetBool("monedero", "fast_forward", cfg.FastForward);
            _jFf.Value = Math.Clamp(ini.GetInt("monedero", "tiempo_fast_forward", cfg.TiempoFastForward), 0, 86400);
            Elegir(_jAviso, ini.GetString("monedero", "aviso", cfg.Aviso));
            Elegir(_jModo, GameConfig.NormalizarModo(ini.GetString("monedero", "modo", cfg.Modo)));
            _jLibres.Text = ini.GetString("monedero", "minutos_libres", cfg.MinutosLibres);
            _jTimer.Value = Math.Clamp(ini.GetInt("monedero", "tiempo_timer", cfg.TiempoTimer), 0, 86400);
        }

        private void GuardarJuego()
        {
            if (SisJuego == null || _lstJuegos.SelectedItem is not string rom) return;
            var ini = new IniFile(_cfg.RomIniPath(SisJuego, rom));
            ini.Set("monedero", "activo", _chkActivo.Checked ? "1" : "0");
            ini.Set("monedero", "tiempo_overlay", ((int)_jOverlay.Value).ToString());
            ini.Set("monedero", "intervalo_loop", ((int)_jLoop.Value).ToString());
            ini.Set("monedero", "tiempo_countdown", ((int)_jCount.Value).ToString());
            ini.Set("monedero", "fast_forward", _jChkFf.Checked ? "1" : "0");
            ini.Set("monedero", "tiempo_fast_forward", ((int)_jFf.Value).ToString());
            ini.Set("monedero", "aviso", _jAviso.SelectedItem?.ToString() ?? "overlay");
            ini.Set("monedero", "modo", GameConfig.NormalizarModo(_jModo.SelectedItem?.ToString()));
            ini.Set("monedero", "minutos_libres", _jLibres.Text.Trim().Length > 0 ? _jLibres.Text.Trim() : "20");
            ini.Set("monedero", "tiempo_timer", ((int)_jTimer.Value).ToString());
            ini.Save();
            Ok(_chkActivo.Checked ? "Guardado: este juego usa sus propios valores."
                                  : "Guardado, pero «activo» está apagado: el juego sigue usando los valores del sistema.");
        }

        // =====================================================================================
        // Vista previa con los modos del arcade
        // =====================================================================================
        private readonly ComboBox _cmbModo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
        private readonly RadioButton _rbCoin = new() { Text = "Insert coin", Checked = true, AutoSize = true };
        private readonly RadioButton _rbOver = new() { Text = "Game over", AutoSize = true };
        private readonly Label _lblInfoModo = new() { Dock = DockStyle.Bottom, Height = 34, ForeColor = Color.DimGray };
        private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 24, 24) };

        // Modos de video del arcade (CRT Emudriver / Super resoluciones); se puede ampliar con [general] modos_preview.
        private const string ModosArcade =
            "640x480,2560x240,2560x224,2560x256,2560x288,2560x448,2560x480,2560x768,2560x800," +
            "256x224,256x240,320x224,320x240,304x224,336x240,352x240,384x256,512x224,512x448," +
            "640x448,768x512,1280x224,1280x240,3520x240,5376x240,1920x1080";

        private TabPage TabVistaPrevia()
        {
            var tab = new TabPage("Vista previa");
            var arriba = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            arriba.Controls.Add(new Label { Text = "Modo:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            arriba.Controls.Add(_cmbModo);
            arriba.Controls.Add(_rbCoin);
            arriba.Controls.Add(_rbOver);

            var extra = Ini().GetString("general", "modos_preview", "");
            foreach (var m in (ModosArcade + "," + extra).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
                _cmbModo.Items.Add(m);
            _cmbModo.SelectedIndex = 1; // 2560x240
            _cmbModo.SelectedIndexChanged += (_, _) => _preview.Invalidate();
            _rbCoin.CheckedChanged += (_, _) => _preview.Invalidate();
            _preview.Paint += PintarPreview;
            _preview.Resize += (_, _) => _preview.Invalidate();

            tab.Controls.Add(_preview);
            tab.Controls.Add(_lblInfoModo);
            tab.Controls.Add(arriba);
            return tab;
        }

        /// <summary>
        /// Simula lo que se ve en el tubo: se dibuja el overlay al tamaño real del modo (igual que
        /// monedero.exe) y esa imagen se muestra en una caja 4:3 (o con el aspecto real si no es CRT).
        /// </summary>
        private void PintarPreview(object? sender, PaintEventArgs e)
        {
            var partes = (_cmbModo.SelectedItem?.ToString() ?? "640x480").Split('x');
            if (partes.Length != 2 || !int.TryParse(partes[0], out var w) || !int.TryParse(partes[1], out var h)) return;
            var modo = new Size(w, h);
            var estilo = new EstiloOverlay
            {
                Fuente = _cmbFuente.SelectedItem?.ToString() ?? "PressStart2P.ttf",
                Aspecto = _cmbAspecto.SelectedItem?.ToString() ?? "auto",
                Linea1 = _txtL1.Text, Linea2 = _txtL2.Text, GameOver1 = _txtG1.Text, GameOver2 = _txtG2.Text
            };
            bool crt = OverlayRenderer.EsCrt(modo, estilo.Aspecto);
            double aspecto = crt ? 4.0 / 3.0 : (double)w / h;

            var area = _preview.ClientRectangle;
            area.Inflate(-10, -10);
            int bw = area.Width, bh = (int)(bw / aspecto);
            if (bh > area.Height) { bh = area.Height; bw = (int)(bh * aspecto); }
            var caja = new Rectangle(area.X + (area.Width - bw) / 2, area.Y + (area.Height - bh) / 2, bw, bh);

            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
                OverlayRenderer.Dibujar(g, new Rectangle(0, 0, w, h), modo, estilo, _baseDir, "17", _rbOver.Checked);

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear; // el tubo mezcla en horizontal
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(bmp, caja);
            using var pen = new Pen(Color.Gray);
            e.Graphics.DrawRectangle(pen, caja);

            int k = Math.Max(1, (int)Math.Round(h / 240.0));
            int vh = (int)Math.Ceiling(h / (double)k);
            _lblInfoModo.Text = crt
                ? $"{w}x{h} en CRT 4:3 → canvas de {vh} líneas (x{k}), ancho lógico {Math.Round(vh * 4.0 / 3.0)}, texto rasterizado a {w} px de ancho."
                : $"{w}x{h} con píxeles cuadrados (monitor moderno).";
        }
    }

    /// <summary>Diálogo que captura la próxima tecla (incluye flechas, F1-F12, numpad).</summary>
    internal class CapturaTecla : Form
    {
        public string? Resultado { get; private set; }

        private CapturaTecla(string mensaje)
        {
            Text = "Asignar tecla";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(360, 90);
            Controls.Add(new Label { Text = mensaje + "\n\nEsc = cancelar", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            if (k is Keys.ShiftKey or Keys.ControlKey or Keys.Menu) return true;
            if (k != Keys.Escape) Resultado = k.ToString();
            Close();
            return true;
        }

        public static string? Pedir(IWin32Window owner, string mensaje)
        {
            using var f = new CapturaTecla(mensaje);
            f.ShowDialog(owner);
            return f.Resultado;
        }
    }

    /// <summary>Detecta el primer botón XInput que se presione (hasta 8 s).</summary>
    internal class CapturaPad : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint i, out XINPUT_STATE s);

        private static readonly (ushort Mask, string Name)[] Mapa =
        {
            (0x0020, "BACK"), (0x0010, "START"), (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"),
            (0x0100, "LB"), (0x0200, "RB"), (0x0040, "L3"), (0x0080, "R3"),
            (0x0001, "UP"), (0x0002, "DOWN"), (0x0004, "LEFT"), (0x0008, "RIGHT")
        };

        private readonly ushort[] _prev = new ushort[4];
        private readonly System.Windows.Forms.Timer _t = new() { Interval = 40 };
        private DateTime _fin = DateTime.Now.AddSeconds(8);
        public string? Resultado { get; private set; }

        private static ushort Leer(uint i)
        {
            try { return XInputGetState(i, out var s) == 0 ? s.Buttons : (ushort)0; } catch { return 0; }
        }

        private CapturaPad()
        {
            Text = "Detectar botón";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(340, 80);
            Controls.Add(new Label { Text = "Presioná el botón de crédito en el joystick (XInput)...\n8 segundos", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
            for (uint i = 0; i < 4; i++) _prev[i] = Leer(i);
            _t.Tick += (_, _) =>
            {
                for (uint i = 0; i < 4; i++)
                {
                    var cur = Leer(i);
                    var nuevos = (ushort)(cur & ~_prev[i]);
                    _prev[i] = cur;
                    foreach (var (m, n) in Mapa)
                        if ((nuevos & m) != 0) { Resultado = n; Close(); return; }
                }
                if (DateTime.Now > _fin) Close();
            };
            _t.Start();
        }

        public static string? Pedir(IWin32Window owner)
        {
            using var f = new CapturaPad();
            f.ShowDialog(owner);
            return f.Resultado;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _t.Dispose();
            base.Dispose(disposing);
        }
    }
}
