using System.IO;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    public static class Nsg_Menu
    {
        /// <summary>
        /// 顶栏根路径。所有 NekoWorks 插件共用一个顶栏栏位，各自是一个子菜单，
        /// 这样装再多插件也不会把 Unity 顶栏横向撑爆。
        /// 改这里必须同步 Nsg_MenuRuntime.Root。
        /// </summary>
        public const string WorksRoot = "NekoWorks/";
        public const string Root = WorksRoot + "NekoScriptGraph/";

        // ------------------------------------------------------------------ Группы второго уровня
        //
        // ЗАЧЕМ ВТОРОЙ УРОВЕНЬ. В подменю плагина было два десятка пунктов
        // вперемешку: «Reload Block Library» рядом с «Release Whole Project», и
        // найти нужное можно было только чтением всего списка. Группы разделяют
        // список по СМЫСЛУ действия — окно, управление файлами, блоки,
        // ассистент, диагностика, инструменты.
        //
        // Имена групп английские и не локализуются: это путь в атрибуте
        // [MenuItem], а он константа времени компиляции. Локализуются листья —
        // группа добавляется к переведённой подписи в Nsg_MenuRuntime.
        public const string GWindow = Root + "Window/";
        public const string GManage = Root + "Manage/";
        public const string GBlocks = Root + "Blocks/";
        public const string GAssistant = Root + "Assistant/";
        public const string GDiagnostics = Root + "Diagnostics/";
        public const string GTools = Root + "Tools/";

        [MenuItem(GDiagnostics + "Languages: Show Loaded", false, 1410)]
        public static void ShowLanguages()
        {
            Nsg_LanguageRegistry.Rediscover();

            var sb = new System.Text.StringBuilder();
            sb.Append("[NekoScriptGraph] Языки:\n");

            var all = Nsg_LanguageRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                sb.Append("  ").Append(e.Ok ? "OK   " : "FAIL ").Append(e.Id);

                if (e.Language != null)
                {
                    sb.Append("  \"").Append(e.Language.DisplayName).Append("\"");
                    sb.Append("  ext: ").Append(string.Join(", ", e.Language.Extensions));
                    sb.Append(e.Language.BuiltIn ? "  [встроенный]" : "  [плагин]");
                }

                string folder = Nsg_LanguageRegistry.LibraryFolder(e);
                if (!string.IsNullOrEmpty(folder)) sb.Append("\n         блоки: ").Append(folder);
                if (!string.IsNullOrEmpty(e.Error)) sb.Append("\n         ошибка: ").Append(e.Error);

                sb.Append('\n');
            }

            if (all.Count == 0) sb.Append("  (ничего не найдено)\n");

            sb.Append("Папка плагинов: ").Append(Nsg_LanguageRegistry.SupportFolder);
            Debug.Log(sb.ToString());
        }

        // Подписи в атрибутах — английские и статические: [MenuItem] это
        // константа времени компиляции. Реальные, локализованные подписи
        // расставляет Nsg_MenuRuntime.Rebuild() при загрузке и смене языка.
        [MenuItem(GWindow + "Open NekoScriptGraph", false, 1000)]
        public static void OpenWindow()
        {
            Nsg_Window.Open();
        }

        [MenuItem(GWindow + "Problems", false, 1010)]
        public static void OpenErrors()
        {
            Nsg_ErrorWindow.Open();
        }

        /// <summary>
        /// Окно кошки. Это то же окно проблем: без ProgramNeko оно показывает
        /// только список ошибок, с ним — сверху кошку, снизу реплику.
        /// Пункт меню подписан статически, как и остальные пункты Unity.
        /// </summary>
        [MenuItem(GAssistant + "Assistant (ProgramNeko)", false, 1300)]
        public static void OpenAssistant()
        {
            Nsg_ErrorWindow.Open();

            if (!Nsg_NekoRegistry.Installed)
            {
                Debug.Log("[NekoScriptGraph] ProgramNeko is not installed. " +
                          "Drop the ProgramNeko folder into the plugin to enable the assistant.");
            }
        }

        /// <summary>
        /// Ctrl/Cmd+Shift+E — кошка объясняет логику выделенного блока.
        /// Ярлык глобальный, а не только внутри окна: так он работает, даже
        /// когда фокус в другом окне, а выделение уже сделано.
        /// </summary>
        [MenuItem(GAssistant + "Explain Selected Block %#e", false, 1310)]
        public static void ExplainSelectedBlock()
        {
            Nsg_Window.ExplainSelectedBlock();
        }

        /// <summary>
        /// Настройки плагина и кошки. Одно окно: «сделать кошку тише» и
        /// «снять расширение» — одно действие пользователя, и искать его по
        /// двум местам он не должен.
        /// </summary>
        [MenuItem(GWindow + "Settings", false, 1020)]
        public static void OpenSettings()
        {
            Nsg_SettingsWindow.Open();
        }

        /// <summary>Расширения: языковые пакеты, кошка, внешние движки.</summary>
        [MenuItem(GWindow + "Extensions", false, 1030)]
        public static void OpenExtensions()
        {
            Nsg_SettingsWindow.OpenExtensions();
        }

        [MenuItem(GDiagnostics + "Architecture Health", false, 1400)]
        public static void OpenHealth()
        {
            string path = SelectionSource();
            Nsg_Document doc = path != null ? Nsg_Manager.Instance.Open(path) : null;
            Nsg_HealthWindow.Open(doc);
        }

        [MenuItem(GManage + "Take Selected Script Under Management", false, 1100)]
        public static void ManageSelection()
        {
            string path = SelectionSource();
            if (path == null)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectSource"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            var doc = Nsg_Manager.Instance.Open(path);
            if (doc.IsManaged)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                    Path.GetFileName(path) + " " + Nsg_L10n.T("confirm.alreadyManaged"), Nsg_L10n.T("confirm.ok"));
                return;
            }

            doc.Diagnostics.Clear();
            if (!Nsg_Manager.Instance.Manage(doc))
            {
                Nsg_ErrorWindow.Push(doc.Diagnostics, doc.CsPath);
                return;
            }

            Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("info.managed", path));
            Nsg_Window.Open();
        }

        [MenuItem(GManage + "Release Selected Script", false, 1110)]
        public static void UnmanageSelection()
        {
            string path = SelectionSource();
            if (path == null)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectSource"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            var doc = Nsg_Manager.Instance.Open(path);
            if (!doc.IsManaged)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                    Path.GetFileName(path) + " " + Nsg_L10n.T("confirm.notManaged"), Nsg_L10n.T("confirm.ok"));
                return;
            }

            if (!EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                Nsg_L10n.T("confirm.unmanage"), Nsg_L10n.T("confirm.unmanageYes"),
                Nsg_L10n.T("confirm.cancel")))
                return;

            Nsg_Manager.Instance.Unmanage(doc);
            Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("info.unmanaged", path));
        }

        /// <summary>
        /// Отдельная функция, как и просили: превратить папку с C# в API-блоки.
        /// </summary>
        [MenuItem(GBlocks + "Generate API Blocks for Selected Folder", false, 1200)]
        public static void GenerateApiBlocks()
        {
            string folder = SelectionFolder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectFolder"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }
            GenerateApiForFolder(folder);
        }

        /// <summary>
        /// API-блоки для папки — по ВСЕМ загруженным языкам.
        ///
        /// Раньше здесь вызывался генератор C# напрямую, поэтому на папке с
        /// .py, .rs или .go он честно находил ноль файлов: эти расширения не
        /// MonoScript, и FindAssets("t:MonoScript") их не возвращает. Теперь
        /// спрашиваем каждый движок, а он сам обходит свою часть файловой
        /// системы — так же, как это уже делает генерация по всему проекту.
        /// </summary>
        public static void GenerateApiForFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;

            bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("api.title"),
                Nsg_L10n.T("api.prompt") + "\n\n" + folder + "\n→ " + Nsg_Settings.Instance.apiOutputFolder,
                Nsg_L10n.T("confirm.continue"), Nsg_L10n.T("confirm.cancel"));
            if (!ok) return;

            int generated = 0;
            int failedLangs = 0;
            var diag = new NsgDiagnostics();

            var entries = Nsg_LanguageRegistry.All;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Language == null) continue;

                var engine = entry.Language.CreateEngine();
                if (engine == null) { failedLangs++; continue; }

                var langDiag = new NsgDiagnostics();
                try
                {
                    generated += engine.GenerateApiBlocks(folder, langDiag);
                }
                catch (System.Exception e)
                {
                    langDiag.Error(NsgCodes.Internal, entry.Id + ": " + e.Message);
                }

                if (langDiag.HasErrors)
                {
                    failedLangs++;
                    for (int k = 0; k < langDiag.Items.Count; k++) diag.Add(langDiag.Items[k]);
                }
            }

            // Библиотеку перечитываем до показа ошибок: часть языков могла
            // отработать успешно, и терять их блоки из-за чужой ошибки нельзя.
            if (generated > 0) Nsg_Manager.Instance.ReloadLibrary();

            if (diag.HasErrors)
            {
                Nsg_ErrorWindow.Push(diag, null);
                return;
            }

            string msg = Nsg_L10n.T("api.doneAll", generated, failedLangs);
            Debug.Log("[NekoScriptGraph] " + msg);
            EditorUtility.DisplayDialog(Nsg_L10n.T("api.title"), msg, Nsg_L10n.T("confirm.ok"));
        }

        /// <summary>
        /// Один клик — API-блоки для всего проекта. Считает файлы заранее и
        /// предупреждает: на большом проекте это тысячи блоков и заметное время.
        /// </summary>
        [MenuItem(GBlocks + "Build API Library for Whole Project", false, 1210)]
        public static void GenerateApiForProject()
        {
            var manager = Nsg_Manager.Instance;
            var files = manager.FindSourceFiles(null);

            if (files.Count == 0)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("api.title"), Nsg_L10n.T("api.noSources"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("api.title"),
                Nsg_L10n.T("api.projectPrompt", files.Count, Nsg_Settings.Instance.apiOutputFolder),
                Nsg_L10n.T("confirm.continue"), Nsg_L10n.T("confirm.cancel"));
            if (!ok) return;

            int total = 0;
            int failed = 0;
            var entries = Nsg_LanguageRegistry.All;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Language == null) continue;

                var engine = entry.Language.CreateEngine();
                if (engine == null) { failed++; continue; }

                var diag = new NsgDiagnostics();
                try
                {
                    total += engine.GenerateApiBlocks("Assets", diag);
                }
                catch (System.Exception e)
                {
                    diag.Error(NsgCodes.Internal, entry.Id + ": " + e.Message);
                }

                if (diag.HasErrors) Nsg_ErrorWindow.Push(diag, null);
            }

            manager.ReloadLibrary();

            string msg = Nsg_L10n.T("api.done", total, failed);
            Debug.Log("[NekoScriptGraph] " + msg);
            EditorUtility.DisplayDialog(Nsg_L10n.T("api.title"), msg, Nsg_L10n.T("confirm.ok"));
        }

        [MenuItem(GManage + "Take Whole Project Under Management", false, 1140)]
        public static void ManageAllProject()
        {
            ManageAllIn(null);
        }

        [MenuItem(GManage + "Take Selected Folder Under Management", false, 1120)]
        public static void ManageSelectedFolder()
        {
            string folder = SelectionFolder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectFolder"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }
            ManageAllIn(folder);
        }

        /// <summary>Берёт под управление все исходники папки (или всего проекта).</summary>
        public static void ManageAllIn(string folder)
        {
            var manager = Nsg_Manager.Instance;
            var files = manager.FindSourceFiles(folder);

            int pending = 0;
            for (int i = 0; i < files.Count; i++)
            {
                if (!File.Exists(Nsg_Document.NsgPathFor(files[i]))) pending++;
            }

            if (pending == 0)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("manage.title"), Nsg_L10n.T("manage.nothing"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("manage.title"),
                Nsg_L10n.T("manage.prompt", pending),
                Nsg_L10n.T("confirm.continue"), Nsg_L10n.T("confirm.cancel"));
            if (!ok) return;

            int failed;
            int done = manager.ManageAll(folder, out failed);

            string msg = Nsg_L10n.T("manage.done", done, failed);
            Debug.Log("[NekoScriptGraph] " + msg);
            EditorUtility.DisplayDialog(Nsg_L10n.T("manage.title"), msg, Nsg_L10n.T("confirm.ok"));
        }

        [MenuItem(GManage + "Release Whole Project", false, 1150)]
        public static void UnmanageAllProject()
        {
            UnmanageAllIn(null);
        }

        [MenuItem(GManage + "Release Selected Folder", false, 1130)]
        public static void UnmanageSelectedFolder()
        {
            string folder = SelectionFolder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectFolder"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            UnmanageAllIn(folder);
        }

        /// <summary>
        /// Снимает с управления все исходники папки (или всего проекта): удаляет
        /// их .nsg.json. Сами .cs не трогаются — они были и остаются источником.
        ///
        /// Нужно, чтобы убрать плагин из проекта, не разбирая скрипты вручную:
        /// после этого рядом с кодом не остаётся ни одного файла NekoScriptGraph.
        /// </summary>
        public static void UnmanageAllIn(string folder)
        {
            var manager = Nsg_Manager.Instance;
            int pending = manager.FindManagedConfigs(folder).Count;

            if (pending == 0)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("manage.title"), Nsg_L10n.T("manage.unmanageNothing"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("manage.title"),
                Nsg_L10n.T("manage.unmanagePrompt", pending),
                Nsg_L10n.T("confirm.continue"), Nsg_L10n.T("confirm.cancel"));
            if (!ok) return;

            int failed;
            int done = manager.UnmanageAll(folder, out failed);

            string msg = Nsg_L10n.T("manage.unmanageDone", done, failed);
            Debug.Log("[NekoScriptGraph] " + msg);
            EditorUtility.DisplayDialog(Nsg_L10n.T("manage.title"), msg, Nsg_L10n.T("confirm.ok"));
        }

        [MenuItem(GBlocks + "Reload Block Library", false, 1220)]
        public static void ReloadLibrary()
        {
            Nsg_Manager.Instance.ReloadLibrary();
            Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("info.libraryReloaded",
                Nsg_Manager.Instance.Library.Blocks.Count));
        }

        [MenuItem(GBlocks + "Export Default Block Library", false, 1230)]
        public static void ExportBlocks()
        {
            var lib = new Nsg_BlockLibrary();
            lib.Blocks.AddRange(Nsg_BlockLibrary.CreateDefaults());
            lib.SaveTo(Nsg_Paths.BlocksDir);
            AssetDatabase.Refresh();
            Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("info.exported", Nsg_Paths.BlocksDir));
        }

        [MenuItem(GDiagnostics + "Self Test: Round Trip", false, 1420)]
        public static void RunSelfTest()
        {
            Nsg_SelfTest.Run();
        }

        /// <summary>
        /// Ctrl/Cmd+Shift+H — показать или снова спрятать файлы .nsg.json.
        /// Ярлык глобальный: он работает и когда окно плагина закрыто, потому
        /// что прятать файлы нужно независимо от того, открыт редактор блоков.
        /// </summary>
        [MenuItem(GTools + "Toggle Block Files Visibility %#h", false, 1500)]
        public static void ToggleBlockFiles()
        {
            bool hidden = Nsg_FileVisibility.Toggle();

            Debug.Log("[NekoScriptGraph] " +
                Nsg_L10n.T(hidden ? "files.hidden" : "files.shown",
                    Nsg_Manager.Instance.FindManagedConfigs("Assets").Count));
        }

        // ------------------------------------------------------------------
        // MCP-мост
        //
        // Подписи намеренно английские: пункты меню Unity пересобираются
        // редко, и смешивать их с переводами интерфейса не стоит.
        // ------------------------------------------------------------------

        [MenuItem(GTools + "MCP Bridge: Start", false, 1510)]
        public static void McpBridgeStart()
        {
            if (Nsg_McpBridge.Start())
            {
                Debug.Log("[NekoScriptGraph] MCP bridge is up. Client URL: " + Nsg_McpBridge.Url);
            }
        }

        [MenuItem(GTools + "MCP Bridge: Stop", false, 1520)]
        public static void McpBridgeStop()
        {
            Nsg_McpBridge.Stop();
            Debug.Log("[NekoScriptGraph] MCP bridge stopped.");
        }

        [MenuItem(GTools + "MCP Bridge: Copy Client URL", false, 1530)]
        public static void McpBridgeCopyUrl()
        {
            EditorGUIUtility.systemCopyBuffer = Nsg_McpBridge.Url;
            Debug.Log("[NekoScriptGraph] MCP URL copied to the clipboard: " + Nsg_McpBridge.Url +
                      " (running: " + Nsg_McpBridge.IsRunning + ")");
        }

        /// <summary>
        /// Путь выделенного исходника ЛЮБОГО поддерживаемого языка.
        ///
        /// Раньше здесь стояла проверка на «.cs», и подсказка требовала выбрать
        /// именно C#-файл — хотя под управление берутся все языки, у которых
        /// есть движок. Расширения берём у реестра языков, а не списком в коде:
        /// новый язык начинает работать сразу, без правки этого места.
        /// </summary>
        static string SelectionSource()
        {
            var obj = Selection.activeObject;
            if (obj == null) return null;

            string p = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(p)) return null;

            string ext = Path.GetExtension(p);
            if (string.IsNullOrEmpty(ext)) return null;

            return Nsg_Manager.Instance.RegisteredExtensions().Contains(ext) ? p : null;
        }

        static string SelectionFolder()
        {
            var obj = Selection.activeObject;
            if (obj == null) return null;
            string p = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(p)) return null;
            return AssetDatabase.IsValidFolder(p) ? p : null;
        }
    }
}
