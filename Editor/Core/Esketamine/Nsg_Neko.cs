using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>Настроение кошки. Влияет на подбор реплик и (позже) на анимацию.</summary>
    public enum Nsg_NekoMood
    {
        Idle,
        Curious,
        Happy,
        Worried,
        Thinking,
        Sleepy
    }

    /// <summary>
    /// Время суток: от него зависит фон окна.
    ///
    /// Sunset отделён от Night намеренно: вечер и глубокая ночь читаются
    /// по-разному, и на вечер хочется свою картинку. Пока её нет, берётся
    /// рассветная — см. Neko_Assistant.Background.
    /// </summary>
    public enum Nsg_NekoDayPhase
    {
        Sunrise,
        Day,
        Sunset,
        Night
    }

    /// <summary>
    /// Событие редактора, о котором кошка узнаёт.
    ///
    /// Ядро только СООБЩАЕТ о том, что произошло, и не решает, стоит ли
    /// говорить. Решение принимает кошка (Neko_Brain): только она знает, что
    /// считать «застрял» или «давно не было ошибок».
    /// </summary>
    public enum Nsg_NekoEvent
    {
        /// <summary>Тик. Кошка сама решает, есть ли повод заговорить.</summary>
        Tick,

        /// <summary>Указатель двигается. value — сколько движений с прошлого раза.</summary>
        PointerMove,

        /// <summary>Модель изменилась: пользователь что-то сделал.</summary>
        Edit,

        /// <summary>Сменилось выделение.</summary>
        Selection,

        /// <summary>Изменилось число диагностик. value — сколько их стало.</summary>
        Diagnostics,

        /// <summary>Окно потеряло фокус.</summary>
        Blur
    }

    /// <summary>
    /// Логика одного блока, вычитанная из графа.
    ///
    /// Описывается ИМЕННО ЛОГИКА — «условие x &gt; 0, внутри две ветки», — а не
    /// смысл. Смысл требует понимания задачи, а это уже языковая модель,
    /// которой здесь сознательно нет.
    /// </summary>
    public class Nsg_NekoBlock
    {
        /// <summary>Вид конструкции: if / loop / assign / call / value / raw / other.</summary>
        public string Shape;

        /// <summary>Подпись блока с подставленными аргументами. Это и есть логика.</summary>
        public string Detail;

        /// <summary>Сколько аргументов заполнено.</summary>
        public int Args;

        /// <summary>Сколько операторов внутри веток.</summary>
        public int Body;

        /// <summary>
        /// Сколько обязательных входов пусто. Именно это делает описание
        /// неполным, и кошка честно об этом говорит.
        /// </summary>
        public int Missing;

        /// <summary>Прочитать нельзя: сырой фрагмент или неизвестный блок.</summary>
        public bool Opaque;

        // ------------------------------------------------------------------
        // Дерево вложенных блоков
        // ------------------------------------------------------------------

        /// <summary>
        /// Разобранные вложенные блоки: тело ветки, ветка else и блоки в
        /// слотах-значениях.
        ///
        /// Раньше их не было вовсе — блок описывался только счётом «внутри N
        /// штук», и кошка не могла сказать, что именно внутри. Дерево строится
        /// из тех же трёх указателей графа (body / els / args[i].link), поэтому
        /// его автоматически получает любой язык, любой блок и любой API-блок.
        /// </summary>
        public List<Nsg_NekoBlock> Children;

        /// <summary>Откуда пришёл блок: body / else / value. null у корня.</summary>
        public string ChildKind;

        /// <summary>Сколько вложенных блоков всего.</summary>
        public int Total;

        /// <summary>Сколько из них разобрано и лежит в Children.</summary>
        public int Shown;

        /// <summary>Сколько осталось неразобранными. Больше нуля — кошка
        /// честно говорит «дальше я устала» и просит выбрать остаток.</summary>
        public int Rest;

        /// <summary>
        /// Логический разбор: при каком условии и что меняется. Заполняется
        /// для конструкций, у которых есть условие или цель; null — разбирать
        /// нечего (сырой фрагмент, неизвестный блок).
        /// </summary>
        public Nsg_NekoLogic Logic;
    }

    /// <summary>
    /// Факт, который кошка пересказывает своими словами.
    ///
    /// Технические подробности УЖЕ локализованы ядром (Message приходит из
    /// NsgDiagnostic), поэтому кошка их не переводит — она только оборачивает
    /// их в свою интонацию. Именно поэтому языковой пакет кошки в разы меньше
    /// основной таблицы: техническую часть он не дублирует.
    /// </summary>
    public class Nsg_NekoFact
    {
        /// <summary>Код диагностики, NSG0001..NSG0009.</summary>
        public string Code;

        /// <summary>Имя файла без пути. null, если файл неизвестен.</summary>
        public string Script;

        public int Line;
        public int Col;

        /// <summary>Локализованное ядром описание проблемы.</summary>
        public string Message;

        /// <summary>Ключ «чем это грозит». Текст живёт в языковом пакете кошки.</summary>
        public string Impact;

        /// <summary>Ключ «что делать». Текст живёт в языковом пакете кошки.</summary>
        public string Fix;
    }

    /// <summary>
    /// Контракт кошки. Реализация живёт в ProgramNeko/ и находится рефлексией,
    /// поэтому ядро НИКОГДА не ссылается на её типы: удаление папки ProgramNeko
    /// не ломает компиляцию плагина.
    ///
    /// Тот же приём, что и у устанавливаемых языков (см. Nsg_LanguageRegistry).
    /// </summary>
    public interface INsg_NekoAssistant
    {
        string Id { get; }
        string DisplayName { get; }

        /// <summary>Есть ли в языковом пакете объяснение для этого кода.</summary>
        bool CanExplain(string code);

        /// <summary>Готовая реплика. null — объяснить нечего, вызывающий показывает сырой текст.</summary>
        string Explain(Nsg_NekoFact fact);

        /// <summary>
        /// Объяснить ЛОГИКУ блока. null — сказать нечего.
        /// </summary>
        string ExplainBlock(Nsg_NekoBlock block);

        /// <summary>
        /// Событие редактора. Возвращает реплику, если кошке есть что сказать,
        /// иначе null. Решение «говорить или молчать» принимает кошка: ядро
        /// лишь сообщает, что произошло.
        /// </summary>
        string Observe(Nsg_NekoEvent evt, int value);

        /// <summary>
        /// Свободная реплика по событию: greet, idle, react.error, react.success,
        /// thinking, react.longIdle... Возвращает null, если реплики нет.
        /// </summary>
        string Say(string lineKey, Nsg_NekoMood mood);

        Nsg_NekoMood Mood { get; set; }

        /// <summary>Время суток для фона.</summary>
        Nsg_NekoDayPhase DayPhase { get; }

        /// <summary>
        /// Фон окна под время суток. null — картинки нет, вызывающий рисует
        /// заглушку. Текстуру отдаёт кошка: ядро не знает, откуда она взялась.
        /// </summary>
        Texture2D Background { get; }

        /// <summary>
        /// Портрет кошки для текущего настроения. null — картинки нет.
        /// Позже сюда встанет многослойная кукла с деформацией.
        /// </summary>
        Texture2D Portrait { get; }

        /// <summary>
        /// Кадр с закрытыми глазами ДЛЯ ТЕКУЩЕГО настроения. null — моргания
        /// не будет, и это нормально: без кадра кошка просто не моргает.
        ///
        /// Кадр на каждое настроение, а не один общий: удивлённая кошка и
        /// сонная закрывают глаза по-разному, и общий кадр выдавал бы подмену.
        ///
        /// Отдельный кадр, а не слой глаз: подмена целой картинки не требует
        /// ни вырезания глазниц, ни знания координат — художник делает его тем
        /// же инструментом, что и остальные настроения.
        /// </summary>
        Texture2D Blink { get; }
    }

    /// <summary>
    /// Время суток по системным часам. Границы намеренно грубые: это фон окна,
    /// а не астрономия.
    /// </summary>
    public static class Nsg_NekoClock
    {
        public const int SunriseFrom = 5;   // 05:00 — рассвет
        public const int DayFrom = 9;       // 09:00 — день
        public const int SunsetFrom = 18;   // 18:00 — закат
        public const int NightFrom = 20;    // 20:00 — ночь

        public static Nsg_NekoDayPhase Phase(DateTime now)
        {
            int h = now.Hour;
            if (h < SunriseFrom) return Nsg_NekoDayPhase.Night;     // 00–04
            if (h < DayFrom) return Nsg_NekoDayPhase.Sunrise;       // 05–08
            if (h < SunsetFrom) return Nsg_NekoDayPhase.Day;        // 09–17
            if (h < NightFrom) return Nsg_NekoDayPhase.Sunset;      // 18–19
            return Nsg_NekoDayPhase.Night;                          // 20–23
        }

        public static Nsg_NekoDayPhase Now
        {
            get { return Phase(DateTime.Now); }
        }
    }

    /// <summary>
    /// Сопоставление кодов диагностики с «чем грозит» и «что делать».
    ///
    /// Ядро знает семантику своих кодов, поэтому ключи выдаёт оно. Текст по
    /// этим ключам берётся из языкового пакета кошки — так формулировку можно
    /// менять, не трогая C#.
    /// </summary>
    public static class Nsg_NekoFacts
    {
        public static string ImpactOf(string code)
        {
            switch (code)
            {
                case NsgCodes.UnknownBlock: return "impact.blockMissing";
                case NsgCodes.OutOfSubset: return "impact.notConverted";
                case NsgCodes.Ambiguous: return "impact.wrongBlock";
                case NsgCodes.Unresolved: return "impact.wrongSymbol";
                case NsgCodes.Unconvertible: return "impact.methodSkipped";
                case NsgCodes.MetadataLoss: return "impact.metadataLost";
                case NsgCodes.HashMismatch: return "impact.overwrite";
                case NsgCodes.Internal: return "impact.pluginBroken";
            }
            return null;
        }

        public static string FixOf(string code)
        {
            switch (code)
            {
                case NsgCodes.UnknownBlock: return "fix.reloadLibrary";
                case NsgCodes.OutOfSubset: return "fix.rawBlock";
                case NsgCodes.Ambiguous: return "fix.pickForm";
                case NsgCodes.Unresolved: return "fix.declareSymbol";
                case NsgCodes.Unconvertible: return "fix.splitMethod";
                case NsgCodes.MetadataLoss: return "fix.manualEdit";
                case NsgCodes.HashMismatch: return "fix.importOrRestore";
                case NsgCodes.Internal: return "fix.reportBug";
            }
            return null;
        }

        public static Nsg_NekoFact From(NsgDiagnostic d, string csPath)
        {
            if (d == null) return null;

            return new Nsg_NekoFact
            {
                Code = d.Code,
                Script = string.IsNullOrEmpty(csPath)
                    ? null
                    : System.IO.Path.GetFileName(csPath),
                Line = d.Line,
                Col = d.Col,
                Message = d.Message,
                Impact = ImpactOf(d.Code),
                Fix = FixOf(d.Code)
            };
        }

        // ------------------------------------------------------------------
        // Логика блока
        // ------------------------------------------------------------------

        /// <summary>Предел обхода веток: глубже кошка не заглядывает.</summary>
        const int BlockDepthLimit = 3;

        /// <summary>
        /// Сколько блоков кошка согласна разобрать за один раз.
        ///
        /// Это защита от «накормить кошку деревом»: большой метод содержит
        /// сотни блоков, и пересказ всех — это и длинная реплика, и заметная
        /// работа на каждый клик. Остаток не теряется: он попадает в
        /// <see cref="Nsg_NekoBlock.Rest"/>, и кошка просит выбрать его
        /// отдельно, вместо того чтобы молча оборваться.
        /// </summary>
        public const int ExplainBudget = 8;

        /// <summary>Сколько детей одного блока показывать. Тот же смысл, что у
        /// бюджета, но на уровень ниже: иначе один узел с двадцатью входами
        /// съел бы весь бюджет.</summary>
        public const int MaxChildrenPerBlock = 6;

        /// <summary>
        /// Вычитывает логику блока из графа вместе с вложенными блоками.
        ///
        /// Описывается именно ЛОГИКА: что за конструкция, что стоит в её
        /// входах, сколько операторов внутри. Смысл («зачем это нужно»)
        /// не выводится — для него нужна языковая модель.
        /// </summary>
        public static Nsg_NekoBlock FromBlock(NsgMethodGraph g, NsgGraphNode node, Nsg_BlockLibrary lib)
        {
            var budget = new int[1] { ExplainBudget };
            var seen = new HashSet<string>();
            return FromBlock(g, node, lib, budget, seen, 0, null);
        }

        /// <summary>
        /// Разбор одного узла. <paramref name="seen"/> защищает от кольца в
        /// графе: без него повреждённый next/body зациклил бы рекурсию, а
        /// существующий CountChain такую защиту уже имеет — её нельзя терять.
        /// </summary>
        static Nsg_NekoBlock FromBlock(NsgMethodGraph g, NsgGraphNode node, Nsg_BlockLibrary lib,
                                       int[] budget, HashSet<string> seen, int depth, string childKind)
        {
            if (node == null) return null;

            // Один узел в дереве описывается один раз. Повторная ссылка не
            // ошибка (значение можно переиспользовать), но и рассказывать про
            // него второй раз нечего.
            if (!string.IsNullOrEmpty(node.id) && !seen.Add(node.id)) return null;

            var def = lib != null ? lib.Get(node.block) : null;
            var b = new Nsg_NekoBlock();
            b.ChildKind = childKind;

            if (def == null)
            {
                // Блок неизвестен: честно говорим, что читать нечего.
                b.Shape = "unknown";
                b.Opaque = true;
                b.Children = new List<Nsg_NekoBlock>();
                return b;
            }

            b.Shape = ShapeOf(def);
            b.Detail = DetailOf(def, node);
            b.Args = CountFilled(node.args);
            b.Body = CountChain(g, node.body, 0) + CountChain(g, node.els, 0);
            b.Missing = CountMissing(node, def);
            b.Opaque = b.Shape == "raw" || b.Shape == "unknown";

            // Логический разбор считается для самого блока, а не для детей:
            // он отвечает на вопрос «что делает эта конструкция», и вложенные
            // блоки получат свой при обходе дерева.
            b.Logic = Nsg_NekoLogicReader.Read(g, node, lib);

            b.Children = new List<Nsg_NekoBlock>();
            CollectChildren(g, node, lib, b, budget, seen, depth);
            b.Shown = b.Children.Count;
            b.Rest = b.Total - b.Shown;
            if (b.Rest < 0) b.Rest = 0;

            return b;
        }

        /// <summary>Вложенные блоки: сначала ветки, потом значения.</summary>
        static void CollectChildren(NsgMethodGraph g, NsgGraphNode node, Nsg_BlockLibrary lib,
                                    Nsg_NekoBlock b, int[] budget, HashSet<string> seen, int depth)
        {
            AddChain(g, node.body, "body", lib, b, budget, seen, depth);
            AddChain(g, node.els, "else", lib, b, budget, seen, depth);

            if (node.args == null) return;
            for (int i = 0; i < node.args.Count; i++)
            {
                var s = node.args[i];
                if (s == null || string.IsNullOrEmpty(s.link)) continue;
                AddChild(g, s.link, "value", lib, b, budget, seen, depth);
            }
        }

        static void AddChain(NsgMethodGraph g, string head, string kind, Nsg_BlockLibrary lib,
                             Nsg_NekoBlock b, int[] budget, HashSet<string> seen, int depth)
        {
            if (g == null || string.IsNullOrEmpty(head)) return;

            string cur = head;
            var guard = new HashSet<string>();
            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                AddChild(g, cur, kind, lib, b, budget, seen, depth);

                var n = g.Find(cur);
                if (n == null) break;
                cur = n.next;
            }
        }

        /// <summary>
        /// Добавляет ребёнка. Всегда считает его в Total, но разбирает только
        /// если хватает бюджета, места и глубины: остаток виден как Rest, и
        /// кошка говорит о нём отдельно.
        /// </summary>
        static void AddChild(NsgMethodGraph g, string id, string kind, Nsg_BlockLibrary lib,
                             Nsg_NekoBlock b, int[] budget, HashSet<string> seen, int depth)
        {
            var n = g != null ? g.Find(id) : null;
            if (n == null) return;

            b.Total++;

            if (b.Children.Count >= MaxChildrenPerBlock) return;
            if (budget[0] <= 0) return;
            if (depth + 1 > BlockDepthLimit) return;

            budget[0]--;
            var child = FromBlock(g, n, lib, budget, seen, depth + 1, kind);
            if (child != null) b.Children.Add(child);
        }

        /// <summary>Вид конструкции. От него зависит, каким шаблоном кошка расскажет.</summary>
        static string ShapeOf(NsgBlockDef def)
        {
            if (def == null) return "unknown";

            switch (def.id)
            {
                case "stmt.if": return "if";
                case "stmt.while":
                case "stmt.for":
                case "stmt.foreach": return "loop";
                case "stmt.raw":
                case "expr.raw": return "raw";
            }

            switch (def.node)
            {
                case "assign": return "assign";
                case "call": return "call";
                case "localDecl": return "declare";
                case "return": return "return";
            }

            if (def.shape == "expression") return "value";
            if (def.IsControl) return "control";
            return "other";
        }

        /// <summary>
        /// Подпись блока с подставленными входами — это и есть описание логики.
        /// Пустой вход даёт «?», вложенный блок — «…»: так видно, где именно
        /// информации не хватает.
        /// </summary>
        /// <summary>Текст блока: подпись с подставленными входами. Публичная —
        /// нужна логическому разбору, который показывает условие и цели.</summary>
        public static string Render(NsgMethodGraph g, string nodeId, Nsg_BlockLibrary lib)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            var n = g != null ? g.Find(nodeId) : null;
            if (n == null) return null;

            var def = lib != null ? lib.Get(n.block) : null;
            return def != null ? DetailOf(def, n) : null;
        }

        public static string DetailOf(NsgBlockDef def, NsgGraphNode node)
        {
            int n = def.SocketCount;
            var values = new string[n];
            for (int i = 0; i < n; i++) values[i] = ArgText(node, i);

            string text = def.RenderLabel(values);
            return string.IsNullOrEmpty(text) ? def.id : text;
        }

        static string ArgText(NsgGraphNode node, int i)
        {
            if (node == null || node.args == null || i < 0 || i >= node.args.Count) return "?";

            var s = node.args[i];
            if (s == null) return "?";
            if (!string.IsNullOrEmpty(s.text)) return s.text;
            if (!string.IsNullOrEmpty(s.link)) return "…";
            return "?";
        }

        static int CountFilled(List<NsgSlot> slots)
        {
            if (slots == null) return 0;

            int n = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null && (!string.IsNullOrEmpty(s.text) || !string.IsNullOrEmpty(s.link))) n++;
            }
            return n;
        }

        /// <summary>Пустые обязательные входы: именно они делают логику неполной.</summary>
        static int CountMissing(NsgGraphNode node, NsgBlockDef def)
        {
            if (node == null || def == null || def.sockets == null) return 0;

            int n = 0;
            for (int i = 0; i < def.sockets.Length; i++)
            {
                var socket = def.sockets[i];
                if (socket == null || !socket.required || socket.kind != "expr") continue;

                var slot = i < node.args.Count ? node.args[i] : null;
                bool filled = slot != null
                    && (!string.IsNullOrEmpty(slot.link) || !string.IsNullOrEmpty(slot.text));
                if (!filled) n++;
            }
            return n;
        }

        /// <summary>Считает операторы в цепочке, не уходя глубже лимита.</summary>
        static int CountChain(NsgMethodGraph g, string head, int depth)
        {
            if (g == null || string.IsNullOrEmpty(head) || depth > BlockDepthLimit) return 0;

            int count = 0;
            string cur = head;
            var guard = new HashSet<string>();

            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;

                count++;
                count += CountChain(g, n.body, depth + 1);
                count += CountChain(g, n.els, depth + 1);
                cur = n.next;
            }
            return count;
        }
    }

    /// <summary>
    /// Поиск установленной кошки. Сканирует сборки рефлексией — ядро не знает
    /// конкретного типа, поэтому ProgramNeko можно удалить целиком.
    /// </summary>
    public static class Nsg_NekoRegistry
    {
        static INsg_NekoAssistant _assistant;
        static bool _scanned;

        /// <summary>Установлена ли кошка. Без неё окно проблем работает как раньше.</summary>
        public static bool Installed
        {
            get { Scan(); return _assistant != null; }
        }

        /// <summary>Установленная кошка или null.</summary>
        public static INsg_NekoAssistant Assistant
        {
            get { Scan(); return _assistant; }
        }

        /// <summary>Сбрасывает кэш: вызывается после установки/удаления папки.</summary>
        public static void Rediscover()
        {
            _scanned = false;
            _assistant = null;
        }

        static void Scan()
        {
            if (_scanned) return;
            _scanned = true;

            var iface = typeof(INsg_NekoAssistant);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    // Часть типов могла не загрузиться — берём те, что загрузились.
                    types = e.Types;
                }
                catch
                {
                    continue;
                }
                if (types == null) continue;

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!iface.IsAssignableFrom(t)) continue;

                    try
                    {
                        var inst = Activator.CreateInstance(t) as INsg_NekoAssistant;
                        if (inst != null)
                        {
                            _assistant = inst;
                            return;
                        }
                    }
                    catch
                    {
                        // Битый конструктор не должен ронять поиск.
                    }
                }
            }
        }
    }
}
