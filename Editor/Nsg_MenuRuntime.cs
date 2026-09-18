using System;
using System.Collections.Generic;
using System.Reflection;

namespace NekoScriptGraph
{
    /// <summary>
    /// Пересобирает меню Unity с локализованными подписями.
    ///
    /// Атрибут <c>[MenuItem]</c> — это константа времени компиляции, поэтому
    /// подпись, заданная в атрибуте, не может зависеть от выбранного языка:
    /// Unity регистрирует пункты один раз при запуске. Единственный способ
    /// менять подписи на лету — обратиться к внутреннему API
    /// <c>UnityEditor.Menu</c> через рефлексию.
    ///
    /// Если API недоступен (другая версия Unity), остаются статические
    /// английские подписи из атрибутов — деградация без поломки.
    /// </summary>
    public static class Nsg_MenuRuntime
    {
        /// <summary>必须与 Nsg_Menu.Root 保持一致。</summary>
        const string Root = Nsg_Menu.Root;

        public class Item
        {
            public string Group;      // сегмент пути второго уровня (английский, не локализуется)
            public string Key;        // ключ локализации
            public string Fallback;   // английская подпись, как в атрибуте
            public string Handler;    // имя статического метода в Nsg_Menu
            public int Priority;
        }

        static readonly Item[] Items =
        {
            new Item { Group = "Window",      Key = "menu.open",      Fallback = "Open NekoScriptGraph",         Handler = "OpenWindow",         Priority = 1000 },
            new Item { Group = "Window",      Key = "menu.problems",  Fallback = "Problems",                  Handler = "OpenErrors",         Priority = 1010 },
            new Item { Group = "Window",      Key = "menu.settings",  Fallback = "Settings",                  Handler = "OpenSettings",       Priority = 1020 },
            new Item { Group = "Window",      Key = "menu.extensions", Fallback = "Extensions",               Handler = "OpenExtensions",     Priority = 1030 },
            new Item { Group = "Manage",      Key = "menu.manage",    Fallback = "Take Selected Script Under Management", Handler = "ManageSelection", Priority = 1100 },
            new Item { Group = "Manage",      Key = "menu.unmanage",  Fallback = "Release Selected Script",   Handler = "UnmanageSelection",  Priority = 1110 },
            new Item { Group = "Manage",      Key = "menu.manageFolder", Fallback = "Take Selected Folder Under Management", Handler = "ManageSelectedFolder", Priority = 1120 },
            new Item { Group = "Manage",      Key = "menu.unmanageFolder", Fallback = "Release Selected Folder", Handler = "UnmanageSelectedFolder", Priority = 1130 },
            new Item { Group = "Manage",      Key = "menu.manageAll", Fallback = "Take Whole Project Under Management", Handler = "ManageAllProject", Priority = 1140 },
            new Item { Group = "Manage",      Key = "menu.unmanageAll", Fallback = "Release Whole Project", Handler = "UnmanageAllProject", Priority = 1150 },
            new Item { Group = "Blocks",      Key = "menu.api",       Fallback = "Generate API Blocks for Selected Folder", Handler = "GenerateApiBlocks", Priority = 1200 },
            new Item { Group = "Blocks",      Key = "menu.apiAll",    Fallback = "Build API Library for Whole Project", Handler = "GenerateApiForProject", Priority = 1210 },
            new Item { Group = "Blocks",      Key = "menu.reload",    Fallback = "Reload Block Library",      Handler = "ReloadLibrary",      Priority = 1220 },
            new Item { Group = "Blocks",      Key = "menu.export",    Fallback = "Export Default Block Library", Handler = "ExportBlocks",    Priority = 1230 },
            new Item { Group = "Diagnostics", Key = "menu.health",    Fallback = "Architecture Health",       Handler = "OpenHealth",         Priority = 1400 },
            new Item { Group = "Diagnostics", Key = "menu.languages", Fallback = "Languages: Show Loaded",    Handler = "ShowLanguages",      Priority = 1410 },
            new Item { Group = "Diagnostics", Key = "menu.selftest",  Fallback = "Self Test: Round Trip",     Handler = "RunSelfTest",        Priority = 1420 },
            new Item { Group = "Tools",       Key = "menu.mcpStart",  Fallback = "MCP Bridge: Start",         Handler = "McpBridgeStart",     Priority = 1510 },
            new Item { Group = "Tools",       Key = "menu.mcpStop",   Fallback = "MCP Bridge: Stop",          Handler = "McpBridgeStop",      Priority = 1520 },
            new Item { Group = "Tools",       Key = "menu.mcpUrl",    Fallback = "MCP Bridge: Copy Client URL", Handler = "McpBridgeCopyUrl", Priority = 1530 },
        };

