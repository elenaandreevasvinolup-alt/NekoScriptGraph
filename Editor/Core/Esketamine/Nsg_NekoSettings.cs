using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>Вид элемента в настройках кошки.</summary>
    public enum Nsg_NekoSettingKind
    {
        /// <summary>Ползунок от Min до Max.</summary>
        Slider,

        /// <summary>Переключатель. 0 — выключено, 1 — включено.</summary>
        Toggle,

        /// <summary>Выбор из ChoiceKeys; значение — индекс.</summary>
        Choice,

        /// <summary>Только текст, ничего не настраивает.</summary>
        Info
    }

    /// <summary>
    /// Описание одного параметра кошки.
    ///
    /// Кошкин характер описывает САМА кошка, а ядро только рисует по этому
    /// описанию. Иначе ядру пришлось бы знать про «частоту речи» и «闲话»,
    /// и любая другая кошка не смогла бы завести свои настройки.
    /// </summary>
    public class Nsg_NekoSettingDef
    {
        /// <summary>Ключ хранения. Стабилен: по нему читается значение.</summary>
        public string Key;

        public Nsg_NekoSettingKind Kind = Nsg_NekoSettingKind.Slider;

        public float Min = 0f;
        public float Max = 2f;
        public float Default = 1f;

        /// <summary>Шаг ползунка. 0 — плавно.</summary>
        public float Step = 0f;

        /// <summary>Ключ названия в языковом пакете кошки.</summary>
        public string TitleKey;

        /// <summary>Ключ пояснения. null — пояснения нет.</summary>
        public string DescKey;

        /// <summary>
        /// Готовая подпись. Заполняется кошкой, когда подпись берётся из ЕЁ
        /// языкового пакета: у кошки своя таблица строк, и ядро в неё не
        /// заглядывает. Если пусто — берётся TitleKey из таблицы ядра.
        /// </summary>
        public string Title;

        /// <summary>Готовое пояснение. Приоритетнее DescKey.</summary>
        public string Desc;

        /// <summary>Варианты для Choice: ключи локализации.</summary>
        public string[] ChoiceKeys;

        /// <summary>Значения для Choice. Длина обязана совпадать с ChoiceKeys.</summary>
        public float[] ChoiceValues;
    }

    /// <summary>
    /// Необязательная способность кошки: рассказать о себе и дать себя
    /// настроить.
    ///
    /// Хранение — забота кошки: она сама решает, где лежит её характер.
    /// Ядро только спрашивает и показывает. Удаление кошки уносит и настройки,
    /// потому что они лежат в её папке, а не в настройках плагина.
    /// </summary>
    public interface INsg_NekoSettings
    {
        /// <summary>Список параметров в порядке показа.</summary>
        List<Nsg_NekoSettingDef> Schema { get; }

        float Get(string key);

        /// <summary>Применить немедленно. Запись на диск — в <see cref="Save"/>.</summary>
        void Set(string key, float value);

        /// <summary>Сохранить на диск. Вызывается по окончании перетаскивания.</summary>
        void Save();

        /// <summary>Именованные наборы значений: «тихая», «обычная», «болтливая».</summary>
        List<Nsg_NekoPreset> Presets { get; }

        /// <summary>Применить набор.</summary>
        void ApplyPreset(Nsg_NekoPreset preset);
    }

    /// <summary>Именованный набор значений кошкиных параметров.</summary>
    public class Nsg_NekoPreset
    {
        /// <summary>Ключ названия в языковом пакете кошки.</summary>
        public string TitleKey;

        /// <summary>Значения по ключам параметров. Недостающие берут Default.</summary>
        public Dictionary<string, float> Values = new Dictionary<string, float>();
    }

    /// <summary>
    /// Поиск настроек у установленной кошки. Ядро не знает типа реализации,
    /// поэтому кошку можно заменить или удалить целиком.
    /// </summary>
    public static class Nsg_NekoSettingsRegistry
    {
        public static INsg_NekoSettings Current
        {
            get
            {
                var a = Nsg_NekoRegistry.Assistant;
                return a as INsg_NekoSettings;
            }
        }

        public static bool Available
        {
            get { return Current != null; }
        }
    }
}
