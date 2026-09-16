namespace NekoScriptGraph
{
    /// <summary>
    /// Письменный набор агента для всего C-семейства.
    ///
    /// Одно описание на C, C++, Java, HLSL, Rust и Python: печататель у них
    /// общий (NsgPrinter + Nsg_CodeMap), различается только профиль. Поэтому
    /// специфика берётся из <see cref="Nsg_CStyleEngine.Profile"/>, а не
    /// дублируется в каждой папке языка.
    ///
    /// Лежит в Editor/Core/_cstyle — там же, где сама общая машина семейства.
    /// </summary>
    public partial class Nsg_CStyleEngine : INsg_AgentSpec
    {
        public string Id
        {
            get { return _profile != null ? _profile.Id : "cstyle"; }
        }

        public string[] CanonicalRules
        {
            get
            {
                var list = new System.Collections.Generic.List<string>();

                if (_profile != null)
                {
                    if (_profile.ParenlessConditions)
                    {
                        list.Add("Conditions are written WITHOUT parentheses: `if x`, `while x` — not `if (x)`.");
                    }
                    else
                    {
                        list.Add("Conditions are always parenthesised: `if (x)`, `while (x)`.");
                    }

                    if (_profile.ForIn)
                    {
                        list.Add("Iteration is written `for item in list`. There is no `foreach`.");
                    }

                    if (_profile.LetBinding)
                    {
                        list.Add("A local declaration starts with `let` (`let x = 1;`); mutability is spelled `let mut x = 1;`.");
                    }

                    if (_profile.IndentBlocks)
                    {
                        list.Add("Blocks are delimited by indentation, not by braces. One level is 4 spaces.");
                    }
                    else
                    {
                        list.Add("Braces are always present, including around a single statement.");
                    }

                    if (!_profile.HasForeach) list.Add("`foreach` is not available in this language.");
                    if (!_profile.HasNew) list.Add("`new` is not available in this language.");
                    if (!_profile.Preprocessor) list.Add("There are no preprocessor directives in this language.");
                }

                return list.ToArray();
            }
        }

        public string[] OutOfSubset
        {
            get
            {
                return new[]
                {
                    "Statements other than: local declaration, expression statement, `if`/`else`, `while`, `for`, `foreach`/`for..in`, `return`, `break`, `continue`, nested block.",
                    "Expressions other than: literal, identifier, member access, call, index, binary/unary/postfix operator, `new`, assignment, ternary, cast.",
                    "Constructs specific to this language (match, macros, slices, templates, attributes, preprocessor tricks) become raw snippets and are reported as NSG0002.",
                    "A raw snippet is preserved verbatim, so nothing is lost — but it stops being blocks, and that is what `escapeRatio` measures.",
                };
            }
        }
    }
}