        /// <summary>Полный путь пункта: корень + группа + подпись.
        ///
        /// Группа стоит В ПУТИ, но не в подписи: она одинакова на всех языках, а
        /// переводится только лист. Иначе пришлось бы переводить и имена групп,
        /// а они — часть пути в атрибуте [MenuItem], то есть константа времени
        /// компиляции.</summary>
        static string PathOf(Item item, string leaf)
        {
            return string.IsNullOrEmpty(item.Group)
                ? Root + leaf
                : Root + item.Group + "/" + leaf;
        }

        static Type _menuType;
        static MethodInfo _remove;
        static MethodInfo _add;
        static Type _delegateType;
        static bool _probed;
        static bool _warned;

        public static bool Available
        {
            get
            {
                Probe();
                return _add != null && _remove != null && _delegateType != null;
            }
        }

        static void Probe()
        {
            if (_probed) return;
            _probed = true;

            try
            {
                var asm = typeof(UnityEditor.EditorApplication).Assembly;
                _menuType = asm.GetType("UnityEditor.Menu");
                if (_menuType == null) return;

                _delegateType = _menuType.GetNestedType("MenuFunction",
                                   BindingFlags.Public | BindingFlags.NonPublic)
                               ?? asm.GetType("UnityEditor.MenuFunction");
                if (_delegateType == null) return;

                _remove = _menuType.GetMethod("RemoveMenuItem",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);

                _add = _menuType.GetMethod("AddMenuItem",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string), typeof(bool), typeof(int), _delegateType }, null);
            }
            catch
            {
                _menuType = null;
                _remove = null;
                _add = null;
                _delegateType = null;
            }
        }

        /// <summary>
        /// Пересобирает пункты меню подписями текущего языка. Вызывается при
        /// загрузке домена и при смене языка.
        /// </summary>
        public static void Rebuild()
        {
            Probe();

            if (!Available)
            {
                if (!_warned)
                {
                    _warned = true;
                    UnityEngine.Debug.LogWarning(
                        "[NekoScriptGraph] внутренний API меню недоступен: подписи пунктов " +
                        "останутся английскими. Остальной интерфейс локализуется нормально.");
                }
                return;
            }

            var menuType = typeof(Nsg_Menu);

            // Сначала убираем статические английские пункты из атрибутов, затем
            // локализованные от прошлого языка. Иначе после первой же смены
            // языка в меню оказались бы оба варианта.
            for (int i = 0; i < Items.Length; i++)
            {
                TryRemove(PathOf(Items[i], Items[i].Fallback));
                TryRemove(PathOf(Items[i], Label(Items[i])));
            }

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                string path = PathOf(item, Label(item));

                // Пункт «Расширения» существует, только если есть чем
                // управлять. В сборке без кошки, без языковых пакетов и без
                // каталога расширений он не появляется вовсе — при этом
                // статический пункт из атрибута уже снят циклом выше.
                if (item.Key == "menu.extensions" && !Nsg_Extensions.HasAnything) continue;

                var method = menuType.GetMethod(item.Handler,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) continue;

                Delegate del;
                try
                {
                    del = Delegate.CreateDelegate(_delegateType, method);
                }
                catch
                {
                    continue;
                }

                try
                {
                    _add.Invoke(null, new object[] { path, false, item.Priority, del });
                }
                catch
                {
                    // не смогли добавить — останется статический пункт из атрибута
                }
            }
        }

        static void TryRemove(string path)
        {
            try
            {
                _remove.Invoke(null, new object[] { path });
            }
            catch
            {
                // пункта могло не быть — это нормально
            }
        }

        public static string Label(Item item)
        {
            string s = Nsg_L10n.T(item.Key);
            // T() возвращает сам ключ, если перевода нет.
            return s == item.Key ? item.Fallback : s;
        }
    }
}
