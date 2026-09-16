using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Язык Swift. Пользуется общей машиной разбора C-семейства, но профиль
    /// включает то, чего у семейства нет и без чего Swift разбирался бы почти
    /// целиком как сырой текст:
    ///
    ///   • автоматическая ';' в конце строки — в Swift её не пишут;
    ///   • скобка тела на строке заголовка, и "}" с "else" тоже: иначе
    ///     конец строки закрыл бы оператор и код стал бы неразборным;
    ///   • условия без круглых скобок: "if x {", а не "if (x)".
    ///
    /// Что учтено отдельно:
    ///   • тип возврата через "->": "func f() -> Int {" (общее правило для
    ///     аннотаций, работает у Rust и HLSL так же);
    ///   • цикл по коллекции: "for x in y {";
    ///   • метки аргументов в вызове: "clamp(value: 1, low: 0)" — метка
    ///     пропускается, аргумент остаётся выражением;
    ///   • объявление через let/var: "let x = 5" разбирается как обычное
    ///     объявление, потому что let и var — не ключевые слова операторов;
    ///   • строки препроцессора ("#if", "#selector", "#available") уходят в
    ///     тривиальный текст и не мешают разбору.
    ///
    /// Что пока остаётся сырым текстом: switch, guard, defer, try/throws,
    /// аннотация типа в объявлении ("let x: Int = 5"), замыкания и
    /// trailing closure. Сырой текст сохраняется дословно, поэтому круговое
    /// преобразование ничего не теряет — эти строки просто ещё не блоки.
    /// </summary>
    public class Nsg_SwiftLanguage : Nsg_CStyleLanguageBase
    {
        static Nsg_LanguageProfile _profile;

        protected override Nsg_LanguageProfile Profile
        {
            get
            {
                if (_profile == null) _profile = Build();
                return _profile;
            }
        }

        protected override void AddLanguageBlocks(List<NsgBlockDef> into)
        {
            AddOutput(into);
            AddConversions(into);
            AddCollections(into);
            AddStrings(into);
        }

        // ------------------------------------------------------------------
        // Вывод и проверки
        // ------------------------------------------------------------------

        static void AddOutput(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Call("swift.print", "statement", "print",
                "print {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("swift.fatalError", "statement", "fatalError",
                "fatal error {0}", Nsg_CStyleBlocks.E("message")));

            into.Add(Nsg_CStyleBlocks.Call("swift.assert", "statement", "assert",
                "assert {0}", Nsg_CStyleBlocks.E("condition")));

            into.Add(Nsg_CStyleBlocks.Call("swift.precondition", "statement", "precondition",
                "precondition {0}", Nsg_CStyleBlocks.E("condition")));

            // Строковая интерполяция: "\\(значение)". Кавычки и обратный слэш
            // в шаблоне — часть синтаксиса Swift, а не обрамление слота.
            into.Add(Nsg_CStyleBlocks.Template("swift.interpolate", "expression",
                "interpolate {0}", "\"\\({{0}})\"", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("swift.guardLet", "statement",
                "guard let {0} = {1}", "guard let {{0}} = {{1}} else {\n\treturn\n}",
                Nsg_CStyleBlocks.S("name", "text"), Nsg_CStyleBlocks.E("value")));
        }

        // ------------------------------------------------------------------
        // Преобразования
        // ------------------------------------------------------------------

        static void AddConversions(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Call("swift.string", "expression", "String",
                "to string {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("swift.int", "expression", "Int",
                "to int {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("swift.double", "expression", "Double",
                "to double {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("swift.abs", "expression", "abs",
                "absolute {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("swift.min", "expression", "min",
                "smaller of {0} and {1}", Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("swift.max", "expression", "max",
                "larger of {0} and {1}", Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));
        }

        // ------------------------------------------------------------------
        // Коллекции
        // ------------------------------------------------------------------

        static void AddCollections(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Template("swift.count", "expression",
                "count of {0}", "{{0}}.count", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("swift.isEmpty", "expression",
                "{0} is empty", "{{0}}.isEmpty", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("swift.append", "statement",
                "append {1} to {0}", "{{0}}.append({{1}})",
                Nsg_CStyleBlocks.E("array"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("swift.removeAt", "expression",
                "remove item {1} from {0}", "{{0}}.remove(at: {{1}})",
                Nsg_CStyleBlocks.E("array"), Nsg_CStyleBlocks.E("index")));

            into.Add(Nsg_CStyleBlocks.Template("swift.arrayOf", "expression",
                "array of {0}", "[{{0}}]", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("swift.map", "expression",
                "map {0} with {1}", "{{0}}.map({{1}})",
                Nsg_CStyleBlocks.E("sequence"), Nsg_CStyleBlocks.E("transform")));

            into.Add(Nsg_CStyleBlocks.Template("swift.filter", "expression",
                "filter {0} by {1}", "{{0}}.filter({{1}})",
                Nsg_CStyleBlocks.E("sequence"), Nsg_CStyleBlocks.E("predicate")));

            into.Add(Nsg_CStyleBlocks.Template("swift.sorted", "expression",
                "sorted {0}", "{{0}}.sorted()", Nsg_CStyleBlocks.E("sequence")));
        }

        // ------------------------------------------------------------------
        // Строки
        // ------------------------------------------------------------------

        static void AddStrings(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Template("swift.uppercased", "expression",
                "uppercase {0}", "{{0}}.uppercased()", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("swift.lowercased", "expression",
                "lowercase {0}", "{{0}}.lowercased()", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("swift.hasPrefix", "expression",
                "{0} starts with {1}", "{{0}}.hasPrefix({{1}})",
                Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("prefix")));

            into.Add(Nsg_CStyleBlocks.Template("swift.split", "expression",
                "split {0} by {1}", "{{0}}.split(separator: {{1}})",
                Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("separator")));

            into.Add(Nsg_CStyleBlocks.Template("swift.joined", "expression",
                "join {0} with {1}", "{{0}}.joined(separator: {{1}})",
                Nsg_CStyleBlocks.E("parts"), Nsg_CStyleBlocks.E("separator")));
        }

        // ------------------------------------------------------------------
        // Профиль
        // ------------------------------------------------------------------

        static Nsg_LanguageProfile Build()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "swift",
                DisplayName = "Swift",
                IconName = "SWIFT",
                Extensions = new[] { ".swift" },
                // "#if", "#selector", "#available": строки целиком уходят в
                // тривиальный текст, поэтому '#' не превращается в ошибку лексера.
                Preprocessor = true,
                HasForeach = false,          // вместо foreach — "for x in y"
                HasNew = false,              // new в Swift нет: T(...)
                ParenlessConditions = true,  // "if x {", а не "if (x)"
                AutoSemicolon = true,        // ';' в конце строки не пишут
                SameLineBrace = true,        // "if x {" и "} else {"
                ArgumentLabels = true,       // "clamp(value: 1, low: 0)"
                ForIn = true                 // "for x in y {"
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                // объявления
                "class", "struct", "enum", "extension", "protocol", "actor",
                "func", "init", "deinit", "subscript", "typealias",
                "associatedtype", "import", "operator", "precedencegroup",
                // модификаторы
                "public", "private", "fileprivate", "internal", "open",
                "static", "final", "override", "mutating", "nonmutating",
                "convenience", "required", "lazy", "weak", "unowned",
                "indirect", "dynamic", "inout",
                // операторы
                "if", "else", "guard", "switch", "case", "default", "for",
                "while", "repeat", "do", "catch", "defer", "return", "break",
                "continue", "fallthrough", "throw", "throws", "rethrows",
                "try", "where", "in", "as", "is",
                // значения
                "let", "var", "nil", "true", "false", "self", "Self", "super",
                "Any", "some", "any", "async", "await");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "Int", "Int8", "Int16", "Int32", "Int64",
                "UInt", "UInt8", "UInt16", "UInt32", "UInt64",
                "Float", "Double", "Bool", "String", "Character", "Void",
                "AnyObject", "Optional", "Array", "Dictionary", "Set", "Result");

            // "let" и "var" сюда НЕ попадают: иначе объявление переменной
            // отвергалось бы как начало оператора и уходило в сырой текст.
            // Так же поступили с "let" у Rust и "var" у Go.
            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "else", "guard", "switch", "case", "default", "for",
                "while", "repeat", "do", "catch", "defer", "return", "break",
                "continue", "fallthrough", "in", "where", "import",
                "func", "class", "struct", "enum", "extension", "protocol",
                "actor", "init", "deinit", "subscript", "typealias");

            // Объявление типа: по этим словам разделитель заводит узел типа,
            // а тело разбирается как поля и методы. "func" здесь НЕТ —
            // функция обязана остаться методом.
            Nsg_LanguageProfile.Fill(p.TypeDeclKeywords,
                "class", "struct", "enum", "extension", "protocol", "actor");

            // Чего в Swift нет: приведение пишется "Int(x)", счётного for нет,
            // "++"/"--" нет. Тернарный оператор есть — его оставляем.
            Nsg_LanguageProfile.Fill(p.ExcludedBlocks,
                "expr.cast", "expr.postfix", "stmt.for");

            return p;
        }
    }
}
