using System.Collections.Generic;
using System.Text;

namespace NekoScriptGraph
{
    /// <summary>
    /// Помощники для сборки блоков языка. Лежат в ядре, потому что ими
    /// пользуются и встроенный HLSL, и языки-плагины из LanguageSupport —
    /// иначе каждый плагин тащил бы свою копию.
    ///
    /// Два вида блоков:
    ///   Call     — полноценный блок вызова (node = "call" + matchCall),
    ///              распознаётся и при обратном разборе;
    ///   Template — только печать по emit, обратно не распознаётся.
    /// </summary>
    public static class Nsg_CStyleBlocks
    {
        public static NsgSocketDef S(string name, string kind, bool required = true, bool variadic = false)
        {
            return new NsgSocketDef { name = name, kind = kind, required = required, variadic = variadic };
        }

        /// <summary>Слот-выражение: соединяется с другим блоком.</summary>
        public static NsgSocketDef E(string name)
        {
            return new NsgSocketDef { name = name, kind = "expr", required = true };
        }

        /// <summary>Блок вызова: печатается и распознаётся по имени и числу аргументов.</summary>
        /// <summary>
        /// Блок-вызов. Подпись задаётся ТОЛЬКО по-английски: это встроенный
        /// язык, а остальные приходят данными из Locale/&lt;code&gt;/strings.json
        /// по идентификатору блока (см. Nsg_BlockDef.Label).
        ///
        /// Если подпись не задана, берётся сам вызов: так блок читается как
        /// «dot({0}, {1})», а не как пустая строка.
        /// </summary>
        public static NsgBlockDef Call(string id, string shape, string name,
                                       string en,
                                       params NsgSocketDef[] sockets)
        {
            var fallback = new StringBuilder();
            fallback.Append(name).Append('(');
            for (int i = 0; i < sockets.Length; i++)
            {
                if (i > 0) fallback.Append(", ");
                fallback.Append('{').Append(i).Append('}');
            }
            fallback.Append(')');
            string auto = fallback.ToString();
            string label = string.IsNullOrEmpty(en) ? auto : en;

            return new NsgBlockDef
            {
                id = id,
                level = "high",
                shape = shape,
                categoryKey = "cat.api",
                category = label,
                label = label,
                labelEn = label,
                labelRu = string.Empty,
                sockets = sockets,
                emit = "",
                node = "call",
                matchCall = name,
                matchArity = sockets.Length,
                builtin = true
            };
        }

        /// <summary>
        /// Помечает блоки, добавленные языком, как его собственную группу.
        ///
        /// Идиомы всех языков по умолчанию получают categoryKey "cat.api" и
        /// сливаются с автогенерированными API-блоками проекта — в палитре
        /// нельзя понять, где чей язык. Здесь группа становится именем языка.
        /// Цвет фиксирован по идентификатору, поэтому не «прыгает» между
        /// запусками.
        /// </summary>
        public static void MarkLanguageGroup(List<NsgBlockDef> blocks, int from,
                                             string langId, string displayName)
        {
            string color = GroupColor(langId);
            for (int i = from; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b == null || string.IsNullOrEmpty(b.id)) continue;

                b.categoryKey = string.Empty;
                b.category = displayName;
                if (string.IsNullOrEmpty(b.color)) b.color = color;
            }
        }

        /// <summary>Цвет группы языка: одинаковый всегда, чтобы палитра не мерцала.</summary>
        public static string GroupColor(string langId)
        {
            switch (langId)
            {
                case "c":      return "#4E9A51";
                case "cpp":    return "#2F6FB5";
                case "java":   return "#B5651D";
                case "python": return "#3B7EA1";
                case "rust":   return "#A0522D";
                case "go":     return "#00ADD8";
                case "hlsl":   return "#7A5AA6";
                case "csharp": return "#2E7D32";
            }
            return string.Empty;
        }

        /// <summary>Идиома, которая не является вызовом — только шаблон.</summary>
        /// <summary>
        /// Блок-шаблон: печатает emit, обратно разборщиком не узнаётся.
        /// Подпись, как и у Call, задаётся только по-английски — остальные
        /// языки приходят из Locale по идентификатору блока.
        /// </summary>
        public static NsgBlockDef Template(string id, string shape,
                                           string en, string emit,
                                           params NsgSocketDef[] sockets)
        {
            return new NsgBlockDef
            {
                id = id,
                level = "high",
                shape = shape,
                categoryKey = "cat.api",
                category = en,
                label = en,
                labelEn = en,
                labelRu = string.Empty,
                sockets = sockets,
                emit = emit,
                node = ""
            };
        }
    }
}
