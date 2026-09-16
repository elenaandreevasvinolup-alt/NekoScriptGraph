using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Движок одного языка: всё, что зависит от синтаксиса.
    ///
    /// Общая часть (модель блоков, граф, диагностика, локализация, отмена,
    /// точки сохранения, проходы, гигиена) от языка не зависит и живёт в Core.
    /// </summary>
    public interface INsg_LanguageEngine
    {
        // ---- файл <-> дерево структуры ----
        NsgFileModel Split(string source, string fileName, NsgDiagnostics diag);
        string Rebuild(NsgFileModel model);
        string Rebuild(NsgFileModel model, List<string> bodies);
        string BodyIndent(string header);
        string InnerBody(string printed);
        string MethodName(string header);

        // ---- текст <-> AST ----
        List<NsgStmt> ParseBody(string innerBody, NsgDiagnostics diag);

        // ---- AST <-> граф блоков ----
        NsgMethodGraph ToGraph(Nsg_BlockLibrary library, NsgDiagnostics diag,
                               List<NsgStmt> statements, NsgMethodGraph previous);
        List<NsgStmt> ToAst(Nsg_BlockLibrary library, NsgDiagnostics diag, NsgMethodGraph graph);

        // ---- AST -> текст ----
        string PrintBody(List<NsgStmt> statements, string indent);

        // ---- генерация API-блоков по папке исходников ----
        int GenerateApiBlocks(string folder, NsgDiagnostics diag);
    }

    /// <summary>
    /// Описание языка-плагина. Реализации ищутся рефлексией по всем
    /// загруженным сборкам, поэтому достаточно положить исходники в
    /// Dependencies/Editor/LanguageSupport/&lt;Id&gt;/ — Unity их скомпилирует,
    /// а плагин найдёт и включит.
    /// </summary>
    public interface INsg_Language
    {
        /// <summary>Стабильный идентификатор: "csharp", "cpp", "hlsl".</summary>
        string Id { get; }

        string DisplayName { get; }

        /// <summary>Имя файла иконки в Dependencies/Editor/Sprite, без расширения.</summary>
        string IconName { get; }

        /// <summary>Расширения файлов, например ".cs".</summary>
        string[] Extensions { get; }

        /// <summary>Встроен ли язык в плагин (C# и HLSL) или лежит в LanguageSupport.</summary>
        bool BuiltIn { get; }

        /// <summary>Версия контракта, которую реализует язык.</summary>
        int ApiVersion { get; }

        INsg_LanguageEngine CreateEngine();

        /// <summary>
        /// Библиотека блоков языка. Папка уже разрешена
        /// (<see cref="Nsg_LanguageRegistry.LibraryFolder"/>).
        /// </summary>
        Nsg_BlockLibrary CreateLibrary(string blocksFolder);
    }

    /// <summary>Текущая версия контракта. Меняется только при ломающих правках.</summary>
    public static class Nsg_LanguageApi
    {
        public const int Version = 1;
    }

    /// <summary>Манифест языка из LanguageSupport/&lt;Id&gt;/&lt;Id&gt;.language.json.</summary>
    [System.Serializable]
    public class Nsg_LanguageManifest
    {
        public int apiVersion = 1;
        public string id;
        public string displayName;
        public string icon;
        public string[] extensions;
        public string blocksFolder = "blocks";
        public string engineType;   // полное имя типа, реализующего INsg_Language
        public string author;
        public string note;
    }

    /// <summary>Найденный язык вместе с его папкой и манифестом.</summary>
    public class Nsg_LanguageEntry
    {
        public INsg_Language Language;
        public string Folder;          // папка языка, либо пусто для встроенных
        public Nsg_LanguageManifest Manifest;
        public string Error;           // причина, по которой язык не загрузился

        public bool Ok
        {
            get { return Language != null && string.IsNullOrEmpty(Error); }
        }

        public string Id
        {
            get
            {
                if (Language != null) return Language.Id;
                if (Manifest != null) return Manifest.id;
                return "?";
            }
        }
    }

    /// <summary>
    /// Реестр языков. Встроенные регистрируются кодом, остальные находятся
    /// рефлексией: пользователь просто кладёт папку в LanguageSupport.
    /// </summary>
    public static class Nsg_LanguageRegistry
    {
        // Папка устанавливаемых языков. Путь обязан совпадать с тем, где языки
        // реально лежат, иначе ScanSupportFolder молча ничего не находит:
        // языки всё равно загрузятся рефлексией (типы уже скомпилированы), но
        // их манифесты — displayName, icon, проверка apiVersion — останутся
        // непрочитанными, и папки blocks/ рядом с языком не будут учитываться.
        public static readonly string SupportFolder = Nsg_Paths.Root + "/LanguageSupport";
        public static readonly string SpriteFolder = Nsg_Paths.Root + "/Dependencies/Editor/Sprite";

        static readonly List<Nsg_LanguageEntry> Entries = new List<Nsg_LanguageEntry>();
        static bool _discovered;

        public static List<Nsg_LanguageEntry> All
        {
            get
            {
                EnsureDiscovered();
                return Entries;
            }
        }

        // ------------------------------------------------------------------
        // Обнаружение
        // ------------------------------------------------------------------

        public static void EnsureDiscovered()
        {
            if (_discovered) return;
            _discovered = true;
            Discover();
        }

        /// <summary>Сбрасывает кэш и ищет заново (после добавления языка).</summary>
        public static void Rediscover()
        {
            _discovered = false;
            Entries.Clear();
            Discover();
        }

        /// <summary>Регистрация встроенного языка.</summary>
        public static void Register(INsg_Language language)
        {
            if (language == null || string.IsNullOrEmpty(language.Id)) return;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Id == language.Id)
                {
                    Entries[i].Language = language;
                    Entries[i].Error = null;
                    return;
                }
            }

            Entries.Add(new Nsg_LanguageEntry
            {
                Language = language,
                Folder = BuiltInFolder(language.Id),
                Manifest = null
            });
        }

        static void Discover()
        {
            // 1) встроенные: реализуют INsg_Language и помечены BuiltIn.
            // 2) плагины: папка LanguageSupport/<Id> с манифестом и типом движка.
            var found = FindAllImplementations();

            for (int i = 0; i < found.Count; i++)
            {
                var lang = found[i];
                if (lang.BuiltIn) Register(lang);
            }

            ScanSupportFolder(found);

            // Плагины, у которых нет манифеста, но есть тип: подхватываем по Id.
            for (int i = 0; i < found.Count; i++)
            {
                var lang = found[i];
                if (lang.BuiltIn) continue;
                if (HasEntry(lang.Id)) continue;

                Entries.Add(new Nsg_LanguageEntry
                {
                    Language = lang,
                    Folder = SupportFolder + "/" + lang.Id
                });
            }
        }

        static void ScanSupportFolder(List<INsg_Language> available)
        {
            if (!Directory.Exists(SupportFolder)) return;

            string[] dirs = Directory.GetDirectories(SupportFolder);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i].Replace('\\', '/');
                string name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;

                // Папки с подчёркиванием — инфраструктура, а не язык
                // (например, _cstyle: общий движок C-семейства).
                if (name.StartsWith("_")) continue;

                string manifestPath = dir + "/" + name + ".language.json";
                if (!File.Exists(manifestPath))
                {
                    // Папка без манифеста: ищем тип по имени папки.
                    var byId = FindById(available, name);
                    if (byId != null && !HasEntry(byId.Id))
                    {
                        Entries.Add(new Nsg_LanguageEntry { Language = byId, Folder = dir });
                    }
                    else if (byId == null)
                    {
                        Entries.Add(new Nsg_LanguageEntry
                        {
                            Folder = dir,
                            Error = "нет " + name + ".language.json и не найден тип INsg_Language с Id '" + name + "'"
                        });
                    }
                    continue;
                }

                Nsg_LanguageManifest manifest = null;
                string error = null;
                try
                {
                    manifest = ReadManifest(File.ReadAllText(manifestPath));
                }
                catch (System.Exception e)
                {
                    error = "манифест не читается: " + e.Message;
                }

                if (manifest == null && error == null) error = "манифест пустой";
                if (manifest != null && manifest.apiVersion > Nsg_LanguageApi.Version)
                {
                    error = "язык требует API v" + manifest.apiVersion +
                            ", плагин умеет v" + Nsg_LanguageApi.Version;
                }

                INsg_Language lang = null;
                if (error == null)
                {
                    lang = !string.IsNullOrEmpty(manifest.engineType)
                        ? FindByTypeName(available, manifest.engineType)
                        : FindById(available, manifest.id);

                    if (lang == null)
                    {
                        error = "не найден тип INsg_Language: " +
                                (string.IsNullOrEmpty(manifest.engineType) ? manifest.id : manifest.engineType) +
                                " (Unity должна была скомпилировать исходники в этой папке)";
                    }
                }

                if (HasEntry(manifest != null ? manifest.id : name)) continue;

                Entries.Add(new Nsg_LanguageEntry
                {
                    Language = lang,
                    Folder = dir,
                    Manifest = manifest,
                    Error = error
                });
            }
        }

        /// <summary>
        /// Разбор манифеста. JsonUtility — единственное место, где реестру
        /// нужен UnityEngine, поэтому вызов вынесен в отдельный метод: JIT
        /// компилирует его только при реальном чтении манифеста, и реестр
        /// остаётся работоспособным вне редактора (автономные тесты).
        /// </summary>
        static Nsg_LanguageManifest ReadManifest(string json)
        {
            return JsonUtility.FromJson<Nsg_LanguageManifest>(json);
        }

        static List<INsg_Language> FindAllImplementations()
        {
            var result = new List<INsg_Language>();
            var iface = typeof(INsg_Language);
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();

            for (int a = 0; a < assemblies.Length; a++)
            {
                System.Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    // Часть типов сборки может не загрузиться (например,
                    // редакторные типы вне редактора). Берём те, что загрузились,
                    // вместо того чтобы отказываться от всей сборки.
                    types = e.Types;
                }
                catch
                {
                    continue; // сборка вообще не отдаёт типы
                }

                if (types == null) continue;

                for (int t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type == null) continue;

                    try
                    {
                        if (type.IsAbstract || type.IsInterface) continue;
                        if (!iface.IsAssignableFrom(type)) continue;

                        var instance = System.Activator.CreateInstance(type) as INsg_Language;
                        if (instance != null) result.Add(instance);
                    }
                    catch
                    {
                        // Тип загрузился не полностью (например, тянет
                        // редакторные зависимости) — просто пропускаем его,
                        // остальные языки это не должно ломать.
                    }
                }
            }

            return result;
        }

        static INsg_Language FindById(List<INsg_Language> list, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, System.StringComparison.OrdinalIgnoreCase)) return list[i];
            }
            return null;
        }

        static INsg_Language FindByTypeName(List<INsg_Language> list, string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i].GetType();
                if (t.FullName == typeName || t.Name == typeName) return list[i];
            }
            return null;
        }

        static bool HasEntry(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Id == id) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Доступ
        // ------------------------------------------------------------------

        public static Nsg_LanguageEntry GetEntry(string id)
        {
            EnsureDiscovered();
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Entries[i].Id, id, System.StringComparison.OrdinalIgnoreCase)) return Entries[i];
            }
            return null;
        }

        public static INsg_Language Get(string id)
        {
            var e = GetEntry(id);
            return e != null ? e.Language : null;
        }

        /// <summary>Запись языка по расширению файла (".cs").</summary>
        public static Nsg_LanguageEntry EntryForExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return null;
            if (!extension.StartsWith(".")) extension = "." + extension;

            EnsureDiscovered();
            for (int i = 0; i < Entries.Count; i++)
            {
                var lang = Entries[i].Language;
                if (lang == null || lang.Extensions == null) continue;
                for (int k = 0; k < lang.Extensions.Length; k++)
                {
                    if (string.Equals(lang.Extensions[k], extension, System.StringComparison.OrdinalIgnoreCase))
                        return Entries[i];
                }
            }
            return null;
        }

        /// <summary>Язык по расширению файла (".cs").</summary>
        public static INsg_Language ForExtension(string extension)
        {
            var e = EntryForExtension(extension);
            return e != null ? e.Language : null;
        }

        /// <summary>Встроенные языки лежат в Editor/Languages/&lt;Id&gt;.</summary>
        public static string BuiltInFolder(string id)
        {
            return Nsg_Paths.Root + "/Editor/Languages/" + id;
        }

        /// <summary>Папка библиотеки блоков языка.</summary>
        public static string LibraryFolder(Nsg_LanguageEntry entry)
        {
            if (entry == null) return null;

            if (entry.Language != null && entry.Language.BuiltIn)
            {
                // Встроенный язык: Blocks/ в корне плагина (текущее поведение).
                return Nsg_Paths.BlocksDir;
            }

            string sub = entry.Manifest != null && !string.IsNullOrEmpty(entry.Manifest.blocksFolder)
                ? entry.Manifest.blocksFolder
                : "blocks";

            if (string.IsNullOrEmpty(entry.Folder)) return null;
            return entry.Folder + "/" + sub;
        }

        public static Nsg_BlockLibrary LoadLibrary(Nsg_LanguageEntry entry)
        {
            if (entry == null || entry.Language == null)
            {
                return new Nsg_BlockLibrary();
            }

            string folder = LibraryFolder(entry);

            // Встроенный язык: база лежит на диске вместе с API/. Это данные,
            // поэтому правка Blocks/*.json работает без пересборки.
            if (entry.Language.BuiltIn)
            {
                var disk = new Nsg_BlockLibrary();
                if (!string.IsNullOrEmpty(folder)) Nsg_BlockLibrary.LoadInto(disk, folder, true);
                if (disk.Blocks.Count == 0)
                {
                    disk.Blocks.AddRange(Nsg_BlockLibrary.CreateDefaults());
                    if (!string.IsNullOrEmpty(folder)) disk.SaveTo(folder);
                }
                disk.Rebuild();
                return disk;
            }

            if (string.IsNullOrEmpty(folder))
            {
                return entry.Language.CreateLibrary(null);
            }

            // Устанавливаемый язык: КОД — источник истины. Он собирает общую
            // базу с учётом ограничений языка и добавляет свои идиомы, поэтому
            // правка AddLanguageBlocks видна сразу.
            var lib = entry.Language.CreateLibrary(folder);

            if (Directory.Exists(folder))
            {
                // Папка — необязательная надстройка: она только ДОБАВЛЯЕТ
                // блоки, которых нет в коде. Так правка языка видна всегда, а
                // устаревший снимок не может молча подменить актуальный блок.
                //
                // Именно на этом ломался HLSL: его папка была полным снимком
                // (база + идиомы), поэтому он выглядел копией C#, а новые
                // блоки из кода не появлялись.
                var extra = new Nsg_BlockLibrary();
                Nsg_BlockLibrary.LoadInto(extra, folder, true);
                AddMissing(lib, extra);
            }

            lib.Rebuild();
            return lib;
        }

        /// <summary>Добавляет блоки, которых ещё нет: код языка приоритетнее папки.</summary>
        static void AddMissing(Nsg_BlockLibrary into, Nsg_BlockLibrary from)
        {
            for (int i = 0; i < from.Blocks.Count; i++)
            {
                var b = from.Blocks[i];
                if (b == null || string.IsNullOrEmpty(b.id)) continue;
                if (into.Get(b.id) != null) continue;
                into.Blocks.Add(b);
            }
        }

        /// <summary>Иконка языка: сначала папка языка, потом Dependencies/Editor/Sprite.</summary>
        public static Sprite LoadIcon(Nsg_LanguageEntry entry)
        {
            if (entry == null) return null;

            string name = entry.Language != null ? entry.Language.IconName
                        : (entry.Manifest != null ? entry.Manifest.icon : null);
            if (string.IsNullOrEmpty(name)) return null;

            if (!string.IsNullOrEmpty(entry.Folder))
            {
                var local = AssetDatabase.LoadAssetAtPath<Sprite>(entry.Folder + "/" + name + ".png");
                if (local != null) return local;
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + "/" + name + ".png");
        }

        public static List<string> Ids()
        {
            var list = new List<string>();
            EnsureDiscovered();
            for (int i = 0; i < Entries.Count; i++) list.Add(Entries[i].Id);
            return list;
        }
    }
}
