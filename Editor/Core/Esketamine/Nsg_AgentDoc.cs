using System.Collections.Generic;
using System.Text;

namespace NekoScriptGraph
{
    /// <summary>
    /// Генератор письменного набора для агента.
    ///
    /// Набор не пишется руками, а выводится из двух источников истины:
    ///   • правила канонической формы — из печатателя (общие + от движка);
    ///   • словарь блоков — из библиотеки языка (Blocks/*.json).
    ///
    /// Поэтому добавление блока автоматически обновляет набор агента, а
    /// расхождение между документацией и плагином невозможно по построению.
    /// Язык набора — английский: это машинно-ориентированный артефакт, а
    /// английский даёт самый предсказуемый перевод кода у моделей.
    /// </summary>
    public static class Nsg_AgentDoc
    {
        public static string Generate(Nsg_LanguageEntry entry, INsg_LanguageEngine engine, Nsg_BlockLibrary lib)
        {
            var spec = engine as INsg_AgentSpec;
            string langId = entry != null ? entry.Id : (spec != null ? spec.Id : "?");

            var sb = new StringBuilder();

            sb.Append("# NekoScriptGraph — writing set for agents (").Append(langId).Append(")\n\n");
            sb.Append("Generated from the block library and the canonical printer. ");
            sb.Append("Do not edit by hand: regenerate it instead.\n\n");

            Section(sb, "1. What you are writing");
            sb.Append("You write ordinary source code. NekoScriptGraph re-parses it and rebuilds the\n");
            sb.Append("block file next to it (`Foo.nsg.json`). The direction is one-way:\n\n");
            sb.Append("```\n");
            sb.Append("code  ──parse──▶  blocks  ──print──▶  code\n");
            sb.Append("  ▲                                      │\n");
            sb.Append("  └──────────── you only write here ─────┘\n");
            sb.Append("```\n\n");
            sb.Append("Only **method bodies** become blocks. Everything else — `using`, `namespace`,\n");
            sb.Append("the type declaration, fields, attributes, comments outside method bodies — is\n");
            sb.Append("kept verbatim and survives both directions. Edit it freely.\n\n");
            sb.Append("Never ask the plugin for \"blocks → code\" on a file you own: it re-prints from\n");
            sb.Append("the block model and would rewrite your formatting.\n\n");

            Section(sb, "2. The one rule that matters");
            sb.Append("```\n");
            sb.Append("P(R(t)) == t\n");
            sb.Append("```\n\n");
            sb.Append("`R` parses into blocks, `P` prints them back. If your text `t` is already a\n");
            sb.Append("fixed point, the document stays `In sync` and nothing is reformatted. If it is\n");
            sb.Append("not, the next \"Blocks → Code\" rewrites it: the meaning survives, your diff does\n");
            sb.Append("not.\n\n");
            sb.Append("Do not try to guess the canonical form. Ask for it and iterate:\n\n");
            sb.Append("```\n");
            sb.Append("op=canon   →  the canonical text\n");
            sb.Append("repeat until canon(t) == t\n");
            sb.Append("```\n\n");

            Section(sb, "3. Canonical form");
            sb.Append("These rules are a projection of the printer, which is the only source of truth.\n\n");
            sb.Append("- Exactly one surface form per construct. No stylistic variation.\n");
            sb.Append("- Allman braces: the opening `{` goes on its own line.\n");
            sb.Append("- Indentation is 4 spaces per level. Tabs are never emitted.\n");
            sb.Append("- Control bodies are always braced. `if (x) return;` is not canonical.\n");
            sb.Append("- Minimum parentheses: only where precedence requires them. Redundant\n");
            sb.Append("  parentheses are dropped, so do not write them.\n");
            sb.Append("- `else { if (...) }` is printed as `else if (...)`, a single chain.\n");
            sb.Append("- Comments are emitted on their own lines immediately before the statement\n");
            sb.Append("  they belong to. Blank lines before a statement are preserved (at most two).\n");
            sb.Append("- Literals are kept verbatim: the number or string is never rewritten.\n");

            if (spec != null && spec.CanonicalRules != null && spec.CanonicalRules.Length > 0)
            {
                sb.Append('\n');
                sb.Append("Language-specific:\n\n");
                for (int i = 0; i < spec.CanonicalRules.Length; i++)
                {
                    sb.Append("- ").Append(spec.CanonicalRules[i]).Append('\n');
                }
            }
            sb.Append('\n');

            Section(sb, "4. Block vocabulary");
            sb.Append("`reversible` means the parser recognises the construct on the way back. ");
            sb.Append("One-way blocks print correctly but are read back as something else, so a ");
            sb.Append("round trip through them is not a fixed point.\n\n");
            AppendBlockTable(sb, lib);

            Section(sb, "5. Blocks you must not use");
            AppendOneWay(sb, lib);

            Section(sb, "6. Constructs that become a raw snippet (NSG0002)");
            sb.Append("Anything outside the subset is preserved verbatim and reported, but it stops\n");
            sb.Append("being blocks. A raw snippet is a hard limit on how completely the file is\n");
            sb.Append("represented, so treat it as a failure, not as an escape.\n");
            if (spec != null && spec.OutOfSubset != null && spec.OutOfSubset.Length > 0)
            {
                sb.Append('\n');
                for (int i = 0; i < spec.OutOfSubset.Length; i++)
                {
                    sb.Append("- ").Append(spec.OutOfSubset[i]).Append('\n');
                }
            }
            sb.Append('\n');

            Section(sb, "7. Acceptance");
            sb.Append("```\n");
            sb.Append("op=plan    diagnostics, block counts, escapeRatio, canonical?\n");
            sb.Append("op=canon   canonical text (the fixed-point oracle)\n");
            sb.Append("op=apply   writes .cs + .nsg.json, only when there are no errors\n");
            sb.Append("op=verify  reads the state from disk\n");
            sb.Append("```\n\n");
            sb.Append("`escapeRatio` is the share of raw blocks among all blocks. Gate on it:\n");
            sb.Append("`maxEscapeRatio: 0` means \"full translation only\".\n\n");
            sb.Append("`apply` writes the canonical text, not the text you sent. When your input is\n");
            sb.Append("already canonical this is a no-op; otherwise it is what makes the result stable.\n");

            return sb.ToString();
        }

