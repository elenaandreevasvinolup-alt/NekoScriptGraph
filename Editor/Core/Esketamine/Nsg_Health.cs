using System.Collections.Generic;
using System.Text;

namespace NekoScriptGraph
{
    /// <summary>Пороги правил. Настраиваются, потому что «слишком длинный» —
    /// понятие проектное.</summary>
    public class Nsg_HealthThresholds
    {
        public int MethodLength = 40;
        public int Nesting = 4;
        public int Locals = 12;
        public int MemberChain = 4;
        public int ExpressionSize = 20;
        public int DuplicateMinNodes = 4;
        public float EscapeRatio = 0.20f;
        public int MagicRepeat = 3;
    }

    public class Nsg_HealthFinding
    {
        public string RuleId;
        public NsgSeverity Severity;
        public string Message;
        public string MethodName;
        public string BlockId;
        public string NodeId;
        public int Metric;
        public int Threshold;
    }

    public class Nsg_HealthReport
    {
        public int Score = 100;
        public int Errors;
        public int Warnings;
        public int Infos;

        public readonly List<Nsg_HealthFinding> Findings = new List<Nsg_HealthFinding>();
        public readonly List<string> Metrics = new List<string>();

        public void Add(Nsg_HealthFinding f)
        {
            if (f == null) return;
            Findings.Add(f);
            switch (f.Severity)
            {
                case NsgSeverity.Error: Errors++; break;
                case NsgSeverity.Warning: Warnings++; break;
                default: Infos++; break;
            }
        }

        /// <summary>Оценка 0..100. Ошибка весит 8, предупреждение 4, замечание 1.</summary>
        public void ComputeScore()
        {
            int s = 100 - Errors * 8 - Warnings * 4 - Infos * 1;
            if (s < 0) s = 0;
            if (s > 100) s = 100;
            Score = s;
        }

        public List<Nsg_HealthFinding> BySeverity(NsgSeverity sev)
        {
            var list = new List<Nsg_HealthFinding>();
            for (int i = 0; i < Findings.Count; i++)
            {
                if (Findings[i].Severity == sev) list.Add(Findings[i]);
            }
            return list;
        }
    }

    /// <summary>
    /// Проверка архитектурной гигиены. Работает по графу блоков, поэтому
    /// видит ровно то, что видит редактор, и не зависит от форматирования
    /// исходника.
    /// </summary>
    public static class Nsg_Health
    {
        // ------------------------------------------------------------------
        // Точка входа
        // ------------------------------------------------------------------

        public static Nsg_HealthReport Analyze(NsgFileModel model, Nsg_BlockLibrary library,
                                               Nsg_HealthThresholds thresholds)
        {
            var report = new Nsg_HealthReport();
            if (model == null) return report;
            if (thresholds == null) thresholds = new Nsg_HealthThresholds();

            var methods = model.AllMethods();
            report.Metrics.Add(Nsg_L10n.T("health.metric.methods", methods.Count));

            int statements = 0;
            int escapes = 0;

            var numberCounts = new Dictionary<string, int>();

            for (int i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (m.graph == null) m.graph = new NsgMethodGraph();

                var nodes = CollectStatementNodes(m.graph);
                statements += nodes.Count;

                for (int k = 0; k < nodes.Count; k++)
                {
                    if (nodes[k].block == "stmt.raw") escapes++;
                }

                CheckEmptyMethod(m, report);
                CheckMethodLength(m, nodes.Count, thresholds, report);
                CheckNesting(m, thresholds, report);
                CheckLocals(m, nodes, thresholds, report);
                CheckAfterReturn(m, report);
                CheckEmptyBodies(m, report);
                CheckConstantConditions(m, report);
                CheckNaming(m, nodes, report);
                CheckUnusedLocals(m, report);
                CheckMemberChains(m, thresholds, report);
                CheckExpressionSize(m, thresholds, report);
                CheckDuplicates(m, thresholds, report);
                CheckDanglingInputs(m, m.graph, library, report);
                CheckDataCycles(m, m.graph, report);
                CheckResourceLeaks(m, m.graph, library, report);
                CollectNumbers(m, numberCounts);
            }

            CheckMagicNumbers(numberCounts, thresholds, report);

            report.Metrics.Add(Nsg_L10n.T("health.metric.statements", statements));
            report.Metrics.Add(Nsg_L10n.T("health.metric.escapes",
                escapes, statements > 0 ? (100f * escapes / statements) : 0f));

            if (statements > 0 && (float)escapes / statements > thresholds.EscapeRatio)
            {
                report.Add(new Nsg_HealthFinding
                {
                    RuleId = "escape.ratio",
                    Severity = NsgSeverity.Warning,
                    Message = Nsg_L10n.T("health.escapeRatio",
                        (int)(100f * escapes / statements), (int)(thresholds.EscapeRatio * 100f)),
                    Metric = escapes,
                    Threshold = (int)(thresholds.EscapeRatio * statements)
                });
            }

            report.ComputeScore();
            return report;
        }

