using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// «Свой блок»: превращает метод или тип в определение блока и кладёт его
    /// в Blocks/Macros/, откуда он сразу попадает в палитру.
    ///
    /// Для метода блок печатает вызов и ОБРАТИМ: разборщик узнаёт вызов по
    /// имени (matchCall) и собирает блок обратно.
    ///
    /// Для типа блок объявляет переменную и создаёт объект. Это шаблон:
    /// параметров конструктора мы не знаем, поэтому обратно он читается как
    /// обычный вызов.
    ///
    /// Отдельного хранилища не нужно: папка Blocks/ читается рекурсивно,
    /// поэтому подпапка Macros подхватывается тем же загрузчиком.
    /// </summary>
    public static class Nsg_Macro
    {
        /// <summary>Категория своих блоков — отдельная группа в палитре.</summary>
        public const string CategoryKey = "cat.macro";

        public static string MacrosDir
        {
            get { return Path.Combine(Nsg_Paths.BlocksDir, "Macros"); }
        }

        /// <summary>Строит определение по методу или типу. null — не получилось.</summary>
        public static NsgBlockDef Build(NsgStructNode node, string displayName)
        {
            if (node == null) return null;

            if (node.kind == "method") return BuildFromMethod(node, displayName);
            if (node.kind == "type") return BuildFromType(node, displayName);
            return null;
        }

        static NsgBlockDef BuildFromMethod(NsgStructNode node, string displayName)
        {
            string call = node.name;
            if (string.IsNullOrEmpty(call)) return null;

            var pars = ParseParameters(node.header);
            string name = string.IsNullOrEmpty(displayName) ? call : displayName;

            var def = new NsgBlockDef
            {
                id = "macro." + call + "." + pars.Count,
                level = "high",
                shape = "statement",
                categoryKey = CategoryKey,
                category = name,
                node = "call",
                emit = string.Empty,
                matchCall = call,
                matchArity = pars.Count,
                builtin = false,
                manual = node.header != null ? node.header.Trim() : string.Empty
            };

            def.sockets = new NsgSocketDef[pars.Count];
            for (int i = 0; i < pars.Count; i++)
            {
                def.sockets[i] = new NsgSocketDef
                {
                    name = pars[i],
                    kind = "expr",
                    required = true,
                    variadic = false,
                    choices = new string[0]
                };
            }

            string label = Signature(name, pars.Count);
            def.label = label;
            def.labelEn = label;
            def.labelRu = label;

            return def;
        }

        static NsgBlockDef BuildFromType(NsgStructNode node, string displayName)
        {
            string type = node.name;
            if (string.IsNullOrEmpty(type)) return null;

            string name = string.IsNullOrEmpty(displayName) ? type : displayName;

            var def = new NsgBlockDef
            {
                id = "macro.new." + type,
                level = "high",
                shape = "statement",
                categoryKey = CategoryKey,
                category = name,
                node = string.Empty,
                emit = type + " {{0}} = new " + type + "();",
                builtin = false,
                manual = "new " + type + "()"
            };

            def.sockets = new NsgSocketDef[]
            {
                new NsgSocketDef
                {
                    name = "name",
                    kind = "var",
                    required = true,
                    variadic = false,
                    choices = new string[0]
                }
            };

            string label = name + " {0} = new " + type + "()";
            def.label = label;
            def.labelEn = label;
            def.labelRu = label;

            return def;
        }

        static string Signature(string name, int arity)
        {
            var sb = new StringBuilder();
            sb.Append(name).Append('(');
            for (int i = 0; i < arity; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('{').Append(i).Append('}');
            }
            sb.Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Достаёт имена параметров из заголовка метода.
        ///
        /// Разбор нарочно простой: заголовок — дословный текст, и полная
        /// грамматика тут не нужна. Достаточно резать по запятым верхнего
        /// уровня, не заходя внутрь угловых скобок и скобок массивов.
        /// </summary>
        public static List<string> ParseParameters(string header)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(header)) return names;

            int open = header.IndexOf('(');
            int close = header.LastIndexOf(')');
            if (open < 0 || close <= open) return names;

            string inner = header.Substring(open + 1, close - open - 1);
            if (inner.Trim().Length == 0) return names;

            int depth = 0;
            var current = new StringBuilder();

            for (int i = 0; i < inner.Length; i++)
            {
                char ch = inner[i];
                if (ch == '<' || ch == '[' || ch == '(') depth++;
                else if (ch == '>' || ch == ']' || ch == ')') depth--;

                if (ch == ',' && depth == 0)
                {
                    AddParamName(names, current.ToString());
                    current.Length = 0;
                    continue;
                }
                current.Append(ch);
            }

            AddParamName(names, current.ToString());
            return names;
        }

        static void AddParamName(List<string> names, string part)
        {
            if (string.IsNullOrEmpty(part)) return;

            var bits = part.Trim().Split(' ');
            // Имя параметра — последний непустой токен, без модификаторов.
            for (int i = bits.Length - 1; i >= 0; i--)
            {
                string t = bits[i].Trim();
                if (t.Length == 0) continue;
                if (t == "ref" || t == "out" || t == "in" || t == "params" || t == "this") continue;

                names.Add(t);
                return;
            }
        }

        /// <summary>Сохраняет определение в Blocks/Macros. Возвращает путь или null.</summary>
        public static string Save(NsgBlockDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.id)) return null;

            Nsg_Paths.EnsureDirectory(MacrosDir);

            string path = Path.Combine(MacrosDir, SafeName(def.id) + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(def, true));
            return path;
        }

        static string SafeName(string id)
        {
            var sb = new StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-';
                sb.Append(ok ? c : '_');
            }
            return sb.ToString();
        }
    }
}
