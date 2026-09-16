namespace NekoScriptGraph
{
    /// <summary>
    /// Версия ПЛАГИНА как пакета — отдельно от версии ядра.
    ///
    /// Обязана совпадать с полем version в package.json: Unity читает именно
    /// файл, а это значение нужно интерфейсу, чтобы показать версию, не
    /// разыскивая package.json на диске (в Assets путь один, в Packages —
    /// другой, а в собранном пакете третий).
    ///
    /// Версия ядра живёт в Nsg_Core.Version и меняется независимо: см.
    /// комментарий там о том, когда какой номер поднимать.
    /// </summary>
    public static class Nsg_Version
    {
        /// <summary>Версия пакета. Держать в согласии с package.json.</summary>
        public const string Plugin = "1.0.2";
    }
}