        // ------------------------------------------------------------------
        // Потенциальные поломки: висячие входы, циклы, утечки
        // ------------------------------------------------------------------

        /// <summary>
        /// Обязательный слот выражения пуст. Напечатается «Foo()» там, где
        /// ждали аргумент: код соберётся, но смысл потерян. Поэтому ошибка,
        /// а не замечание.
        /// </summary>
        static void CheckDanglingInputs(NsgStructNode m, NsgMethodGraph g,
                                        Nsg_BlockLibrary lib, Nsg_HealthReport report)
        {
            if (g == null || lib == null) return;

            var all = new List<NsgGraphNode>();
            CollectAll(g, g.entry, new HashSet<string>(), all);

            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                var def = lib.Get(n.block);
                if (def == null || def.sockets == null) continue;

                for (int s = 0; s < def.sockets.Length; s++)
                {
                    var socket = def.sockets[s];
                    if (socket == null || !socket.required) continue;
                    if (socket.kind != "expr") continue;

                    var slot = s < n.args.Count ? n.args[s] : null;
                    bool filled = slot != null
                        && (!string.IsNullOrEmpty(slot.link) || !string.IsNullOrEmpty(slot.text));
                    if (filled) continue;

                    report.Add(new Nsg_HealthFinding
                    {
                        RuleId = "input.dangling",
                        Severity = NsgSeverity.Error,
                        Message = Nsg_L10n.T("health.danglingInput", socket.name),
                        MethodName = m != null ? m.name : null,
                        BlockId = n.block,
                        NodeId = n.id
                    });
                }
            }
        }

        /// <summary>
        /// Цикл по связям данных: узел прямо или косвенно зависит от себя.
        /// Печать такого графа уходит в бесконечность, поэтому это ошибка.
        ///
        /// Обход идёт только по args/extra и стартует от каждого оператора:
        /// next/body/els ведут вперёд по структуре и замкнуться не могут,
        /// а выражения, не связанные ни с одним оператором, не печатаются.
        /// </summary>
        static void CheckDataCycles(NsgStructNode m, NsgMethodGraph g, Nsg_HealthReport report)
        {
            if (g == null) return;

            var statements = CollectStatementNodes(g);
            for (int i = 0; i < statements.Count; i++)
            {
                var state = new Dictionary<string, int>();
                WalkData(g, statements[i].id, state, m, report);
            }
        }

        static void WalkData(NsgMethodGraph g, string id, Dictionary<string, int> state,
                             NsgStructNode m, Nsg_HealthReport report)
        {
            if (string.IsNullOrEmpty(id)) return;

            int st;
            if (state.TryGetValue(id, out st))
            {
                if (st != 1) return;   // уже проверен целиком

                // Узел ещё в текущем пути — ссылка замкнулась на себя.
                var looped = g.Find(id);
                report.Add(new Nsg_HealthFinding
                {
                    RuleId = "graph.cycle",
                    Severity = NsgSeverity.Error,
                    Message = Nsg_L10n.T("health.cycle"),
                    MethodName = m != null ? m.name : null,
                    BlockId = looped != null ? looped.block : null,
                    NodeId = id
                });
                return;
            }

            var n = g.Find(id);
            if (n == null) return;

            state[id] = 1;
            WalkSlots(g, n.args, state, m, report);
            WalkSlots(g, n.extra, state, m, report);
            state[id] = 2;
        }

