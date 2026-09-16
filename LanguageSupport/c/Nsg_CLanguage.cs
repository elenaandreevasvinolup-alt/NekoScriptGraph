using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Язык C. Устанавливаемый: лежит в LanguageSupport, ядро на него НЕ
    /// ссылается, удаление папки полностью убирает язык из плагина.
    ///
    /// Грамматику приносит общий движок, а словарь — этот профиль.
    /// </summary>
    public class Nsg_CLanguage : Nsg_CStyleLanguageBase
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
            // printf вариативна, поэтому формат и аргументы пишутся одним полем:
            // разбирать строку формата на отдельные слоты смысла нет.
            into.Add(Nsg_CStyleBlocks.Template("c.printf", "statement", "printf({0})",
                "printf({{0}});", Nsg_CStyleBlocks.S("args", "text")));

            // sizeof принимает и выражение, и тип; тип разборщик сохраняет
            // сырым фрагментом внутри вызова, поэтому блок обратим.
            into.Add(Nsg_CStyleBlocks.Call("c.sizeof", "expression", "sizeof", "sizeof({0})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("c.malloc", "expression", "malloc", "allocate {0} bytes",
                Nsg_CStyleBlocks.E("size")));

            into.Add(Nsg_CStyleBlocks.Call("c.free", "statement", "free", "free {0}", Nsg_CStyleBlocks.E("value")));

            AddStdlib(into);
        }

        /// <summary>
        /// Стандартная библиотека C — собственные идиомы языка. Всё, что
        /// является настоящим вызовом, обратимо; форматные функции остаются
        /// шаблонами, потому что их аргументы вариативны.
        /// </summary>
        static void AddStdlib(List<NsgBlockDef> into)
        {
            // --- память и строки ---
            into.Add(Nsg_CStyleBlocks.Call("c.memcpy", "statement", "memcpy", "copy {2} bytes {1}→{0}",
                Nsg_CStyleBlocks.E("dst"), Nsg_CStyleBlocks.E("src"), Nsg_CStyleBlocks.E("size")));

            into.Add(Nsg_CStyleBlocks.Call("c.memset", "statement", "memset", "fill {0} with {1} for {2} bytes",
                Nsg_CStyleBlocks.E("dst"), Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("size")));

            into.Add(Nsg_CStyleBlocks.Call("c.strlen", "expression", "strlen", "length of {0}",
                Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Call("c.strcmp", "expression", "strcmp", "compare strings {0} {1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("c.strcpy", "expression", "strcpy", "copy string {1}→{0}",
                Nsg_CStyleBlocks.E("dst"), Nsg_CStyleBlocks.E("src")));

            into.Add(Nsg_CStyleBlocks.Call("c.strcat", "expression", "strcat", "concat strings {0}+{1}",
                Nsg_CStyleBlocks.E("dst"), Nsg_CStyleBlocks.E("src")));

            // --- числа ---
            into.Add(Nsg_CStyleBlocks.Call("c.atoi", "expression", "atoi", "string to int {0}",
                Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Call("c.atof", "expression", "atof", "string to double {0}",
                Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Call("c.abs", "expression", "abs", "absolute {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("c.fabs", "expression", "fabs", "absolute double {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("c.sqrt", "expression", "sqrt", "square root {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("c.pow", "expression", "pow", "power {0}^{1}",
                Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("power")));

            // --- файлы и выход ---
            into.Add(Nsg_CStyleBlocks.Call("c.fopen", "expression", "fopen", "open file {0} as {1}",
                Nsg_CStyleBlocks.E("path"), Nsg_CStyleBlocks.E("mode")));

            into.Add(Nsg_CStyleBlocks.Call("c.fclose", "statement", "fclose", "close file {0}",
                Nsg_CStyleBlocks.E("stream")));

            into.Add(Nsg_CStyleBlocks.Call("c.fgets", "expression", "fgets", "read line from {0} into {1} up to {2} bytes",
                Nsg_CStyleBlocks.E("buffer"), Nsg_CStyleBlocks.E("size"), Nsg_CStyleBlocks.E("stream")));

            into.Add(Nsg_CStyleBlocks.Template("c.fprintf", "statement", "write {1} to {0}",
                "fprintf({{0}}, {{1}});",
                Nsg_CStyleBlocks.E("stream"), Nsg_CStyleBlocks.S("args", "text")));

            into.Add(Nsg_CStyleBlocks.Call("c.exit", "statement", "exit", "exit with {0}", Nsg_CStyleBlocks.E("code")));

            into.Add(Nsg_CStyleBlocks.Call("c.assert", "statement", "assert", "assert {0}", Nsg_CStyleBlocks.E("condition")));

            into.Add(Nsg_CStyleBlocks.Call("c.perror", "statement", "perror", "report error {0}",
                Nsg_CStyleBlocks.E("message")));
        }

        static Nsg_LanguageProfile Build()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "c",
                DisplayName = "C",
                IconName = "C",
                Extensions = new[] { ".c", ".h" },
                Preprocessor = true,
                HasForeach = false,
                HasNew = false
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                "auto", "break", "case", "char", "const", "continue", "default", "do",
                "double", "else", "enum", "extern", "float", "for", "goto", "if",
                "inline", "int", "long", "register", "restrict", "return", "short",
                "signed", "sizeof", "static", "struct", "switch", "typedef", "union",
                "unsigned", "void", "volatile", "while", "_Bool", "bool");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "void", "char", "short", "int", "long", "float", "double",
                "signed", "unsigned", "_Bool", "bool", "size_t", "FILE");

            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "else", "for", "while", "do", "switch", "case", "default",
                "break", "continue", "return", "goto", "sizeof", "typedef");

            Nsg_LanguageProfile.Fill(p.TypeDeclKeywords, "struct", "union", "enum");

            return p;
        }
    }
}
