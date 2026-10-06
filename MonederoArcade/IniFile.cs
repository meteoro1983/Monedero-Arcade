using System.Text;

namespace MonederoArcade
{
    /// <summary>
    /// Lector/escritor de .ini minimalista, sin dependencias externas.
    /// Soporta secciones [nombre] y pares clave=valor. Preserva comentarios (#/;)
    /// y líneas en blanco al reescribir, modificando solo el valor de la clave tocada
    /// (edición quirúrgica, según lo definido en la spec).
    /// </summary>
    public class IniFile
    {
        private readonly List<string> _lines = new();
        public string Path { get; }

        public IniFile(string path)
        {
            Path = path;
            if (File.Exists(path))
                _lines.AddRange(File.ReadAllLines(path));
        }

        public static IniFile LoadOrEmpty(string path) => new IniFile(path);

        public string? Get(string section, string key)
        {
            string? currentSection = null;
            foreach (var raw in _lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (currentSection != null &&
                    string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var k = line[..idx].Trim();
                    var v = line[(idx + 1)..].Trim();
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        return v;
                }
            }
            return null;
        }

        public List<string> Sections()
        {
            var res = new List<string>();
            foreach (var raw in _lines)
            {
                var l = raw.Trim();
                if (l.StartsWith("[") && l.EndsWith("]")) res.Add(l[1..^1].Trim());
            }
            return res;
        }

        public int GetInt(string section, string key, int fallback)
        {
            var v = Get(section, key);
            return int.TryParse(v, out var n) ? n : fallback;
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            var v = Get(section, key);
            if (v == null) return fallback;
            return v.Trim() == "1" || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public string GetString(string section, string key, string fallback)
        {
            return Get(section, key) ?? fallback;
        }

        /// <summary>
        /// Crea (o reemplaza por completo) una sección con los pares clave=valor dados.
        /// Usado solo para generar archivos nuevos (plantilla por ROM), no para editar
        /// archivos existentes con contenido del usuario.
        /// </summary>
        public static void WriteFreshSection(string path, string section, IEnumerable<(string Key, string Value)> pairs)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine($"[{section}]");
            foreach (var (k, v) in pairs)
                sb.AppendLine($"{k}={v}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Edición quirúrgica: cambia el valor de una clave existente, o la agrega al final
        /// de la sección si no existía, sin tocar el resto del archivo.
        /// </summary>
        public void Set(string section, string key, string value)
        {
            int sectionStart = -1, sectionEnd = -1;
            int keyLine = -1;

            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var name = line[1..^1].Trim();
                    if (sectionStart == -1 && string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionStart = i;
                        continue;
                    }
                    if (sectionStart != -1 && sectionEnd == -1)
                    {
                        sectionEnd = i;
                    }
                }

                if (sectionStart != -1 && (sectionEnd == -1 || i < sectionEnd))
                {
                    var idx = line.IndexOf('=');
                    if (idx > 0)
                    {
                        var k = line[..idx].Trim();
                        if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        {
                            keyLine = i;
                        }
                    }
                }
            }
            if (sectionEnd == -1 && sectionStart != -1) sectionEnd = _lines.Count;

            if (keyLine != -1)
            {
                _lines[keyLine] = $"{key}={value}";
            }
            else if (sectionStart != -1)
            {
                _lines.Insert(sectionEnd, $"{key}={value}");
            }
            else
            {
                if (_lines.Count > 0 && _lines[^1].Trim().Length != 0) _lines.Add("");
                _lines.Add($"[{section}]");
                _lines.Add($"{key}={value}");
            }
        }

        public void Save()
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(Path, _lines, Encoding.UTF8);
        }
    }
}
