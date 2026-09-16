using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Язык Python. Не входит в C-семейство: блоки задаются отступами, а не
    /// скобками, поэтому у него собственные разделитель файла, разборщик и
    /// печататель. Общими остаются модель блоков, граф, диагностика и карта
    /// блоков — идентификаторы блоков не зависят от языка.
    /// </summary>
    public class Nsg_PythonLanguage : INsg_Language
    {
        public const string IndentUnit = "    ";

        public string Id
        {
            get { return "python"; }
        }

        public string DisplayName
        {
            get { return "Python"; }
        }

        public string IconName
        {
            get { return "PYTHON"; }
        }

        public string[] Extensions
        {
            get { return new[] { ".py" }; }
        }

        public bool BuiltIn
        {
            get { return false; }
        }

        public int ApiVersion
        {
            get { return Nsg_LanguageApi.Version; }
        }

        public INsg_LanguageEngine CreateEngine()
        {
            return new Nsg_PythonEngine();
        }

        public Nsg_BlockLibrary CreateLibrary(string blocksFolder)
        {
            var lib = new Nsg_BlockLibrary();
            var all = Nsg_BlockLibrary.CreateDefaults();

            for (int i = 0; i < all.Count; i++)
            {
                // В Python нет ни объявления типа, ни new.
                if (all[i].id == "stmt.localDecl") continue;
                if (all[i].id == "expr.new") continue;
                if (all[i].id == "stmt.for") continue;   // есть только "for x in y"
                lib.Blocks.Add(all[i]);
            }

            lib.Blocks.Add(Nsg_CStyleBlocks.Template("py.print", "statement", "print({0})",
                "print({{0}})", Nsg_CStyleBlocks.E("value")));

            lib.Blocks.Add(Nsg_CStyleBlocks.Template("py.pass", "statement", "pass", "pass", null));

            lib.Blocks.Add(Nsg_CStyleBlocks.Call("py.len", "expression", "len", "length of {0}", Nsg_CStyleBlocks.E("value")));

            lib.Blocks.Add(Nsg_CStyleBlocks.Call("py.range", "expression", "range", "range {0}", Nsg_CStyleBlocks.E("value")));

            lib.Blocks.Add(Nsg_CStyleBlocks.Call("py.str", "expression", "str", "to string {0}", Nsg_CStyleBlocks.E("value")));

            lib.Blocks.Add(Nsg_CStyleBlocks.Call("py.int", "expression", "int", "to int {0}", Nsg_CStyleBlocks.E("value")));

            // Всё, что ниже, — собственные идиомы Python. Они образуют в
            // палитре отдельную группу «Python», а не тонут в общей «API».
            int before = lib.Blocks.Count;
            AddIdioms(lib.Blocks);
            Nsg_CStyleBlocks.MarkLanguageGroup(lib.Blocks, before, Id, DisplayName);

            lib.Rebuild();
            return lib;
        }

        /// <summary>
        /// Идиомы Python: встроенные функции (обратимы — статический вызов)
        /// и операторы языка (печатаются верно, обратно читаются как вызов).
        /// </summary>
        static void AddIdioms(List<NsgBlockDef> into)
        {
            // --- встроенные функции: разборщик узнаёт их обратно ---
            into.Add(Nsg_CStyleBlocks.Call("py.float", "expression", "float", "to float {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("py.abs", "expression", "abs", "absolute {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("py.round", "expression", "round", "round {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("py.sum", "expression", "sum", "sum of {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.max", "expression", "max", "max of {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.min", "expression", "min", "min of {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.sorted", "expression", "sorted", "sorted {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.list", "expression", "list", "list of {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.dict", "expression", "dict", "dict from {0}", Nsg_CStyleBlocks.E("source")));
            into.Add(Nsg_CStyleBlocks.Call("py.set", "expression", "set", "set of {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.tuple", "expression", "tuple", "tuple of {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.enumerate", "expression", "enumerate", "enumerate {0}", Nsg_CStyleBlocks.E("iterable")));
            into.Add(Nsg_CStyleBlocks.Call("py.zip", "expression", "zip", "zip {0} {1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));
            into.Add(Nsg_CStyleBlocks.Call("py.isinstance", "expression", "isinstance", "is {0} a {1}",
                Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("type")));
            into.Add(Nsg_CStyleBlocks.Call("py.input", "expression", "input", "read input {0}", Nsg_CStyleBlocks.E("prompt")));
            into.Add(Nsg_CStyleBlocks.Call("py.repr", "expression", "repr", "representation of {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("py.ord", "expression", "ord", "char code of {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("py.chr", "expression", "chr", "char from code {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("py.type", "expression", "type", "type of {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("py.format", "expression", "format", "format {0} with {1}",
                Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("spec")));

            // --- идиомы языка ---
            into.Add(Nsg_CStyleBlocks.Template("py.import", "statement", "import {0}",
                "import {{0}}", Nsg_CStyleBlocks.S("module", "text")));

            into.Add(Nsg_CStyleBlocks.Template("py.fromImport", "statement", "from {0} import {1}",
                "from {{0}} import {{1}}",
                Nsg_CStyleBlocks.S("module", "text"), Nsg_CStyleBlocks.S("name", "text")));

            into.Add(Nsg_CStyleBlocks.Template("py.raise", "statement", "raise {0}",
                "raise {{0}}", Nsg_CStyleBlocks.E("error")));

            into.Add(Nsg_CStyleBlocks.Template("py.del", "statement", "delete {0}",
                "del {{0}}", Nsg_CStyleBlocks.S("target", "text")));

            into.Add(Nsg_CStyleBlocks.Template("py.assert", "statement", "assert {0}",
                "assert {{0}}", Nsg_CStyleBlocks.E("condition")));

            into.Add(Nsg_CStyleBlocks.Template("py.global", "statement", "global {0}",
                "global {{0}}", Nsg_CStyleBlocks.S("name", "text")));

            into.Add(Nsg_CStyleBlocks.Template("py.yield", "expression", "yield {0}",
                "yield {{0}}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("py.append", "statement", "append {0} to {1}",
                "{{1}}.append({{0}})", Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("target")));

            into.Add(Nsg_CStyleBlocks.Template("py.keys", "expression", "keys of {0}",
                "{{0}}.keys()", Nsg_CStyleBlocks.E("target")));

            into.Add(Nsg_CStyleBlocks.Template("py.items", "expression", "items of {0}",
                "{{0}}.items()", Nsg_CStyleBlocks.E("target")));

            into.Add(Nsg_CStyleBlocks.Template("py.join", "expression", "join {1} by {0}",
                "{{0}}.join({{1}})", Nsg_CStyleBlocks.E("sep"), Nsg_CStyleBlocks.E("parts")));

            into.Add(Nsg_CStyleBlocks.Template("py.split", "expression", "split {1} by {0}",
                "{{1}}.split({{0}})", Nsg_CStyleBlocks.E("sep"), Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("py.upper", "expression", "uppercase {0}",
                "{{0}}.upper()", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("py.lower", "expression", "lowercase {0}",
                "{{0}}.lower()", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("py.strip", "expression", "strip {0}",
                "{{0}}.strip()", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("py.replace", "expression", "replace {1} with {2} in {0}",
                "{{0}}.replace({{1}}, {{2}})",
                Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("from"), Nsg_CStyleBlocks.E("to")));
        }
    }

    // ======================================================================
    // Движок
    // ======================================================================

    public class Nsg_PythonEngine : INsg_LanguageEngine
    {
        static readonly Nsg_LanguageProfile PyProfile = BuildProfile();

        public static Nsg_LanguageProfile Profile
        {
            get { return PyProfile; }
        }

        public NsgMethodGraph ToGraph(Nsg_BlockLibrary library, NsgDiagnostics diag,
                                      List<NsgStmt> statements, NsgMethodGraph previous)
        {
            return new NsgCodeMap(library, diag).ToGraph(statements, previous);
        }

        public List<NsgStmt> ToAst(Nsg_BlockLibrary library, NsgDiagnostics diag, NsgMethodGraph graph)
        {
            return new NsgCodeMap(library, diag).ToAst(graph);
        }

        /// <summary>
        /// API-блоки для Python: по блоку вызова на каждую функцию верхнего
        /// уровня.
        ///
        /// Разбирается только имя и число параметров. Тело чужой функции и её
        /// типы нам неизвестны, а аннотаций в Python обычно и нет — этого
        /// хватает, чтобы вызвать функцию блоком и увидеть её в палитре.
        ///
        /// Методы классов не описываются: у них нет имени без класса, а сам
        /// класс — отдельная сущность, для которой нужна своя модель.
        /// </summary>
        public int GenerateApiBlocks(string folder, NsgDiagnostics diag)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                diag.Error(NsgCodes.Internal, "нет папки для генерации API-блоков");
                return 0;
            }

            var defs = new List<NsgBlockDef>();
            var seen = new HashSet<string>();

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.py", SearchOption.AllDirectories);
            }
            catch
            {
                return 0;
            }

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (path.Contains("/.checkpoints/")) continue;
                if (path.Contains("/Dependencies/")) continue;

                string src;
                try { src = File.ReadAllText(path); }
                catch { continue; }

                var fileDiag = new NsgDiagnostics();
                var model = Split(src, Path.GetFileName(path), fileDiag);
                if (model == null || model.roots == null) continue;

                for (int k = 0; k < model.roots.Count; k++)
                {
                    var node = model.roots[k];
                    if (node == null || node.kind != "method") continue;

                    var def = ApiDefFor(node);
                    if (def == null) continue;

                    if (seen.Add(def.id)) defs.Add(def);
                }
            }

            if (defs.Count == 0) return 0;

            string outDir = Nsg_Settings.Instance.apiOutputFolder;
            Nsg_Paths.EnsureDirectory(outDir);

            for (int i = 0; i < defs.Count; i++)
            {
                string p = Path.Combine(outDir, Nsg_BlockLibrary.SafeFileName(defs[i].id) + ".json");
                File.WriteAllText(p, JsonUtility.ToJson(defs[i], true));
            }

            AssetDatabase.Refresh();
            return defs.Count;
        }

        /// <summary>
        /// Блок вызова по строке "def name(a, b, c):".
        ///
        /// Подпись разбирается как текст, а не разборщиком выражений: у Python
        /// в заголовке стоят значения по умолчанию, *args и **kwargs, и
        /// приводить их к типам нечем — типов в языке нет.
        /// </summary>
        static NsgBlockDef ApiDefFor(NsgStructNode node)
        {
            string header = node.header;
            if (string.IsNullOrEmpty(header)) return null;

            int defIdx = header.IndexOf("def ", System.StringComparison.Ordinal);
            if (defIdx < 0) return null;

            int open = header.IndexOf('(', defIdx);
            int close = header.LastIndexOf(')');
            if (open < 0 || close <= open) return null;

            string name = header.Substring(defIdx + 4, open - (defIdx + 4)).Trim();
            if (name.Length == 0) return null;

            string inner = header.Substring(open + 1, close - (open + 1)).Trim();
            int arity = 0;
            if (inner.Length > 0)
            {
                arity = 1;
                for (int i = 0; i < inner.Length; i++)
                {
                    if (inner[i] == ',') arity++;
                }
            }

            var sockets = new List<NsgSocketDef>();
            var label = new StringBuilder(name);
            for (int i = 0; i < arity; i++)
            {
                sockets.Add(Nsg_CStyleBlocks.E("arg" + i));
                label.Append(" {").Append(i).Append('}');
            }

            var def = Nsg_CStyleBlocks.Call("py.api." + name, "expression", name,
                                            label.ToString(), sockets.ToArray());
            def.categoryKey = "cat.api";
            def.category = "module";
            return def;
        }

        // ------------------------------------------------------------------
        // Файл <-> дерево
        // ------------------------------------------------------------------

        public NsgFileModel Split(string source, string fileName, NsgDiagnostics diag)
        {
            var model = new NsgFileModel { sourceFile = fileName };
            if (source == null) source = string.Empty;

            List<string> lines = SplitLines(source);
            var nodes = new List<NsgStructNode>();

            int i = 0;
            int pending = 0; // индекс первой строки, ещё не попавшей ни в один узел

            while (i < lines.Count)
            {
                string trimmed = lines[i].TrimStart();
                bool isDef = trimmed.StartsWith("def ") || trimmed.StartsWith("class ");
                bool topLevel = IndentOf(lines[i]) == 0;

                if (!isDef || !topLevel)
                {
                    i++;
                    continue;
                }

                // Ведущий текст до этого объявления — отдельный сырой узел.
                string leading = Join(lines, pending, i);
                if (leading.Length > 0)
                {
                    nodes.Add(new NsgStructNode { kind = "raw", text = leading });
                }

                // Тело: все последующие строки с отступом (и пустые внутри).
                int bodyEnd = i + 1;
                while (bodyEnd < lines.Count)
                {
                    string t = lines[bodyEnd].Trim();
                    if (t.Length == 0) { bodyEnd++; continue; }
                    if (IndentOf(lines[bodyEnd]) == 0) break;
                    bodyEnd++;
                }

                var method = new NsgStructNode { kind = "method" };
                method.header = lines[i];
                method.printed = Join(lines, i + 1, bodyEnd);
                method.name = NameAfterDef(lines[i]);
                method.line = i + 1;
                method.graph = new NsgMethodGraph();

                nodes.Add(method);

                i = bodyEnd;
                pending = bodyEnd;
            }

            string tail = Join(lines, pending, lines.Count);
            if (tail.Length > 0)
            {
                nodes.Add(new NsgStructNode { kind = "raw", text = tail });
            }

            model.roots = nodes;
            model.epilogue = string.Empty;
            return model;
        }

        public string Rebuild(NsgFileModel model)
        {
            return NsgFileSplitter.Rebuild(model);
        }

        public string Rebuild(NsgFileModel model, List<string> bodies)
        {
            return NsgFileSplitter.Rebuild(model, bodies);
        }

        /// <summary>Отступ тела: как у строки def, плюс один уровень.</summary>
        public string BodyIndent(string header)
        {
            return IndentOf(header ?? string.Empty) + Nsg_PythonLanguage.IndentUnit;
        }

        /// <summary>Тело уже хранится с отступами, поэтому берём как есть.</summary>
        public string InnerBody(string printed)
        {
            return printed ?? string.Empty;
        }

        public string MethodName(string header)
        {
            return NameAfterDef(header ?? string.Empty);
        }

        // ------------------------------------------------------------------
        // Текст <-> AST
        // ------------------------------------------------------------------

        public List<NsgStmt> ParseBody(string innerBody, NsgDiagnostics diag)
        {
            var lines = SplitLines(innerBody ?? string.Empty);
            int index = 0;
            var list = ParseBlock(lines, ref index, -1, diag);
            return list;
        }

        List<NsgStmt> ParseBlock(List<string> lines, ref int index, int parentIndent, NsgDiagnostics diag)
        {
            var result = new List<NsgStmt>();

            while (index < lines.Count)
            {
                string raw = lines[index];
                if (raw.Trim().Length == 0) { index++; continue; }

                int indent = IndentOf(raw);
                if (indent <= parentIndent) break;

                string text = raw.Trim();

                // Комментарий целиком: сохраняем как сырой оператор.
                if (text.StartsWith("#"))
                {
                    result.Add(new NsgRawStmt { Text = text, Line = index + 1 });
                    index++;
                    continue;
                }

                int line = index + 1;
                index++;

                NsgStmt stmt = ParseLine(lines, ref index, indent, text, line, diag);
                if (stmt != null) result.Add(stmt);
            }

            return result;
        }

        NsgStmt ParseLine(List<string> lines, ref int index, int indent, string text, int line,
                          NsgDiagnostics diag)
        {
            // ---- составные операторы ----
            if (text.StartsWith("if ") && text.EndsWith(":"))
            {
                var node = new NsgIfStmt();
                node.Cond = ParseExpr(text.Substring(3, text.Length - 4).Trim(), diag);
                node.Then = new NsgBlockStmt { Statements = ParseBlock(lines, ref index, indent, diag) };
                node.Else = ParseElseChain(lines, ref index, indent, diag);
                node.Line = line;
                return node;
            }

            if (text.StartsWith("while ") && text.EndsWith(":"))
            {
                var node = new NsgWhileStmt();
                node.Cond = ParseExpr(text.Substring(6, text.Length - 7).Trim(), diag);
                node.Body = new NsgBlockStmt { Statements = ParseBlock(lines, ref index, indent, diag) };
                node.Line = line;
                return node;
            }

            if (text.StartsWith("for ") && text.EndsWith(":"))
            {
                string inner = text.Substring(4, text.Length - 5).Trim();
                int at = inner.IndexOf(" in ", System.StringComparison.Ordinal);
                if (at > 0)
                {
                    var node = new NsgForEachStmt();
                    node.Name = inner.Substring(0, at).Trim();
                    node.Source = ParseExpr(inner.Substring(at + 4).Trim(), diag);
                    node.Body = new NsgBlockStmt { Statements = ParseBlock(lines, ref index, indent, diag) };
                    node.Line = line;
                    return node;
                }
            }

            if (text == "else:" || text.StartsWith("elif "))
            {
                // Сюда попадаем только при разборе без предшествующего if —
                // оставляем сырым текстом, чтобы ничего не потерять.
                return new NsgRawStmt { Text = text, Line = line };
            }

            if (text.StartsWith("def ") || text.StartsWith("class "))
            {
                // Вложенные определения пока не моделируются.
                return new NsgRawStmt { Text = text, Line = line };
            }

            // ---- простые операторы ----
            if (text == "return" || text.StartsWith("return "))
            {
                var node = new NsgReturnStmt { Line = line };
                if (text.Length > 6) node.Value = ParseExpr(text.Substring(7).Trim(), diag);
                return node;
            }

            if (text == "break") return new NsgBreakStmt { Line = line };
            if (text == "continue") return new NsgContinueStmt { Line = line };
            if (text == "pass") return new NsgRawStmt { Text = "pass", Line = line };

            // Присваивание: ищем "=" вне скобок, не путая с "==".
            int eq = FindAssign(text);
            if (eq > 0)
            {
                var node = new NsgExprStmt { Line = line };
                node.Expr = new NsgAssignExpr
                {
                    Op = "=",
                    Target = ParseExpr(text.Substring(0, eq).Trim(), diag),
                    Value = ParseExpr(text.Substring(eq + 1).Trim(), diag)
                };
                return node;
            }

            var expr = ParseExpr(text, diag);
            if (expr == null) return new NsgRawStmt { Text = text, Line = line };
            return new NsgExprStmt { Expr = expr, Line = line };
        }

        NsgStmt ParseElseChain(List<string> lines, ref int index, int indent, NsgDiagnostics diag)
        {
            if (index >= lines.Count) return null;

            string raw = lines[index];
            if (raw.Trim().Length == 0) return null;
            if (IndentOf(raw) != indent) return null;

            string text = raw.Trim();

            if (text.StartsWith("elif ") && text.EndsWith(":"))
            {
                index++;
                var node = new NsgIfStmt();
                node.Cond = ParseExpr(text.Substring(5, text.Length - 6).Trim(), diag);
                node.Then = new NsgBlockStmt { Statements = ParseBlock(lines, ref index, indent, diag) };
                node.Else = ParseElseChain(lines, ref index, indent, diag);
                return node;
            }

            if (text == "else:")
            {
                index++;
                return new NsgBlockStmt { Statements = ParseBlock(lines, ref index, indent, diag) };
            }

            return null;
        }

        /// <summary>
        /// Разбор выражения общим разборщиком C-семейства: выражения Python
        /// к нему близки (and / or / not добавлены в таблицы).
        /// </summary>
        NsgExpr ParseExpr(string text, NsgDiagnostics diag)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var local = new NsgDiagnostics();
            var tokens = new NsgLexer(text, local, PyProfile).Lex();
            var parser = new NsgParser(tokens, text, local, 0, tokens.Count - 1, PyProfile);
            var expr = parser.ParseExpression();

            if (expr == null && diag != null) diag.Warn(NsgCodes.OutOfSubset, Nsg_L10n.T("lang.python.unparsed", text));
            return expr;
        }

        // ------------------------------------------------------------------
        // AST -> текст (отступами)
        // ------------------------------------------------------------------

        public string PrintBody(List<NsgStmt> statements, string indent)
        {
            var sb = new StringBuilder();
            PrintStatements(sb, statements, indent);
            return sb.ToString();
        }

        void PrintStatements(StringBuilder sb, List<NsgStmt> statements, string indent)
        {
            if (statements == null) return;

            for (int i = 0; i < statements.Count; i++)
            {
                var s = statements[i];
                if (s == null) continue;

                if (i > 0)
                {
                    for (int b = 0; b < s.BlankBefore; b++) sb.Append('\n');
                }

                PrintStatement(sb, s, indent);
            }
        }

        void PrintStatement(StringBuilder sb, NsgStmt s, string indent)
        {
            if (!string.IsNullOrEmpty(s.Comments))
            {
                var comments = s.Comments.Split('\n');
                for (int i = 0; i < comments.Length; i++)
                {
                    sb.Append(indent).Append(comments[i]).Append('\n');
                }
            }

            switch (s.Kind)
            {
                case NsgStmtKind.Expr:
                {
                    sb.Append(indent).Append(PrintExpr(((NsgExprStmt)s).Expr)).Append('\n');
                    break;
                }
                case NsgStmtKind.If:
                {
                    PrintIf(sb, (NsgIfStmt)s, indent, false);
                    break;
                }
                case NsgStmtKind.While:
                {
                    var w = (NsgWhileStmt)s;
                    sb.Append(indent).Append("while ").Append(PrintExpr(w.Cond)).Append(":\n");
                    PrintBodyStatement(sb, w.Body, indent);
                    break;
                }
                case NsgStmtKind.ForEach:
                {
                    var f = (NsgForEachStmt)s;
                    sb.Append(indent).Append("for ").Append(f.Name)
                      .Append(" in ").Append(PrintExpr(f.Source)).Append(":\n");
                    PrintBodyStatement(sb, f.Body, indent);
                    break;
                }
                case NsgStmtKind.Return:
                {
                    var r = (NsgReturnStmt)s;
                    sb.Append(indent).Append("return");
                    if (r.Value != null) sb.Append(' ').Append(PrintExpr(r.Value));
                    sb.Append('\n');
                    break;
                }
                case NsgStmtKind.Break:
                    sb.Append(indent).Append("break\n");
                    break;
                case NsgStmtKind.Continue:
                    sb.Append(indent).Append("continue\n");
                    break;
                case NsgStmtKind.Raw:
                    sb.Append(indent).Append(((NsgRawStmt)s).Text).Append('\n');
                    break;
                default:
                    // Объявления типов и счётный for в Python не существуют:
                    // такие блоки помечаем комментарием, чтобы не потерять.
                    //
                    // Текст комментария английский: он попадает в СГЕНЕРИРОВАННЫЙ
                    // код пользователя, а не в интерфейс, поэтому переводу не
                    // подлежит.
                    sb.Append(indent).Append("# unsupported block: ").Append(s.Kind).Append('\n');
                    break;
            }
        }

        void PrintIf(StringBuilder sb, NsgIfStmt s, string indent, bool isElif)
        {
            sb.Append(indent).Append(isElif ? "elif " : "if ")
              .Append(PrintExpr(s.Cond)).Append(":\n");
            PrintBodyStatement(sb, s.Then, indent);

            if (s.Else == null) return;

            if (s.Else.Kind == NsgStmtKind.If)
            {
                PrintIf(sb, (NsgIfStmt)s.Else, indent, true);
                return;
            }

            sb.Append(indent).Append("else:\n");
            PrintBodyStatement(sb, s.Else, indent);
        }

        void PrintBodyStatement(StringBuilder sb, NsgStmt body, string indent)
        {
            string inner = indent + Nsg_PythonLanguage.IndentUnit;

            if (body == null)
            {
                sb.Append(inner).Append("pass\n");
                return;
            }

            if (body.Kind == NsgStmtKind.Block)
            {
                var statements = ((NsgBlockStmt)body).Statements;
                if (statements.Count == 0)
                {
                    sb.Append(inner).Append("pass\n");
                    return;
                }
                PrintStatements(sb, statements, inner);
                return;
            }

            PrintStatement(sb, body, inner);
        }

        string PrintExpr(NsgExpr e)
        {
            var printer = new NsgPrinter();
            printer.Profile = PyProfile;
            return printer.Expr(e);
        }

        // ------------------------------------------------------------------
        // Помощники
        // ------------------------------------------------------------------

        static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines.Add(text.Substring(start, i - start + 1));
                    start = i + 1;
                }
            }

            if (start < text.Length) lines.Add(text.Substring(start));
            return lines;
        }

        static string Join(List<string> lines, int from, int to)
        {
            var sb = new StringBuilder();
            for (int i = from; i < to && i < lines.Count; i++) sb.Append(lines[i]);
            return sb.ToString();
        }

        static int IndentOf(string line)
        {
            int n = 0;
            while (n < line.Length && (line[n] == ' ' || line[n] == '\t')) n++;
            return n;
        }

        static string NameAfterDef(string header)
        {
            string t = header.TrimStart();
            if (t.StartsWith("def ")) t = t.Substring(4);
            else if (t.StartsWith("class ")) t = t.Substring(6);
            else return string.Empty;

            int end = t.Length;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '(' || c == ':' || c == ' ' || c == '\t' || c == '\n')
                {
                    end = i;
                    break;
                }
            }
            return t.Substring(0, end);
        }

        /// <summary>Позиция "=" присваивания, не путая с "==" и "&gt;=".</summary>
        static int FindAssign(string text)
        {
            int depth = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == '=' && depth == 0)
                {
                    if (i + 1 < text.Length && text[i + 1] == '=') { i++; continue; }
                    if (i > 0 && (text[i - 1] == '!' || text[i - 1] == '<' || text[i - 1] == '>' ||
                                  text[i - 1] == '+' || text[i - 1] == '-' || text[i - 1] == '*' ||
                                  text[i - 1] == '/' || text[i - 1] == '%' || text[i - 1] == '='))
                    {
                        continue;
                    }
                    return i;
                }
            }
            return -1;
        }

        static Nsg_LanguageProfile BuildProfile()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "python",
                DisplayName = "Python",
                IconName = "PYTHON",
                Extensions = new[] { ".py" },
                Preprocessor = false,
                HasForeach = true,   // for x in y — это foreach
                HasNew = false,
                ParenlessConditions = true,
                IndentBlocks = true
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                "and", "as", "assert", "async", "await", "break", "class", "continue",
                "def", "del", "elif", "else", "except", "False", "finally", "for",
                "from", "global", "if", "import", "in", "is", "lambda", "None",
                "nonlocal", "not", "or", "pass", "raise", "return", "True", "try",
                "while", "with", "yield");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "int", "float", "str", "bool", "list", "dict", "set", "tuple", "bytes");

            // Слова, начинающие оператор. def/class сюда НЕ входят: они
            // обрабатываются разделителем файла, а внутри тела — сырым текстом.
            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "elif", "else", "for", "while", "return", "break", "continue",
                "pass", "raise", "try", "except", "finally", "with", "import", "from", "del");

            return p;
        }
    }
}
