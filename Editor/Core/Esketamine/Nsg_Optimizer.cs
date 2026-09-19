using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>Что сделал прогон оптимизаторов. Данные, а не текст: их
    /// показывает и окно, и кошка, и агент через MCP.</summary>
    public class NsgOptimizeReport
    {
        public int PassesRun;
        public int NodesBefore;
        public int NodesAfter;
        public int MethodsChanged;

        /// <summary>Прогон откатили: результат не прошёл проверку.</summary>
        public bool RolledBack;

        /// <summary>Почему откатили. null — не откатывали.</summary>
        public string Reason;

        public readonly NsgDiagnostics Diagnostics = new NsgDiagnostics();

        /// <summary>Сколько узлов убрано. Отрицательное значение означало бы
        /// «стало больше» — такое откатывается до отчёта.</summary>
        public int Removed
        {
            get { return NodesBefore - NodesAfter; }
        }
    }

    /// <summary>
    /// Прогон оптимизаторов с обязательной проверкой и откатом.
    ///
    /// Правило плагина: автоматика меняет код пользователя только тогда, когда
    /// может показать, что не сломала его. Здесь это практическая проверка, а
    /// не обещание:
    ///
    ///   · модель снимается КОПИЕЙ до прогона (Nsg_Cloner);
    ///   · после прогона проверяется структура — все ссылки разрешаются,
    ///     циклов нет, узлов не стало больше;
    ///   · модель обязана напечататься без ошибок;
    ///   · напечатанный текст обязан заново разобраться без ошибок.
    ///
    /// Любой провал — откат к копии и причина в отчёте. Молчаливого «вроде
    /// сработало» здесь быть не может: оптимизатор трогает чужой код.
    /// </summary>
    public static class Nsg_Optimizer
    {
        public static NsgOptimizeReport Run(Nsg_Document doc)
        {
            var report = new NsgOptimizeReport();
            if (doc == null || doc.Model == null || doc.Engine == null) return report;

            // Без кошки оптимизаторы не запускаются: правку обязан кто-то
            // объяснить. Это то же правило, что у реестра проходов.
            if (!Nsg_Advanced.Enabled) return report;

            var before = Nsg_Cloner.CloneModel(doc.Model);
            report.NodesBefore = CountNodes(before);

            // Оптимизатор правит чужой код — это необратимо. Снимок делается
            // ДО прогона и независимо от того, пройдёт ли он проверку: точка
            // возврата нужна именно на случай, когда что-то пошло не так.
            Nsg_Checkpoints.AutoSave(doc.CsPath, doc.Model, null, "before optimise");

            // Сколько проблем у модели было ДО прогона. Файл может быть не
            // идеальным (сырые фрагменты, неизвестные блоки), и требовать от
            // оптимизатора «стало идеально» значило бы откатывать безобидную
            // чистку. Поэтому планка — «не стало хуже».
            int beforeErrors = CountRenderErrors(doc);

            var context = new Nsg_PassContext
            {
                Model = doc.Model,
                Library = doc.Library,
                CsPath = doc.CsPath
            };

            report.PassesRun = Nsg_PassRegistry.RunAll(context, NsgPassKind.Optimizer, report.Diagnostics);
            report.NodesAfter = CountNodes(doc.Model);
            report.MethodsChanged = CountChangedMethods(before, doc.Model);

            string reason;
            if (!Validate(doc, beforeErrors, before, out reason))
            {
                Rollback(doc, before, report, reason);
                return report;
            }

            // ИДЕМПОТЕНТНОСТЬ.
            //
            // Проходы обязаны приходить к неподвижной точке: если второй прогон
            // на уже оптимизированной модели снова что-то меняет, значит проход
            // осциллирует (удаляет и возвращает, или порождает новое из
            // своего же результата). Такой проход опаснее бесполезного: его
            // результат зависит от того, сколько раз его запустили.
            //
            // Проверка идёт на КОПИИ, поэтому рабочая модель не затрагивается.
            if (report.NodesBefore != report.NodesAfter && !IsIdempotent(doc, out reason))
            {
                Rollback(doc, before, report, reason);
                return report;
            }

            return report;
        }

        static void Rollback(Nsg_Document doc, NsgFileModel before, NsgOptimizeReport report, string reason)
        {
            doc.Model = before;
            report.NodesAfter = report.NodesBefore;
            report.MethodsChanged = 0;
            report.RolledBack = true;
            report.Reason = reason;
        }

        static bool IsIdempotent(Nsg_Document doc, out string reason)
        {
            reason = null;

            var probe = Nsg_Cloner.CloneModel(doc.Model);
            var context = new Nsg_PassContext
            {
                Model = probe,
                Library = doc.Library,
                CsPath = doc.CsPath
            };

            int fired = Nsg_PassRegistry.RunAll(context, NsgPassKind.Optimizer, new NsgDiagnostics());
            if (fired > 0)
            {
                reason = "the passes are not idempotent: a second run still changes the model (" +
                         fired + " pass(es) fired)";
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ проверка

        static bool Validate(Nsg_Document doc, int beforeErrors, NsgFileModel before, out string reason)
        {
            reason = null;

            // 1. Структура: ссылки разрешаются, циклов нет.
            var methods = doc.Model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i] != null ? methods[i].graph : null;
                if (g == null) continue;

                if (!Walk(g, g.entry, new HashSet<string>(), new HashSet<string>(), out reason))
                    return false;
            }

            // 1b. Ничего не ВЫДУМАНО.
            //
            // Оптимизатор имеет право удалять — и только удалять. Узел, которого
            // не было до прогона, означает, что проход сочиняет код, а не чистит
            // его; это уже не оптимизация, и принимать такое нельзя.
            if (!NoInventedNodes(before, doc.Model, out reason)) return false;

            // 1c. Точка входа пережила прогон.
            //
            // Если у метода была точка входа, а после прогона её нет, метод
            // опустел — это не чистка, это потеря тела.
            if (!EntriesPreserved(before, doc.Model, out reason)) return false;

            // 2. Модель печатается, и ошибок не стало больше, чем было.
            var diag = new NsgDiagnostics();
            string text;
            List<string> bodies;
            doc.TryRender(out text, out bodies, diag);

            if (CountErrors(diag) > beforeErrors)
            {
                reason = FirstMessage(diag, "the optimised model renders worse than before");
                return false;
            }

            // 3. Напечатанный текст снова разбирается. Это самый дешёвый
            //    оракул «код остался кодом»: печать — единственный потребитель
            //    графа, и если он споткнулся, правка негодная.
            if (!string.IsNullOrEmpty(text))
            {
                var back = new NsgDiagnostics();
                var split = doc.Engine.Split(text, System.IO.Path.GetFileName(doc.CsPath), back);
                if (split == null || back.HasErrors)
                {
                    reason = FirstMessage(back, "the printed text does not parse back");
                    return false;
                }
            }

            return true;
        }

        /// <summary>Ни один узел после прогона не является новым. Оптимизатор
        /// имеет право удалять — и только удалять.</summary>
        static bool NoInventedNodes(NsgFileModel before, NsgFileModel after, out string reason)
        {
            reason = null;

            var old = new HashSet<string>();
            CollectIds(before, old);

            var methods = after.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i] != null ? methods[i].graph : null;
                if (g == null || g.nodes == null) continue;

                for (int n = 0; n < g.nodes.Count; n++)
                {
                    var node = g.nodes[n];
                    if (node == null || string.IsNullOrEmpty(node.id)) continue;

                    if (!old.Contains(node.id))
                    {
                        reason = "the run invented node '" + node.id + "'";
                        return false;
                    }
                }
            }
            return true;
        }

        static void CollectIds(NsgFileModel model, HashSet<string> into)
        {
            if (model == null) return;

            var methods = model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i] != null ? methods[i].graph : null;
                if (g == null || g.nodes == null) continue;

                for (int n = 0; n < g.nodes.Count; n++)
                {
                    var node = g.nodes[n];
                    if (node != null && !string.IsNullOrEmpty(node.id)) into.Add(node.id);
                }
            }
        }

        /// <summary>Точка входа, которая была, осталась. Пустой метод — это не
        /// чистка, это потеря тела.</summary>
        static bool EntriesPreserved(NsgFileModel before, NsgFileModel after, out string reason)
        {
            reason = null;

            var a = before.AllMethods();
            var b = after.AllMethods();

            for (int i = 0; i < a.Count && i < b.Count; i++)
            {
                string ea = a[i] != null && a[i].graph != null ? a[i].graph.entry : null;
                string eb = b[i] != null && b[i].graph != null ? b[i].graph.entry : null;

                if (!string.IsNullOrEmpty(ea) && string.IsNullOrEmpty(eb))
                {
                    reason = "method '" + (a[i] != null ? a[i].name : ("#" + i)) + "' lost its body";
                    return false;
                }
            }
            return true;
        }

        static int CountRenderErrors(Nsg_Document doc)
        {
            var diag = new NsgDiagnostics();
            string text;
            List<string> bodies;
            doc.TryRender(out text, out bodies, diag);
            return CountErrors(diag);
        }

        static int CountErrors(NsgDiagnostics diag)
        {
            int n = 0;
            if (diag == null) return n;

            for (int i = 0; i < diag.Items.Count; i++)
            {
                var d = diag.Items[i];
                if (d != null && d.Severity == NsgSeverity.Error) n++;
            }
            return n;
        }

        /// <summary>Обход графа: ссылки разрешаются, циклов нет.</summary>
        static bool Walk(NsgMethodGraph g, string id, HashSet<string> path, HashSet<string> done, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(id)) return true;
            if (path.Contains(id)) { reason = "cycle at '" + id + "'"; return false; }
            if (done.Contains(id)) return true;

            var n = g.Find(id);
            if (n == null) { reason = "dangling reference '" + id + "'"; return false; }

            path.Add(id);

            bool ok = Walk(g, n.next, path, done, out reason)
                   && Walk(g, n.body, path, done, out reason)
                   && Walk(g, n.els, path, done, out reason);

            if (ok) ok = WalkSlots(g, n.args, path, done, out reason);
            if (ok) ok = WalkSlots(g, n.extra, path, done, out reason);

            path.Remove(id);
            if (ok) done.Add(id);
            return ok;
        }

        static bool WalkSlots(NsgMethodGraph g, List<NsgSlot> slots,
                              HashSet<string> path, HashSet<string> done, out string reason)
        {
            reason = null;
            if (slots == null) return true;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || string.IsNullOrEmpty(s.link)) continue;
                if (!Walk(g, s.link, path, done, out reason)) return false;
            }
            return true;
        }

        static string FirstMessage(NsgDiagnostics diag, string fallback)
        {
            if (diag != null && diag.Items.Count > 0 && diag.Items[0] != null)
                return diag.Items[0].Message;
            return fallback;
        }

        // ------------------------------------------------------------------ счёт

        public static int CountNodes(NsgFileModel model)
        {
            int n = 0;
            if (model == null) return n;

            var methods = model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i] != null ? methods[i].graph : null;
                if (g != null && g.nodes != null) n += g.nodes.Count;
            }
            return n;
        }

        static int CountChangedMethods(NsgFileModel before, NsgFileModel after)
        {
            if (before == null || after == null) return 0;

            var a = before.AllMethods();
            var b = after.AllMethods();
            int n = 0;

            for (int i = 0; i < a.Count && i < b.Count; i++)
            {
                int ca = a[i] != null && a[i].graph != null && a[i].graph.nodes != null ? a[i].graph.nodes.Count : 0;
                int cb = b[i] != null && b[i].graph != null && b[i].graph.nodes != null ? b[i].graph.nodes.Count : 0;
                if (ca != cb) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// Второй оптимизатор: свёртка условия-константы.
    ///
    /// `if (true) { … }` выполняется ровно как `{ … }`, а `if (false) { … }`
    /// — как пустота. Литерал `true`/`false` не имеет побочных эффектов, поэтому
    /// подстановка взятой ветки не меняет поведение — в отличие от условия,
    /// которое что-то вычисляет.
    ///
    /// Порядок важен: проход идёт ДО чистки недостижимого, чтобы осиротевший
    /// литерал условия убрал уже знакомый Nsg_DeadCodePass.
    /// </summary>
    public class Nsg_ConstantConditionPass : INsg_Pass
    {
        public const string PassId = "optimizer.constantcondition";

        public string Id { get { return PassId; } }
        public NsgPassKind Kind { get { return NsgPassKind.Optimizer; } }
        public string TitleKey { get { return "health.optimizerConstCond"; } }

        public bool Run(Nsg_PassContext context, NsgDiagnostics diagnostics)
        {
            if (context == null || context.Model == null) return false;

            bool changed = false;
            var methods = context.Methods();

            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i] != null ? methods[i].graph : null;
                if (g == null || g.nodes == null) continue;

                if (Fold(g)) changed = true;
            }

            return changed;
        }

        /// <summary>Свёртка повторяется до устойчивости: одна подстановка может
        /// открыть следующую (вложенный if с константой).</summary>
        static bool Fold(NsgMethodGraph g)
        {
            bool any = false;

            for (int guard = 0; guard < 256; guard++)
            {
                bool again = false;

                for (int i = 0; i < g.nodes.Count; i++)
                {
                    var n = g.nodes[i];
                    if (n == null || n.block != "stmt.if") continue;

                    bool value;
                    if (!ConstantOf(g, n, out value)) continue;

                    if (Apply(g, n, value)) { any = true; again = true; break; }
                }

                if (!again) break;
            }

            return any;
        }

        /// <summary>Условие — литерал true/false, напрямую или через блок литерала.</summary>
        static bool ConstantOf(NsgMethodGraph g, NsgGraphNode node, out bool value)
        {
            value = false;

            var slot = node.Arg(0);
            if (slot == null) return false;

            if (!string.IsNullOrEmpty(slot.text)) return ParseBool(slot.text, out value);
            if (string.IsNullOrEmpty(slot.link)) return false;

            var lit = g.Find(slot.link);
            if (lit == null || lit.block != "expr.literal") return false;

            var text = lit.Arg(0);
            return text != null && ParseBool(text.text, out value);
        }

        static bool ParseBool(string s, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(s)) return false;

            s = s.Trim();
            if (s == "true") { value = true; return true; }
            if (s == "false") { value = false; return true; }
            return false;
        }

        static bool Apply(NsgMethodGraph g, NsgGraphNode node, bool value)
        {
            string branch = value ? node.body : node.els;
            string tail = TailOf(g, branch);

            string replacement;
            if (!string.IsNullOrEmpty(tail))
            {
                // Хвост взятой ветки продолжается тем, что шло после if.
                var tailNode = g.Find(tail);
                if (tailNode != null) tailNode.next = node.next;
                replacement = branch;
            }
            else
            {
                // Ветка пуста — выполняется то, что шло после if.
                replacement = node.next;
            }

            Rewire(g, node.id, replacement);
            g.Remove(node.id);
            return true;
        }

        /// <summary>Перенаправляет все ссылки с одного узла на другой.</summary>
        static void Rewire(NsgMethodGraph g, string from, string to)
        {
            if (g == null || string.IsNullOrEmpty(from)) return;

            if (g.entry == from) g.entry = to;

            for (int i = 0; i < g.nodes.Count; i++)
            {
                var n = g.nodes[i];
                if (n == null) continue;

                if (n.next == from) n.next = to;
                if (n.body == from) n.body = to;
                if (n.els == from) n.els = to;

                RewireSlots(n.args, from, to);
                RewireSlots(n.extra, from, to);
            }
        }

        static void RewireSlots(List<NsgSlot> slots, string from, string to)
        {
            if (slots == null) return;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null && s.link == from) s.link = to;
            }
        }

        static string TailOf(NsgMethodGraph g, string head)
        {
            if (g == null || string.IsNullOrEmpty(head)) return null;

            var guard = new HashSet<string>();
            string cur = head;
            string last = null;

            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                last = cur;
                var n = g.Find(cur);
                if (n == null) break;
                cur = n.next;
            }
            return last;
        }
    }

    /// <summary>
    /// Первый оптимизатор: чистка недостижимого.
    ///
    /// Что именно убирается:
    ///   · операторы после return / break / continue в том же списке — они не
    ///     выполняются никогда;
    ///   · узлы, на которые уже никто не ссылается (осиротевшие после правок):
    ///     они и так не печатались, но продолжали занимать граф.
    ///
    /// Почему это безопасно: обход достижимости идёт от entry и останавливается
    /// на терминаторе. Всё, что не помечено достижимым, не может быть
    /// выполнено ни при каком входе, поэтому поведение не меняется. Проверка в
    /// Nsg_Optimizer.Run дополнительно убеждается, что модель печатается и
    /// текст разбирается заново.
    /// </summary>
    public class Nsg_DeadCodePass : INsg_Pass
    {
        public const string PassId = "optimizer.deadcode";

        static readonly HashSet<string> Terminators = new HashSet<string>
        {
            "stmt.return", "stmt.break", "stmt.continue"
        };

        public string Id
        {
            get { return PassId; }
        }

        public NsgPassKind Kind
        {
            get { return NsgPassKind.Optimizer; }
        }

        public string TitleKey
        {
            get { return "health.optimizerDeadCode"; }
        }

        public bool Run(Nsg_PassContext context, NsgDiagnostics diagnostics)
        {
            if (context == null || context.Model == null) return false;

            bool changed = false;
            var methods = context.Methods();

            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i] != null ? methods[i].graph : null;
                if (g == null || g.nodes == null || g.nodes.Count == 0) continue;

                if (Prune(g)) changed = true;
            }

            return changed;
        }

        static bool Prune(NsgMethodGraph g)
        {
            var live = new HashSet<string>();
            Mark(g, g.entry, live);

            if (live.Count >= g.nodes.Count) return false;

            int removed = g.nodes.RemoveAll(n => n == null || !live.Contains(n.id));
            return removed > 0;
        }

        /// <summary>Помечает достижимые узлы. На терминаторе цепочка next
        /// обрывается: продолжение того же списка уже не выполнится.</summary>
        static void Mark(NsgMethodGraph g, string id, HashSet<string> live)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!live.Add(id)) return;

            var n = g.Find(id);
            if (n == null) return;

            // Значения живого узла живут всегда — в том числе у return.
            MarkSlots(g, n.args, live);
            MarkSlots(g, n.extra, live);

            if (Terminators.Contains(n.block)) return;

            Mark(g, n.body, live);
            Mark(g, n.els, live);
            Mark(g, n.next, live);
        }

        static void MarkSlots(NsgMethodGraph g, List<NsgSlot> slots, HashSet<string> live)
        {
            if (slots == null) return;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || string.IsNullOrEmpty(s.link)) continue;
                Mark(g, s.link, live);
            }
        }
    }
}