        // ------------------------------------------------------------------

        static void Section(StringBuilder sb, string title)
        {
            sb.Append("## ").Append(title).Append("\n\n");
        }

        static void AppendBlockTable(StringBuilder sb, Nsg_BlockLibrary lib)
        {
            if (lib == null || lib.Blocks.Count == 0)
            {
                sb.Append("(the block library is empty)\n\n");
                return;
            }

            sb.Append("| block | node | shape | reversible | writing form |\n");
            sb.Append("|---|---|---|---|---|\n");

            for (int i = 0; i < lib.Blocks.Count; i++)
            {
                var def = lib.Blocks[i];
                if (def == null || string.IsNullOrEmpty(def.id)) continue;

                sb.Append("| `").Append(def.id).Append("` | ");
                sb.Append(string.IsNullOrEmpty(def.node) ? "—" : "`" + def.node + "`").Append(" | ");
                sb.Append(def.shape).Append(" | ");
                sb.Append(IsOneWay(def) ? "no" : "yes").Append(" | ");
                sb.Append(Cell(FormOf(def))).Append(" |\n");
            }
            sb.Append('\n');
        }

        static void AppendOneWay(StringBuilder sb, Nsg_BlockLibrary lib)
        {
            if (lib == null) return;

            bool any = false;
            for (int i = 0; i < lib.Blocks.Count; i++)
            {
                var def = lib.Blocks[i];
                if (def == null || !IsOneWay(def)) continue;

                if (!any)
                {
                    sb.Append("These print correctly but are NOT recognised on the way back. ");
                    sb.Append("Using them in code means the round trip changes the block, so they ");
                    sb.Append("are not a fixed point.\n\n");
                    any = true;
                }

                sb.Append("- `").Append(def.id).Append("` — ");
                if (string.IsNullOrEmpty(def.node))
                    sb.Append("template block (`node` is empty): write the underlying construct directly instead");
                else
                    sb.Append("instance call target: the parser sees a generic call, not this block");
                sb.Append('\n');
            }

            if (!any) sb.Append("None. Every block in this library is reversible.\n");
            sb.Append('\n');
        }

        /// <summary>
        /// Блок односторонний, если у него нет ключа сопоставления (шаблон) либо
        /// он описывает вызов метода экземпляра: печатается он верно, но при
        /// разборе цель вызова не совпадёт с <c>matchCall</c>.
        /// </summary>
        static bool IsOneWay(NsgBlockDef def)
        {
            if (string.IsNullOrEmpty(def.node)) return true;
            if (string.IsNullOrEmpty(def.matchCall)) return false;
            return HasTargetSocket(def);
        }

        static bool HasTargetSocket(NsgBlockDef def)
        {
            if (def.sockets == null) return false;
            for (int i = 0; i < def.sockets.Length; i++)
            {
                if (def.sockets[i] != null && def.sockets[i].name == "target") return true;
            }
            return false;
        }

        static string FormOf(NsgBlockDef def)
        {
            if (!string.IsNullOrEmpty(def.emit)) return def.emit;
            if (!string.IsNullOrEmpty(def.manual)) return def.manual;
            return def.Label();
        }

        static string Cell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            return "`" + s.Replace("|", "\\|") + "`";
        }
    }
}
