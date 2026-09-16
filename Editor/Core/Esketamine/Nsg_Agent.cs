using System.Collections.Generic;
using System.IO;

namespace NekoScriptGraph
{
    /// <summary>
    /// Необязательная возможность движка: описать агенту канонический
    /// письменный набор языка.
    ///
    /// Реализуется ДВИЖКОМ, а не языком, потому что описание неразрывно
    /// связано с печатателем: ровно то, что выводит PrintBody, и есть
    /// каноническая форма. Движок, не реализующий интерфейс, всё равно
    /// получит описание — оно выводится из библиотеки блоков.
    ///
    /// Где лежат реализации (по месту жительства самого движка):
    ///   • C#          — Editor/Languages/CSharp (встроенный язык, ядро);
    ///   • C-семейство — Editor/Core/_cstyle (общая машина C-подобных:
    ///                   C, C++, Java, HLSL, Rust, Python);
    ///   • остальные   — папка языка в Dependencies/Editor/LanguageSupport.
    ///
    /// Интерфейс отдельный, а не метод в INsg_LanguageEngine, намеренно:
    /// добавление метода в контракт сломало бы все уже установленные языки,
    /// а так старый движок просто не имеет описания — и это не ошибка.
    /// </summary>
    public interface INsg_AgentSpec
    {
        /// <summary>Идентификатор языка, к которому относится описание.</summary>
        string Id { get; }

        /// <summary>
        /// Правила канонической формы, специфичные для языка. Общие правила
        /// (Allman, отступ 4, обязательные скобки) добавляются генератором.
        /// </summary>
        string[] CanonicalRules { get; }

        /// <summary>
        /// Конструкции, которые заведомо уходят в сырой фрагмент (NSG0002).
        /// Агент обязан их избегать: они ломают «полный» перевод в блоки.
        /// </summary>
        string[] OutOfSubset { get; }
    }

    /// <summary>Имена операций агентского интерфейса.</summary>
    public static class Nsg_AgentOps
    {
        /// <summary>Разобрать и посчитать, ничего не записывая.</summary>
        public const string Plan = "plan";

        /// <summary>Вернуть канонический текст (оракул неподвижной точки).</summary>
        public const string Canon = "canon";

        /// <summary>Записать канонический .cs и пересобранный .nsg.json.</summary>
        public const string Apply = "apply";

        /// <summary>Прочитать состояние с диска и отчитаться.</summary>
        public const string Verify = "verify";

        /// <summary>Сгенерировать письменный набор для агента.</summary>
        public const string Spec = "spec";

        /// <summary>Перечислить файлы .nsg.json в папке (или во всём проекте).</summary>
        public const string List = "list";

        /// <summary>
        /// Удалить файлы .nsg.json в папке (или во всём проекте). Исходники не
        /// трогаются: это путь чистого удаления плагина из проекта.
        /// </summary>
        public const string Release = "release";
    }

    /// <summary>
    /// Запрос агента. Поля намеренно плоские: так его сериализует JsonUtility
    /// и так же легко собрать руками в bash.
    /// </summary>
    [System.Serializable]
    public class Nsg_AgentRequest
    {
        public string op = Nsg_AgentOps.Verify;

        /// <summary>Путь к исходнику, например Assets/Scripts/Foo.cs.</summary>
        public string file;

        /// <summary>
        /// Предлагаемый текст. Если пусто — берётся с диска. Именно это
        /// позволяет plan/canon работать до записи и без компиляции.
        /// </summary>
        public string source;

        /// <summary>Наследовать раскладку блоков из текущего .nsg.json.</summary>
        public bool inheritLayout = true;

        /// <summary>Отказываться работать при состоянии Conflict.</summary>
        public bool requireClean = true;

        /// <summary>
        /// Верхняя граница доли сырых блоков. Отрицательное значение —
        /// границы нет. 0 означает «только полный перевод».
        /// </summary>
        public float maxEscapeRatio = -1f;

        /// <summary>Куда положить письменный набор (операция spec).</summary>
        public string specOut;

        /// <summary>Язык письменного набора (операция spec).</summary>
        public string specLanguage;

        /// <summary>
        /// Папка для операций list и release. Пусто — весь Assets.
        /// </summary>
        public string folder;
    }

    /// <summary>Итог удаления конфигураций блоков.</summary>
    [System.Serializable]
    public class Nsg_ReleaseResult
    {
        public int released;
        public int failed;
    }

