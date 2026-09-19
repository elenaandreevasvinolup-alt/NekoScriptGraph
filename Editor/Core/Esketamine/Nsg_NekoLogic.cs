using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Логический разбор блока: не «что это за конструкция», а «что она делает
    /// с данными».
    ///
    /// ЗАЧЕМ ОТДЕЛЬНО ОТ ДЕРЕВА. Дерево (Nsg_NekoBlock) отвечает на вопрос
    /// «что внутри». Это отвечает на другой вопрос: «при каком условии и что
    /// именно меняется». Для блока-цикла это «пока cond — меняются вот эти
    /// переменные», для присваивания — «меняется вот эта».
    ///
    /// ПОЧЕМУ ЭТО РАБОТАЕТ ДЛЯ ВСЕХ ЯЗЫКОВ. Разбор не знает ни одного языка.
    /// Он читает две вещи из определения блока, которые язык заполняет сам:
    ///
    ///   · поле `node` — вид конструкции: while / for / foreach / if / assign /
    ///     localDecl / call. Словарь общий для всех восьми языков: у них одни и
    ///     те же Blocks/*.json, а синтаксис подставляет движок языка;
    ///   · имена сокетов — `cond` у условия, `target` у цели присваивания,
    ///     `name` у объявления. Это тоже данные блока.
    ///
    /// Поэтому новый язык получает логический разбор бесплатно: достаточно,
    /// чтобы его блоки были размечены теми же именами.
    ///
    /// ЧЕГО ОН НЕ ДЕЛАЕТ. Не пытается объяснить СМЫСЛ («зачем это нужно») — для
    /// этого нужна языковая модель. Он говорит только то, что видно в графе:
    /// условие, цели, число действий.
    /// </summary>
    public class Nsg_NekoLogic
    {
        /// <summary>loop | branch | action.</summary>
        public string Kind;

        /// <summary>Условие цикла или ветки, уже отрисованное. null — не нашлось.</summary>
        public string Condition;

        /// <summary>Переменные, которые блок меняет. Пусто — не меняет ничего.</summary>
        public List<string> Targets;

        /// <summary>Сколько операторов внутри ветки или тела цикла.</summary>
        public int Actions;

        public bool ChangesSomething
        {
            get { return Targets != null && Targets.Count > 0; }
        }
    }

    public static class Nsg_NekoLogicReader
    {
        /// <summary>Сколько целей показывать. Дальше перечисление уже не читается.</summary>
        const int MaxTargets = 4;

        /// <summary>Сколько операторов обойти в поисках целей. Защита от
        /// «накормить кошку» большим телом цикла.</summary>
        const int MaxScan = 64;

        static readonly HashSet<string> LoopNodes = new HashSet<string>
        {
            "while", "for", "foreach"
        };

        public static Nsg_NekoLogic Read(NsgMethodGraph g, NsgGraphNode node, Nsg_BlockLibrary lib)
        {
            if (node == null || lib == null) return null;

            var def = lib.Get(node.block);
            if (def == null || string.IsNullOrEmpty(def.node)) return null;

            string kind = KindOf(def.node);
            if (kind == null) return null;

            var logic = new Nsg_NekoLogic { Kind = kind };

            logic.Condition = ConditionOf(g, node, def, lib);
            logic.Targets = TargetsOf(g, node, def, lib);
            logic.Actions = CountChain(g, node.body, 0) + CountChain(g, node.els, 0);

            return logic;
        }

        /// <summary>Вид конструкции по полю `node`. null — разбирать нечего.</summary>
        static string KindOf(string node)
        {
            if (LoopNodes.Contains(node)) return "loop";
            if (node == "if") return "branch";

            // Действия: то, что меняет состояние или вызывает код.
            if (node == "assign" || node == "localDecl" || node == "call"
                || node == "return" || node == "expr")
                return "action";

            return null;
        }

        /// <summary>
        /// Условие цикла или ветки. Ищется по имени сокета, а не по номеру:
        /// у `for` сокеты идут init/cond/incr, у `if` — один cond, и номер
        /// зависел бы от языка и от порядка в JSON.
        /// </summary>
        static string ConditionOf(NsgMethodGraph g, NsgGraphNode node, NsgBlockDef def, Nsg_BlockLibrary lib)
        {
            int i = SocketIndexOf(def, "cond");
            if (i >= 0) return ValueOf(g, node, i, lib);

            // У foreach условия нет — есть источник обхода. Он и есть «при
            // каком наборе»: «для каждого элемента в <источник>».
            int src = SocketIndexOf(def, "source");
            if (src >= 0) return ValueOf(g, node, src, lib);

            return null;
        }

        /// <summary>
        /// Переменные, которые блок меняет.
        ///
        /// Своя цель (target у присваивания, name у объявления) плюс цели
        /// операторов внутри ветки и тела цикла: вопрос «что изменится» про
        /// цикл подразумевает именно тело, а не заголовок.
        /// </summary>
        static List<string> TargetsOf(NsgMethodGraph g, NsgGraphNode node, NsgBlockDef def, Nsg_BlockLibrary lib)
        {
            var found = new List<string>(4);
            var seen = new HashSet<string>();

            AddOwn(g, node, def, lib, found, seen);

            CollectFromChain(g, node.body, lib, found, seen, 0);
            CollectFromChain(g, node.els, lib, found, seen, 0);

            return found;
        }

        static void AddOwn(NsgMethodGraph g, NsgGraphNode node, NsgBlockDef def, Nsg_BlockLibrary lib,
                           List<string> found, HashSet<string> seen)
        {
            if (node == null || def == null) return;

            AddSocket(g, node, def, "target", lib, found, seen);
            AddSocket(g, node, def, "name", lib, found, seen);
        }

        static void AddSocket(NsgMethodGraph g, NsgGraphNode node, NsgBlockDef def, string name,
                              Nsg_BlockLibrary lib, List<string> found, HashSet<string> seen)
        {
            if (found.Count >= MaxTargets) return;

            int i = SocketIndexOf(def, name);
            if (i < 0) return;

            string v = ValueOf(g, node, i, lib);
            if (string.IsNullOrEmpty(v)) return;

            v = v.Trim();
            if (v.Length == 0) return;
            if (seen.Add(v)) found.Add(v);
        }

        static void CollectFromChain(NsgMethodGraph g, string head, Nsg_BlockLibrary lib,
                                     List<string> found, HashSet<string> seen, int depth)
        {
            if (g == null || string.IsNullOrEmpty(head)) return;
            if (depth > MaxScan) return;

            var guard = new HashSet<string>();
            string cur = head;

            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                if (depth++ > MaxScan) return;
                if (found.Count >= MaxTargets) return;

                var n = g.Find(cur);
                if (n == null) return;

                var def = lib.Get(n.block);
                if (def != null)
                {
                    AddOwn(g, n, def, lib, found, seen);

                    // Вложенные ветки тоже меняют переменные: цикл с if внутри
                    // обязан показать цели обоих.
                    CollectFromChain(g, n.body, lib, found, seen, depth + 1);
                    CollectFromChain(g, n.els, lib, found, seen, depth + 1);
                }

                cur = n.next;
            }
        }

        /// <summary>Индекс сокета по имени. Сравнение без учёта регистра и с
        /// вхождением: `condition`, `cond`, `test` — одно и то же по смыслу.</summary>
        static int SocketIndexOf(NsgBlockDef def, string name)
        {
            if (def == null || def.sockets == null) return -1;

            for (int i = 0; i < def.sockets.Length; i++)
            {
                var s = def.sockets[i];
                if (s == null || string.IsNullOrEmpty(s.name)) continue;

                if (s.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return i;
            }

            // Второй проход — по вхождению: у части языков имена длиннее.
            for (int i = 0; i < def.sockets.Length; i++)
            {
                var s = def.sockets[i];
                if (s == null || string.IsNullOrEmpty(s.name)) continue;

                if (s.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }

            return -1;
        }

        /// <summary>
        /// Значение сокета: текст, а если там вложенный блок — его отрисованная
        /// подпись с подставленными входами.
        /// </summary>
        static string ValueOf(NsgMethodGraph g, NsgGraphNode node, int index, Nsg_BlockLibrary lib)
        {
            var slot = node != null ? node.Arg(index) : null;
            if (slot == null) return null;

            if (!string.IsNullOrEmpty(slot.text)) return slot.text;
            if (string.IsNullOrEmpty(slot.link)) return null;

            return Nsg_NekoFacts.Render(g, slot.link, lib);
        }

        static int CountChain(NsgMethodGraph g, string head, int depth)
        {
            if (g == null || string.IsNullOrEmpty(head) || depth > MaxScan) return 0;

            var guard = new HashSet<string>();
            int n = 0;
            string cur = head;

            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                n++;
                if (n > MaxScan) break;

                var node = g.Find(cur);
                if (node == null) break;
                cur = node.next;
            }
            return n;
        }
    }
}
