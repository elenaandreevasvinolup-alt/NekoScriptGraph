using System.Collections.Generic;
using System.Text;

namespace NekoScriptGraph
{
    /// <summary>
    /// Один узел дерева структуры скрипта.
    ///
    ///   raw       дословный текст (using, поля, атрибуты, свойства, ...)
    ///   method    C-образная рамка, тело которой — стопка блоков-операторов
    ///   type      class / struct / interface / enum, C-образная рамка членов
    ///   namespace C-образная рамка типов
    ///
    /// Каждый узел хранит точный исходный текст вокруг себя, поэтому
    /// пересборка файла воспроизводит исходные байты. Именно это сохраняет
    /// [SerializeField], атрибуты, комментарии и ссылки в инспекторе.
    /// </summary>
    [System.Serializable]
    public class NsgStructNode
    {
        public string kind;      // raw | method | type | namespace
        public string leading;   // verbatim text before this node

        public string header;    // verbatim text from the node start up to (not including) '{'

        // только для типа: заголовок разбит, чтобы список баз правился безопасно
        public string headerPrefix;      // "public class "
        public string name;              // "CompassManager" / "Foo<T>"
        public string headerBasePrefix;  // " : " or ""
        public string baseList;          // "MonoBehaviour" or ""
        public string headerSuffix;      // whitespace between the base list and '{'

        public string typeKeyword;       // class | struct | interface | enum

        /// <summary>
        /// Рамка структурного блока (Shader, Properties, Pass, Tags...).
        /// Ставится разделителем файла. Нужна интерфейсу, чтобы не показывать
        /// у таких рамок список базового типа.
        /// </summary>
        public bool isContainer;

        public string tail;              // verbatim text from the last child's end through '}'

        public string text;              // raw only

        public string printed;           // method only: verbatim body including braces
        public int line;

        // Не сериализуется внутри дерева: держать графы вне дерева — значит
        // сохранять вложенность JSON плоской, а это важно, потому что у
        // сериализатора Unity есть предел глубины. Плоский список лежит в
        // NsgFileModel.graphs.
        [System.NonSerialized] public NsgMethodGraph graph;

        public List<NsgStructNode> children = new List<NsgStructNode>();

        public bool IsContainer
        {
            get { return kind == "type" || kind == "namespace"; }
        }

        public string RenderHeader()
        {
            if (kind == "type")
            {
                return (headerPrefix ?? string.Empty) + (name ?? string.Empty) +
                       (headerBasePrefix ?? string.Empty) + (baseList ?? string.Empty) +
                       (headerSuffix ?? string.Empty);
            }
            return header ?? string.Empty;
        }

        public void Render(StringBuilder sb)
        {
            sb.Append(leading ?? string.Empty);

            if (kind == "raw" || kind == "field")
            {
                sb.Append(text ?? string.Empty);
                return;
            }

            if (kind == "method")
            {
                sb.Append(header ?? string.Empty);
                sb.Append(printed ?? string.Empty);
                return;
            }

            sb.Append(RenderHeader());
            sb.Append('{');
            for (int i = 0; i < children.Count; i++) children[i].Render(sb);
            sb.Append(tail ?? string.Empty);
        }

        public int CountMethods()
        {
            int n = kind == "method" ? 1 : 0;
            for (int i = 0; i < children.Count; i++) n += children[i].CountMethods();
            return n;
        }

        public void CollectMethods(List<NsgStructNode> into)
        {
            if (kind == "method") into.Add(this);
            for (int i = 0; i < children.Count; i++) children[i].CollectMethods(into);
        }
    }

    /// <summary>
    /// Плоский контейнер графа блоков одного метода. Графы хранятся рядом с
    /// деревом, а не внутри него, чтобы сериализованная вложенность оставалась
    /// плоской.
    /// </summary>
    [System.Serializable]
    public class NsgGraphSlot
    {
        public string key;   // the method header, which is unique within a file
        public NsgMethodGraph graph;
    }

    [System.Serializable]
    public class NsgFileModel
    {
        public int schemaVersion = 2;
        public string target = "csharp";
        public string engineMin = "0.1.0";
        public string sourceFile;
        public string sourceGuid;

        public List<NsgStructNode> roots = new List<NsgStructNode>();
        public List<NsgGraphSlot> graphs = new List<NsgGraphSlot>();
        public string epilogue;