    /// <summary>Ответ агента.</summary>
    [System.Serializable]
    public class Nsg_AgentResponse
    {
        public bool ok;
        public string op;
        public string file;
        public string language;

        /// <summary>Состояние документа: Unmanaged / Synced / CsDirty / BlocksDirty / Conflict.</summary>
        public string state;

        public int methods;
        public int blocks;

        /// <summary>Блоки «сырой фрагмент» (stmt.raw / expr.raw).</summary>
        public int rawBlocks;

        /// <summary>Доля сырых блоков от всех блоков файла, 0..1.</summary>
        public float escapeRatio;

        /// <summary>true, если P(R(t)) == t: текст уже неподвижная точка.</summary>
        public bool canonical;

        public bool wrote;
        public string[] wroteFiles;
        public string[] diagnostics;

        /// <summary>Найденные файлы .nsg.json (операция list).</summary>
        public string[] files;

        /// <summary>Сколько файлов удалено и сколько не удалось (операция release).</summary>
        public int released;
        public int failed;

        /// <summary>Канонический текст (операции canon и spec).</summary>
        public string text;

        public string error;
    }

    /// <summary>
    /// Агентский интерфейс NekoScriptGraph.
    ///
    /// Направление ровно одно: КОД → БЛОКИ. Это то единственное направление,
    /// которое в плагине покрыто приёмочными тестами (T2/T3), и то, которое
    /// описано каноническим печатателем. Агент пишет обычный исходник, плагин
    /// пересобирает .nsg.json.
    ///
    /// Ядро не ссылается на UnityEditor: всё, что требует редактора (GUID
    /// ассета, импорт, перезагрузка библиотеки), приходит через хуки. Так этот
    /// же код работает в batchmode и в автономных тестах.
    /// </summary>
    public static class Nsg_AgentApi
    {
        /// <summary>Хук редактора: GUID ассета по пути. Может быть null.</summary>
        public static System.Func<string, string> GuidOf;

        /// <summary>Хук редактора: сообщить Unity, что файл изменился.</summary>
        public static System.Action<string> FileWritten;

        /// <summary>Хук редактора: загрузка библиотеки языка. Иначе берётся из реестра.</summary>
        public static System.Func<Nsg_LanguageEntry, Nsg_BlockLibrary> LibraryOf;

        /// <summary>
        /// Хук редактора: файлы .nsg.json в папке. Операции list и release
        /// требуют файловой системы редактора, поэтому приходят снаружи — ядро
        /// по-прежнему не ссылается на UnityEditor.
        /// </summary>
        public static System.Func<string, string[]> ListManagedConfigs;

        /// <summary>Хук редактора: удалить файлы .nsg.json в папке.</summary>
        public static System.Func<string, Nsg_ReleaseResult> ReleaseConfigs;

        // ------------------------------------------------------------------

        public static Nsg_AgentResponse Run(Nsg_AgentRequest req)
        {
            var res = new Nsg_AgentResponse();
            if (req == null)
            {
                res.error = "request is null";
                return res;
            }

            res.op = req.op;

            try
            {
                if (req.op == Nsg_AgentOps.Spec) return RunSpec(req, res);
                if (req.op == Nsg_AgentOps.List) return RunList(req, res);
                if (req.op == Nsg_AgentOps.Release) return RunRelease(req, res);
                return RunFile(req, res);
            }
            catch (System.Exception e)
            {
                res.ok = false;
                res.error = e.GetType().Name + ": " + e.Message;
                return res;
            }
        }

        // ==================================================================
        // Файловые операции
        // ==================================================================

