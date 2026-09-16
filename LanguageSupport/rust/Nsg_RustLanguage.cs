using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Язык Rust. Устанавливаемый и НЕ входящий в C-семейство: он пользуется
    /// общей машиной разбора (скобки, выражения), но с другим словарём и
    /// другими формами операторов.
    ///
    /// Отличия от C-семейства, которые учтены:
    ///   • условия без круглых скобок: "if x {", а не "if (x)"
    ///   • объявление переменной через let: "let x = 5;" и "let mut x = 5;"
    ///   • цикл по коллекции: "for x in y {", а не "foreach (T x in y)"
    ///   • тип возврата через "->": "fn f() -> i32 {"
    ///
    /// Что пока остаётся сырым текстом: match, макросы (println!), срезы
    /// диапазонов (0..10), аннотация типа в let (let x: i32).
    /// </summary>
    public class Nsg_RustLanguage : Nsg_CStyleLanguageBase
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
            into.Add(Nsg_CStyleBlocks.Template("rust.println", "statement", "println!({0})",
                "println!(\"{{0}}\");", Nsg_CStyleBlocks.S("args", "text")));

            into.Add(Nsg_CStyleBlocks.Template("rust.panic", "statement", "panic!({0})",
                "panic!(\"{{0}}\");", Nsg_CStyleBlocks.S("message", "text")));

            into.Add(Nsg_CStyleBlocks.Call("rust.vec", "expression", "vec!", "vector {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("rust.some", "expression", "Some", "Some({0})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("rust.ok", "expression", "Ok", "Ok({0})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("rust.err", "expression", "Err", "Err({0})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("rust.unwrap", "expression", "unwrap", "unwrap {0}", Nsg_CStyleBlocks.E("value")));

            AddStdlib(into);
        }

        /// <summary>
        /// Идиомы Rust: Option/Result, итераторы, строки. Методы у значения и
        /// макросы не сводятся к статическому вызову, поэтому они шаблоны:
        /// печатаются верно, но обратно читаются как обычный вызов.
        /// </summary>
        static void AddStdlib(List<NsgBlockDef> into)
        {
            // --- Option / Result ---
            into.Add(Nsg_CStyleBlocks.Template("rust.none", "expression", "None", "None"));

            into.Add(Nsg_CStyleBlocks.Template("rust.expect", "expression", "unwrap {0} or fail with {1}",
                "{{0}}.expect({{1}})",
                Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("message")));

            into.Add(Nsg_CStyleBlocks.Template("rust.unwrap_or", "expression", "unwrap {0} or use {1}",
                "{{0}}.unwrap_or({{1}})",
                Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("fallback")));

            into.Add(Nsg_CStyleBlocks.Template("rust.is_ok", "expression", "{0} is Ok",
                "{{0}}.is_ok()", Nsg_CStyleBlocks.E("value")));

            // --- коллекции и итераторы ---
            into.Add(Nsg_CStyleBlocks.Template("rust.len", "expression", "length of {0}",
                "{{0}}.len()", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("rust.push", "statement", "push {1} into {0}",
                "{{0}}.push({{1}});",
                Nsg_CStyleBlocks.E("vec"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("rust.pop", "expression", "pop from {0}",
                "{{0}}.pop()", Nsg_CStyleBlocks.E("vec")));

            into.Add(Nsg_CStyleBlocks.Template("rust.iter", "expression", "iterate {0}",
                "{{0}}.iter()", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("rust.map", "expression", "map {0} with {1}",
                "{{0}}.map({{1}})",
                Nsg_CStyleBlocks.E("iter"), Nsg_CStyleBlocks.E("fn")));

            into.Add(Nsg_CStyleBlocks.Template("rust.filter", "expression", "filter {0} by {1}",
                "{{0}}.filter({{1}})",
                Nsg_CStyleBlocks.E("iter"), Nsg_CStyleBlocks.E("fn")));

            into.Add(Nsg_CStyleBlocks.Template("rust.collect", "expression", "collect {0}",
                "{{0}}.collect()", Nsg_CStyleBlocks.E("iter")));

            into.Add(Nsg_CStyleBlocks.Template("rust.hashmap", "statement", "map {0}",
                "let mut {{0}} = std::collections::HashMap::new();",
                Nsg_CStyleBlocks.S("name", "var")));

            // --- строки и макросы ---
            into.Add(Nsg_CStyleBlocks.Template("rust.to_string", "expression", "to string {0}",
                "{{0}}.to_string()", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("rust.parse", "expression", "parse {0}",
                "{{0}}.parse()", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("rust.string_from", "expression", "string {0}",
                "String::from({{0}})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("rust.format", "expression", "format {0}",
                "format!(\"{{0}}\", {{1}})",
                Nsg_CStyleBlocks.S("template", "text"), Nsg_CStyleBlocks.E("args")));

            into.Add(Nsg_CStyleBlocks.Template("rust.eprintln", "statement", "print error {0}",
                "eprintln!(\"{{0}}\");", Nsg_CStyleBlocks.S("args", "text")));

            into.Add(Nsg_CStyleBlocks.Template("rust.assert", "statement", "assert {0}",
                "assert!({{0}});", Nsg_CStyleBlocks.E("condition")));

            into.Add(Nsg_CStyleBlocks.Template("rust.todo", "statement", "todo placeholder",
                "todo!();"));

            into.Add(Nsg_CStyleBlocks.Template("rust.unreachable", "statement", "unreachable",
                "unreachable!();"));
        }

        static Nsg_LanguageProfile Build()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "rust",
                DisplayName = "Rust",
                IconName = "RUST",
                Extensions = new[] { ".rs" },
                Preprocessor = false,   // препроцессора в Rust нет
                HasForeach = false,     // вместо foreach — "for x in y"
                HasNew = false,
                ParenlessConditions = true,
                LetBinding = true,
                ForIn = true
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                "as", "async", "await", "break", "const", "continue", "crate", "dyn",
                "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in",
                "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return",
                "self", "Self", "static", "struct", "super", "trait", "true", "type",
                "unsafe", "use", "where", "while");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "i8", "i16", "i32", "i64", "i128", "isize",
                "u8", "u16", "u32", "u64", "u128", "usize",
                "f32", "f64", "bool", "char", "str", "String",
                "Vec", "Option", "Result", "Box", "Rc", "Arc", "HashMap", "HashSet");

            // let сюда НЕ попадает: иначе объявление переменной отвергалось бы
            // как начало оператора и уходило в сырой текст.
            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "else", "for", "while", "loop", "match", "return", "break",
                "continue", "use", "impl", "fn", "struct", "enum", "trait", "mod", "pub");

            Nsg_LanguageProfile.Fill(p.TypeDeclKeywords, "struct", "enum", "trait", "impl", "mod");

            return p;
        }
    }
}
