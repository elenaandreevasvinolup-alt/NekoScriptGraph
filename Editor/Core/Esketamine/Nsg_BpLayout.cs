using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Детерминированная раскладка узлов для режима чертежа.
    ///
    /// Зачем отдельный проход: до чертежа координаты узлов никто не заполнял.
    /// Код -> блоки кладёт все узлы в (0,0), потому что стопке координаты не
    /// нужны. Чертежу они нужны всем, иначе узлы лягут друг на друга.
    ///
    /// Раскладка обязана быть ДЕТЕРМИНИРОВАННОЙ: один и тот же граф всегда
    /// даёт одну и ту же картинку. Поэтому порядок берётся из структуры
    /// (цепочка next, ветки body/els, связи args), а не из порядка обхода
    /// списка, который зависит от истории правок.
    /// </summary>
    public static class Nsg_BpLayout
    {
        /// <summary>Шаг колонки: ширина карточки плюс зазор под провода.</summary>
        public const float ColW = 300f;

        /// <summary>Шаг строки: высота карточки плюс зазор.</summary>
        public const float RowH = 150f;

        /// <summary>
        /// Раскладывает узлы без координат. Узлы, которые пользователь уже
        /// подвинул, не трогаются: иначе правка терялась бы при пересборке.
        /// </summary>
        public static void Arrange(NsgMethodGraph graph)
        {
            if (graph == null || graph.nodes == null || graph.nodes.Count == 0) return;

            bool allEmpty = true;
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                var n = graph.nodes[i];
                if (n != null && (n.x != 0f || n.y != 0f)) { allEmpty = false; break; }
            }

            if (allEmpty) ArrangeAll(graph);
            else ArrangeMissing(graph);
        }

        /// <summary>Полная раскладка: перезаписывает координаты всех узлов.</summary>
        public static void ArrangeAll(NsgMethodGraph graph)
        {
            if (graph == null || graph.nodes == null) return;

            var placed = new HashSet<string>();
            var used = new HashSet<long>();

            PlaceChain(graph, graph.entry, 0, 0, placed, used);
            PlaceOrphans(graph, placed, used);
        }

        /// <summary>Доставляет координаты только тем узлам, у которых их нет.</summary>
        static void ArrangeMissing(NsgMethodGraph graph)
        {
            // Свободная строка ниже всего, что уже стоит. Считаем только по
            // РАССТАВЛЕННЫМ узлам: нерасставленный узел стоит в (0,0) и, если
            // учесть и его, нижняя граница съехала бы на строку вверх.
            int row = 0;
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                var n = graph.nodes[i];
                if (n == null) continue;
                if (n.x == 0f && n.y == 0f) continue;

                int r = (int)System.Math.Round(n.y / RowH);
                if (r + 1 > row) row = r + 1;
            }

            int col = 0;
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                var n = graph.nodes[i];
                if (n == null) continue;
                if (n.x != 0f || n.y != 0f) continue;

                n.x = col * ColW;
                n.y = row * RowH;
                col++;
                if (col >= 4) { col = 0; row++; }
            }
        }

        // ------------------------------------------------------------------

        static void PlaceChain(NsgMethodGraph g, string head, int col, int row,
                               HashSet<string> placed, HashSet<long> used)
        {
            var guard = new HashSet<string>();
            string cur = head;
            int c = col;

            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;

                Place(n, c, row, placed, used);

                // Данные, питающие узел, уходят на строки под ним.
                for (int i = 0; i < n.args.Count; i++)
                {
                    PlaceData(g, n.args[i], c, row, placed, used);
                }
                for (int i = 0; i < n.extra.Count; i++)
                {
                    PlaceData(g, n.extra[i], c, row, placed, used);
                }

                // Ветки управления — отдельные строки, начиная со следующей
                // колонки, чтобы читались как «вложенные».
                if (!string.IsNullOrEmpty(n.body))
                {
                    int br = FirstFreeRow(used, c + 1, row + 1);
                    PlaceChain(g, n.body, c + 1, br, placed, used);
                }
                if (!string.IsNullOrEmpty(n.els))
                {
                    int er = FirstFreeRow(used, c + 1, row + 1);
                    PlaceChain(g, n.els, c + 1, er, placed, used);
                }

                cur = n.next;
                c++;
            }
        }

        static void PlaceData(NsgMethodGraph g, NsgSlot slot, int col, int row,
                              HashSet<string> placed, HashSet<long> used)
        {
            if (slot == null || string.IsNullOrEmpty(slot.link)) return;

            var n = g.Find(slot.link);
            if (n == null || placed.Contains(n.id)) return;

            int r = FirstFreeRow(used, col, row + 1);
            Place(n, col, r, placed, used);

            for (int i = 0; i < n.args.Count; i++) PlaceData(g, n.args[i], col, r, placed, used);
            for (int i = 0; i < n.extra.Count; i++) PlaceData(g, n.extra[i], col, r, placed, used);
        }

        static void PlaceOrphans(NsgMethodGraph g, HashSet<string> placed, HashSet<long> used)
        {
            // Отвал начинается ниже всего, что уже занято: иначе брошенные
            // узлы легли бы поверх веток.
            int row = 0;
            foreach (long k in used)
            {
                int r = (int)(k & 0xFFFFFFFFL);
                if (r + 1 > row) row = r + 1;
            }

            int col = 0;

            for (int i = 0; i < g.nodes.Count; i++)
            {
                var n = g.nodes[i];
                if (n == null || placed.Contains(n.id)) continue;

                int r = FirstFreeRow(used, col, row);
                Place(n, col, r, placed, used);

                col++;
                if (col >= 4) { col = 0; row++; }
            }
        }

        static void Place(NsgGraphNode n, int col, int row,
                          HashSet<string> placed, HashSet<long> used)
        {
            if (n == null) return;
            n.x = col * ColW;
            n.y = row * RowH;
            used.Add(Key(col, row));
            if (!string.IsNullOrEmpty(n.id)) placed.Add(n.id);
        }

        static int FirstFreeRow(HashSet<long> used, int col, int from)
        {
            int r = from;
            while (used.Contains(Key(col, r))) r++;
            return r;
        }

        static long Key(int col, int row)
        {
            return ((long)col << 32) ^ (uint)row;
        }
    }
}
