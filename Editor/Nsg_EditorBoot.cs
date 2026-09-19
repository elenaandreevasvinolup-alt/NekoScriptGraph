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

            // Оптимизаторы. Регистрируются здесь и только тут, движку править
            // ничего не нужно — окно гигиены показывает их число само.
            //
            // Первый проход — чистка недостижимого: операторы после
            // return/break/continue и осиротевшие узлы. Прогон всегда идёт
            // через Nsg_Optimizer.Run, то есть с копией модели и проверкой
            // «печатается и разбирается заново»; не прошедшая правка
            // откатывается целиком.
            //
            // Следующие на очереди — свёртка констант и слияние сообщений:
            // для них нужны блоки низкого уровня, и до тех пор они не
            // регистрируются, а не изображаются пустышками.
            // Порядок регистрации = порядок прогона. Свёртка идёт первой:
            // осиротевший литерал условия уберёт чистка недостижимого,
            // зарегистрированная сразу за ней.
            Nsg_PassRegistry.Register(new Nsg_ConstantConditionPass());
            Nsg_PassRegistry.Register(new Nsg_DeadCodePass());
        }
    }
}
