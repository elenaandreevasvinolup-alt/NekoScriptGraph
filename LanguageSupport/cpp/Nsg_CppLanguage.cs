using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>Язык C++. Устанавливаемый: лежит в LanguageSupport.</summary>
    public class Nsg_CppLanguage : Nsg_CStyleLanguageBase
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

        public override bool BuiltIn
        {
            get { return false; }
        }

        protected override void AddLanguageBlocks(List<NsgBlockDef> into)
        {
            // Вывод через << — это не вызов, поэтому остаётся шаблоном.
            into.Add(Nsg_CStyleBlocks.Template("cpp.cout", "statement", "print {0}",
                "std::cout << {{0}} << std::endl;", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("cpp.delete", "statement", "delete", "delete {0}", Nsg_CStyleBlocks.E("value")));

            AddStdlib(into);
        }

        /// <summary>
        /// Идиомы C++. Почти всё живёт в std::, а квалифицированное имя
        /// (std::vector) разборщик не сводит к статическому вызову, поэтому
        /// такие блоки остаются шаблонами: печатаются верно, но обратно
        /// читаются как обычный вызов. Обратимы только неквалифицированные.
        /// </summary>
        static void AddStdlib(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Call("cpp.sizeof", "expression", "sizeof", "sizeof({0})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.printf", "statement", "printf({0})",
                "printf({{0}});", Nsg_CStyleBlocks.S("args", "text")));

            // --- потоки ---
            into.Add(Nsg_CStyleBlocks.Template("cpp.cin", "statement", "read into {0}",
                "std::cin >> {{0}};", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.getline", "statement", "read line into {0}",
                "std::getline(std::cin, {{0}});", Nsg_CStyleBlocks.E("value")));

            // --- строки ---
            into.Add(Nsg_CStyleBlocks.Template("cpp.string", "statement", "string {0} = {1}",
                "std::string {{0}} = {{1}};",
                Nsg_CStyleBlocks.S("name", "var"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.stoi", "expression", "string to int {0}",
                "std::stoi({{0}})", Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.to_string", "expression", "to string {0}",
                "std::to_string({{0}})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.size", "expression", "size of {0}",
                "{{0}}.size()", Nsg_CStyleBlocks.E("container")));

            // --- контейнеры ---
            into.Add(Nsg_CStyleBlocks.Template("cpp.vector", "statement", "vector {0} of {1}",
                "std::vector<{{1}}> {{0}};",
                Nsg_CStyleBlocks.S("name", "var"), Nsg_CStyleBlocks.S("type", "text")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.push_back", "statement", "append {1} to {0}",
                "{{0}}.push_back({{1}});",
                Nsg_CStyleBlocks.E("container"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.at", "expression", "item {1} of {0}",
                "{{0}}.at({{1}})",
                Nsg_CStyleBlocks.E("container"), Nsg_CStyleBlocks.E("index")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.map", "statement", "map {0} of {1} to {2}",
                "std::map<{{1}}, {{2}}> {{0}};",
                Nsg_CStyleBlocks.S("name", "var"),
                Nsg_CStyleBlocks.S("key", "text"), Nsg_CStyleBlocks.S("value", "text")));

            // --- указатели и исключения ---
            into.Add(Nsg_CStyleBlocks.Template("cpp.unique_ptr", "statement", "unique pointer {0} = new {1}",
                "std::unique_ptr<{{1}}> {{0}}(new {{1}}());",
                Nsg_CStyleBlocks.S("name", "var"), Nsg_CStyleBlocks.S("type", "text")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.make_shared", "expression", "shared {0} = new",
                "std::make_shared<{{0}}>()", Nsg_CStyleBlocks.S("type", "text")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.throw", "statement", "throw {0}",
                "throw {{0}};", Nsg_CStyleBlocks.E("error")));

            into.Add(Nsg_CStyleBlocks.Template("cpp.move", "expression", "move {0}",
                "std::move({{0}})", Nsg_CStyleBlocks.E("value")));
        }

        static Nsg_LanguageProfile Build()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "cpp",
                DisplayName = "C++",
                IconName = "C++",
                Extensions = new[] { ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".h" },
                Preprocessor = true,
                HasForeach = true,
                HasNew = true
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                "auto", "break", "case", "char", "const", "constexpr", "continue",
                "default", "delete", "do", "double", "else", "enum", "explicit",
                "extern", "false", "float", "for", "friend", "goto", "if", "inline",
                "int", "long", "mutable", "namespace", "new", "nullptr", "operator",
                "override", "private", "protected", "public", "register", "return",
                "short", "signed", "sizeof", "static", "struct", "switch", "template",
                "this", "throw", "true", "try", "catch", "typedef", "typename",
                "union", "unsigned", "using", "virtual", "void", "volatile", "while", "final");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "void", "char", "short", "int", "long", "float", "double",
                "signed", "unsigned", "bool", "size_t", "string", "wstring", "vector");

            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "else", "for", "while", "do", "switch", "case", "default",
                "break", "continue", "return", "goto", "sizeof", "typedef",
                "class", "namespace", "using", "new", "delete", "try", "catch", "throw");

            Nsg_LanguageProfile.Fill(p.TypeDeclKeywords, "class", "struct", "union", "enum", "namespace");

            return p;
        }
    }
}
