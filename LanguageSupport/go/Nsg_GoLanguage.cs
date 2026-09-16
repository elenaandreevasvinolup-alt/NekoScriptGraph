using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Язык Go. Пользуется общей машиной разбора C-семейства (скобки, граф
    /// блоков, печататель), но профиль включает три вещи, которых у семейства
    /// нет и без которых Go разбирался бы почти целиком как сырой текст:
    ///
    ///   • автоматическая ';' в конце строки — в Go её не пишут;
    ///   • короткое объявление ":=" — отдельная лексема и отдельный оператор;
    ///   • тип возврата ПОСЛЕ параметров — "func f() error {".
    ///
    /// Отличия, которые учтены профилем:
    ///   • условия без круглых скобок: "if x > 0 {", а не "if (x > 0)"
    ///   • нет foreach и нет оператора new (есть встроенные make/new)
    ///   • нет препроцессора
    ///
    /// Что пока остаётся сырым текстом: "for ... range", switch/select,
    /// defer и go (как заголовки), объявление с типом "var x int = 5",
    /// составные литералы и несколько возвращаемых значений. Сырой текст
    /// сохраняется дословно, поэтому круговое преобразование не теряет ничего
    /// — эти строки просто ещё не стали блоками.
    /// </summary>
    public class Nsg_GoLanguage : Nsg_CStyleLanguageBase
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
            AddDeclarations(into);
            AddStatements(into);
            AddBuiltins(into);
            AddStdlib(into);
        }

        // ------------------------------------------------------------------
        // Объявления и операторы, которых нет у C-семейства
        // ------------------------------------------------------------------

        /// <summary>
        /// Собственные формы объявления Go.
        ///
        /// Короткое объявление печатается шаблоном, но ОБРАТНО читается
        /// настоящим оператором присваивания: разборщик знает ":=" как
        /// оператор, поэтому код, собранный из блоков, потом разберётся в
        /// обычное присваивание, а не в сырой текст.
        /// </summary>
        static void AddDeclarations(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Template("go.shortDecl", "statement",
                "declare {0} as {1}", "{{0}} := {{1}}",
                Nsg_CStyleBlocks.S("name", "var"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("go.varDecl", "statement",
                "var {0} = {1}", "var {{0}} = {{1}}",
                Nsg_CStyleBlocks.S("name", "var"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("go.constDecl", "statement",
                "const {0} = {1}", "const {{0}} = {{1}}",
                Nsg_CStyleBlocks.S("name", "var"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("go.import", "statement",
                "import {0}", "import \"{{0}}\"",
                Nsg_CStyleBlocks.S("path", "text")));

            into.Add(Nsg_CStyleBlocks.Template("go.typeStruct", "statement",
                "type {0} struct", "type {{0}} struct {\n}",
                Nsg_CStyleBlocks.S("name", "var")));

            into.Add(Nsg_CStyleBlocks.Template("go.typeInterface", "statement",
                "type {0} interface", "type {{0}} interface {\n}",
                Nsg_CStyleBlocks.S("name", "var")));
        }

        static void AddStatements(List<NsgBlockDef> into)
        {
            // --- параллелизм: у Go это синтаксис, а не библиотека ---
            into.Add(Nsg_CStyleBlocks.Template("go.deferCall", "statement",
                "defer {0}()", "defer {{0}}()", Nsg_CStyleBlocks.E("call")));

            into.Add(Nsg_CStyleBlocks.Template("go.goCall", "statement",
                "go {0}()", "go {{0}}()", Nsg_CStyleBlocks.E("call")));

            into.Add(Nsg_CStyleBlocks.Template("go.chanOf", "expression",
                "channel of {0}", "make(chan {{0}})", Nsg_CStyleBlocks.S("type", "text")));

            // --- ошибки ---
            into.Add(Nsg_CStyleBlocks.Template("go.panic", "statement",
                "panic {0}", "panic({{0}})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("go.errCheck", "statement",
                "if err is not nil, return it", "if err != nil {\n\treturn err\n}"));

            into.Add(Nsg_CStyleBlocks.Template("go.errAs", "expression",
                "errors.As {0} into {1}", "errors.As({{0}}, &{{1}})",
                Nsg_CStyleBlocks.E("err"), Nsg_CStyleBlocks.E("target")));

            // --- вывод ---
            into.Add(Nsg_CStyleBlocks.Template("go.print", "statement",
                "print {0}", "fmt.Print({{0}})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("go.println", "statement",
                "println {0}", "fmt.Println({{0}})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("go.printf", "statement",
                "printf {0} {1}", "fmt.Printf(\"{{0}}\", {{1}})",
                Nsg_CStyleBlocks.S("format", "text"), Nsg_CStyleBlocks.E("args")));

            into.Add(Nsg_CStyleBlocks.Template("go.printErr", "statement",
                "print error {0}", "fmt.Fprintln(os.Stderr, {{0}})",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("go.logFatal", "statement",
                "log fatal {0}", "log.Fatal({{0}})", Nsg_CStyleBlocks.E("value")));

            // --- синхронизация: самые частые методы ---
            into.Add(Nsg_CStyleBlocks.Template("go.mutexLock", "statement",
                "lock {0}", "{{0}}.Lock()", Nsg_CStyleBlocks.E("mutex")));

            into.Add(Nsg_CStyleBlocks.Template("go.mutexUnlock", "statement",
                "unlock {0}", "{{0}}.Unlock()", Nsg_CStyleBlocks.E("mutex")));

            into.Add(Nsg_CStyleBlocks.Template("go.wgAdd", "statement",
                "wait group {0} add {1}", "{{0}}.Add({{1}})",
                Nsg_CStyleBlocks.E("group"), Nsg_CStyleBlocks.E("delta")));

            into.Add(Nsg_CStyleBlocks.Template("go.wgDone", "statement",
                "wait group {0} done", "{{0}}.Done()", Nsg_CStyleBlocks.E("group")));

            into.Add(Nsg_CStyleBlocks.Template("go.wgWait", "statement",
                "wait for group {0}", "{{0}}.Wait()", Nsg_CStyleBlocks.E("group")));

            into.Add(Nsg_CStyleBlocks.Template("go.ctxDone", "expression",
                "context {0} done", "<-{{0}}.Done()", Nsg_CStyleBlocks.E("context")));
        }

        /// <summary>
        /// Встроенные функции: это настоящие вызовы, поэтому разборщик узнаёт
        /// их обратно (в отличие от шаблонов выше).
        /// </summary>
        static void AddBuiltins(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Call("go.make", "expression", "make", "make {0}",
                Nsg_CStyleBlocks.E("type")));

            into.Add(Nsg_CStyleBlocks.Call("go.new", "expression", "new", "new {0}",
                Nsg_CStyleBlocks.E("type")));

            into.Add(Nsg_CStyleBlocks.Call("go.len", "expression", "len", "length of {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.cap", "expression", "cap", "capacity of {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.append", "expression", "append", "append {1} to {0}",
                Nsg_CStyleBlocks.E("slice"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.copy", "expression", "copy", "copy {1} into {0}",
                Nsg_CStyleBlocks.E("dst"), Nsg_CStyleBlocks.E("src")));

            into.Add(Nsg_CStyleBlocks.Call("go.delete", "expression", "delete", "delete {1} from {0}",
                Nsg_CStyleBlocks.E("map"), Nsg_CStyleBlocks.E("key")));

            into.Add(Nsg_CStyleBlocks.Call("go.close", "expression", "close", "close {0}",
                Nsg_CStyleBlocks.E("channel")));

            into.Add(Nsg_CStyleBlocks.Call("go.recover", "expression", "recover", "recover"));

            into.Add(Nsg_CStyleBlocks.Call("go.string", "expression", "string", "to string {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.int", "expression", "int", "to int {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.float64", "expression", "float64", "to float64 {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.rune", "expression", "rune", "to rune {0}",
                Nsg_CStyleBlocks.E("value")));
        }

        /// <summary>
        /// Стандартная библиотека Go. Имена с точкой печатаются как есть и
        /// узнаются обратно: так же устроен String::from у Rust.
        /// </summary>
        static void AddStdlib(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Call("go.sprintf", "expression", "fmt.Sprintf",
                "format {0} with {1}",
                Nsg_CStyleBlocks.E("format"), Nsg_CStyleBlocks.E("args")));

            into.Add(Nsg_CStyleBlocks.Call("go.errorf", "expression", "fmt.Errorf",
                "error {0} {1}",
                Nsg_CStyleBlocks.E("format"), Nsg_CStyleBlocks.E("args")));

            into.Add(Nsg_CStyleBlocks.Call("go.errorsNew", "expression", "errors.New",
                "new error {0}", Nsg_CStyleBlocks.E("message")));

            into.Add(Nsg_CStyleBlocks.Call("go.errorsIs", "expression", "errors.Is",
                "{0} is {1}", Nsg_CStyleBlocks.E("err"), Nsg_CStyleBlocks.E("target")));

            into.Add(Nsg_CStyleBlocks.Call("go.atoi", "expression", "strconv.Atoi",
                "parse int {0}", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Call("go.itoa", "expression", "strconv.Itoa",
                "format int {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.stringsContains", "expression", "strings.Contains",
                "{0} contains {1}", Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("part")));

            into.Add(Nsg_CStyleBlocks.Call("go.stringsHasPrefix", "expression", "strings.HasPrefix",
                "{0} starts with {1}", Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("prefix")));

            into.Add(Nsg_CStyleBlocks.Call("go.stringsSplit", "expression", "strings.Split",
                "split {0} by {1}", Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("sep")));

            into.Add(Nsg_CStyleBlocks.Call("go.stringsJoin", "expression", "strings.Join",
                "join {0} with {1}", Nsg_CStyleBlocks.E("parts"), Nsg_CStyleBlocks.E("sep")));

            into.Add(Nsg_CStyleBlocks.Call("go.jsonMarshal", "expression", "json.Marshal",
                "to JSON {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("go.jsonUnmarshal", "expression", "json.Unmarshal",
                "from JSON {0} into {1}",
                Nsg_CStyleBlocks.E("data"), Nsg_CStyleBlocks.E("target")));

            into.Add(Nsg_CStyleBlocks.Call("go.timeNow", "expression", "time.Now", "now"));
        }

        // ------------------------------------------------------------------
        // Профиль
        // ------------------------------------------------------------------

        static Nsg_LanguageProfile Build()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "go",
                DisplayName = "Go",
                IconName = "GO",
                Extensions = new[] { ".go" },
                Preprocessor = false,        // препроцессора в Go нет
                HasForeach = false,          // вместо foreach — "for ... range"
                HasNew = false,              // new(T) — встроенная функция, не оператор
                ParenlessConditions = true,  // "if x > 0 {", а не "if (x > 0)"
                AutoSemicolon = true,        // ';' в конце строки не пишут
                ShortDecl = true,            // ":="
                TrailingReturnType = true    // "func f() error {"
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                "break", "case", "chan", "const", "continue", "default", "defer",
                "else", "fallthrough", "for", "func", "go", "goto", "if", "import",
                "interface", "map", "package", "range", "return", "select", "struct",
                "switch", "type", "var",
                "true", "false", "nil", "iota");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "bool", "byte", "complex64", "complex128", "error",
                "float32", "float64", "int", "int8", "int16", "int32", "int64",
                "rune", "string", "uint", "uint8", "uint16", "uint32", "uint64",
                "uintptr", "any");

            // "var" сюда НЕ попадает: иначе объявление переменной отвергалось бы
            // как начало оператора и уходило в сырой текст. Так же поступили с
            // "let" у Rust.
            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "else", "for", "switch", "select", "return", "break",
                "continue", "goto", "defer", "go", "fallthrough");

            // Объявление типа: по "type" разделитель заводит узел типа, а его
            // тело разбирается как поля. "func" здесь НЕТ — функция обязана
            // остаться методом, иначе её тело стало бы телом типа.
            Nsg_LanguageProfile.Fill(p.TypeDeclKeywords, "type");

            return p;
        }
    }
}
