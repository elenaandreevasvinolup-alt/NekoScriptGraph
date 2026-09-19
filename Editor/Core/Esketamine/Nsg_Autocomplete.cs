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
            return Suggest(graph, lib, statementPosition, limit, null);
        }

        /// <summary>
        /// Подсказка с учётом соседа.
        ///
        /// <paramref name="afterBlockId"/> — блок, после которого вставляют
        /// (или который открывает список). Из него берётся переходная
        /// статистика ПО ЭТОМУ ЖЕ файлу: если в методе уже встречалось
        /// «объявление → использование», следующий такой же шаг получает
        /// надбавку. Данные не новые — это тот же граф, прочитанный иначе,
        /// поэтому подсказка подстраивается под конкретный скрипт без
        /// словарей и без обучения.
        /// </summary>
        public static List<Nsg_Suggestion> Suggest(NsgMethodGraph graph, Nsg_BlockLibrary lib,
                                                   bool statementPosition, int limit, string afterBlockId)
        {
            var result = new List<Nsg_Suggestion>();
            if (lib == null || lib.Blocks == null) return result;

            // Подсказки — продвинутая возможность. Без кошки их некому
            // объяснить, поэтому список не показывается вовсе: молчаливая
            // пустота честнее, чем подсказки без объяснения.
            if (!Nsg_Advanced.Enabled) return result;

            var inMethod = CountBlocks(graph);
            var shownGroups = new HashSet<string>();
            var transitions = !string.IsNullOrEmpty(afterBlockId) ? CountTransitions(graph) : null;

            Dictionary<string, int> nextOf = null;
            if (transitions != null) transitions.TryGetValue(afterBlockId, out nextOf);

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

                // Что уже шло ПОСЛЕ этого блока — то, скорее всего, пойдёт и
                // сейчас. Надбавка ограничена: частый переход не должен
                // превращаться в обязательный.
                if (nextOf != null)
                {
                    int n;
                    if (nextOf.TryGetValue(def.id, out n)) score += System.Math.Min(n, 3) * 0.75f;
                }

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
        /// Переходы «блок → следующий блок» по графу. Учитываются все три
        /// связи: продолжение списка (next) и вход в ветку (body / els).
        /// Именно поэтому надбавка работает и для «что поставить внутрь if»,
        /// а не только для «что поставить после».
        /// </summary>
        static Dictionary<string, Dictionary<string, int>> CountTransitions(NsgMethodGraph g)
        {
            var map = new Dictionary<string, Dictionary<string, int>>();
            if (g == null || g.nodes == null) return map;

            for (int i = 0; i < g.nodes.Count; i++)
            {
                var n = g.nodes[i];
                if (n == null || string.IsNullOrEmpty(n.block)) continue;

                AddTransition(map, n.block, g.Find(n.next));
                AddTransition(map, n.block, g.Find(n.body));
                AddTransition(map, n.block, g.Find(n.els));
            }
            return map;
        }

        static void AddTransition(Dictionary<string, Dictionary<string, int>> map,
                                  string from, NsgGraphNode to)
        {
            if (string.IsNullOrEmpty(from) || to == null || string.IsNullOrEmpty(to.block)) return;

            Dictionary<string, int> nexts;
            if (!map.TryGetValue(from, out nexts))
            {
                nexts = new Dictionary<string, int>(4);
                map[from] = nexts;
            }

            int c;
            nexts.TryGetValue(to.block, out c);
            nexts[to.block] = c + 1;
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

    /// <summary>
    /// Имена, которые уже есть в методе: локальные переменные и переменные
    /// цикла.
    ///
    /// Источник намеренно ОБЩИЙ: имя берётся из любого слота с видом «var».
    /// Так индекс работает для всех языков и всех блоков сразу — новый блок
    /// с именем-переменной попадает в подсказки без единой правки здесь.
    /// Это тот самый «маленький символьный стол», который нужен слотам
    /// значений, и ничего сверх него.
    /// </summary>
    public static class Nsg_SymbolIndex
    {
        public static List<string> CollectLocals(NsgMethodGraph g, Nsg_BlockLibrary lib)
        {
            var names = new List<string>();
            if (g == null || g.nodes == null || lib == null) return names;

            var seen = new HashSet<string>();

            for (int i = 0; i < g.nodes.Count; i++)
            {
                var n = g.nodes[i];
                if (n == null || n.args == null) continue;

                var def = lib.Get(n.block);
                if (def == null || def.sockets == null) continue;

                int count = System.Math.Min(def.sockets.Length, n.args.Count);
                for (int s = 0; s < count; s++)
                {
                    var sock = def.sockets[s];
                    if (sock == null || sock.kind != "var") continue;

                    var slot = n.args[s];
                    if (slot == null || string.IsNullOrEmpty(slot.text)) continue;

                    string name = slot.text.Trim();
                    if (name.Length == 0) continue;
                    if (seen.Add(name)) names.Add(name);
                }
            }

            names.Sort(System.StringComparer.Ordinal);
            return names;
        }
    }
}
