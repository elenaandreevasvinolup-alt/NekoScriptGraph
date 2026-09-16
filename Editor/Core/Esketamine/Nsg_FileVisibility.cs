using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Видимость файлов .nsg.json в окне Project.
    ///
    /// Файлы конфигурации служебные: лежат рядом со скриптами и мешают их
    /// просматривать. По умолчанию они скрыты, а показать их можно
    /// переключателем (пункт меню с горячей клавишей или кнопка ленты).
    ///
    /// Скрытие — это HideFlags.HideInHierarchy на импортированном объекте.
    /// Флаг хранится в базе ассетов, а НЕ в .meta, поэтому после перезаписи
    /// файла (переимпорт создаёт новый объект) его нужно ставить заново —
    /// поэтому ApplyTo вызывается сразу после сохранения.
    ///
    /// На работу плагина это не влияет: и чтение, и поиск файлов идут через
    /// File/Directory, а не через AssetDatabase.
    /// </summary>
    [InitializeOnLoad]
    public static class Nsg_FileVisibility
    {
        /// <summary>Скрыты ли файлы конфигурации сейчас.</summary>
        public static bool Hidden
        {
            get { return Nsg_Settings.Instance.hideBlockFiles; }
        }

        static Nsg_FileVisibility()
        {
            // После перезагрузки домена флаги на объектах могли сброситься
            // (переимпорт, перезапуск). Восстанавливаем их отложенно, чтобы не
            // работать во время инициализации базы ассетов.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!Hidden) return;

                ApplyAll(false);
            };
        }

        /// <summary>Переключает видимость и возвращает новое состояние.</summary>
        public static bool Toggle()
        {
            var s = Nsg_Settings.Instance;
            s.hideBlockFiles = !s.hideBlockFiles;
            s.Save();

            ApplyAll(true);
            return s.hideBlockFiles;
        }

        /// <summary>
        /// Ставит или снимает скрытие у одного файла. Путь — вида
        /// «Assets/Scripts/Foo.nsg.json».
        /// </summary>
        public static void ApplyTo(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (!assetPath.EndsWith(Nsg_Paths.ManagedExtension, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

            // Файл мог быть только что записан на диск: сначала даём Unity его
            // увидеть, иначе объекта ещё нет и флаг ставить некуда.
            if (obj == null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            }
            if (obj == null) return;

            var want = Hidden ? HideFlags.HideInHierarchy : HideFlags.None;
            if (obj.hideFlags == want) return;

            obj.hideFlags = want;
        }

        /// <summary>
        /// Применяет состояние ко всем файлам проекта. Возвращает число
        /// затронутых файлов.
        ///
        /// save — сохранять ли базу ассетов сразу. Пакетное сохранение дорогое,
        /// поэтому при восстановлении после перезагрузки оно не нужно.
        /// </summary>
        public static int ApplyAll(bool save)
        {
            if (Nsg_Manager.Instance == null) return 0;

            var configs = Nsg_Manager.Instance.FindManagedConfigs("Assets");
            for (int i = 0; i < configs.Count; i++)
            {
                ApplyTo(configs[i]);
            }

            if (save && configs.Count > 0) AssetDatabase.SaveAssets();
            return configs.Count;
        }
    }
}
