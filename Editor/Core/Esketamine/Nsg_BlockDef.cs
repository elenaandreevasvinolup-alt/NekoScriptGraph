using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>Цвета категорий. Спрайты тонируются ими, поэтому одного
    /// серого 9-slice набора хватает на все категории.</summary>
    public static class Nsg_Palette
    {
        public static readonly Color Variable = new Color32(0xFF, 0x8C, 0x1A, 0xFF);
        public static readonly Color Control = new Color32(0xFF, 0xAB, 0x19, 0xFF);
        public static readonly Color Expression = new Color32(0x4C, 0x97, 0xFF, 0xFF);
        public static readonly Color Escape = new Color32(0x8C, 0x8C, 0x8C, 0xFF);
        public static readonly Color Api = new Color32(0x99, 0x66, 0xFF, 0xFF);
        /// <summary>Свои блоки: заметно отличаются от всего остального.</summary>
        public static readonly Color Macro = new Color32(0x2F, 0xA8, 0x9B, 0xFF);
        public static readonly Color Frame = new Color32(0x5A, 0x63, 0x74, 0xFF);
        public static readonly Color Header = new Color32(0x2E, 0x33, 0x3D, 0xFF);
        public static readonly Color Error = new Color32(0xFF, 0x5A, 0x5A, 0xFF);
        public static readonly Color Warning = new Color32(0xFF, 0xC1, 0x4D, 0xFF);
        public static readonly Color Info = new Color32(0x7F, 0xB3, 0xFF, 0xFF);

        public static Color ForCategoryKey(string key)
        {
            switch (key)
            {
                case "cat.var": return Variable;
                case "cat.ctrl": return Control;
                case "cat.expr": return Expression;
                case "cat.raw": return Escape;
                case "cat.api": return Api;
                case "cat.macro": return Macro;
                case "cat.frame": return Frame;
            }
            return Frame;
        }

        public static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string s = hex.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8) return fallback;
            try
            {
                byte r = System.Convert.ToByte(s.Substring(0, 2), 16);
                byte g = System.Convert.ToByte(s.Substring(2, 2), 16);
                byte b = System.Convert.ToByte(s.Substring(4, 2), 16);
                byte a = s.Length == 8 ? System.Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
                return new Color32(r, g, b, a);
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>Читаемый цвет текста для заданного фона.</summary>
        public static Color TextOn(Color background)
        {
            float luma = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
            return luma > 0.6f ? new Color(0.12f, 0.12f, 0.14f) : Color.white;
        }
    }

    /// <summary>
    /// Декларативное определение блока. Именно этот файл агент должен
    /// править вместо правки C#.
    /// </summary>
    [System.Serializable]
    public class NsgSocketDef
    {
        public string name;
        public string kind = "expr"; // expr | text | var
        public bool required;
        public bool variadic;

        /// <summary>
        /// Допустимые значения текстового слота. Если задано, редактор рисует
        /// выпадающий список вместо поля ввода: так «подобные блоки» не
        /// размножаются, а оператор выбирается прямо в блоке.
        /// </summary>
        public string[] choices;
    }

    [System.Serializable]
    public class NsgBlockDef
    {
        public string id;
        public string level = "high";      // high | low
        public string shape = "statement"; // statement | expression | control
        public string category = "";       // legacy display string
        public string categoryKey = "";    // localisation key, e.g. "cat.var"
        public string label = "";          // Chinese (also the fallback)
        public string labelEn = "";
        public string labelRu = "";
        public NsgSocketDef[] sockets = new NsgSocketDef[0];
        public string emit = "";           // "{{0}} = {{1}};"
        public string node = "";           // AST match key
        public string op = "";             // operator, for op-parameterised blocks
        public string color = "";          // "#RRGGBB"; empty = category colour
        public string matchCall = "";      // static call target, e.g. "UnityEngine.Debug.Log"
        public int matchArity = -1;        // -1 = any
        public bool builtin;               // printed by code (precedence aware)
        public string manual = "";

        /// <summary>
        /// Группа вариантов: блоки с одинаковой формой, отличающиеся только
        /// оператором. В редакторе такая группа показывается одним блоком с
        /// выпадающим списком вместо шести отдельных блоков.
        /// </summary>
        public string variantGroup = "";

        /// <summary>Подпись варианта в списке (обычно сам оператор).</summary>
        public string variantLabel = "";

        public int SocketCount
        {
            get { return sockets == null ? 0 : sockets.Length; }
        }

        public int VariadicIndex
        {
            get
            {
                if (sockets == null) return -1;
                for (int i = 0; i < sockets.Length; i++)
                {
                    if (sockets[i].variadic) return i;
                }
                return -1;
            }
        }

        public bool IsControl
        {
            get { return shape == "control"; }
        }

        public bool IsExpression
        {
            get { return shape == "expression"; }
        }

        // ------------------------------------------------------------------

        public string Label()
        {
            // Встроенный язык — английский. Остальные приходят данными из
            // Locale/<code>/strings.json по идентификатору блока.
            if (!Nsg_L10n.IsEnglish)
            {
                string localized = Nsg_L10n.BlockLabel(id);
                if (!string.IsNullOrEmpty(localized)) return localized;
            }

            // Английская подпись есть у всех блоков; поле label остаётся
            // последним рубежом на случай старых файлов без labelEn.
            if (!string.IsNullOrEmpty(labelEn)) return labelEn;
            return label;
        }

        public string CategoryLabel()
        {
            if (!string.IsNullOrEmpty(categoryKey)) return Nsg_L10n.T(categoryKey);
            return category;
        }

        public Color BlockColor()
        {
            Color fallback = Nsg_Palette.ForCategoryKey(categoryKey);
            return Nsg_Palette.ParseHex(color, fallback);
        }

        public string RenderLabel(string[] values)
        {
            return NsgTemplate.RenderBraced(Label(), values);
        }

        public string RenderEmit(string[] values)
        {
            return NsgTemplate.Render(emit, values);
        }
    }

    [System.Serializable]
    public class NsgBlockPack
    {
        public NsgBlockDef[] blocks;
    }

    public static class NsgTemplate
    {
        /// <summary>Подставляет заполнители в одинарных скобках {n}. Для подписей на экране.</summary>
        public static string RenderBraced(string template, string[] values)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            var sb = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] == '{')
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        string inner = template.Substring(i + 1, close - (i + 1)).Trim();
                        int n;
                        if (int.TryParse(inner, out n) && values != null && n >= 0 && n < values.Length)
                        {
                            sb.Append(values[n] ?? string.Empty);
                            i = close;
                            continue;
                        }
                    }
                }
                sb.Append(template[i]);
            }
            return sb.ToString();
        }

        /// <summary>Подставляет заполнители {{n}}. Двойные скобки отличают
        /// шаблон от подписей в одинарных скобках.</summary>
        public static string Render(string template, string[] values)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            var sb = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    int close = template.IndexOf("}}", i + 2, System.StringComparison.Ordinal);
                    if (close > 0)
                    {
                        string inner = template.Substring(i + 2, close - (i + 2)).Trim();
                        int n;
                        if (int.TryParse(inner, out n) && values != null && n >= 0 && n < values.Length)
                        {
                            sb.Append(values[n] ?? string.Empty);
                        }
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(template[i]);
            }
            return sb.ToString();
        }
    }
}
