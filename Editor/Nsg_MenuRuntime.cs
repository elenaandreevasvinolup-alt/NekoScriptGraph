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
            public string Key;        // ключ локализации
            public string Fallback;   // английская подпись, как в атрибуте
            public string Handler;    // имя статического метода в Nsg_Menu
            public int Priority;
        }

        static readonly Item[] Items =
        {
            new Item { Key = "menu.open",      Fallback = "Open NekoScriptGraph",         Handler = "OpenWindow",         Priority = 10 },
            new Item { Key = "menu.problems",  Fallback = "Problems",                  Handler = "OpenErrors",         Priority = 20 },
            new Item { Key = "menu.health",    Fallback = "Architecture Health",       Handler = "OpenHealth",         Priority = 30 },
            new Item { Key = "menu.manage",    Fallback = "Take Selected Script Under Management", Handler = "ManageSelection", Priority = 50 },
            new Item { Key = "menu.unmanage",  Fallback = "Release Selected Script",   Handler = "UnmanageSelection",  Priority = 60 },
            new Item { Key = "menu.manageFolder", Fallback = "Take Selected Folder Under Management", Handler = "ManageSelectedFolder", Priority = 65 },
            new Item { Key = "menu.manageAll", Fallback = "Take Whole Project Under Management", Handler = "ManageAllProject", Priority = 70 },
            new Item { Key = "menu.unmanageFolder", Fallback = "Release Selected Folder", Handler = "UnmanageSelectedFolder", Priority = 66 },
            new Item { Key = "menu.unmanageAll", Fallback = "Release Whole Project", Handler = "UnmanageAllProject", Priority = 72 },
            new Item { Key = "menu.api",       Fallback = "Generate API Blocks for Selected Folder", Handler = "GenerateApiBlocks", Priority = 80 },
            new Item { Key = "menu.apiAll",    Fallback = "Build API Library for Whole Project", Handler = "GenerateApiForProject", Priority = 85 },
            new Item { Key = "menu.settings",  Fallback = "Settings",                  Handler = "OpenSettings",       Priority = 90 },
            new Item { Key = "menu.extensions", Fallback = "Extensions",               Handler = "OpenExtensions",     Priority = 92 },
            new Item { Key = "menu.languages", Fallback = "Languages: Show Loaded",    Handler = "ShowLanguages",      Priority = 100 },
            new Item { Key = "menu.reload",    Fallback = "Reload Block Library",      Handler = "ReloadLibrary",      Priority = 110 },
            new Item { Key = "menu.export",    Fallback = "Export Default Block Library", Handler = "ExportBlocks",    Priority = 120 },
            new Item { Key = "menu.selftest",  Fallback = "Self Test: Round Trip",     Handler = "RunSelfTest",        Priority = 140 },
            new Item { Key = "menu.mcpStart",  Fallback = "MCP Bridge: Start",         Handler = "McpBridgeStart",     Priority = 200 },
            new Item { Key = "menu.mcpStop",   Fallback = "MCP Bridge: Stop",          Handler = "McpBridgeStop",      Priority = 201 },
            new Item { Key = "menu.mcpUrl",    Fallback = "MCP Bridge: Copy Client URL", Handler = "McpBridgeCopyUrl", Priority = 202 },
        };

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
                TryRemove(Root + Items[i].Fallback);
                TryRemove(Root + Label(Items[i]));
            }

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                string path = Root + Label(item);

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
