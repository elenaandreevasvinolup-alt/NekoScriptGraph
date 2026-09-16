using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NekoScriptGraph
{
    public enum NsgJsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// Минимальная модель JSON для протокола MCP.
    ///
    /// Своя, а не JsonUtility, по одной причине: JsonUtility умеет только
    /// заранее известный тип. А JSON-RPC 2.0 присылает произвольный объект
    /// <c>arguments</c>, состав которого зависит от вызванного инструмента, —
    /// его нельзя описать классом. Здесь же нужен динамический разбор.
    ///
    /// Парсер написан вручную и намеренно строгий: принимает только корректный
    /// JSON, ограничивает глубину вложенности (иначе стек сорвётся на
    /// специально склеенном запросе) и не глотает мусор после значения.
    /// </summary>
    public class NsgJson
    {
        public NsgJsonKind Kind;
        public bool Bool;
        public double Number;
        public string Str;

        public List<NsgJson> Items;    // Array
        public List<string> Keys;      // Object
        public List<NsgJson> Values;   // Object

        const int MaxDepth = 64;

        // ------------------------------------------------------------------
        // Создание
        // ------------------------------------------------------------------

        public static NsgJson Null()
        {
            return new NsgJson { Kind = NsgJsonKind.Null };
        }

        public static NsgJson NewBool(bool v)
        {
            return new NsgJson { Kind = NsgJsonKind.Bool, Bool = v };
        }

        public static NsgJson NewNumber(double v)
        {
            return new NsgJson { Kind = NsgJsonKind.Number, Number = v };
        }

        public static NsgJson NewString(string v)
        {
            return new NsgJson { Kind = NsgJsonKind.String, Str = v ?? string.Empty };
        }

        public static NsgJson NewArray()
        {
            return new NsgJson { Kind = NsgJsonKind.Array, Items = new List<NsgJson>() };
        }

        public static NsgJson NewObject()
        {
            return new NsgJson
            {
                Kind = NsgJsonKind.Object,
                Keys = new List<string>(),
                Values = new List<NsgJson>()
            };
        }

        // ------------------------------------------------------------------
        // Наполнение
        // ------------------------------------------------------------------

        public NsgJson Add(NsgJson v)
        {
            if (Kind != NsgJsonKind.Array) return this;
            Items.Add(v ?? Null());
            return this;
        }

        public NsgJson Set(string key, NsgJson v)
        {
            if (Kind != NsgJsonKind.Object || string.IsNullOrEmpty(key)) return this;

            int i = Keys.IndexOf(key);
            if (i >= 0) Values[i] = v ?? Null();
            else
            {
                Keys.Add(key);
                Values.Add(v ?? Null());
            }
            return this;
        }

        public NsgJson Set(string key, string v)
        {
            return Set(key, v == null ? Null() : NewString(v));
        }

        public NsgJson Set(string key, double v)
        {
            return Set(key, NewNumber(v));
        }

        public NsgJson Set(string key, int v)
        {
            return Set(key, NewNumber(v));
        }

        public NsgJson Set(string key, bool v)
        {
            return Set(key, NewBool(v));
        }

        // ------------------------------------------------------------------
        // Чтение
        // ------------------------------------------------------------------

        public bool IsNull
        {
            get { return Kind == NsgJsonKind.Null; }
        }

        public int Count
        {
            get
            {
                if (Kind == NsgJsonKind.Array) return Items.Count;
                if (Kind == NsgJsonKind.Object) return Keys.Count;
                return 0;
            }
        }

        public bool Has(string key)
        {
            return Kind == NsgJsonKind.Object && Keys.IndexOf(key) >= 0;
        }

        /// <summary>Поле объекта. Отсутствующее поле — null, а не исключение.</summary>
        public NsgJson Get(string key)
        {
            if (Kind != NsgJsonKind.Object) return null;
            int i = Keys.IndexOf(key);
            return i >= 0 ? Values[i] : null;
        }

        public NsgJson Get(int index)
        {
            if (Kind != NsgJsonKind.Array) return null;
            if (index < 0 || index >= Items.Count) return null;
            return Items[index];
        }

        public string AsString(string fallback)
        {
            if (Kind == NsgJsonKind.String) return Str;
            return fallback;
        }

        public double AsNumber(double fallback)
        {
            if (Kind == NsgJsonKind.Number) return Number;
            return fallback;
        }

        public int AsInt(int fallback)
        {
            if (Kind != NsgJsonKind.Number) return fallback;
            return (int)System.Math.Round(Number);
        }

        public bool AsBool(bool fallback)
        {
            if (Kind == NsgJsonKind.Bool) return Bool;
            return fallback;
        }

        /// <summary>Строка объекта в виде «ключ=значение» для отчётов об ошибке.</summary>
        public string DescribeKeys()
        {
            if (Kind != NsgJsonKind.Object || Keys.Count == 0) return "(none)";
            return string.Join(", ", Keys.ToArray());
        }

        // ------------------------------------------------------------------
        // Разбор
        // ------------------------------------------------------------------

        /// <summary>
        /// Разбирает текст. Возвращает false и заполняет error при любой
        /// некорректности — молчаливое «что-то распарсилось» здесь опаснее
        /// отказа, потому что дальше по конвейеру идут записи на диск.
        /// </summary>
        public static bool TryParse(string text, out NsgJson value, out string error)
        {
            value = null;
            error = null;

            if (text == null)
            {
                error = "empty input";
                return false;
            }

            int i = 0;
            try
            {
                SkipWhitespace(text, ref i);
                if (i >= text.Length)
                {
                    error = "empty input";
                    return false;
                }

                value = ParseValue(text, ref i, 0);

                SkipWhitespace(text, ref i);
                if (i != text.Length)
                {
                    error = "trailing characters at offset " + i;
                    value = null;
                    return false;
                }
                return true;
            }
            catch (JsonError e)
            {
                error = e.Message;
                value = null;
                return false;
            }
        }

        class JsonError : System.Exception
        {
            public JsonError(string m) : base(m) { }
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        static NsgJson ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new JsonError("nesting is too deep (limit " + MaxDepth + ")");

            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new JsonError("unexpected end of input");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i, depth);
                case '[': return ParseArray(s, ref i, depth);
                case '"': return NewString(ParseString(s, ref i));
                case 't': Expect(s, ref i, "true"); return NewBool(true);
                case 'f': Expect(s, ref i, "false"); return NewBool(false);
                case 'n': Expect(s, ref i, "null"); return Null();
            }

            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
            throw new JsonError("unexpected character '" + c + "' at offset " + i);
        }

        static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new JsonError("expected '" + word + "' at offset " + i);
            i += word.Length;
        }

        static NsgJson ParseObject(string s, ref int i, int depth)
        {
            var o = NewObject();
            i++; // {

            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new JsonError("expected a key string at offset " + i);

                string key = ParseString(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new JsonError("expected ':' at offset " + i);
                i++;

                var v = ParseValue(s, ref i, depth + 1);
                o.Set(key, v);

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new JsonError("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return o; }
                throw new JsonError("expected ',' or '}' at offset " + i);
            }
        }

        static NsgJson ParseArray(string s, ref int i, int depth)
        {
            var a = NewArray();
            i++; // [

            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }

            while (true)
            {
                a.Add(ParseValue(s, ref i, depth + 1));

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new JsonError("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new JsonError("expected ',' or ']' at offset " + i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();

            while (true)
            {
                if (i >= s.Length) throw new JsonError("unterminated string");
                char c = s[i++];

                if (c == '"') return sb.ToString();

                if (c != '\\')
                {
                    // Управляющие символы внутри строки запрещены стандартом.
                    if (c < 0x20) throw new JsonError("raw control character in a string at offset " + (i - 1));
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length) throw new JsonError("unterminated escape");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new JsonError("truncated \\u escape");
                        int code = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int d = HexDigit(s[i + k]);
                            if (d < 0) throw new JsonError("bad \\u escape at offset " + (i + k));
                            code = code * 16 + d;
                        }
                        i += 4;
                        sb.Append((char)code);
                        break;
                    default:
                        throw new JsonError("unknown escape '\\" + e + "'");
                }
            }
        }

        static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        static NsgJson ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && s[i] == '-') i++;

            // Ведущий ноль запрещён стандартом: 0 и 0.5 можно, 01 нельзя.
            if (i < s.Length && s[i] == '0')
            {
                i++;
                if (i < s.Length && s[i] >= '0' && s[i] <= '9')
                    throw new JsonError("leading zero in a number at offset " + start);
            }
            else
            {
                int digits = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                if (digits == 0) throw new JsonError("bad number at offset " + start);
            }

            if (i < s.Length && s[i] == '.')
            {
                i++;
                int frac = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; frac++; }
                if (frac == 0) throw new JsonError("no digits after the decimal point at offset " + start);
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int exp = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; exp++; }
                if (exp == 0) throw new JsonError("no digits in the exponent at offset " + start);
            }

            string raw = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JsonError("bad number '" + raw + "' at offset " + start);

            return NewNumber(d);
        }

        // ------------------------------------------------------------------
        // Запись
        // ------------------------------------------------------------------

        public string ToJson()
        {
            return ToJson(false);
        }

        public string ToJson(bool pretty)
        {
            var sb = new StringBuilder();
            Write(sb, this, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, NsgJson v, bool pretty, int depth)
        {
            if (v == null) { sb.Append("null"); return; }

            switch (v.Kind)
            {
                case NsgJsonKind.Null:
                    sb.Append("null");
                    return;

                case NsgJsonKind.Bool:
                    sb.Append(v.Bool ? "true" : "false");
                    return;

                case NsgJsonKind.Number:
                    WriteNumber(sb, v.Number);
                    return;

                case NsgJsonKind.String:
                    WriteString(sb, v.Str);
                    return;

                case NsgJsonKind.Array:
                    WriteArray(sb, v, pretty, depth);
                    return;

                case NsgJsonKind.Object:
                    WriteObject(sb, v, pretty, depth);
                    return;
            }
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("0"); return; }

            // Целые пишем без «.0»: так ответ читается и diff'ится.
            if (d == System.Math.Floor(d) && System.Math.Abs(d) < 1e15)
            {
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                return;
            }

            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteArray(StringBuilder sb, NsgJson v, bool pretty, int depth)
        {
            if (v.Items == null || v.Items.Count == 0) { sb.Append("[]"); return; }

            sb.Append('[');
            for (int i = 0; i < v.Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty) NewLine(sb, depth + 1);
                Write(sb, v.Items[i], pretty, depth + 1);
            }
            if (pretty) NewLine(sb, depth);
            sb.Append(']');
        }

        static void WriteObject(StringBuilder sb, NsgJson v, bool pretty, int depth)
        {
            if (v.Keys == null || v.Keys.Count == 0) { sb.Append("{}"); return; }

            sb.Append('{');
            for (int i = 0; i < v.Keys.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty) NewLine(sb, depth + 1);
                WriteString(sb, v.Keys[i]);
                sb.Append(':');
                if (pretty) sb.Append(' ');
                Write(sb, v.Values[i], pretty, depth + 1);
            }
            if (pretty) NewLine(sb, depth);
            sb.Append('}');
        }

        static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
