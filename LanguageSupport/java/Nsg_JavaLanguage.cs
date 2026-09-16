using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>Язык Java. Устанавливаемый: лежит в LanguageSupport.</summary>
    public class Nsg_JavaLanguage : Nsg_CStyleLanguageBase
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
            into.Add(Nsg_CStyleBlocks.Call("java.println", "statement", "System.out.println", "print {0}", Nsg_CStyleBlocks.E("value")));

            AddStdlib(into);
        }

        /// <summary>
        /// Идиомы Java. Статические вызовы (Math.*, Integer.*, String.*)
        /// обратимы; вызовы у экземпляра — нет: разборщик видит общий вызов,
        /// а не этот блок, поэтому они остаются шаблонами.
        /// </summary>
        static void AddStdlib(List<NsgBlockDef> into)
        {
            // --- статические вызовы: обратимы ---
            into.Add(Nsg_CStyleBlocks.Call("java.print", "statement", "System.out.print", "print without newline {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("java.format", "expression", "String.format", "format {0} with {1}",
                Nsg_CStyleBlocks.E("format"), Nsg_CStyleBlocks.E("args")));

            into.Add(Nsg_CStyleBlocks.Call("java.valueOf", "expression", "String.valueOf", "to string {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("java.parseInt", "expression", "Integer.parseInt", "string to int {0}",
                Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Call("java.parseDouble", "expression", "Double.parseDouble", "string to double {0}",
                Nsg_CStyleBlocks.E("text")));

            into.Add(Nsg_CStyleBlocks.Call("java.mathAbs", "expression", "Math.abs", "absolute {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("java.mathMax", "expression", "Math.max", "max {0} {1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("java.mathMin", "expression", "Math.min", "min {0} {1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("java.mathSqrt", "expression", "Math.sqrt", "square root {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("java.mathPow", "expression", "Math.pow", "power {0}^{1}",
                Nsg_CStyleBlocks.E("base"), Nsg_CStyleBlocks.E("exp")));

            into.Add(Nsg_CStyleBlocks.Call("java.mathRandom", "expression", "Math.random", "random number"));

            into.Add(Nsg_CStyleBlocks.Call("java.arraysSort", "statement", "Arrays.sort", "sort {0}", Nsg_CStyleBlocks.E("array")));

            // --- идиомы у экземпляра ---
            into.Add(Nsg_CStyleBlocks.Template("java.length", "expression", "length of {0}",
                "{{0}}.length()", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("java.equals", "expression", "{0} equals {1}",
                "{{0}}.equals({{1}})",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Template("java.toString", "expression", "string of {0}",
                "{{0}}.toString()", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("java.substring", "expression", "substring {1}..{2} of {0}",
                "{{0}}.substring({{1}}, {{2}})",
                Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("from"), Nsg_CStyleBlocks.E("to")));

            into.Add(Nsg_CStyleBlocks.Template("java.split", "expression", "split {0} by {1}",
                "{{0}}.split({{1}})",
                Nsg_CStyleBlocks.E("text"), Nsg_CStyleBlocks.E("regex")));

            into.Add(Nsg_CStyleBlocks.Template("java.list", "statement", "list {0}",
                "java.util.ArrayList<{{0}}> {{1}} = new java.util.ArrayList<>();",
                Nsg_CStyleBlocks.S("type", "text"), Nsg_CStyleBlocks.S("name", "var")));

            into.Add(Nsg_CStyleBlocks.Template("java.add", "statement", "add {1} to {0}",
                "{{0}}.add({{1}});",
                Nsg_CStyleBlocks.E("list"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("java.get", "expression", "item {1} of {0}",
                "{{0}}.get({{1}})",
                Nsg_CStyleBlocks.E("list"), Nsg_CStyleBlocks.E("index")));

            into.Add(Nsg_CStyleBlocks.Template("java.put", "statement", "put {1} → {2} into {0}",
                "{{0}}.put({{1}}, {{2}});",
                Nsg_CStyleBlocks.E("map"), Nsg_CStyleBlocks.E("key"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("java.map", "statement", "map {0}",
                "java.util.HashMap<{{0}}, {{1}}> {{2}} = new java.util.HashMap<>();",
                Nsg_CStyleBlocks.S("key", "text"), Nsg_CStyleBlocks.S("value", "text"),
                Nsg_CStyleBlocks.S("name", "var")));

            into.Add(Nsg_CStyleBlocks.Template("java.throw", "statement", "throw {0}",
                "throw {{0}};", Nsg_CStyleBlocks.E("error")));
        }

        static Nsg_LanguageProfile Build()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "java",
                DisplayName = "Java",
                IconName = "JAVA",
                Extensions = new[] { ".java" },
                Preprocessor = false, // в Java препроцессора нет
                HasForeach = true,
                HasNew = true
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                "abstract", "assert", "boolean", "break", "byte", "case", "catch",
                "char", "class", "const", "continue", "default", "do", "double",
                "else", "enum", "extends", "final", "finally", "float", "for", "goto",
                "if", "implements", "import", "instanceof", "int", "interface", "long",
                "native", "new", "package", "private", "protected", "public", "return",
                "short", "static", "strictfp", "super", "switch", "synchronized",
                "this", "throw", "throws", "transient", "try", "void", "volatile",
                "while", "true", "false", "null");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "boolean", "byte", "char", "short", "int", "long", "float", "double",
                "String", "Object", "void");

            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "else", "for", "while", "do", "switch", "case", "default",
                "break", "continue", "return", "new", "throw", "throws", "try",
                "catch", "finally", "synchronized", "package", "import",
                "class", "interface", "enum", "extends", "implements");

            Nsg_LanguageProfile.Fill(p.TypeDeclKeywords, "class", "interface", "enum");

            return p;
        }
    }
}
