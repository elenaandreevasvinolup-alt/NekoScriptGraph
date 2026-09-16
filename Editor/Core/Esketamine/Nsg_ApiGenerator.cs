using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Превращает папку с C# в API-блоки.
    ///
    /// Генерируются только public-методы без обобщений и с простыми
    /// параметрами. Статические методы становятся полностью обратимыми
    /// блоками (они сопоставляются по точечной цели вызова). Методы
    /// экземпляра получают отдельный слот цели и поэтому односторонние:
    /// печатаются правильно, но при импорте распознаются как общий блок вызова.
    /// </summary>
    public static class Nsg_ApiGenerator
    {
        public class Result
        {
            public int Generated;
            public int Skipped;
            public readonly List<string> Notes = new List<string>();
        }

        static readonly HashSet<string> Modifiers = new HashSet<string>
        {
            "public", "private", "protected", "internal", "static", "virtual", "override",
            "abstract", "sealed", "async", "extern", "unsafe", "new", "partial", "readonly"
        };

        static readonly HashSet<string> BadParamKeywords = new HashSet<string>
        {
            "ref", "out", "in", "params", "this"
        };

        public static Result Generate(string folder, NsgDiagnostics diag)
        {
            var result = new Result();

            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                diag.Error(NsgCodes.Internal, Nsg_L10n.T("api.noFolder") + " (" + folder + ")");
                return result;
            }

            string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { folder });
            var defs = new List<NsgBlockDef>();
            var seen = new HashSet<string>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs")) continue;

                string src;
                try
                {
                    src = File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                var fileDiag = new NsgDiagnostics();
                var model = NsgFileSplitter.Split(src, Path.GetFileName(path), fileDiag);
                Walk(model.roots, string.Empty, defs, seen, result);
            }

            string outDir = Nsg_Settings.Instance.apiOutputFolder;
            Nsg_Paths.EnsureDirectory(outDir);

            for (int i = 0; i < defs.Count; i++)
            {
                string p = Path.Combine(outDir, Nsg_BlockLibrary.SafeFileName(defs[i].id) + ".json");
                File.WriteAllText(p, JsonUtility.ToJson(defs[i], true));
            }

            result.Generated = defs.Count;
            AssetDatabase.Refresh();
            return result;
        }

        static void Walk(List<NsgStructNode> nodes, string ns, List<NsgBlockDef> defs,
                         HashSet<string> seen, Result result)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];

                if (node.kind == "namespace")
                {
                    string inner = string.IsNullOrEmpty(ns)
                        ? (node.name ?? string.Empty)
                        : ns + "." + (node.name ?? string.Empty);
                    Walk(node.children, inner, defs, seen, result);
                    continue;
                }

                if (node.kind != "type") continue;

                string typeName = node.name ?? string.Empty;
                int genericAt = typeName.IndexOf('<');
                if (genericAt >= 0) typeName = typeName.Substring(0, genericAt); // closed generic types only

                string fullType = string.IsNullOrEmpty(ns) ? typeName : ns + "." + typeName;

                for (int k = 0; k < node.children.Count; k++)
                {
                    var child = node.children[k];
                    if (child.kind == "method")
                    {
                        var def = ParseMethod(fullType, typeName, child, result);
                        if (def == null) continue;
                        if (seen.Add(def.id)) defs.Add(def);
                        else result.Skipped++;
                    }
                    else if (child.kind == "type")
                    {
                        // вложенные типы сохраняют собственное имя
                        var single = new List<NsgStructNode> { child };
                        Walk(single, fullType, defs, seen, result);
                    }
                }
            }
        }

        /// <summary>
        /// То же, но для C-подобных языков: обходим файловую систему по
        /// расширениям профиля, а не AssetDatabase — .c, .cpp, .rs и .hlsl
        /// не являются MonoScript и FindAssets их не вернёт.
        /// </summary>
        public static Result GenerateFromFiles(string folder, Nsg_LanguageProfile profile, NsgDiagnostics diag)
        {
            var result = new Result();

            if (profile == null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                diag.Error(NsgCodes.Internal, "нет папки или профиля для генерации API-блоков");
                return result;
            }

            var defs = new List<NsgBlockDef>();
            var seen = new HashSet<string>();

            string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (path.Contains("/.checkpoints/")) continue;
                if (path.Contains("/Dependencies/")) continue;
                if (!MatchesExtension(path, profile.Extensions)) continue;

                string src;
                try
                {
                    src = File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                var fileDiag = new NsgDiagnostics();
                var model = NsgFileSplitter.Split(src, Path.GetFileName(path), fileDiag, profile);
                CollectMethods(model.roots, string.Empty, defs, seen, result);
            }

            string outDir = Nsg_Settings.Instance.apiOutputFolder;
            Nsg_Paths.EnsureDirectory(outDir);

            for (int i = 0; i < defs.Count; i++)
            {
                string p = Path.Combine(outDir, Nsg_BlockLibrary.SafeFileName(defs[i].id) + ".json");
                File.WriteAllText(p, JsonUtility.ToJson(defs[i], true));
            }

            result.Generated = defs.Count;
            AssetDatabase.Refresh();
            return result;
        }

        static bool MatchesExtension(string path, string[] extensions)
        {
            if (extensions == null) return false;
            string ext = Path.GetExtension(path);

            for (int i = 0; i < extensions.Length; i++)
            {
                if (string.Equals(extensions[i], ext, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Обходит дерево и делает блоки из всех найденных функций.</summary>
        static void CollectMethods(List<NsgStructNode> nodes, string scope, List<NsgBlockDef> defs,
                                   HashSet<string> seen, Result result)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];

                if (node.kind == "namespace")
                {
                    string inner = string.IsNullOrEmpty(scope)
                        ? (node.name ?? string.Empty)
                        : scope + "." + (node.name ?? string.Empty);
                    CollectMethods(node.children, inner, defs, seen, result);
                    continue;
                }

                if (node.kind == "method")
                {
                    // Свободная функция: области-владельца нет, scope пуст.
                    var def = ParseMethod(scope, scope, node, result);
                    if (def == null) continue;

                    if (seen.Add(def.id)) defs.Add(def);
                    else result.Skipped++;
                    continue;
                }

                if (node.kind == "type")
                {
                    string typeName = node.name ?? string.Empty;
                    int genericAt = typeName.IndexOf('<');
                    if (genericAt >= 0) typeName = typeName.Substring(0, genericAt);

                    string full = string.IsNullOrEmpty(scope) ? typeName : scope + "." + typeName;
                    CollectMethods(node.children, full, defs, seen, result);
                }
            }
        }

        static string Qualify(string scope, string name)
        {
            return string.IsNullOrEmpty(scope) ? name : scope + "." + name;
        }

        static NsgBlockDef ParseMethod(string fullType, string shortType, NsgStructNode method, Result result)
        {
            string header = method.header ?? string.Empty;
            if (header.Length == 0) { result.Skipped++; return null; }

            var diag = new NsgDiagnostics();
            var tokens = new NsgLexer(header, diag).Lex();
            int n = tokens.Count;

            // Первая скобка верхнего уровня — это список параметров объявления.
            int open = -1, close = -1, depth = 0;
            for (int i = 0; i < n; i++)
            {
                if (tokens[i].Is("("))
                {
                    if (depth == 0) { open = i; }
                    depth++;
                }
                else if (tokens[i].Is(")"))
                {
                    depth--;
                    if (depth == 0 && open >= 0 && close < 0) close = i;
                }
            }
            if (open < 0 || close < 0 || close <= open + 1) { result.Skipped++; return null; }

            int nameIdx = open - 1;
            if (nameIdx <= 0 || tokens[nameIdx].Kind != NsgTokenKind.Ident) { result.Skipped++; return null; }

            string name = tokens[nameIdx].Text;
            if (name == shortType) { result.Skipped++; return null; } // constructor

            // модификаторы
            bool isPublic = false;
            bool isStatic = false;
            int lastModifier = -1;
            for (int i = 0; i < nameIdx; i++)
            {
                if (tokens[i].Kind != NsgTokenKind.Keyword && tokens[i].Kind != NsgTokenKind.Ident) break;
                string t = tokens[i].Text;
                if (!Modifiers.Contains(t)) break;
                if (t == "public") isPublic = true;
                if (t == "static") isStatic = true;
                if (t == "async") { result.Skipped++; return null; }
                lastModifier = i;
            }
            if (!isPublic) { result.Skipped++; return null; }

            // отбрасываем where-условия и обобщённые методы
            for (int i = close + 1; i < n; i++)
            {
                if (tokens[i].Is("where")) { result.Skipped++; return null; }
            }

            // Свободная функция (C, HLSL): владельца нет, значит нет и this.
            if (string.IsNullOrEmpty(shortType)) isStatic = true;

            int typeStartTok = lastModifier + 1;
            if (typeStartTok >= nameIdx) { result.Skipped++; return null; }
            string returnType = header.Substring(tokens[typeStartTok].Start,
                                                 tokens[nameIdx].Start - tokens[typeStartTok].Start).Trim();
            if (string.IsNullOrEmpty(returnType) || returnType.Contains("<")) { result.Skipped++; return null; }
            if (returnType == "void" && !isStatic) { /* fine */ }

            // параметры
            var paramNames = new List<string>();
            int groupStart = open + 1;
            int d2 = 0;
            for (int i = open + 1; i <= close; i++)
            {
                bool atEnd = i == close;
                if (!atEnd && tokens[i].Is("(")) d2++;
                if (!atEnd && tokens[i].Is(")")) d2--;

                if (atEnd || (tokens[i].Is(",") && d2 == 0))
                {
                    if (i > groupStart)
                    {
                        int last = i - 1;
                        if (tokens[last].Kind != NsgTokenKind.Ident) { result.Skipped++; return null; }
                        string pname = tokens[last].Text;

                        for (int k = groupStart; k < last; k++)
                        {
                            if (BadParamKeywords.Contains(tokens[k].Text)) { result.Skipped++; return null; }
                            if (tokens[k].Is("=")) { result.Skipped++; return null; }
                        }
                        paramNames.Add(pname);
                    }
                    else if (!atEnd)
                    {
                        result.Skipped++;
                        return null; // empty parameter slot
                    }
                    groupStart = i + 1;
                }
            }

            int arity = paramNames.Count;
            string id = "api." + Qualify(fullType, name) + "." + arity;

            var def = new NsgBlockDef();
            def.id = id;
            def.level = "high";
            def.shape = returnType == "void" ? "statement" : "expression";
            def.categoryKey = "cat.api";
            def.category = string.IsNullOrEmpty(shortType) ? "API" : shortType;
            def.node = "call";
            def.matchCall = Qualify(shortType, name);
            def.matchArity = arity;
            def.builtin = true;
            def.manual = Qualify(fullType, name) + "(" + string.Join(", ", paramNames.ToArray()) + ") : " + returnType;

            var sockets = new List<NsgSocketDef>();
            if (!isStatic) sockets.Add(new NsgSocketDef { name = "target", kind = "expr", required = true });

            var label = new StringBuilder();
            int labelIndex = 0;
            if (!isStatic)
            {
                label.Append("{0}");
                labelIndex = 1;
            }
            label.Append(Qualify(shortType, name)).Append('(');

            for (int i = 0; i < paramNames.Count; i++)
            {
                if (i > 0) label.Append(", ");
                label.Append('{').Append(labelIndex).Append('}');
                sockets.Add(new NsgSocketDef { name = paramNames[i], kind = "expr", required = true });
                labelIndex++;
            }
            label.Append(')');

            def.label = label.ToString();
            def.labelEn = def.label;
            def.labelRu = def.label;
            def.sockets = sockets.ToArray();

            return def;
        }
    }
}