        public int MethodCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < roots.Count; i++) n += roots[i].CountMethods();
                return n;
            }
        }

        public List<NsgStructNode> AllMethods()
        {
            var list = new List<NsgStructNode>();
            for (int i = 0; i < roots.Count; i++) roots[i].CollectMethods(list);
            return list;
        }

        /// <summary>Копирует графы из памяти в плоский сериализуемый список.</summary>
        public void PackGraphs()
        {
            graphs = new List<NsgGraphSlot>();
            var methods = AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                if (methods[i].graph == null) continue;
                graphs.Add(new NsgGraphSlot { key = methods[i].header, graph = methods[i].graph });
            }
        }

        /// <summary>Раскладывает плоский список обратно по дереву.</summary>
        public void UnpackGraphs()
        {
            var map = new Dictionary<string, NsgMethodGraph>();
            if (graphs != null)
            {
                for (int i = 0; i < graphs.Count; i++)
                {
                    var s = graphs[i];
                    if (s == null || string.IsNullOrEmpty(s.key)) continue;
                    map[s.key] = s.graph;
                }
            }

            var methods = AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                NsgMethodGraph g;
                if (map.TryGetValue(methods[i].header, out g)) methods[i].graph = g;
                else if (methods[i].graph == null) methods[i].graph = new NsgMethodGraph();
            }
        }
    }

    /// <summary>
    /// Разбивает файл C# на нетронутый каркас и управляемые тела методов,
    /// в виде дерева, чтобы редактор мог рисовать рамки namespace / class / метода.
    /// </summary>
    public static class NsgFileSplitter
    {
        // ------------------------------------------------------------------
        // Разбор
        // ------------------------------------------------------------------

        public static NsgFileModel Split(string source, string sourceFile, NsgDiagnostics diag)
        {
            return Split(source, sourceFile, diag, null);
        }

        /// <summary>
        /// Профиль языка задаёт, какие слова открывают объявление типа
        /// (class / struct / union / enum / namespace / cbuffer...).
        /// null — прежнее поведение (C#).
        /// </summary>
        public static NsgFileModel Split(string source, string sourceFile, NsgDiagnostics diag,
                                         Nsg_LanguageProfile profile)
        {
            var model = new NsgFileModel();
            model.sourceFile = sourceFile;
            if (source == null) source = string.Empty;

            var tokens = new NsgLexer(source, diag, profile).Lex();
            int n = tokens.Count;

            var depth = new int[n];
            int d = 0;
            for (int i = 0; i < n; i++)
            {
                var t = tokens[i];
                if (t.Kind == NsgTokenKind.Punct)
                {
                    if (t.Text == "{") d++;
                    else if (t.Text == "}") d--;
                }
                depth[i] = d;
            }

            int prevEnd = 0;
            model.roots = ParseRange(source, tokens, depth, 0, n, 0, ref prevEnd, diag, profile);
            model.epilogue = prevEnd <= source.Length ? source.Substring(prevEnd) : string.Empty;
            return model;
        }

        static List<NsgStructNode> ParseRange(string src, List<NsgToken> t, int[] depth,
                                              int from, int to, int depthLevel,
                                              ref int prevEnd, NsgDiagnostics diag,
                                              Nsg_LanguageProfile profile)
        {
            var nodes = new List<NsgStructNode>();
            if (from >= to) return nodes;

            int i = from;
            int declStart = from;

            while (i < to)
            {
                if (depth[i] != depthLevel)
                {
                    i++;
                    continue;
                }

                var tk = t[i];

                // ---- конец дословного члена ----
                if (tk.Is(";") || tk.Is("}"))
                {
                    int endTok = i + 1;
                    if (declStart < endTok && t[declStart].Start < tk.End)
                    {
                        // Член с семантикой (HLSL): "float4 vertex : POSITION;"
                        // становится отдельным блоком, остальное — сырым текстом.
                        var field = TryMakeField(src, t, declStart, endTok, ref prevEnd);
                        if (field != null) nodes.Add(field);
                        else nodes.Add(MakeRaw(src, t, declStart, endTok, ref prevEnd));
                    }
                    else
                    {
                        prevEnd = tk.End;
                    }
                    i = endTok;
                    declStart = i;
                    continue;
                }

                // ---- объявление типа ----
                if (tk.Kind == NsgTokenKind.Keyword && IsTypeKeyword(tk.Text, profile))
                {
                    int braceIdx = FindOpenBrace(t, i, to);
                    if (braceIdx > 0)
                    {
                        int closeIdx = FindMatchingClose(t, depth, braceIdx, depthLevel, to);
                        if (closeIdx > 0)
                        {
                            var node = MakeType(src, t, declStart, i, braceIdx, tk.Text);
                            node.leading = src.Substring(prevEnd, t[declStart].Start - prevEnd);

                            int childPrev = t[braceIdx].End;
                            node.children = ParseRange(src, t, depth, braceIdx + 1, closeIdx,
                                                       depthLevel + 1, ref childPrev, diag, profile);
                            node.tail = src.Substring(childPrev, t[closeIdx].End - childPrev);

                            prevEnd = t[closeIdx].End;
                            nodes.Add(node);
                            i = closeIdx + 1;
                            declStart = i;
                            continue;
                        }
                    }
                }

                // ---- пространство имён (только блочная форма) ----
                if (tk.Is("namespace"))
                {
                    int braceIdx = FindOpenBrace(t, i, to);
                    if (braceIdx > 0)
                    {
                        int closeIdx = FindMatchingClose(t, depth, braceIdx, depthLevel, to);
                        if (closeIdx > 0)
                        {
                            var node = new NsgStructNode { kind = "namespace" };
                            node.leading = src.Substring(prevEnd, t[declStart].Start - prevEnd);
                            node.header = src.Substring(t[declStart].Start, t[braceIdx].Start - t[declStart].Start);
                            node.name = NameAfterKeyword(src, t, i, braceIdx);

                            int childPrev = t[braceIdx].End;
                            node.children = ParseRange(src, t, depth, braceIdx + 1, closeIdx,
                                                       depthLevel + 1, ref childPrev, diag, profile);
                            node.tail = src.Substring(childPrev, t[closeIdx].End - childPrev);

                            prevEnd = t[closeIdx].End;
                            nodes.Add(node);
                            i = closeIdx + 1;
                            declStart = i;
                            continue;
                        }
                    }
                }

                // ---- структурный блок ShaderLab ----
                // Shader "X" { }, Properties { }, SubShader { }, Pass { }, Tags { }
                if (IsContainerKeyword(tk.Text, profile))
                {
                    int braceIdx = FindOpenBrace(t, i, to);
                    if (braceIdx > 0)
                    {
                        int closeIdx = FindMatchingClose(t, depth, braceIdx, depthLevel, to);
                        if (closeIdx > 0)
                        {
                            var node = MakeType(src, t, declStart, i, braceIdx, tk.Text, true);
                            node.leading = src.Substring(prevEnd, t[declStart].Start - prevEnd);

                            int childPrev = t[braceIdx].End;
                            node.children = ParseRange(src, t, depth, braceIdx + 1, closeIdx,
                                                       depthLevel + 1, ref childPrev, diag, profile);
                            node.tail = src.Substring(childPrev, t[closeIdx].End - childPrev);

                            prevEnd = t[closeIdx].End;
                            nodes.Add(node);
                            i = closeIdx + 1;
                            declStart = i;
                            continue;
                        }
                    }
                }

                // ---- метод: ')' сразу за которой идёт '{' или семантика ----
                if (tk.Is(")"))
                {
                    int braceIdx = FindBodyBrace(t, i + 1, to, profile);
                    int closeIdx = braceIdx > 0
                        ? FindMatchingClose(t, depth, braceIdx, depthLevel, to)
                        : -1;
                    if (closeIdx > 0)
                    {
                        var node = new NsgStructNode { kind = "method" };
                        node.leading = src.Substring(prevEnd, t[declStart].Start - prevEnd);
                        node.header = src.Substring(t[declStart].Start, t[braceIdx].Start - t[declStart].Start);
                        node.printed = src.Substring(t[braceIdx].Start, t[closeIdx].End - t[braceIdx].Start);
                        node.line = t[braceIdx].Line;
                        node.name = ExtractMethodName(node.header);
                        node.graph = new NsgMethodGraph();

                        prevEnd = t[closeIdx].End;
                        nodes.Add(node);
                        i = closeIdx + 1;
                        declStart = i;
                        continue;
                    }
                }

                i++;
            }

            return nodes;
        }

        /// <summary>
        /// Позиция '{', открывающей тело функции.
        ///
        /// Между ')' и '{' допускается семантика HLSL — ": SV_Position",
        /// ": SV_Target0", ": register(0)". Без этого входные функции шейдеров
        /// не распознавались бы как методы и оставались сырым текстом.
        ///
        /// Профиль может разрешить ещё и тип возврата после параметров (Go):
        /// "func f() error {", "func f() (int, error) {". Правило включено
        /// только там, где объявлено: у C-семейства после ')' сразу '{'.
        /// </summary>
        static int FindBodyBrace(List<NsgToken> t, int from, int to, Nsg_LanguageProfile profile)
        {
            if (from >= to) return -1;
            if (t[from].Is("{")) return from;

            bool annotation = t[from].Is(":") || t[from].Is("->");
            bool trailingReturn = profile != null && profile.TrailingReturnType;
            if (!annotation && !trailingReturn) return -1;

            // ": SV_Position" (HLSL), "-> i32" (Rust) и тип возврата (Go):
            // пропускаем аннотацию до открывающей скобки тела.
            int depth = 0;
            for (int k = from + 1; k < to; k++)
            {
                var tk = t[k];
                if (tk.Is("("))
                {
                    depth++;
                }
                else if (tk.Is(")"))
                {
                    if (depth > 0) depth--;
                }
                else if (tk.Is("{"))
                {
                    if (depth == 0) return k;
                }
                else if (tk.Is(";"))
                {
                    return -1;
                }
                else if (tk.Is("}"))
                {
                    // Закрылась внешняя область — тела у этой функции нет.
                    return -1;
                }
                else if (tk.Is("=") && depth == 0)
                {
                    // "x = ..." — это не заголовок функции, а инициализация.
                    return -1;
                }
            }
            return -1;
        }

        /// <summary>
        /// Член с семантикой: "float4 vertex : POSITION;".
        ///
        /// Ровно два слова до ':' (тип и имя), после ':' — только слова и точки
        /// (SV_Position, TEXCOORD0). Всё остальное остаётся сырым текстом, чтобы
        /// не принять за поле битовое поле или что-то ещё.
        /// </summary>
        static NsgStructNode TryMakeField(string src, List<NsgToken> t, int fromTok,
                                          int toTokExclusive, ref int prevEnd)
        {
            int semi = toTokExclusive - 1;
            if (semi <= fromTok) return null;

            int colon = -1;
            for (int k = fromTok; k < semi; k++)
            {
                if (t[k].Is(":"))
                {
                    colon = k;
                    break;
                }
            }

            if (colon < 0 || colon - fromTok != 2) return null;

            if (t[fromTok].Kind != NsgTokenKind.Ident && t[fromTok].Kind != NsgTokenKind.Keyword) return null;
            if (t[fromTok + 1].Kind != NsgTokenKind.Ident) return null;
            if (colon + 1 >= semi) return null;

            for (int k = colon + 1; k < semi; k++)
            {
                if (t[k].Kind != NsgTokenKind.Ident && !t[k].Is(".")) return null;
            }

            var node = new NsgStructNode { kind = "field" };
            node.leading = src.Substring(prevEnd, t[fromTok].Start - prevEnd);
            node.headerPrefix = t[fromTok].Text;
            node.name = t[fromTok + 1].Text;
            node.baseList = src.Substring(t[colon + 1].Start, t[semi - 1].End - t[colon + 1].Start);
            node.text = src.Substring(t[fromTok].Start, t[semi].End - t[fromTok].Start);

            prevEnd = t[semi].End;
            return node;
        }

        static bool IsContainerKeyword(string s, Nsg_LanguageProfile profile)
        {
            if (profile == null || profile.ContainerKeywords == null) return false;
            return profile.ContainerKeywords.Contains(s);
        }

        static bool IsTypeKeyword(string s, Nsg_LanguageProfile profile)
        {
            if (profile != null) return profile.IsTypeDeclKeyword(s);
            return s == "class" || s == "struct" || s == "interface" || s == "enum";
        }

        static NsgStructNode MakeRaw(string src, List<NsgToken> t, int fromTok, int toTokExclusive, ref int prevEnd)
        {
            var node = new NsgStructNode { kind = "raw" };
            node.leading = src.Substring(prevEnd, t[fromTok].Start - prevEnd);
            node.text = src.Substring(t[fromTok].Start, t[toTokExclusive - 1].End - t[fromTok].Start);
            prevEnd = t[toTokExclusive - 1].End;
            return node;
        }

        static NsgStructNode MakeType(string src, List<NsgToken> t, int declStart, int kwIdx, int braceIdx,
                                      string keyword, bool isContainer = false)
        {
            var node = new NsgStructNode { kind = "type", typeKeyword = keyword, isContainer = isContainer };
            int h0 = t[declStart].Start;
            string header = src.Substring(h0, t[braceIdx].Start - h0);
            node.header = header;

            int nameStartTok = kwIdx + 1;
            int colonTok = -1;
            for (int k = nameStartTok; k < braceIdx; k++)
            {
                if (t[k].Is("where")) break; // a where-clause colon is not a base list
                if (t[k].Is(":"))
                {
                    colonTok = k;
                    break;
                }
            }

            int nameEndTok = colonTok > 0 ? colonTok : braceIdx;
            if (nameStartTok >= nameEndTok)
            {
                node.headerPrefix = header;
                node.name = string.Empty;
                node.headerBasePrefix = string.Empty;
                node.baseList = string.Empty;
                node.headerSuffix = string.Empty;
                return node;
            }

            int nameStartOff = t[nameStartTok].Start - h0;
            int nameEndOff = t[nameEndTok - 1].End - h0;

            node.headerPrefix = header.Substring(0, nameStartOff);
            node.name = header.Substring(nameStartOff, nameEndOff - nameStartOff);

            if (colonTok > 0 && colonTok + 1 < braceIdx)
            {
                int colonOff = t[colonTok].Start - h0;
                int baseEndOff = t[braceIdx - 1].End - h0;
                string rawBase = header.Substring(colonOff + 1, baseEndOff - (colonOff + 1));

                int lead = 0;
                while (lead < rawBase.Length && char.IsWhiteSpace(rawBase[lead])) lead++;
                int tail = rawBase.Length;
                while (tail > lead && char.IsWhiteSpace(rawBase[tail - 1])) tail--;

                node.headerBasePrefix = header.Substring(nameEndOff, colonOff + 1 - nameEndOff) +
                                        rawBase.Substring(0, lead);
                node.baseList = rawBase.Substring(lead, tail - lead);
                node.headerSuffix = rawBase.Substring(tail) + header.Substring(baseEndOff);
            }
            else
            {
                node.headerBasePrefix = string.Empty;
                node.baseList = string.Empty;
                node.headerSuffix = header.Substring(nameEndOff);
            }

            return node;
        }

        static string NameAfterKeyword(string src, List<NsgToken> t, int kwIdx, int braceIdx)
        {
            int k = kwIdx + 1;
            if (k >= braceIdx) return string.Empty;
            int start = t[k].Start;
            int end = start;
            while (k < braceIdx && !t[k].Is("{"))
            {
                end = t[k].End;
                k++;
            }
            if (end <= start) return string.Empty;
            return src.Substring(start, end - start).Trim();
        }

        static int FindOpenBrace(List<NsgToken> t, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                if (t[i].Is("{")) return i;
                if (t[i].Is(";")) return -1;
            }
            return -1;
        }

        static int FindMatchingClose(List<NsgToken> t, int[] depth, int braceIdx, int constructDepth, int to)
        {
            for (int i = braceIdx + 1; i < to; i++)
            {
                if (t[i].Is("}") && depth[i] == constructDepth) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // Пересборка
        // ------------------------------------------------------------------

        public static string Rebuild(NsgFileModel model)
        {
            if (model == null) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < model.roots.Count; i++) model.roots[i].Render(sb);
            sb.Append(model.epilogue ?? string.Empty);
            return sb.ToString();
        }

        /// <summary>
        /// Пересобирает файл с заново сгенерированными телами методов.
        /// <paramref name="bodies"/> упорядочен как <see cref="NsgFileModel.AllMethods"/>.
        /// </summary>
        public static string Rebuild(NsgFileModel model, List<string> bodies)
        {
            if (model == null) return string.Empty;
            if (bodies == null) return Rebuild(model);

            var methods = model.AllMethods();
            for (int i = 0; i < methods.Count && i < bodies.Count; i++)
            {
                if (bodies[i] != null) methods[i].printed = bodies[i];
            }
            return Rebuild(model);
        }

        /// <summary>
        /// Отступ для заново сгенерированного тела. Заголовок сохраняет свои
        /// конечные пробелы, поэтому тело выводится БЕЗ ведущего отступа для '{'.
        /// </summary>
        public static string BodyIndent(string header)
        {
            if (string.IsNullOrEmpty(header)) return string.Empty;
            int lastNl = header.LastIndexOf('\n');
            if (lastNl < 0) return string.Empty;
            string tail = header.Substring(lastNl + 1);
            if (tail.Trim().Length != 0) return string.Empty;
            return tail;
        }

        public static string InnerBody(string printed)
        {
            if (string.IsNullOrEmpty(printed)) return string.Empty;
            int open = printed.IndexOf('{');
            int close = printed.LastIndexOf('}');
            if (open < 0 || close <= open) return string.Empty;
            return printed.Substring(open + 1, close - open - 1);
        }

        public static string ExtractMethodName(string header)
        {
            if (string.IsNullOrEmpty(header)) return string.Empty;
            int p = header.LastIndexOf('(');
            if (p < 0) return string.Empty;

            int e = p - 1;
            while (e >= 0 && char.IsWhiteSpace(header[e])) e--;
            int s = e;
            while (s >= 0 && (char.IsLetterOrDigit(header[s]) || header[s] == '_')) s--;
            if (e < s + 1) return string.Empty;
            return header.Substring(s + 1, e - s);
        }
    }
}