        static Nsg_AgentResponse RunFile(Nsg_AgentRequest req, Nsg_AgentResponse res)
        {
            string path = Normalize(req.file);
            res.file = path;

            if (string.IsNullOrEmpty(path))
            {
                res.error = "file is required";
                return res;
            }

            string ext = Path.GetExtension(path);
            var entry = Nsg_LanguageRegistry.EntryForExtension(ext);
            if (entry == null || entry.Language == null)
            {
                res.error = "no language is registered for '" + ext + "'";
                return res;
            }
            res.language = entry.Id;

            var lib = LibraryFor(entry);

            // Документ нужен только для чтения состояния и наследования
            // раскладки; разбор идёт в отдельном «черновом» документе, чтобы
            // неудачный plan не оставил следов в памяти редактора.
            var doc = new Nsg_Document(entry);
            doc.CsPath = path;
            doc.NsgPath = Nsg_Document.NsgPathFor(path);
            doc.Library = lib;

            if (File.Exists(doc.NsgPath) && doc.LoadModel()) doc.RefreshState();
            else doc.State = NsgDocState.Unmanaged;

            res.state = doc.State.ToString();

            if (req.op == Nsg_AgentOps.Verify) return RunVerify(req, res, doc);

            if (req.op != Nsg_AgentOps.Plan && req.op != Nsg_AgentOps.Canon &&
                req.op != Nsg_AgentOps.Apply)
            {
                res.error = "unknown op '" + req.op + "'";
                return res;
            }

            string src = req.source;
            if (src == null && File.Exists(path)) src = File.ReadAllText(path);
            if (src == null)
            {
                res.error = "no source: pass \"source\" or create the file first";
                return res;
            }

            var scratch = new Nsg_Document(entry);
            scratch.CsPath = path;
            scratch.NsgPath = doc.NsgPath;
            scratch.Library = lib;

            // Наследование раскладки читает предыдущую модель, но не меняет её:
            // ImportFromSource подменяет Model только у черновика.
            scratch.Model = req.inheritLayout ? doc.Model : null;

            var all = new NsgDiagnostics();
            if (!scratch.ImportFromSource(src, req.inheritLayout))
            {
                Collect(all, scratch.Diagnostics);
                res.diagnostics = ToLines(all);
                res.error = "import failed: the source is not convertible";
                return res;
            }
            Collect(all, scratch.Diagnostics);

            CountBlocks(scratch.Model, out int total, out int raw);
            res.methods = scratch.Model != null ? scratch.Model.MethodCount : 0;
            res.blocks = total;
            res.rawBlocks = raw;
            res.escapeRatio = total > 0 ? (float)raw / total : 0f;

            var renderDiag = new NsgDiagnostics();
            string text;
            List<string> bodies;
            if (!scratch.TryRender(out text, out bodies, renderDiag))
            {
                Collect(all, renderDiag);
                res.diagnostics = ToLines(all);
                res.error = "render failed: the block model cannot be printed back";
                return res;
            }
            Collect(all, renderDiag);

            res.diagnostics = ToLines(all);
            res.canonical = text == src;
            res.ok = true;

            if (req.op == Nsg_AgentOps.Canon)
            {
                res.text = text;
                return res;
            }

            if (req.op == Nsg_AgentOps.Plan) return res;

            return Commit(req, res, doc, scratch, text, bodies);
        }

        // ------------------------------------------------------------------

        static Nsg_AgentResponse RunVerify(Nsg_AgentRequest req, Nsg_AgentResponse res, Nsg_Document doc)
        {
            res.ok = true;

            if (doc.Model == null)
            {
                res.state = NsgDocState.Unmanaged.ToString();
                return res;
            }

            CountBlocks(doc.Model, out int total, out int raw);
            res.methods = doc.Model.MethodCount;
            res.blocks = total;
            res.rawBlocks = raw;
            res.escapeRatio = total > 0 ? (float)raw / total : 0f;

            if (File.Exists(doc.CsPath))
            {
                var diag = new NsgDiagnostics();
                string text;
                List<string> bodies;
                if (doc.TryRender(out text, out bodies, diag))
                {
                    res.canonical = text == File.ReadAllText(doc.CsPath);
                }
                res.diagnostics = ToLines(diag);
            }

            return res;
        }

        /// <summary>
        /// Единственная запись на диск. Сначала всё считается в памяти, и
        /// только полный успех доходит до файлов: неудачный apply не оставляет
        /// проект в половинчатом состоянии.
        /// </summary>
        static Nsg_AgentResponse Commit(Nsg_AgentRequest req, Nsg_AgentResponse res,
                                        Nsg_Document doc, Nsg_Document scratch,
                                        string text, List<string> bodies)
        {
            if (req.requireClean && doc.State == NsgDocState.Conflict)
            {
                res.error = "document is in Conflict state; decide which side wins first";
                return res;
            }

            if (req.maxEscapeRatio >= 0f && res.escapeRatio > req.maxEscapeRatio)
            {
                res.error = "escape ratio " + res.escapeRatio.ToString("F4") +
                            " exceeds the allowed maximum " + req.maxEscapeRatio.ToString("F4");
                return res;
            }

            doc.Model = scratch.Model;
            if (GuidOf != null) doc.Model.sourceGuid = GuidOf(doc.CsPath);

            // Пишем канонический текст, а не присланный: только он даёт
            // устойчивое состояние Synced. Если присланный текст уже был
            // каноническим (res.canonical), запись ничего не меняет.
            doc.WriteCode(text, bodies);
            doc.Diagnostics.Clear();
            doc.RefreshState();

            res.wrote = true;
            res.wroteFiles = new[] { doc.CsPath, doc.NsgPath };
            res.state = doc.State.ToString();
            res.ok = true;

            if (FileWritten != null) FileWritten(doc.CsPath);
            return res;
        }

