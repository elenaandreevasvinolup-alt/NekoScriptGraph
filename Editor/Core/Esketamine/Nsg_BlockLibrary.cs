using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NekoScriptGraph
{
    public static class Nsg_Paths
    {
        public const string PluginFolderName = "NekoScriptGraph";
        public const string BlocksFolder = "Blocks";
        public const string ManagedExtension = ".nsg.json";

        public static string Root
        {
            get { return "Assets/" + PluginFolderName; }
        }

        public static string BlocksDir
        {
            get { return Root + "/" + BlocksFolder; }
        }

        /// <summary>
        /// Папка переводов: по одной подпапке на язык, внутри strings.json.
        /// Встроен только английский, всё остальное — данные здесь.
        /// </summary>
        public static string LocaleDir
        {
            get { return Root + "/Locale"; }
        }

        /// <summary>
        /// Необязательная папка кошки-ассистента. Ядро её только упоминает —
        /// ссылок на типы внутри нет, поэтому папку можно удалить целиком.
        /// </summary>
        public static string NekoDir
        {
            get { return Root + "/ProgramNeko"; }
        }

        /// <summary>Языковые пакеты кошки. Отдельно от LocaleDir: разные потребители.</summary>
        public static string NekoLocaleDir
        {
            get { return NekoDir + "/Locale"; }
        }

        public static string SettingsPath
        {
            get { return Root + "/NekoScriptGraph.settings.json"; }
        }

        public static void EnsureDirectory(string assetRelativePath)
        {
            if (!string.IsNullOrEmpty(assetRelativePath) && !Directory.Exists(assetRelativePath))
            {
                Directory.CreateDirectory(assetRelativePath);
            }
        }
    }

    /// <summary>
    /// Каталог блоков. Блоки — это чистые данные, поэтому агент (или
    /// пользователь) может добавлять, менять и удалять блоки, не трогая код
    /// движка.
    /// </summary>
    public class Nsg_BlockLibrary
    {
        public readonly List<NsgBlockDef> Blocks = new List<NsgBlockDef>();

        readonly Dictionary<string, NsgBlockDef> _byId = new Dictionary<string, NsgBlockDef>();
        readonly Dictionary<string, List<NsgBlockDef>> _byCall = new Dictionary<string, List<NsgBlockDef>>();

        public void Rebuild()
        {
            _byId.Clear();
            _byCall.Clear();

            // Один идентификатор — один блок. Дубликат (язык дважды объявил
            // один блок, или папка-надстройка повторила код) не должен
            // появиться в палитре дважды. Побеждает последнее объявление —
            // ровно так же, как решает Get().
            for (int i = Blocks.Count - 1; i >= 0; i--)
            {
                var b = Blocks[i];
                if (b == null || string.IsNullOrEmpty(b.id) || _byId.ContainsKey(b.id))
                {
                    Blocks.RemoveAt(i);
                    continue;
                }

                // Пустой набор слотов вместо null. Так приходит блок из
                // blocks/*.json, где поля sockets нет вовсе: JsonUtility
                // оставляет null, а палитра, проверки и отрисовка читают
                // def.sockets.Length напрямую.
                if (b.sockets == null) b.sockets = new NsgSocketDef[0];

                _byId[b.id] = b;
            }

            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                if (!string.IsNullOrEmpty(b.matchCall))
                {
                    List<NsgBlockDef> list;
                    if (!_byCall.TryGetValue(b.matchCall, out list))
                    {
                        list = new List<NsgBlockDef>();
                        _byCall[b.matchCall] = list;
                    }
                    list.Add(b);
                }
            }
        }

        public NsgBlockDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            NsgBlockDef d;
            return _byId.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>
        /// Находит API-блок, который печатает статический вызов вроде
        /// "UnityEngine.Debug.Log". Нужен, чтобы API-блоки переживали
        /// круговое преобразование.
        /// </summary>
        public NsgBlockDef FindByStaticCall(string targetText, int arity)
        {
            if (string.IsNullOrEmpty(targetText)) return null;

            List<NsgBlockDef> list;
            if (!_byCall.TryGetValue(targetText, out list)) return null;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].matchArity == arity) return list[i];
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].matchArity < 0) return list[i];
            }
            return null;
        }

        /// <summary>Блоки одной группы вариантов, в порядке регистрации.</summary>
        public List<NsgBlockDef> InVariantGroup(string group)
        {
            var list = new List<NsgBlockDef>();
            if (string.IsNullOrEmpty(group)) return list;

            for (int i = 0; i < Blocks.Count; i++)
            {
                if (Blocks[i].variantGroup == group) list.Add(Blocks[i]);
            }
            return list;
        }

        public List<string> Categories()
        {
            var list = new List<string>();
            for (int i = 0; i < Blocks.Count; i++)
            {
                string c = Blocks[i].categoryKey;
                if (string.IsNullOrEmpty(c)) c = Blocks[i].category ?? string.Empty;
                if (!list.Contains(c)) list.Add(c);
            }
            list.Sort();
            return list;
        }

        public List<NsgBlockDef> InCategory(string key)
        {
            var list = new List<NsgBlockDef>();
            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                string c = string.IsNullOrEmpty(b.categoryKey) ? (b.category ?? string.Empty) : b.categoryKey;
                if (c == key) list.Add(b);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list;
        }

        /// <summary>
        /// Поиск блоков по подстроке. Ищет и по идентификатору, и по подписи,
        /// и по категории, и по сигнатуре: пользователь набирает то, что
        /// помнит, а помнит он по-разному.
        ///
        /// limit &lt;= 0 — без ограничения.
        /// </summary>
        public List<NsgBlockDef> Search(string query, int limit)
        {
            var result = new List<NsgBlockDef>();
            if (string.IsNullOrEmpty(query)) return result;

            string q = query.Trim();
            if (q.Length == 0) return result;

            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                if (b == null || string.IsNullOrEmpty(b.id)) continue;
                if (!Matches(b, q)) continue;

                result.Add(b);
                if (limit > 0 && result.Count >= limit) break;
            }
            return result;
        }

        static bool Matches(NsgBlockDef b, string q)
        {
            return Has(b.id, q)
                || Has(b.label, q)
                || Has(b.labelEn, q)
                || Has(b.labelRu, q)
                || Has(b.category, q)
                || Has(b.manual, q);
        }

        static bool Has(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------
        // Хранение
        // ------------------------------------------------------------------

        public static Nsg_BlockLibrary LoadOrCreate(string blocksDir)
        {
            var lib = new Nsg_BlockLibrary();
            LoadInto(lib, blocksDir, false);

            if (lib.Blocks.Count == 0)
            {
                lib.Blocks.AddRange(CreateDefaults());
                lib.SaveTo(blocksDir);
            }

            lib.Rebuild();
            return lib;
        }

        /// <summary>Читает все *.json внутри папки, рекурсивно.</summary>
        public static void LoadInto(Nsg_BlockLibrary lib, string dir, bool recursive)
        {
            if (!Directory.Exists(dir)) return;

            string[] files = Directory.GetFiles(dir, "*.json", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                NsgBlockDef def = null;
                try
                {
                    def = JsonUtility.FromJson<NsgBlockDef>(File.ReadAllText(files[i]));
                }
                catch
                {
                    def = null;
                }
                if (def != null && !string.IsNullOrEmpty(def.id)) lib.Blocks.Add(def);
            }
        }

        public void SaveTo(string blocksDir)
        {
            Nsg_Paths.EnsureDirectory(blocksDir);
            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                if (b == null || string.IsNullOrEmpty(b.id)) continue;
                string path = Path.Combine(blocksDir, SafeFileName(b.id) + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(b, true));
            }
        }

        public static string SafeFileName(string id)
        {
            var sb = new System.Text.StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Встроенные по умолчанию
        // ------------------------------------------------------------------

        static NsgSocketDef S(string name, string kind, bool required = true, bool variadic = false)
        {
            return new NsgSocketDef { name = name, kind = kind, required = required, variadic = variadic };
        }

        /// <summary>Текстовый слот с фиксированным набором значений — рисуется списком.</summary>
        static NsgSocketDef SChoice(string name, params string[] choices)
        {
            return new NsgSocketDef { name = name, kind = "text", required = true, choices = choices };
        }

        /// <summary>
        /// Базовый блок. Подпись задаётся ТОЛЬКО по-английски: это встроенный
        /// язык, а переводы приходят данными из Locale/&lt;code&gt;/strings.json
        /// по идентификатору блока (см. Nsg_BlockDef.Label).
        /// </summary>
        static NsgBlockDef D(string id, string shape, string categoryKey,
                             string en,
                             string node, string emit, bool builtin,
                             params NsgSocketDef[] sockets)
        {
            return new NsgBlockDef
            {
                id = id,
                level = "high",
                shape = shape,
                categoryKey = categoryKey,
                category = en,
                label = en,
                labelEn = en,
                labelRu = string.Empty,
                sockets = sockets,
                emit = emit,
                node = node,
                builtin = builtin
            };
        }

        /// <summary>Блок присваивания. Подпись, как и у D, только английская.</summary>
        static NsgBlockDef Op(string id, string en, string op, string emit)
        {
            return new NsgBlockDef
            {
                id = id,
                level = "high",
                shape = "statement",
                categoryKey = "cat.var",
                category = en,
                label = en,
                labelEn = en,
                labelRu = string.Empty,
                sockets = new[] { S("target", "expr"), S("value", "expr") },
                emit = emit,
                node = "assign",
                op = op,
                // Все шесть блоков присваивания — один блок с выпадающим
                // списком операторов.
                variantGroup = "assign",
                variantLabel = op
            };
        }

        /// <summary>
        /// Общий набор блоков под C-подобный язык: убираем то, чего в языке нет
        /// (foreach, new). Идиомы языка добавляет сам язык в CreateLibrary.
        /// </summary>
        public static List<NsgBlockDef> CreateDefaultsFor(Nsg_LanguageProfile profile)
        {
            var all = CreateDefaults();
            if (profile == null) return all;

            var result = new List<NsgBlockDef>();
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (!profile.HasForeach && b.id == "stmt.foreach") continue;
                if (!profile.HasNew && b.id == "expr.new") continue;
                if (profile.Excludes(b.id)) continue;
                result.Add(b);
            }

            return result;
        }

        public static List<NsgBlockDef> CreateDefaults()
        {
            var l = new List<NsgBlockDef>();

            // ---- переменные ----
            l.Add(D("stmt.localDecl", "statement", "cat.var", "declare {0} {1} = {2}",
                "localDecl", "{{0}} {{1}} = {{2}};", true,
                S("type", "text"), S("name", "var"), S("value", "expr", false)));

            l.Add(Op("stmt.assign", "set {0} to {1}", "=", "{{0}} = {{1}};"));
            l.Add(Op("stmt.add", "increase {0} by {1}", "+=", "{{0}} += {{1}};"));
            l.Add(Op("stmt.sub", "decrease {0} by {1}", "-=", "{{0}} -= {{1}};"));
            l.Add(Op("stmt.mul", "multiply {0} by {1}", "*=", "{{0}} *= {{1}};"));
            l.Add(Op("stmt.div", "divide {0} by {1}", "/=", "{{0}} /= {{1}};"));
            l.Add(Op("stmt.mod", "{0} mod {1}", "%=", "{{0}} %= {{1}};"));

            l.Add(D("stmt.expr", "statement", "cat.var", "run {0}",
                "expr", "{{0}};", false, S("value", "expr")));

            // ---- управление ----
            l.Add(D("stmt.if", "control", "cat.ctrl", "if {0}",
                "if", "", true, S("cond", "expr")));

            l.Add(D("stmt.while", "control", "cat.ctrl", "repeat while {0}",
                "while", "", true, S("cond", "expr")));

            l.Add(D("stmt.for", "control", "cat.ctrl", "count loop",
                "for", "", true,
                S("init", "expr", false), S("cond", "expr", false), S("incr", "expr", false)));

            l.Add(D("stmt.foreach", "control", "cat.ctrl", "for each {0} {1} in {2}",
                "foreach", "", true, S("type", "text"), S("name", "var"), S("source", "expr")));

            l.Add(D("stmt.return", "statement", "cat.ctrl", "return {0}",
                "return", "return {{0}};", true, S("value", "expr", false)));

            l.Add(D("stmt.break", "statement", "cat.ctrl", "break out of loop",
                "break", "break;", false));

            l.Add(D("stmt.continue", "statement", "cat.ctrl", "continue next iteration",
                "continue", "continue;", false));

            // ---- выражения ----
            l.Add(D("expr.literal", "expression", "cat.expr", "{0}",
                "literal", "{{0}}", false, S("text", "text")));

            l.Add(D("expr.ident", "expression", "cat.expr", "variable {0}",
                "ident", "{{0}}", false, S("name", "var")));

            l.Add(D("expr.member", "expression", "cat.expr", "{1} of {0}",
                "member", "{{0}}.{{1}}", false, S("target", "expr"), S("name", "text")));

            l.Add(D("expr.call", "expression", "cat.expr", "call {0}",
                "call", "", true, S("target", "expr"), S("args", "expr", false, true)));

            l.Add(D("expr.index", "expression", "cat.expr", "item {1} of {0}",
                "index", "", true, S("target", "expr"), S("args", "expr", false, true)));

            l.Add(D("expr.binary", "expression", "cat.expr", "{0} {1} {2}",
                "binary", "", true,
                S("left", "expr"),
                SChoice("op", "+", "-", "*", "/", "%", "==", "!=", "<", ">", "<=", ">=",
                               "&&", "||", "??", "&", "|", "^", "<<", ">>"),
                S("right", "expr")));

            l.Add(D("expr.unary", "expression", "cat.expr", "{0}{1}",
                "unary", "", true,
                SChoice("op", "!", "-", "+", "~", "++", "--"),
                S("operand", "expr")));

            l.Add(D("expr.postfix", "expression", "cat.expr", "{0}{1}",
                "postfix", "{{0}}{{1}}", false,
                S("operand", "expr"),
                SChoice("op", "++", "--")));

            l.Add(D("expr.new", "expression", "cat.expr", "create {0}",
                "new", "", true, S("type", "text"), S("args", "expr", false, true)));

            l.Add(D("expr.conditional", "expression", "cat.expr", "if {0} then {1} else {2}",
                "conditional", "", true, S("cond", "expr"), S("then", "expr"), S("else", "expr")));

            l.Add(D("expr.cast", "expression", "cat.expr", "cast {1} to {0}",
                "cast", "({{0}}){{1}}", false, S("type", "text"), S("operand", "expr")));

            // ---- аварийный выход ----
            // Подпись нейтральная: блок общий для всех языков, и «raw C#» в
            // палитре Rust или Go читался как чужая деталь.
            l.Add(D("stmt.raw", "statement", "cat.raw", "raw text {0}",
                "rawStmt", "{{0}}", false, S("text", "text")));

            l.Add(D("expr.raw", "expression", "cat.raw", "raw text {0}",
                "rawExpr", "{{0}}", false, S("text", "text")));

            return l;
        }
    }
}
