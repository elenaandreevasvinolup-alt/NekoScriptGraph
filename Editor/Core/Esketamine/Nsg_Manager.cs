using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Точка входа уровня проекта: владеет реестром языков, их библиотеками
    /// блоков и списком управляемых документов.
    ///
    /// Язык выбирается по расширению файла, поэтому один и тот же менеджер
    /// обслуживает C#, C++, HLSL, Rust и всё, что появится в
    /// Dependencies/Editor/LanguageSupport.
    /// </summary>
    public class Nsg_Manager
    {
        static Nsg_Manager _instance;

        public static Nsg_Manager Instance
        {
            get
            {
                if (_instance == null) _instance = new Nsg_Manager();
                return _instance;
            }
        }

        /// <summary>Библиотека языка по умолчанию (C#). Оставлено для совместимости.</summary>
        public Nsg_BlockLibrary Library { get; private set; }

        readonly Dictionary<string, Nsg_BlockLibrary> _libraries = new Dictionary<string, Nsg_BlockLibrary>();
        readonly Dictionary<string, Nsg_Document> _documents = new Dictionary<string, Nsg_Document>();

        Nsg_Manager()
        {
            ReloadLibrary();
        }

        public void ReloadLibrary()
        {
            _libraries.Clear();
            Nsg_LanguageRegistry.Rediscover();

            Library = GetLibrary(Nsg_LanguageRegistry.GetEntry("csharp"));
        }

        // ------------------------------------------------------------------
        // Языки
        // ------------------------------------------------------------------

        public List<Nsg_LanguageEntry> Languages
        {
            get { return Nsg_LanguageRegistry.All; }
        }

        /// <summary>Библиотека блоков языка, с кэшем на время сессии.</summary>
        public Nsg_BlockLibrary GetLibrary(Nsg_LanguageEntry entry)
        {
            if (entry == null)
            {
                if (Library != null) return Library;
                var empty = new Nsg_BlockLibrary();
                empty.Rebuild();
                return empty;
            }

            string id = entry.Id;
            Nsg_BlockLibrary cached;
            if (_libraries.TryGetValue(id, out cached)) return cached;

            var lib = Nsg_LanguageRegistry.LoadLibrary(entry);
            _libraries[id] = lib;
            return lib;
        }

        public INsg_Language LanguageFor(string path)
        {
            var entry = Nsg_LanguageRegistry.EntryForExtension(Path.GetExtension(path ?? string.Empty));
            return entry != null ? entry.Language : null;
        }

        // ------------------------------------------------------------------
        // Документы
        // ------------------------------------------------------------------

        public Nsg_Document Open(string csPath)
        {
            csPath = Normalize(csPath);

            Nsg_Document doc;
            if (_documents.TryGetValue(csPath, out doc)) return doc;

            var entry = Nsg_LanguageRegistry.EntryForExtension(Path.GetExtension(csPath));

            doc = new Nsg_Document(entry);
            doc.CsPath = csPath;
            doc.NsgPath = Nsg_Document.NsgPathFor(csPath);

            // Библиотека собирается кодом языка, и упасть она может по его
            // вине. Ошибку записываем в документ, а не выпускаем наружу:
            // иначе открытие файла выглядит как «кнопка не работает».
            try
            {
                doc.Library = GetLibrary(entry);
            }
            catch (System.Exception ex)
            {
                doc.Library = new Nsg_BlockLibrary();
                doc.Diagnostics.Error(NsgCodes.Internal,
                    "язык '" + (entry != null ? entry.Id : "?") +
                    "': библиотека блоков не собрана — " + ex.GetType().Name + ": " + ex.Message);
            }

            if (entry == null)
            {
                doc.Diagnostics.Error(NsgCodes.Unconvertible,
                    "нет языка для расширения '" + Path.GetExtension(csPath) +
                    "'. Положите язык в " + Nsg_LanguageRegistry.SupportFolder);
            }
            else if (doc.Engine == null)
            {
                doc.Diagnostics.Error(NsgCodes.Unconvertible,
                    "язык '" + entry.Id + "' не смог создать движок.");
            }

            if (doc.IsManaged && doc.Engine != null)
            {
                doc.LoadModel();
                doc.RefreshState();
            }
            else
            {
                doc.State = NsgDocState.Unmanaged;
            }

            LogErrors(doc);

            _documents[csPath] = doc;
            return doc;
        }

        public Nsg_Document OpenNsg(string nsgPath)
        {
            return Open(Nsg_Document.CsPathFor(Normalize(nsgPath)));
        }

        public void Close(Nsg_Document doc)
        {
            if (doc == null) return;
            _documents.Remove(doc.CsPath);
        }

        /// <summary>
        /// Дублирует ошибки документа в консоль.
        ///
        /// Окно проблем легко не заметить, а до его открытия причины отказа
        /// вообще не видно — и тогда «ничего не произошло» невозможно починить.
        /// Строка в консоли с путём и причиной — минимально необходимое.
        /// </summary>
        static void LogErrors(Nsg_Document doc)
        {
            if (doc == null || doc.Diagnostics == null) return;

            for (int i = 0; i < doc.Diagnostics.Items.Count; i++)
            {
                var d = doc.Diagnostics.Items[i];
                if (d == null || d.Severity != NsgSeverity.Error) continue;
                Debug.LogError("[NekoScriptGraph] " + doc.CsPath + ": " + d.Message);
            }
        }

        /// <summary>Превращает свободный файл в управляемый (код -> блоки).</summary>
        public bool Manage(Nsg_Document doc)
        {
            if (doc == null || doc.Engine == null)
            {
                LogErrors(doc);
                return false;
            }

            if (!doc.ImportFromCode(false))
            {
                LogErrors(doc);
                return false;
            }

            doc.Model.sourceGuid = GuidOf(doc.CsPath);
            doc.SaveModel();
            doc.RefreshState();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>Блоки перезаписывают код.</summary>
        public bool OverwriteCode(Nsg_Document doc)
        {
            if (doc == null || doc.Engine == null) return false;

            var diag = new NsgDiagnostics();
            string text;
            List<string> bodies;
            if (!doc.Generate(out text, out bodies, diag))
            {
                for (int i = 0; i < diag.Items.Count; i++) doc.Diagnostics.Add(diag.Items[i]);
                return false;
            }

            doc.WriteCode(text, bodies);
            doc.Diagnostics.Clear();
            doc.RefreshState();
            AssetDatabase.ImportAsset(doc.CsPath);
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>Код перезаписывает блоки (повторный импорт).</summary>
        public bool ReimportCode(Nsg_Document doc)
        {
            if (doc == null || doc.Engine == null)
            {
                LogErrors(doc);
                return false;
            }

            if (!doc.ImportFromCode(true))
            {
                LogErrors(doc);
                return false;
            }

            doc.Model.sourceGuid = GuidOf(doc.CsPath);
            doc.SaveModel();
            doc.RefreshState();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>Предпросмотр того, что запишет «блоки -> код».</summary>
        public bool Preview(Nsg_Document doc, out string text)
        {
            text = null;
            if (doc == null || doc.Engine == null) return false;

            var diag = new NsgDiagnostics();
            List<string> bodies;
            if (!doc.Generate(out text, out bodies, diag))
            {
                for (int i = 0; i < diag.Items.Count; i++) doc.Diagnostics.Add(diag.Items[i]);
                return false;
            }
            return true;
        }

        public void Unmanage(Nsg_Document doc)
        {
            if (doc == null) return;
            doc.Unmanage();
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------
        // Обнаружение
        // ------------------------------------------------------------------

        /// <summary>Все управляемые файлы проекта.</summary>
        public List<string> FindManagedFiles()
        {
            return FindManagedFiles(null);
        }

        /// <summary>
        /// Управляемые файлы в папке (null или пусто — весь Assets). Ищем по
        /// файловой системе, а не через AssetDatabase: .cpp, .rs, .hlsl не
        /// являются MonoScript, и FindAssets("t:MonoScript") их не вернёт.
        /// </summary>
        public List<string> FindManagedFiles(string folder)
        {
            var result = new List<string>();
            var extensions = RegisteredExtensions();
            if (extensions.Count == 0) return result;

            string root = string.IsNullOrEmpty(folder) ? "Assets" : Normalize(folder);
            if (!Directory.Exists(root)) return result;

            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (path.Contains("/.checkpoints/")) continue;
                if (path.Contains("/Dependencies/")) continue;

                string ext = Path.GetExtension(path);
                if (!extensions.Contains(ext)) continue;
                if (!File.Exists(Nsg_Document.NsgPathFor(path))) continue;

                result.Add(path);
            }

            result.Sort();
            return result;
        }

        /// <summary>Совместимость со старым именем.</summary>
        public List<string> FindManagedCsFiles()
        {
            return FindManagedFiles();
        }

        /// <summary>
        /// Все файлы .nsg.json в папке (null или пусто — весь Assets).
        ///
        /// Ищем именно по расширению, а не по управляемым исходникам. Так
        /// находятся и «осиротевшие» файлы, чей .cs уже удалили, и файлы языков,
        /// которые сейчас не загружены. Для разбора плагина из проекта это
        /// принципиально: иначе мусор остался бы навсегда.
        /// </summary>
        public List<string> FindManagedConfigs(string folder)
        {
            var result = new List<string>();

            string root = string.IsNullOrEmpty(folder) ? "Assets" : Normalize(folder);
            if (!Directory.Exists(root)) return result;

            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (path.Contains("/.checkpoints/")) continue;
                if (path.Contains("/Dependencies/")) continue;
                if (!path.EndsWith(Nsg_Paths.ManagedExtension, System.StringComparison.Ordinal)) continue;

                result.Add(path);
            }

            result.Sort();
            return result;
        }

        /// <summary>Расширения всех загруженных языков.</summary>
        public List<string> RegisteredExtensions()
        {
            var extensions = new List<string>();
            var entries = Nsg_LanguageRegistry.All;

            for (int i = 0; i < entries.Count; i++)
            {
                var lang = entries[i].Language;
                if (lang == null || lang.Extensions == null) continue;
                for (int k = 0; k < lang.Extensions.Length; k++)
                {
                    string e = lang.Extensions[k];
                    if (!string.IsNullOrEmpty(e) && !extensions.Contains(e)) extensions.Add(e);
                }
            }
            return extensions;
        }

        /// <summary>
        /// Все исходники проекта с расширениями загруженных языков.
        /// Папка null или пустая — весь Assets.
        /// </summary>
        public List<string> FindSourceFiles(string folder)
        {
            var result = new List<string>();
            var extensions = RegisteredExtensions();
            if (extensions.Count == 0) return result;

            string root = string.IsNullOrEmpty(folder) ? "Assets" : Normalize(folder);
            if (!Directory.Exists(root)) return result;

            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (IsInfrastructure(path)) continue;

                if (extensions.Contains(Path.GetExtension(path))) result.Add(path);
            }

            result.Sort();
            return result;
        }

        /// <summary>
        /// Служебные пути плагина: их исходники брать под управление нельзя.
        ///
        /// РАНЬШЕ ЗДЕСЬ СТОЯЛО `path.Contains("/NekoScriptGraph/")` — то есть
        /// исключалась ВСЯ папка плагина. Вместе с ней исключался и любой код,
        /// положенный туда намеренно (например Script4Test), и «Взять папку под
        /// управление» на такой папке молча отвечала «нечего брать». При этом
        /// «Снять с управления» работала: она ищет по суффиксу .nsg.json и
        /// ничего не исключает. Отсюда и картина «снять можно, взять нельзя».
        ///
        /// Теперь исключается ровно инфраструктура, а не территория.
        /// </summary>
        static bool IsInfrastructure(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;

            if (path.Contains("/.checkpoints/")) return true;
            if (path.Contains("/Dependencies/")) return true;
            if (path.Contains("/Library/")) return true;
            if (path.Contains("/Locale/")) return true;
            if (path.Contains("/Blocks/")) return true;

            string root = Nsg_Paths.Root + "/";
            if (!path.StartsWith(root)) return false;

            string rest = path.Substring(root.Length);
            if (rest.StartsWith("Editor/")) return true;
            if (rest.StartsWith("Runtime/")) return true;
            if (rest.StartsWith("LanguageSupport/")) return true;

            return false;
        }

        /// <summary>
        /// Берёт под управление все ещё не управляемые исходники в папке.
        /// Возвращает число обработанных, failed — сколько не удалось.
        /// </summary>
        public int ManageAll(string folder, out int failed)
        {
            failed = 0;
            int done = 0;

            var files = FindSourceFiles(folder);
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                if (File.Exists(Nsg_Document.NsgPathFor(path))) continue; // уже под управлением

                var doc = Open(path);
                if (doc == null || doc.Engine == null)
                {
                    failed++;
                    continue;
                }

                doc.Diagnostics.Clear();
                if (Manage(doc)) done++;
                else failed++;
            }

            return done;
        }

        /// <summary>
        /// Снимает с управления всё в папке: удаляет файлы .nsg.json, возвращая
        /// исходники в состояние обычных скриптов. Сами .cs не трогаются — они
        /// были и остаются источником.
        ///
        /// Работает по расширению, поэтому подбирает и осиротевшие файлы, и
        /// файлы незагруженных языков. Возвращает число удалённых, failed —
        /// сколько не удалось удалить.
        /// </summary>
        public int UnmanageAll(string folder, out int failed)
        {
            failed = 0;
            int done = 0;

            var configs = FindManagedConfigs(folder);
            for (int i = 0; i < configs.Count; i++)
            {
                string nsg = configs[i];
                string cs = Nsg_Document.CsPathFor(nsg);

                // СНИМОК ДО УДАЛЕНИЯ.
                //
                // Release необратим по своей природе: файла конфигурации больше
                // не будет. Единственное место, откуда модель ещё можно взять, —
                // сам удаляемый файл, поэтому читаем его и кладём в
                // автоматический слот ДО File.Delete, а не после.
                //
                // Сбой снимка не мешает удалению: он лишает точки возврата, но
                // не является причиной отказываться от Release.
                if (!string.IsNullOrEmpty(cs))
                {
                    try
                    {
                        var model = JsonUtility.FromJson<NsgFileModel>(File.ReadAllText(nsg));
                        if (model != null)
                        {
                            model.UnpackGraphs();
                            Nsg_Checkpoints.AutoSave(cs, model, null, "before release");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[NekoScriptGraph] release snapshot failed for " + nsg + ": " + e.Message);
                    }
                }

                try
                {
                    File.Delete(nsg);
                }
                catch
                {
                    failed++;
                    continue;
                }

                // Кэш обязан забыть документ: держать в памяти модель, которой
                // больше нет на диске, нельзя — состояние стало бы враньём, а
                // следующее сохранение воскресило бы удалённый файл.
                if (!string.IsNullOrEmpty(cs)) _documents.Remove(Normalize(cs));

                done++;
            }

            if (done > 0) AssetDatabase.Refresh();
            return done;
        }

        static string GuidOf(string assetPath)
        {
            // Файл может лежать вне Assets или быть не-ассетом (.rs): тогда
            // GUID пустой, и это нормально.
            try
            {
                return AssetDatabase.AssetPathToGUID(assetPath);
            }
            catch
            {
                return string.Empty;
            }
        }

        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.Replace('\\', '/');
        }
    }
}
