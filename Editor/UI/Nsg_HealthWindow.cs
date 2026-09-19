using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Окно архитектурной гигиены. Считает оценку 0..100 по графу блоков и
    /// перечисляет находки по правилам.
    ///
    /// Внизу показано состояние точки расширения авто-оптимизации: она уже
    /// подключена к тому же реестру проходов, поэтому будущие оптимизаторы
    /// появятся здесь без правок этого окна.
    /// </summary>
    public class Nsg_HealthWindow : EditorWindow
    {
        static Nsg_HealthWindow _instance;

        Nsg_Document _doc;
        Nsg_HealthReport _report;

        /// <summary>Итог последнего прогона оптимизатора. Показывается под
        /// списком находок, пока окно открыто.</summary>
        NsgOptimizeReport _optimizeResult;

        Label _score;
        Label _summary;
        VisualElement _list;
        VisualElement _optimizer;

        public static void Open(Nsg_Document doc)
        {
            if (_instance == null)
            {
                _instance = GetWindow<Nsg_HealthWindow>();
                _instance.titleContent = new GUIContent(Nsg_L10n.T("health.title"));
                _instance.minSize = new Vector2(520, 320);
            }
            _instance._doc = doc;
            _instance.Show();
            _instance.Run();
        }

        /// <summary>Пересчитывает, если окно уже открыто. Иначе ничего не делает.</summary>
        public static void RefreshIfOpen(Nsg_Document doc)
        {
            if (_instance == null) return;
            _instance._doc = doc;
            _instance.Run();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            var bar = new VisualElement();
            bar.style.flexDirection = Nsg_Rtl.Row;
            bar.style.alignItems = Align.Center;
            bar.style.marginBottom = 4;

            bar.Add(new Button(Run) { text = Nsg_L10n.T("health.rerun") });

            _score = new Label(string.Empty);
            _score.style.unityFontStyleAndWeight = FontStyle.Bold;
            _score.style.fontSize = 15;
            Nsg_Rtl.MarginStart(_score.style, 10);
            bar.Add(_score);

            _summary = new Label(string.Empty);
            Nsg_Rtl.MarginStart(_summary.style, 10);
            bar.Add(_summary);

            root.Add(bar);

            _list = new ScrollView();
            _list.style.flexGrow = 1;
            root.Add(_list);

            _optimizer = new VisualElement();
            _optimizer.style.borderTopWidth = 1;
            _optimizer.style.borderTopColor = new Color(0.25f, 0.26f, 0.29f);
            _optimizer.style.paddingTop = 4;
            _optimizer.style.paddingBottom = 2;
            root.Add(_optimizer);

            Run();
        }

        void Run()
        {
            if (_doc == null || _doc.Model == null)
            {
                _report = null;
                Rebuild();
                return;
            }

            var passes = new NsgDiagnostics();
            var context = new Nsg_PassContext
            {
                Model = _doc.Model,
                Library = _doc.Library,
                CsPath = _doc.CsPath
            };

            // Один прогон — один анализ. Раньше результат RunAll выбрасывался,
            // а следом вызывался Analyze, то есть модель разбиралась дважды.
            // Отчёт берётся у самого прохода.
            Nsg_PassRegistry.RunAll(context, NsgPassKind.Diagnostic, passes);
            _report = Nsg_HealthPass.Last;

            Rebuild();
        }

        void Rebuild()
        {
            if (_list == null) return;
            _list.Clear();

            if (_report == null)
            {
                var none = new Label(Nsg_L10n.T("win.noDoc"));
                none.style.color = new Color(0.6f, 0.63f, 0.68f);
                _list.Add(none);
                if (_score != null) _score.text = string.Empty;
                if (_summary != null) _summary.text = string.Empty;
                RebuildOptimizerNote();
                return;
            }

            if (_score != null)
            {
                _score.text = Nsg_L10n.T("health.score", _report.Score);
                _score.style.color = ScoreColor(_report.Score);
            }

            if (_summary != null)
            {
                _summary.text = Nsg_L10n.T("health.summary",
                    _report.Errors, _report.Warnings, _report.Infos);
            }

            for (int i = 0; i < _report.Metrics.Count; i++)
            {
                var m = new Label(_report.Metrics[i]);
                m.style.color = new Color(0.66f, 0.70f, 0.76f);
                m.style.unityTextAlign = Nsg_Rtl.TextAlign;
                m.style.marginBottom = 2;
                _list.Add(m);
            }

            if (_report.Findings.Count == 0)
            {
                var ok = new Label(Nsg_L10n.T("health.none"));
                ok.style.marginTop = 8;
                ok.style.unityTextAlign = Nsg_Rtl.TextAlign;
                ok.style.color = new Color(0.45f, 0.85f, 0.45f);
                _list.Add(ok);
                RebuildOptimizerNote();
                return;
            }

            AddGroup(NsgSeverity.Error, _report.BySeverity(NsgSeverity.Error));
            AddGroup(NsgSeverity.Warning, _report.BySeverity(NsgSeverity.Warning));
            AddGroup(NsgSeverity.Info, _report.BySeverity(NsgSeverity.Info));

            RebuildOptimizerNote();
        }

        void AddGroup(NsgSeverity severity, List<Nsg_HealthFinding> findings)
        {
            if (findings.Count == 0) return;

            Color color = SeverityColor(severity);

            var head = new Label(Label(severity) + "  (" + findings.Count + ")");
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.unityTextAlign = Nsg_Rtl.TextAlign;
            head.style.color = color;
            head.style.marginTop = 8;
            head.style.marginBottom = 3;
            _list.Add(head);

            for (int i = 0; i < findings.Count; i++)
            {
                var f = findings[i];

                var row = new VisualElement();
                row.style.flexDirection = Nsg_Rtl.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;
                row.style.paddingLeft = 4;
                row.style.paddingRight = 4;
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;
                row.style.backgroundColor = new Color(color.r, color.g, color.b, 0.10f);
                Nsg_Visual.Round(row, 3);

                var rule = new Label(f.RuleId);
                rule.style.minWidth = 150;
                rule.style.color = new Color(0.72f, 0.76f, 0.82f);
                rule.style.unityTextAlign = Nsg_Rtl.TextAlign;
                rule.style.fontSize = 10;
                row.Add(rule);

                if (!string.IsNullOrEmpty(f.MethodName))
                {
                    var where = new Label(f.MethodName);
                    where.style.minWidth = 110;
                    where.style.color = new Color(0.85f, 0.87f, 0.92f);
                    where.style.unityTextAlign = Nsg_Rtl.TextAlign;
                    where.style.unityFontStyleAndWeight = FontStyle.Bold;
                    row.Add(where);
                }

                var msg = new Label(f.Message);
                msg.style.whiteSpace = WhiteSpace.Normal;
                msg.style.flexGrow = 1;
                msg.style.unityTextAlign = Nsg_Rtl.TextAlign;
                msg.style.color = new Color(0.9f, 0.91f, 0.94f);
                row.Add(msg);

                _list.Add(row);
            }
        }

        void RebuildOptimizerNote()
        {
            if (_optimizer == null) return;
            _optimizer.Clear();

            // Оптимизаторы без кошки не запускаются вовсе, поэтому и говорить
            // о них нечего: строка про «пока не включены» обещала бы
            // возможность, которой в этой сборке нет.
            if (!Nsg_Advanced.Enabled) return;

            int optimizers = Nsg_PassRegistry.OfKind(NsgPassKind.Optimizer).Count;

            var note = new Label(optimizers > 0
                ? Nsg_L10n.T("health.optimizerReady", optimizers)
                : Nsg_L10n.T("health.optimizerNone"));
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.unityTextAlign = Nsg_Rtl.TextAlign;
            note.style.color = optimizers > 0
                ? new Color(0.5f, 0.85f, 0.55f)
                : new Color(0.65f, 0.68f, 0.74f);
            _optimizer.Add(note);

            var ids = Nsg_PassRegistry.Ids();
            if (ids.Count > 0)
            {
                var passes = new Label(Nsg_L10n.T("health.passes", string.Join(", ", ids.ToArray())));
                passes.style.whiteSpace = WhiteSpace.Normal;
                passes.style.unityTextAlign = Nsg_Rtl.TextAlign;
                passes.style.fontSize = 10;
                passes.style.color = new Color(0.55f, 0.58f, 0.64f);
                _optimizer.Add(passes);
            }

            // Кнопка есть только когда есть что запускать и есть документ.
            if (optimizers > 0 && _doc != null && _doc.Model != null)
            {
                var run = new Button(RunOptimizer);
                run.text = Nsg_L10n.T("health.optimizeRun");
                run.style.marginTop = 4;
                run.style.alignSelf = Align.FlexStart;
                _optimizer.Add(run);
            }

            if (_optimizeResult != null)
            {
                var r = _optimizeResult;
                string text;

                if (r.RolledBack)
                    text = Nsg_L10n.T("health.optimizeRolledBack", r.Reason ?? "?");
                else if (r.Removed > 0)
                    text = Nsg_L10n.T("health.optimizeDone", r.Removed, r.MethodsChanged);
                else
                    text = Nsg_L10n.T("health.optimizeNothing");

                var result = new Label(text);
                result.style.whiteSpace = WhiteSpace.Normal;
                result.style.unityTextAlign = Nsg_Rtl.TextAlign;
                result.style.fontSize = 10;
                result.style.marginTop = 3;
                result.style.color = r.RolledBack
                    ? Nsg_Palette.Warning
                    : (r.Removed > 0 ? new Color(0.5f, 0.85f, 0.55f) : new Color(0.65f, 0.68f, 0.74f));
                _optimizer.Add(result);
            }
        }

        /// <summary>
        /// Прогон оптимизаторов с записью результата.
        ///
        /// Запись идёт ТОЛЬКО если прогон не откатился и правка что-то дала:
        /// Nsg_Optimizer уже проверил, что модель печатается и текст
        /// разбирается заново, поэтому здесь остаётся применить результат к
        /// диску и перерисовать окно блоков.
        /// </summary>
        void RunOptimizer()
        {
            if (_doc == null || _doc.Model == null) return;

            var report = Nsg_Optimizer.Run(_doc);

            if (!report.RolledBack && report.NodesBefore != report.NodesAfter)
            {
                var diag = new NsgDiagnostics();
                string text;
                List<string> bodies;

                if (_doc.Generate(out text, out bodies, diag) && !diag.HasErrors)
                    _doc.WriteCode(text, bodies);
                else
                {
                    report.RolledBack = true;
                    report.Reason = diag.Items.Count > 0 && diag.Items[0] != null
                        ? diag.Items[0].Message
                        : "write failed";
                }
            }

            _optimizeResult = report;
            Nsg_Window.RefreshOpen();
            Run();
        }

        static string Label(NsgSeverity s)
        {
            switch (s)
            {
                case NsgSeverity.Error: return Nsg_L10n.T("err.error");
                case NsgSeverity.Warning: return Nsg_L10n.T("err.warning");
                default: return Nsg_L10n.T("err.info");
            }
        }

        static Color SeverityColor(NsgSeverity s)
        {
            switch (s)
            {
                case NsgSeverity.Error: return Nsg_Palette.Error;
                case NsgSeverity.Warning: return Nsg_Palette.Warning;
                default: return Nsg_Palette.Info;
            }
        }

        static Color ScoreColor(int score)
        {
            if (score >= 85) return new Color(0.45f, 0.85f, 0.45f);
            if (score >= 65) return new Color(0.95f, 0.82f, 0.35f);
            return new Color(1f, 0.5f, 0.5f);
        }
    }
}
