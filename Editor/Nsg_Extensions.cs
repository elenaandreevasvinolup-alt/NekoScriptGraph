using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Расширения плагина: то, что можно поставить и снять.
    ///
    /// Три разных сущности живут под одним управлением, потому что для
    /// пользователя это одно действие — «поставить/снять»:
    ///
    ///   * языковые пакеты (Locale/&lt;code&gt;). Английский встроен в код и
    ///     снятию не подлежит; остальные — обычные папки;
    ///   * кошка-ассистент (ProgramNeko). Её отсутствие выключает продвинутое,
    ///     но не ломает базовое;
    ///   * внешний движок (C-уровень). Он лежит ЗА пакетом и ставится через
    ///     Package Manager, поэтому в основном пакете нет ни одной его строки.
    ///
    /// Выключение папок сделано ПЕРЕМЕЩЕНИЕМ в .disabled, а не удалением:
    /// так «снять» обратимо, и ни один файл пользователя не теряется.
    /// </summary>
    public static class Nsg_Extensions
    {
        public const string DisabledFolder = ".disabled";

        public static string DisabledRoot
        {
            get { return Nsg_Paths.Root + "/" + DisabledFolder; }
        }

        public static string DisabledLocaleRoot
        {
            get { return DisabledRoot + "/Locale"; }
        }

        public static string DisabledAssistantPath
        {
            get { return DisabledRoot + "/ProgramNeko"; }
        }

        /// <summary>
        /// Есть ли вообще что ставить или снимать.
        ///
        /// Нужно интерфейсу: в сборке без кошки, без языковых папок и без
        /// каталога расширений раздел «Расширения» показывать нечего, и он не
        /// должен появляться. Снятое тоже считается: его надо вернуть.
        ///
        /// Папки .disabled здесь НЕ создаются — проверка только читает.
        /// </summary>
        public static bool HasAnything
        {
            get
            {
                if (Nsg_AssistantPack.Installed || Nsg_AssistantPack.Disabled) return true;
                if (Nsg_LocalePacks.All().Count > 0) return true;
                if (Nsg_ExtensionCatalog.All().Count > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// Готовит служебные папки. Внутри .disabled пишется собственный
        /// .gitignore: это производное состояние, а не часть проекта.
        /// </summary>
        public static void EnsureLayout()
        {
            Nsg_Paths.EnsureDirectory(DisabledRoot);
            Nsg_Paths.EnsureDirectory(DisabledLocaleRoot);

            string gi = DisabledRoot + "/.gitignore";
            if (!File.Exists(gi))
            {
                try
                {
                    File.WriteAllText(gi,
                        "# Состояние установки NekoScriptGraph: папки, снятые через настройки.\n" +
                        "# Производное, а не исходники: в репозиторий не попадает.\n" +
                        "*\n!.gitignore\n");
                }
                catch
                {
                    // нет прав — не повод падать
                }
            }
        }

        /// <summary>
        /// Перемещает папку внутрь .disabled. Вместе с папкой едет её .meta,
        /// иначе Unity оставит осиротевший метафайл и переимпортирует папку.
        /// </summary>
        internal static bool MoveAside(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return false;
            if (!Directory.Exists(from)) return false;
            if (Directory.Exists(to)) return false;

            try
            {
                Nsg_Paths.EnsureDirectory(Path.GetDirectoryName(to));
                Directory.Move(from, to);

                string metaFrom = from + ".meta";
                if (File.Exists(metaFrom))
                {
                    string metaTo = to + ".meta";
                    try
                    {
                        if (File.Exists(metaTo)) File.Delete(metaTo);
                        File.Move(metaFrom, metaTo);
                    }
                    catch
                    {
                        // метафайл не критичен: Unity создаст новый
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NekoScriptGraph] не удалось снять " + from + ": " + e.Message);
                return false;
            }
        }

        internal static void RefreshAssets()
        {
            try { AssetDatabase.Refresh(); }
            catch { }
        }
    }

    // ---------------------------------------------------------------------
    // Языковые пакеты
    // ---------------------------------------------------------------------

    public class Nsg_PackInfo
    {
        public string Code;
        public string Name;
        public bool Enabled;
        public long Bytes;

        /// <summary>
        /// Имя папки. Отдельно от Code: код берётся из strings.json и может
        /// не совпадать с именем папки, а перемещать надо именно папку.
        /// </summary>
        public string Folder;

        public string SizeText
        {
            get { return Bytes > 0 ? Mathf.Max(1, (int)(Bytes / 1024)) + " KB" : "—"; }
        }
    }

    /// <summary>
    /// Языковые пакеты. Английский не показывается вовсе: он не папка, а
    /// встроенная таблица в Nsg_L10n, и снять его нельзя — иначе интерфейс
    /// остался бы без языка отката.
    /// </summary>
    public static class Nsg_LocalePacks
    {
        public static List<Nsg_PackInfo> All()
        {
            var list = new List<Nsg_PackInfo>();

            // Только чтение: список спрашивают и при пересборке меню, а
            // создавать папки в этот момент нельзя — база ассетов ещё грузится.
            Collect(Nsg_Paths.LocaleDir, true, list);
            Collect(Nsg_Extensions.DisabledLocaleRoot, false, list);

            list.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.Name ?? string.Empty, b.Name ?? string.Empty);
                return byName != 0 ? byName : string.CompareOrdinal(a.Code, b.Code);
            });
            return list;
        }

        static void Collect(string root, bool enabled, List<Nsg_PackInfo> into)
        {
            if (!Directory.Exists(root)) return;

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return; }

            for (int i = 0; i < dirs.Length; i++)
            {
                string file = Path.Combine(dirs[i], "strings.json");
                if (!File.Exists(file)) continue;

                string folder = Path.GetFileName(dirs[i]);
                string code = folder;
                string name = folder;

                try
                {
                    var data = JsonUtility.FromJson<Nsg_LocaleName>(File.ReadAllText(file));
                    if (data != null)
                    {
                        if (!string.IsNullOrEmpty(data.code)) code = data.code;
                        if (!string.IsNullOrEmpty(data.name)) name = data.name;
                    }
                }
                catch
                {
                    // битый файл языка — показываем папку как есть
                }

                into.Add(new Nsg_PackInfo
                {
                    Code = code,
                    Folder = folder,
                    Name = name,
                    Enabled = enabled,
                    Bytes = SizeOf(dirs[i])
                });
            }
        }

        [Serializable]
        class Nsg_LocaleName
        {
            public string code;
            public string name;
        }

        static long SizeOf(string dir)
        {
            long total = 0;
            try
            {
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try { total += new FileInfo(files[i]).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        /// <summary>
        /// Снимает пакет. folder — имя папки, code — код языка из strings.json:
        /// перемещаем папку, а активный язык сравниваем по коду.
        /// </summary>
        public static bool Disable(string folder, string code)
        {
            if (string.IsNullOrEmpty(folder)) return false;

            string from = Nsg_Paths.LocaleDir + "/" + folder;
            string to = Nsg_Extensions.DisabledLocaleRoot + "/" + folder;
            if (!Nsg_Extensions.MoveAside(from, to)) return false;

            Nsg_Extensions.RefreshAssets();
            AfterChange(code, false);
            return true;
        }

        public static bool Enable(string folder, string code)
        {
            if (string.IsNullOrEmpty(folder)) return false;

            string from = Nsg_Extensions.DisabledLocaleRoot + "/" + folder;
            string to = Nsg_Paths.LocaleDir + "/" + folder;
            if (!Nsg_Extensions.MoveAside(from, to)) return false;

            Nsg_Extensions.RefreshAssets();
            AfterChange(code, true);
            return true;
        }

        /// <summary>
        /// После снятия языка надо перечитать таблицу и, если сняли активный,
        /// вернуться на английский: иначе интерфейс молча оказался бы на
        /// другом языке из-за сдвига индексов.
        /// </summary>
        static void AfterChange(string code, bool enabled)
        {
            string active = Nsg_L10n.LangCode;
            Nsg_L10n.Reload();

            if (!enabled && string.Equals(active, code, StringComparison.OrdinalIgnoreCase))
            {
                Nsg_L10n.Lang = Nsg_L10n.EnIndex;
            }

            Nsg_MenuRuntime.Rebuild();
        }
    }

    // ---------------------------------------------------------------------
    // Кошка-ассистент
    // ---------------------------------------------------------------------

    /// <summary>
    /// Кошка как снимаемая часть. Снятие — перемещение папки, поэтому
    /// вернуть её можно одной кнопкой, и настройки характера не теряются.
    /// </summary>
    public static class Nsg_AssistantPack
    {
        public static bool Installed
        {
            get { return Directory.Exists(Nsg_Paths.NekoDir); }
        }

        public static bool Disabled
        {
            get { return Directory.Exists(Nsg_Extensions.DisabledAssistantPath); }
        }

        public static bool Disable()
        {
            if (!Installed) return false;
            if (!Nsg_Extensions.MoveAside(Nsg_Paths.NekoDir, Nsg_Extensions.DisabledAssistantPath))
                return false;

            Nsg_NekoRegistry.Rediscover();
            Nsg_CompletionRegistry.Rediscover();
            Nsg_Extensions.RefreshAssets();

            // Пункт меню «Расширения» существует только пока есть что снимать
            // и возвращать: без кошки он исчезает.
            Nsg_MenuRuntime.Rebuild();
            return true;
        }

        public static bool Enable()
        {
            if (!Disabled) return false;
            if (!Nsg_Extensions.MoveAside(Nsg_Extensions.DisabledAssistantPath, Nsg_Paths.NekoDir))
                return false;

            Nsg_NekoRegistry.Rediscover();
            Nsg_CompletionRegistry.Rediscover();
            Nsg_Extensions.RefreshAssets();
            Nsg_MenuRuntime.Rebuild();
            return true;
        }
    }

    // ---------------------------------------------------------------------
    // Внешние движки (C-уровень)
    // ---------------------------------------------------------------------

    /// <summary>Одна запись каталога расширений.</summary>
    [Serializable]
    public class Nsg_ExtensionEntry
    {
        public string id;
        public string nameKey;
        public string descKey;
        public string version;
        public string minUnity;

        /// <summary>Относительный путь к шаблону внутри Extensions~.</summary>
        public string local;

        /// <summary>Git-адрес. Имеет приоритет над local.</summary>
        public string git;
        public string tag;
    }

    [Serializable]
    public class Nsg_ExtensionCatalogFile
    {
        public int schemaVersion = 1;
        public List<Nsg_ExtensionEntry> extensions = new List<Nsg_ExtensionEntry>();
    }

    /// <summary>
    /// Каталог расширений. Лежит данными, поэтому новое расширение
    /// добавляется строкой в JSON, а не правкой кода.
    /// </summary>
    public static class Nsg_ExtensionCatalog
    {
        public static string CatalogPath
        {
            get { return Nsg_Paths.Root + "/Extensions~/catalog.json"; }
        }

        static List<Nsg_ExtensionEntry> _cache;

        public static List<Nsg_ExtensionEntry> All()
        {
            if (_cache != null) return _cache;

            _cache = new List<Nsg_ExtensionEntry>();
            try
            {
                if (File.Exists(CatalogPath))
                {
                    var data = JsonUtility.FromJson<Nsg_ExtensionCatalogFile>(
                        File.ReadAllText(CatalogPath));
                    if (data != null && data.extensions != null) _cache = data.extensions;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NekoScriptGraph] каталог расширений не прочитан: " + e.Message);
            }
            return _cache;
        }

        public static Nsg_ExtensionEntry Find(string id)
        {
            var all = All();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].id == id) return all[i];
            }
            return null;
        }

        public static void Reload()
        {
            _cache = null;
        }

        /// <summary>Откуда ставить: git важнее локального шаблона.</summary>
        public static string SourceOf(Nsg_ExtensionEntry e)
        {
            if (e == null) return null;

            if (!string.IsNullOrEmpty(e.git))
            {
                return string.IsNullOrEmpty(e.tag) ? e.git : e.git + "#" + e.tag;
            }

            if (!string.IsNullOrEmpty(e.local))
            {
                string abs;
                try { abs = Path.GetFullPath(e.local); }
                catch { return null; }

                if (Directory.Exists(abs)) return "file:" + abs;
            }

            return null;
        }
    }

    /// <summary>
    /// Установка и снятие внешних движков через Package Manager.
    ///
    /// Сложность здесь одна, и она не в кнопке: Package Manager перезагружает
    /// домен, а вместе с ним исчезает и объект запроса. Поэтому «что мы
    /// делали» запоминается в EditorPrefs, а результат проверяется по факту —
    /// наличию пакета, — а не по объекту, который к тому времени уже мёртв.
    /// </summary>
    public static class Nsg_ExtInstaller
    {
        const string PendingKey = "NekoScriptGraph.Ext.Pending";

        static AddRequest _add;
        static RemoveRequest _remove;
        static bool _polling;

        public static string LastError { get; private set; }

        /// <summary>Установлен ли пакет. Проверка синхронная и переживает перезагрузку.</summary>
        public static bool IsInstalled(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                // Полное имя обязательно: UnityEditor.PackageInfo — другой тип,
                // и короткое имя здесь неоднозначно.
                return UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                    "Packages/" + id + "/package.json") != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsBusy
        {
            get { return _add != null || _remove != null; }
        }

        public static void Install(string id)
        {
            var entry = Nsg_ExtensionCatalog.Find(id);
            string source = Nsg_ExtensionCatalog.SourceOf(entry);

            if (string.IsNullOrEmpty(source))
            {
                LastError = Nsg_L10n.T("ext.noSource");
                return;
            }

            LastError = null;
            EditorPrefs.SetString(PendingKey, "install|" + id);

            try
            {
                _add = Client.Add(source);
                StartPolling();
            }
            catch (Exception e)
            {
                LastError = e.Message;
                EditorPrefs.DeleteKey(PendingKey);
            }
        }

        public static void Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            LastError = null;
            EditorPrefs.SetString(PendingKey, "remove|" + id);

            try
            {
                _remove = Client.Remove(id);
                StartPolling();
            }
            catch (Exception e)
            {
                LastError = e.Message;
                EditorPrefs.DeleteKey(PendingKey);
            }
        }

        static void StartPolling()
        {
            if (_polling) return;
            _polling = true;
            EditorApplication.update += Poll;
        }

        static void StopPolling()
        {
            if (!_polling) return;
            _polling = false;
            EditorApplication.update -= Poll;
        }

        static void Poll()
        {
            if (_add != null)
            {
                if (!_add.IsCompleted) return;

                var r = _add;
                _add = null;
                Finish(r.Status == UnityEditor.PackageManager.StatusCode.Success,
                       r.Error != null ? r.Error.message : null,
                       "install", r.Result != null ? r.Result.packageId : null);
                return;
            }

            if (_remove != null)
            {
                if (!_remove.IsCompleted) return;

                var r = _remove;
                _remove = null;
                Finish(r.Status == UnityEditor.PackageManager.StatusCode.Success,
                       r.Error != null ? r.Error.message : null,
                       "remove", null);
            }
        }

        static void Finish(bool ok, string error, string op, string packageId)
        {
            StopPolling();

            if (!ok) LastError = error;

            string pending = EditorPrefs.GetString(PendingKey, null);
            string id = IdOf(pending);
            EditorPrefs.DeleteKey(PendingKey);

            if (ok)
            {
                Nsg_CompletionRegistry.Rediscover();
                Nsg_ExtensionCatalog.Reload();
                Debug.Log("[NekoScriptGraph] расширение " +
                          (string.IsNullOrEmpty(id) ? packageId : id) + ": " + op + " выполнено.");
            }
            else
            {
                Debug.LogWarning("[NekoScriptGraph] расширение не удалось " + op + ": " + error);
            }
        }

        static string IdOf(string pending)
        {
            if (string.IsNullOrEmpty(pending)) return null;
            int bar = pending.IndexOf('|');
            return bar >= 0 && bar + 1 < pending.Length ? pending.Substring(bar + 1) : null;
        }

        /// <summary>
        /// Возобновление после перезагрузки домена. Запрос умер, но намерение
        /// осталось: сверяем его с фактическим состоянием пакета.
        /// </summary>
        internal static void Resume()
        {
            string pending = EditorPrefs.GetString(PendingKey, null);
            if (string.IsNullOrEmpty(pending)) return;

            string id = IdOf(pending);
            if (string.IsNullOrEmpty(id))
            {
                EditorPrefs.DeleteKey(PendingKey);
                return;
            }

            bool installed = IsInstalled(id);
            bool wantedInstalled = pending.StartsWith("install|", StringComparison.Ordinal);

            EditorPrefs.DeleteKey(PendingKey);
            Nsg_CompletionRegistry.Rediscover();

            if (installed == wantedInstalled)
            {
                Debug.Log("[NekoScriptGraph] расширение " + id + ": состояние подтверждено после " +
                          "перезагрузки (" + (installed ? "установлено" : "снято") + ").");
            }
            else
            {
                LastError = Nsg_L10n.T("ext.reloadFailed");
                Debug.LogWarning("[NekoScriptGraph] расширение " + id +
                                 ": состояние не совпало с намерением. Проверьте Package Manager.");
            }
        }
    }

    /// <summary>
    /// Точка возобновления: переживает перезагрузку домена, потому что
    /// намерение лежит в EditorPrefs, а не в памяти.
    /// </summary>
    [InitializeOnLoad]
    internal static class Nsg_ExtInstallerBoot
    {
        static Nsg_ExtInstallerBoot()
        {
            // Отложенно: создание папок и запись .gitignore во время загрузки
            // домена дёргают базу ассетов в самый неудачный момент.
            EditorApplication.delayCall += () =>
            {
                Nsg_Extensions.EnsureLayout();
                Nsg_ExtInstaller.Resume();
            };
        }
    }
}
