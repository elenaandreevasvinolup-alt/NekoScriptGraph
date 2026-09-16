using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Ворота продвинутых возможностей.
    ///
    /// Правило плагина: базовое работает ВСЕГДА. Разбор, печать, диагностика,
    /// окно блоков, библиотека, управление файлами — от кошки не зависят.
    /// Всё, что «умнее» — подсказки, авто-исправления, оптимизаторы, разбор
    /// подблоков, внешние движки — включается только когда установлена кошка.
    ///
    /// Причина не в лицензии. Продвинутая функция обязана уметь объяснить себя
    /// и свои правки, а объяснять умеет только кошка. Функция без объяснения
    /// пугает сильнее, чем помогает.
    ///
    /// Поэтому проверка одна на весь плагин. Удаление папки ProgramNeko
    /// выключает продвинутое и НЕ ломает базовое — это и есть проверка
    /// корректности всей архитектуры.
    /// </summary>
    public static class Nsg_Advanced
    {
        /// <summary>Кошка установлена: продвинутое разрешено.</summary>
        public static bool Enabled
        {
            get { return Nsg_NekoRegistry.Installed; }
        }

        /// <summary>Ключ локализации «почему нельзя». null — можно.</summary>
        public static string BlockedKey
        {
            get { return Enabled ? null : "adv.needAssistant"; }
        }

        /// <summary>Ключ локализации состояния для настроек.</summary>
        public static string StatusKey
        {
            get { return Enabled ? "adv.enabled" : "adv.disabled"; }
        }

        /// <summary>Короткое пояснение для интерфейса. Пусто — пояснять нечего.</summary>
        public static string Explain
        {
            get { return Enabled ? string.Empty : Nsg_L10n.T("adv.needAssistant"); }
        }
    }

    // ---------------------------------------------------------------------
    // Глобальный план исправлений
    // ---------------------------------------------------------------------

    /// <summary>
    /// Один шаг плана. План — это ДАННЫЕ, а не действие: его можно показать,
    /// отфильтровать и применить по частям. Именно поэтому он живёт в ядре,
    /// а не в интерфейсе.
    /// </summary>
    public class Nsg_FixStep
    {
        /// <summary>Идентификатор авто-исправления («graph.cycle» и т.п.) или null, если только совет.</summary>
        public string QuickFixId;

        /// <summary>Правило гигиены, из которого шаг вырос.</summary>
        public string RuleId;

        /// <summary>Ключ локализации названия правки. null — брать Message.</summary>
        public string TitleKey;

        /// <summary>Готовое (уже локализованное ядром) описание находки.</summary>
        public string Message;

        public string MethodName;
        public string NodeId;

        /// <summary>Можно применять автоматически, не спрашивая.</summary>
        public bool AutoSafe;

        /// <summary>Правка обязана идти первой: без неё модель непечатаема.</summary>
        public bool Blocking;

        public NsgSeverity Severity;
    }

    /// <summary>План целиком. Порядок шагов — это порядок применения.</summary>
    public class Nsg_FixPlan
    {
        public readonly List<Nsg_FixStep> Steps = new List<Nsg_FixStep>();

        /// <summary>Сколько находок осталось без авто-правки: их видно, но руками.</summary>
        public int Manual;

        public bool IsEmpty
        {
            get { return Steps.Count == 0; }
        }

        public int AutoSafeCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Steps.Count; i++) if (Steps[i].AutoSafe) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// Строит план исправлений по модели.
    ///
    /// Порядок не косметический: сначала то, что мешает напечатать модель
    /// (цикл, пустой обязательный вход), потом утечки, потом советы. Если
    /// поменять порядок, шаги начнут противоречить друг другу.
    ///
    /// План строится ТОЛЬКО когда установлена кошка: без неё правки некому
    /// объяснить, а молча менять код пользователя плагин не имеет права.
    /// </summary>
    public static class Nsg_FixPlanner
    {
        /// <summary>Авто-правки, которые умеет применять полотно блоков.</summary>
        public static readonly string[] AutoFixes = { "graph.cycle", "input.dangling", "resource.leak" };

        public static Nsg_FixPlan Plan(NsgFileModel model, Nsg_BlockLibrary library)
        {
            var plan = new Nsg_FixPlan();
            if (!Nsg_Advanced.Enabled) return plan;
            if (model == null) return plan;

            Nsg_HealthReport report;
            try
            {
                report = Nsg_Health.Analyze(model, library, null);
            }
            catch
            {
                return plan;
            }

            var blocking = new List<Nsg_FixStep>();
            var rest = new List<Nsg_FixStep>();

            for (int i = 0; i < report.Findings.Count; i++)
            {
                var f = report.Findings[i];
                if (f == null || string.IsNullOrEmpty(f.RuleId)) continue;

                string fixId = QuickFixOf(f.RuleId);
                var step = new Nsg_FixStep
                {
                    QuickFixId = fixId,
                    RuleId = f.RuleId,
                    TitleKey = fixId != null ? TitleKeyOf(fixId) : null,
                    Message = f.Message,
                    MethodName = f.MethodName,
                    NodeId = f.NodeId,
                    AutoSafe = fixId != null,
                    Blocking = fixId == "graph.cycle" || fixId == "input.dangling",
                    Severity = f.Severity
                };

                if (step.AutoSafe && step.Blocking) blocking.Add(step);
                else rest.Add(step);

                if (!step.AutoSafe) plan.Manual++;
            }

            // Тот же порядок, что у авто-правок: он же порядок применения.
            for (int i = 0; i < AutoFixes.Length; i++)
            {
                for (int k = 0; k < blocking.Count; k++)
                {
                    if (blocking[k].QuickFixId == AutoFixes[i]) plan.Steps.Add(blocking[k]);
                }
            }
            for (int i = 0; i < rest.Count; i++) plan.Steps.Add(rest[i]);

            return plan;
        }

        static string QuickFixOf(string ruleId)
        {
            for (int i = 0; i < AutoFixes.Length; i++)
            {
                if (AutoFixes[i] == ruleId) return ruleId;
            }
            return null;
        }

        static string TitleKeyOf(string fixId)
        {
            switch (fixId)
            {
                case "graph.cycle": return "health.fixBreakLink";
                case "input.dangling": return "health.fixFillZero";
                case "resource.leak": return "health.fixAddRelease";
            }
            return null;
        }
    }
}
