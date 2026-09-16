using UnityEditor;

namespace NekoScriptGraph
{
    /// <summary>
    /// Связывает ядро с редактором Unity при загрузке: хранит выбранный язык
    /// в EditorPrefs, не затаскивая UnityEditor в само ядро перевода.
    /// </summary>
    [InitializeOnLoad]
    internal static class Nsg_EditorBoot
    {
        const string LangKey = "NekoScriptGraph.lang";

        /// <summary>Английский — индекс 0. Остальные языки находятся в Locale/.</summary>
        const int DefaultLang = Nsg_L10n.EnIndex;

        static Nsg_EditorBoot()
        {
            // Связка ядро ↔ редактор: GUID ассетов, импорт, файлы .nsg.json.
            // Одна на всё, включая агентский интерфейс и MCP-мост.
            Nsg_EditorHooks.Install();

            Nsg_L10n.Restore = () => EditorPrefs.GetInt(LangKey, DefaultLang);
            Nsg_L10n.Persist = v => EditorPrefs.SetInt(LangKey, v);
            Nsg_L10n.LoadFromHook();

            // Встроен только C#. Всё остальное (C, C++, Java, HLSL, Rust, Python
            // и любые будущие) находится рефлексией в
            // Dependencies/Editor/LanguageSupport/<Id>/.
            //
            // Важно: ядро НЕ ссылается на типы устанавливаемых языков — иначе
            // удаление папки ломало бы компиляцию плагина и «съёмность» была бы
            // фиктивной.
            Nsg_LanguageRegistry.Register(new Nsg_CSharpLanguage());
            Nsg_LanguageRegistry.EnsureDiscovered();

            // Подписи пунктов меню: [MenuItem] статичен, поэтому пересобираем
            // меню подписями выбранного языка.
            Nsg_MenuRuntime.Rebuild();

            Nsg_PassRegistry.Register(new Nsg_HealthPass());

            // Точка расширения авто-оптимизации.
            //
            // Оптимизаторы регистрируются здесь же и только тут, движку
            // править ничего не нужно:
            //
            //   Nsg_PassRegistry.Register(new Nsg_ConstantFoldPass());
            //   Nsg_PassRegistry.Register(new Nsg_SignalMergePass());
            //
            // Появятся вместе с блоками низкого уровня: свёртка констант,
            // удаление мёртвого кода, слияние сообщений, устранение
            // повторных подписок. До тех пор окно гигиены честно показывает,
            // что оптимизаторов ноль.
        }
    }
}
