using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>Один вариант подсказки.</summary>
    public class Nsg_Suggestion
    {
        public NsgBlockDef Def;
        public float Score;
    }

    /// <summary>
    /// Подсказка следующего блока.
    ///
    /// Считается по библиотеке АКТИВНОГО языка, поэтому подсказки появляются
    /// в любом языке автоматически: отдельного словаря на каждый язык не
    /// нужно, новый блок получает подсказку сразу после объявления.
    ///
    /// Оценка намеренно простая: важен порядок в списке, а не точность
    /// предсказания. Пять первых вариантов должны быть осмысленными, и этого
    /// достаточно.
    /// </summary>
    public static class Nsg_Autocomplete
    {
        public static List<Nsg_Suggestion> Suggest(NsgMethodGraph graph, Nsg_BlockLibrary lib,
                                                   bool statementPosition, int limit)
        {
            var result = new List<Nsg_Suggestion>();
            if (lib == null || lib.Blocks == null) return result;

            // Подсказки — продвинутая возможность. Без кошки их некому
            // объяснить, поэтому список не показывается вовсе: молчаливая
            // пустота честнее, чем подсказки без объяснения.
            if (!Nsg_Advanced.Enabled) return result;

            var inMethod = CountBlocks(graph);
            var shownGroups = new HashSet<string>();

            for (int i = 0; i < lib.Blocks.Count; i++)
            {
                var def = lib.Blocks[i];
                if (def == null || string.IsNullOrEmpty(def.id)) continue;

                if (IsStatement(def) != statementPosition) continue;

                // Группа вариантов занимает одну позицию списка.
                if (!string.IsNullOrEmpty(def.variantGroup) && !shownGroups.Add(def.variantGroup)) continue;

                float score = PriorOf(def);

                // Что уже используется в этом методе — то, скорее всего,
                // понадобится и дальше: так подсказка подстраивается под файл.
                if (inMethod.ContainsKey(def.id)) score += 3f;

                result.Add(new Nsg_Suggestion { Def = def, Score = score });

                // Подсказка вызывается на каждую пересборку и на каждый метод.
                // Список кандидатов ограничен, иначе на проекте с тысячами
                // API-блоков сортировка стала бы заметной.
                if (result.Count >= 400) break;
            }

            result.Sort((a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                // Ничья разрешается по идентификатору: порядок обязан быть
                // устойчивым, иначе список дрожал бы между пересборками.
                return byScore != 0 ? byScore : string.CompareOrdinal(a.Def.id, b.Def.id);
            });

            if (limit > 0 && result.Count > limit)
            {
                result.RemoveRange(limit, result.Count - limit);
            }
            return result;
        }

        public static bool IsStatement(NsgBlockDef def)
        {
            if (def == null) return false;
            return def.shape == "statement" || def.shape == "control";
        }

        static Dictionary<string, int> CountBlocks(NsgMethodGraph g)
        {
            var counts = new Dictionary<string, int>();
            if (g == null || g.nodes == null) return counts;

            for (int i = 0; i < g.nodes.Count; i++)
            {
                var n = g.nodes[i];
                if (n == null || string.IsNullOrEmpty(n.block)) continue;

                int c;
                counts.TryGetValue(n.block, out c);
                counts[n.block] = c + 1;
            }
            return counts;
        }

        /// <summary>
        /// Приоритет категории: сначала то, что нужно постоянно, потом идиомы
        /// языка, в самом конце — автогенерированное API.
        /// </summary>
        static float PriorOf(NsgBlockDef def)
        {
            switch (def.categoryKey)
            {
                case "cat.var": return 3f;
                case "cat.ctrl": return 2.5f;
                case "cat.expr": return 2f;
                case "cat.raw": return 0.4f;
                case "cat.api": return 0.2f;
            }
            return 1.2f;
        }
    }
}