        // ==================================================================
        // Файлы конфигураций блоков
        // ==================================================================

        static Nsg_AgentResponse RunList(Nsg_AgentRequest req, Nsg_AgentResponse res)
        {
            if (ListManagedConfigs == null)
            {
                res.error = "list is only available inside the Unity editor";
                return res;
            }

            res.files = ListManagedConfigs(Normalize(req.folder)) ?? new string[0];
            res.ok = true;
            return res;
        }

        static Nsg_AgentResponse RunRelease(Nsg_AgentRequest req, Nsg_AgentResponse res)
        {
            if (ReleaseConfigs == null)
            {
                res.error = "release is only available inside the Unity editor";
                return res;
            }

            var r = ReleaseConfigs(Normalize(req.folder));
            if (r == null)
            {
                res.error = "release failed";
                return res;
            }

            res.released = r.released;
            res.failed = r.failed;
            res.ok = true;
            return res;
        }

        // ==================================================================
        // Описание для агента
        // ==================================================================

        static Nsg_AgentResponse RunSpec(Nsg_AgentRequest req, Nsg_AgentResponse res)
        {
            string langId = string.IsNullOrEmpty(req.specLanguage) ? "csharp" : req.specLanguage;
            var entry = Nsg_LanguageRegistry.GetEntry(langId);
            if (entry == null || entry.Language == null)
            {
                res.error = "unknown language '" + langId + "'";
                return res;
            }

            res.language = entry.Id;
            res.text = Nsg_AgentDoc.Generate(entry, entry.Language.CreateEngine(), LibraryFor(entry));
            res.ok = true;

            if (!string.IsNullOrEmpty(req.specOut))
            {
                string dir = Path.GetDirectoryName(req.specOut);
                Nsg_Paths.EnsureDirectory(dir);
                File.WriteAllText(req.specOut, res.text);
                res.wrote = true;
                res.wroteFiles = new[] { req.specOut };
            }

            return res;
        }

        // ==================================================================
        // Помощники
        // ==================================================================

        static Nsg_BlockLibrary LibraryFor(Nsg_LanguageEntry entry)
        {
            if (LibraryOf != null) return LibraryOf(entry);
            return Nsg_LanguageRegistry.LoadLibrary(entry);
        }

        /// <summary>
        /// Считает блоки файла. Доля сырых считается от ВСЕХ блоков, а не
        /// только от блоков-операторов: для агента важен вес непереводимого
        /// кода в модели целиком.
        /// </summary>
        static void CountBlocks(NsgFileModel model, out int total, out int raw)
        {
            total = 0;
            raw = 0;
            if (model == null) return;

            var methods = model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i].graph;
                if (g == null || g.nodes == null) continue;

                for (int k = 0; k < g.nodes.Count; k++)
                {
                    var n = g.nodes[k];
                    if (n == null) continue;
                    total++;
                    if (n.block == "stmt.raw" || n.block == "expr.raw") raw++;
                }
            }
        }

        static void Collect(NsgDiagnostics into, NsgDiagnostics from)
        {
            if (into == null || from == null) return;
            for (int i = 0; i < from.Items.Count; i++) into.Add(from.Items[i]);
        }

        static string[] ToLines(NsgDiagnostics d)
        {
            if (d == null || d.Items.Count == 0) return new string[0];

            var list = new List<string>(d.Items.Count);
            for (int i = 0; i < d.Items.Count; i++)
            {
                var it = d.Items[i];
                string line = it.Code + " " + it.Severity + ": " + it.Message;
                if (it.Line > 0) line += " (line " + it.Line + ")";
                list.Add(line);
            }
            return list.ToArray();
        }

        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.Replace('\\', '/');
        }
    }
}