        static void WalkSlots(NsgMethodGraph g, List<NsgSlot> slots, Dictionary<string, int> state,
                              NsgStructNode m, Nsg_HealthReport report)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null) WalkData(g, slots[i].link, state, m, report);
            }
        }

        /// <summary>Пары «взял ресурс — отдал ресурс».</summary>
        static readonly string[][] ResourcePairs =
        {
            new[] { "c.malloc",  "c.free"   },
            new[] { "c.calloc",  "c.free"   },
            new[] { "c.realloc", "c.free"   },
            new[] { "c.fopen",   "c.fclose" },
            new[] { "expr.new",  "cpp.delete" }
        };

        /// <summary>Блок освобождения для блока захвата, если пара известна.</summary>
        public static string ReleaseFor(string acquireBlock)
        {
            if (string.IsNullOrEmpty(acquireBlock)) return null;

            for (int i = 0; i < ResourcePairs.Length; i++)
            {
                if (ResourcePairs[i][0] == acquireBlock) return ResourcePairs[i][1];
            }
            return null;
        }

        /// <summary>
        /// Взяли ресурс чаще, чем отдали. Проверка грубая — она не смотрит на
        /// ветки и ранние выходы, поэтому это предупреждение, а не ошибка.
        ///
        /// Пары применяются только если в библиотеке языка ЕСТЬ блок
        /// освобождения: иначе каждый C# `new` считался бы утечкой.
        /// </summary>
        static void CheckResourceLeaks(NsgStructNode m, NsgMethodGraph g,
                                       Nsg_BlockLibrary lib, Nsg_HealthReport report)
        {
            if (g == null || lib == null) return;

            var all = new List<NsgGraphNode>();
            CollectAll(g, g.entry, new HashSet<string>(), all);

            for (int p = 0; p < ResourcePairs.Length; p++)
            {
                string acquire = ResourcePairs[p][0];
                string release = ResourcePairs[p][1];

                if (lib.Get(acquire) == null || lib.Get(release) == null) continue;

                int got = 0;
                int freed = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].block == acquire) got++;
                    else if (all[i].block == release) freed++;
                }

                if (got == 0 || freed >= got) continue;

                report.Add(new Nsg_HealthFinding
                {
                    RuleId = "resource.leak",
                    Severity = NsgSeverity.Warning,
                    Message = Nsg_L10n.T("health.leak", acquire, release, got, freed),
                    MethodName = m != null ? m.name : null,
                    BlockId = acquire,
                    Metric = got - freed
                });
            }
        }

        // ------------------------------------------------------------------
        // Обход
        // ------------------------------------------------------------------

        /// <summary>Все узлы-операторы метода (без выражений).</summary>
        static List<NsgGraphNode> CollectStatementNodes(NsgMethodGraph g)
        {
            var list = new List<NsgGraphNode>();
            if (g == null) return list;

            var seen = new HashSet<string>();
            CollectChain(g, g.entry, seen, list);
            return list;
        }

        static void CollectChain(NsgMethodGraph g, string head, HashSet<string> seen, List<NsgGraphNode> into)
        {
            string cur = head;
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;
                into.Add(n);
                CollectChain(g, n.body, seen, into);
                CollectChain(g, n.els, seen, into);
                cur = n.next;
            }
        }

        /// <summary>Все узлы, включая выражения.</summary>
        static void CollectAll(NsgMethodGraph g, string head, HashSet<string> seen, List<NsgGraphNode> into)
        {
            string cur = head;
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;
                into.Add(n);

                CollectAll(g, n.body, seen, into);
                CollectAll(g, n.els, seen, into);
                for (int i = 0; i < n.args.Count; i++)
                {
                    if (n.args[i] != null) CollectAll(g, n.args[i].link, seen, into);
                }
                for (int i = 0; i < n.extra.Count; i++)
                {
                    if (n.extra[i] != null) CollectAll(g, n.extra[i].link, seen, into);
                }
                cur = n.next;
            }
        }

        static string TextOf(NsgGraphNode n, int index)
        {
            var s = n.Arg(index);
            return s != null ? (s.text ?? string.Empty) : string.Empty;
        }

        // ------------------------------------------------------------------
        // Правила
        // ------------------------------------------------------------------

        static void Add(Nsg_HealthReport r, string rule, NsgSeverity sev, string message,
                        string method, string blockId, string nodeId, int metric, int threshold)
        {
            r.Add(new Nsg_HealthFinding
            {
                RuleId = rule,
                Severity = sev,
                Message = message,
                MethodName = method,
                BlockId = blockId,
                NodeId = nodeId,
                Metric = metric,
                Threshold = threshold
            });
        }

        static void CheckEmptyMethod(NsgStructNode m, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            // Пустой метод — это метод БЕЗ точки входа: EmitChain возвращает
            // null, когда операторов нет (Nsg_CSharpCodeMap.cs). Раньше условие
            // стояло наоборот, и правило срабатывало ровно на непустых методах,
            // то есть сообщало «тело пустое» про метод с телом.
            if (!string.IsNullOrEmpty(m.graph.entry)) return;

            Add(r, "empty.method", NsgSeverity.Info, Nsg_L10n.T("health.emptyMethod"),
                m.name, null, null, 0, 0);
        }

        static void CheckMethodLength(NsgStructNode m, int count, Nsg_HealthThresholds t, Nsg_HealthReport r)
        {
            if (count <= t.MethodLength) return;
            Add(r, "complexity.length", NsgSeverity.Warning,
                Nsg_L10n.T("health.methodLength", count, t.MethodLength),
                m.name, null, null, count, t.MethodLength);
        }

        static void CheckNesting(NsgStructNode m, Nsg_HealthThresholds t, Nsg_HealthReport r)
        {
            int max = MaxDepth(m.graph, m.graph != null ? m.graph.entry : null, 0, new HashSet<string>());
            if (max <= t.Nesting) return;
            Add(r, "complexity.nesting", NsgSeverity.Warning,
                Nsg_L10n.T("health.nesting", max, t.Nesting),
                m.name, null, null, max, t.Nesting);
        }

        static int MaxDepth(NsgMethodGraph g, string head, int depth, HashSet<string> seen)
        {
            if (g == null) return depth;

            int max = depth;
            string cur = head;
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;

                if (!string.IsNullOrEmpty(n.body))
                    max = System.Math.Max(max, MaxDepth(g, n.body, depth + 1, seen));
                if (!string.IsNullOrEmpty(n.els))
                    max = System.Math.Max(max, MaxDepth(g, n.els, depth + 1, seen));

                cur = n.next;
            }
            return max;
        }

        static void CheckLocals(NsgStructNode m, List<NsgGraphNode> nodes, Nsg_HealthThresholds t, Nsg_HealthReport r)
        {
            int locals = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].block == "stmt.localDecl") locals++;
            }
            if (locals <= t.Locals) return;
            Add(r, "complexity.locals", NsgSeverity.Info,
                Nsg_L10n.T("health.locals", locals, t.Locals),
                m.name, null, null, locals, t.Locals);
        }

        static void CheckAfterReturn(NsgStructNode m, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            var seen = new HashSet<string>();
            var nodes = new List<NsgGraphNode>();
            CollectChain(m.graph, m.graph.entry, seen, nodes);

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].block != "stmt.return") continue;
                if (string.IsNullOrEmpty(nodes[i].next)) continue;

                var after = m.graph.Find(nodes[i].next);
                Add(r, "dead.afterReturn", NsgSeverity.Error,
                    Nsg_L10n.T("health.afterReturn"),
                    m.name, after != null ? after.block : null, nodes[i].next, 0, 0);
            }
        }

        static void CheckEmptyBodies(NsgStructNode m, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            var seen = new HashSet<string>();
            var nodes = new List<NsgGraphNode>();
            CollectAll(m.graph, m.graph.entry, seen, nodes);

            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n.block != "stmt.if" && n.block != "stmt.while" &&
                    n.block != "stmt.for" && n.block != "stmt.foreach") continue;

                if (!string.IsNullOrEmpty(n.body)) continue;

                Add(r, "dead.emptyBody", NsgSeverity.Warning,
                    Nsg_L10n.T("health.emptyBody"),
                    m.name, n.block, n.id, 0, 0);
            }
        }

        static void CheckConstantConditions(NsgStructNode m, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            var seen = new HashSet<string>();
            var nodes = new List<NsgGraphNode>();
            CollectAll(m.graph, m.graph.entry, seen, nodes);

            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n.block != "stmt.if" && n.block != "stmt.while") continue;

                string cond = TextOf(n, 0).Trim();
                if (cond != "true" && cond != "false") continue;

                Add(r, "dead.constantCondition", NsgSeverity.Warning,
                    Nsg_L10n.T("health.constantCondition", cond),
                    m.name, n.block, n.id, 0, 0);
            }
        }

        static readonly HashSet<string> AllowedShortNames = new HashSet<string>
        {
            "i", "j", "k", "n", "m", "x", "y", "z", "t", "e", "v", "a", "b", "c"
        };

        static readonly string[] PlaceholderFragments =
        {
            "temp", "tmp", "foo", "bar", "baz", "qux", "thing", "stuff", "asdf", "qwerty", "dummy"
        };

        static void CheckNaming(NsgStructNode m, List<NsgGraphNode> nodes, Nsg_HealthReport r)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].block != "stmt.localDecl") continue;

                string name = TextOf(nodes[i], 1);
                if (string.IsNullOrEmpty(name)) continue;

                if (name.Length == 1 && !AllowedShortNames.Contains(name))
                {
                    Add(r, "naming.short", NsgSeverity.Info,
                        Nsg_L10n.T("health.shortName", name),
                        m.name, nodes[i].block, nodes[i].id, name.Length, 2);
                    continue;
                }

                string lower = name.ToLowerInvariant();
                for (int k = 0; k < PlaceholderFragments.Length; k++)
                {
                    if (lower.IndexOf(PlaceholderFragments[k], System.StringComparison.Ordinal) < 0) continue;

                    Add(r, "naming.placeholder", NsgSeverity.Info,
                        Nsg_L10n.T("health.placeholderName", name),
                        m.name, nodes[i].block, nodes[i].id, 0, 0);
                    break;
                }
            }
        }

        static void CheckUnusedLocals(NsgStructNode m, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            var all = new List<NsgGraphNode>();
            CollectAll(m.graph, m.graph.entry, new HashSet<string>(), all);

            var declared = new List<NsgGraphNode>();
            var used = new HashSet<string>();

            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];

                if (n.block == "stmt.localDecl")
                {
                    declared.Add(n);
                    // начальное значение не считается использованием самого имени
                    continue;
                }

                if (n.block == "expr.ident")
                {
                    string name = TextOf(n, 0);
                    if (!string.IsNullOrEmpty(name)) used.Add(name);
                }
                else if (n.block == "expr.member")
                {
                    string name = TextOf(n, 1);
                    if (!string.IsNullOrEmpty(name)) used.Add(name);
                }

                // Листья живут во встроенных текстовых слотах, а не в узлах:
                // переменная вроде "rawYaw" внутри "rawYaw * 10f" — это текст
                // слота. Без этого прохода правило давало ложные срабатывания.
                for (int k = 0; k < n.args.Count; k++)
                {
                    var s = n.args[k];
                    if (s == null || s.IsLinked) continue;
                    if (NsgCodeMap.IsIdentifierLike(s.text)) used.Add(s.text);
                }
                for (int k = 0; k < n.extra.Count; k++)
                {
                    var s = n.extra[k];
                    if (s == null || s.IsLinked) continue;
                    if (NsgCodeMap.IsIdentifierLike(s.text)) used.Add(s.text);
                }
            }

            for (int i = 0; i < declared.Count; i++)
            {
                string name = TextOf(declared[i], 1);
                if (string.IsNullOrEmpty(name) || used.Contains(name)) continue;

                Add(r, "unused.local", NsgSeverity.Warning,
                    Nsg_L10n.T("health.unusedLocal", name),
                    m.name, declared[i].block, declared[i].id, 0, 0);
            }
        }

        static void CheckMemberChains(NsgStructNode m, Nsg_HealthThresholds t, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            var all = new List<NsgGraphNode>();
            CollectAll(m.graph, m.graph.entry, new HashSet<string>(), all);

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].block != "expr.member") continue;

                int depth = ChainDepth(m.graph, all[i], 0);
                if (depth <= t.MemberChain) continue;

                Add(r, "coupling.memberChain", NsgSeverity.Info,
                    Nsg_L10n.T("health.memberChain", depth, t.MemberChain),
                    m.name, all[i].block, all[i].id, depth, t.MemberChain);
            }
        }

        static int ChainDepth(NsgMethodGraph g, NsgGraphNode node, int depth)
        {
            if (node == null || depth > 32) return depth;

            var target = node.Arg(0);
            if (target == null || !target.IsLinked) return depth;

            var parent = g.Find(target.link);
            if (parent == null) return depth;
            if (parent.block != "expr.member") return depth;

            return ChainDepth(g, parent, depth + 1);
        }

        static void CheckExpressionSize(NsgStructNode m, Nsg_HealthThresholds t, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            var all = new List<NsgGraphNode>();
            CollectAll(m.graph, m.graph.entry, new HashSet<string>(), all);

            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];

                // считаем только «корни» выражений: слоты операторов
                if (n.block != "stmt.expr" && n.block != "stmt.localDecl" &&
                    n.block != "stmt.return" && n.block != "stmt.assign") continue;

                int size = ExpressionSize(m.graph, n);
                if (size <= t.ExpressionSize) continue;

                Add(r, "complexity.expression", NsgSeverity.Info,
                    Nsg_L10n.T("health.expressionSize", size, t.ExpressionSize),
                    m.name, n.block, n.id, size, t.ExpressionSize);
            }
        }

        static int ExpressionSize(NsgMethodGraph g, NsgGraphNode owner)
        {
            int size = 0;
            var seen = new HashSet<string>();

            for (int i = 0; i < owner.args.Count; i++)
            {
                var s = owner.args[i];
                if (s != null && s.IsLinked) size += CountSubtree(g, s.link, seen);
            }
            for (int i = 0; i < owner.extra.Count; i++)
            {
                var s = owner.extra[i];
                if (s != null && s.IsLinked) size += CountSubtree(g, s.link, seen);
            }
            return size;
        }

        static int CountSubtree(NsgMethodGraph g, string id, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) return 0;
            var n = g.Find(id);
            if (n == null) return 0;

            int size = 1;
            for (int i = 0; i < n.args.Count; i++)
            {
                if (n.args[i] != null) size += CountSubtree(g, n.args[i].link, seen);
            }
            for (int i = 0; i < n.extra.Count; i++)
            {
                if (n.extra[i] != null) size += CountSubtree(g, n.extra[i].link, seen);
            }
            return size;
        }

        static void CheckDuplicates(NsgStructNode m, Nsg_HealthThresholds t, Nsg_HealthReport r)
        {
            if (m.graph == null) return;

            var nodes = CollectStatementNodes(m.graph);
            var counts = new Dictionary<string, int>();

            for (int i = 0; i < nodes.Count; i++)
            {
                int size;
                string fp = Fingerprint(m.graph, nodes[i].id, 0, out size);
                if (size < t.DuplicateMinNodes) continue;

                int c;
                counts.TryGetValue(fp, out c);
                counts[fp] = c + 1;
            }

            foreach (var kv in counts)
            {
                if (kv.Value < 2) continue;
                Add(r, "duplication.blocks", NsgSeverity.Info,
                    Nsg_L10n.T("health.duplicate", kv.Value),
                    m.name, null, null, kv.Value, 2);
            }
        }

        static string Fingerprint(NsgMethodGraph g, string id, int depth, out int size)
        {
            size = 0;
            if (string.IsNullOrEmpty(id) || depth > 12) return string.Empty;

            var n = g.Find(id);
            if (n == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append(n.block).Append('(');
            for (int i = 0; i < n.args.Count; i++)
            {
                var s = n.args[i];
                if (i > 0) sb.Append(',');
                if (s == null) continue;
                if (s.IsLinked)
                {
                    int childSize;
                    sb.Append(Fingerprint(g, s.link, depth + 1, out childSize));
                    size += childSize;
                }
                else
                {
                    sb.Append(s.text);
                }
            }
            sb.Append("){");

            int bodySize;
            sb.Append(Fingerprint(g, n.body, depth + 1, out bodySize));
            size += bodySize;

            sb.Append("}[");
            int elseSize;
            sb.Append(Fingerprint(g, n.els, depth + 1, out elseSize));
            size += elseSize;
            sb.Append(']');

            size += 1;
            return sb.ToString();
        }

        static readonly HashSet<string> IgnoredNumbers = new HashSet<string>
        {
            "0", "1", "2", "-1", "0f", "1f", "2f", "0.0f", "1.0f", "0d", "1d", "100f"
        };

        static void CollectNumbers(NsgStructNode m, Dictionary<string, int> counts)
        {
            if (m.graph == null) return;

            var all = new List<NsgGraphNode>();
            CollectAll(m.graph, m.graph.entry, new HashSet<string>(), all);

            // Листовые значения живут во встроенных текстовых слотах, а не в
            // отдельных узлах, поэтому обходим слоты, а не только expr.literal.
            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                for (int k = 0; k < n.args.Count; k++) CountNumber(n.args[k], counts);
                for (int k = 0; k < n.extra.Count; k++) CountNumber(n.extra[k], counts);
            }
        }

        static void CountNumber(NsgSlot slot, Dictionary<string, int> counts)
        {
            if (slot == null || slot.IsLinked) return;

            string text = slot.text;
            if (string.IsNullOrEmpty(text) || IgnoredNumbers.Contains(text)) return;

            char c0 = text[0];
            if (!(char.IsDigit(c0) || (c0 == '-' && text.Length > 1 && char.IsDigit(text[1])))) return;

            int v;
            counts.TryGetValue(text, out v);
            counts[text] = v + 1;
        }

        static void CheckMagicNumbers(Dictionary<string, int> counts, Nsg_HealthThresholds t, Nsg_HealthReport r)
        {
            foreach (var kv in counts)
            {
                if (kv.Value < t.MagicRepeat) continue;
                Add(r, "magic.number", NsgSeverity.Info,
                    Nsg_L10n.T("health.magicNumber", kv.Key, kv.Value),
                    null, "expr.literal", null, kv.Value, t.MagicRepeat);
            }
        }
    }

    /// <summary>
    /// Проверка гигиены как диагностический проход. Регистрируется в
    /// <see cref="Nsg_PassRegistry"/>, поэтому вызывается тем же механизмом,
    /// что и будущие оптимизаторы.
    /// </summary>
    public class Nsg_HealthPass : INsg_Pass
    {
        public string Id
        {
            get { return "diagnostic.health"; }
        }

        public NsgPassKind Kind
        {
            get { return NsgPassKind.Diagnostic; }
        }

        public string TitleKey
        {
            get { return "health.title"; }
        }

        /// <summary>Отчёт последнего прогона. Окно гигиены читает его отсюда, а
        /// не считает анализ второй раз: один прогон прохода — один анализ.</summary>
        public static Nsg_HealthReport Last { get; private set; }

        public bool Run(Nsg_PassContext context, NsgDiagnostics diagnostics)
        {
            if (context == null || context.Model == null) return false;

            var report = Nsg_Health.Analyze(context.Model, context.Library, null);
            Last = report;
            if (diagnostics == null) return report.Findings.Count > 0;

            for (int i = 0; i < report.Findings.Count; i++)
            {
                var f = report.Findings[i];
                string where = string.IsNullOrEmpty(f.MethodName) ? string.Empty : (f.MethodName + ": ");
                diagnostics.Add(new NsgDiagnostic("HEALTH/" + f.RuleId, f.Severity,
                    where + f.Message, 0, 0));
            }
            return report.Findings.Count > 0;
        }
    }
}
