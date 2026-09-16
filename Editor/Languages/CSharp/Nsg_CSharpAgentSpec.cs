namespace NekoScriptGraph
{
    /// <summary>
    /// Письменный набор агента для встроенного C#.
    ///
    /// Лежит рядом с движком C# (Editor/Languages/CSharp), а не в ядре:
    /// описание неразрывно связано с печатателем и разборщиком именно этого
    /// языка. Общие правила формы (Allman, отступ 4, обязательные скобки)
    /// добавляет генератор — здесь только то, что специфично для C#.
    /// </summary>
    public partial class Nsg_CSharpEngine : INsg_AgentSpec
    {
        public string Id
        {
            get { return "csharp"; }
        }

        public string[] CanonicalRules
        {
            get
            {
                return new[]
                {
                    "`var` and an explicit type are preserved exactly as written; never swap one for the other.",
                    "`foreach (var item in list)` keeps `var`. The iteration type text is preserved verbatim.",
                    "`??` chains are printed without added parentheses: `a ?? b ?? c` stays as written.",
                    "Casts are printed as written: `(int)value` keeps its parentheses.",
                    "`new Foo { A = 1 }` keeps its initializer list and its order.",
                };
            }
        }

        public string[] OutOfSubset
        {
            get
            {
                return new[]
                {
                    "Statements other than: local declaration, expression statement, `if`/`else`, `while`, `for`, `foreach`, `return`, `break`, `continue`, nested block. So `switch`, `try`/`catch`, `throw`, `do`, `lock`, `using (...)`, `yield`, `goto` all become raw snippets.",
                    "Expressions other than: literal, identifier, member access, call, index, binary/unary/postfix operator, `new`, assignment, ternary, cast. So lambdas, `await`, `?.`, query syntax and `switch` expressions become raw snippets.",
                    "Type and member declarations inside a method body (local functions, nested types) are outside the subset.",
                    "`?.` and `?[]` are the classic trap: `Foo?.Bar()` is kept verbatim but reported as NSG0002.",
                };
            }
        }
    }
}
