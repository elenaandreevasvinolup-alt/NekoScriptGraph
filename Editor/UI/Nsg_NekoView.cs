using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Панель кошки: фон под время суток, портрет сверху, реплика снизу.
    ///
    /// Один и тот же элемент используется в двух местах: в зарезервированном
    /// слоте главного окна (справа внизу) и внизу окна проблем. Логика
    /// разговора живёт здесь, поэтому поведение везде одинаковое.
    ///
    /// Без установленного ProgramNeko панель не занимает места вообще —
    /// style.display = None, и родитель выглядит как раньше.
    /// </summary>
    public class Nsg_NekoView : VisualElement
    {
        /// <summary>
        /// Интервал тика. Дважды в секунду: счётчики копятся, а решение
        /// «говорить или молчать» принимает сама кошка. Стоимость — два
        /// вызова в секунду, ничего заметного.
        /// </summary>
        public const long TickMs = 2000;

        /// <summary>Ниже этого портрет не читается, поэтому панель не даёт себя сжать.</summary>
        const float ArtMinHeight = 150f;

        /// <summary>
        /// Кроссфейд при смене настроения: лицо «перетекает», а не щёлкает.
        /// </summary>
        const long MoodFadeMs = 220;

        /// <summary>
        /// Кроссфейд при моргании — короткий, но НЕ мгновенный.
        ///
        /// Мгновенная подмена выглядит как сбой, если кадры хоть чуть-чуть не
        /// совпали (а у нарисованных отдельно они не совпадают). Быстрое
        /// перетекание это скрывает и читается как движение века.
        /// </summary>
        const long BlinkCloseMs = 45;

        /// <summary>Открытие чуть медленнее закрытия: так ведёт себя живое веко.</summary>
        const long BlinkOpenMs = 70;

        /// <summary>Смена фона под время суток: редко, поэтому можно плавно.</summary>
        const long BackgroundFadeMs = 700;

        /// <summary>
        /// Высота плашки с репликой. Одно число на два места: сама плашка и
        /// нижняя граница фона. Разъедутся — фон снова начнёт залезать под
        /// текст или над ним появится полоса пустоты.
        /// </summary>
        const float TextPlateHeight = 92f;

        /// <summary>
        /// Проявление новой реплики. Короткое намеренно: текст — это смысл, и
        /// задерживать его ради красоты нельзя. Достаточно, чтобы фраза не
        /// щёлкала, но читать она начинает сразу.
        /// </summary>
        const long TextFadeMs = 140;

        /// <summary>Профиль моргания под настроение: интервал и длительность закрытия.</summary>
        struct BlinkProfile
        {
            public int MinMs;
            public int MaxMs;
            public long HoldMs;
        }

        VisualElement _artHost;
        VisualElement _artBase;
        VisualElement _artA;
        VisualElement _artB;
        VisualElement _bgA;
        VisualElement _bgB;
        VisualElement _textBox;
        Label _textA;
        Label _textB;
        Label _placeholder;

        /// <summary>Какой из двух слоёв реплики сейчас сверху.</summary>
        bool _textFrontIsA = true;

        /// <summary>Что написано сейчас: чтобы не переписывать одно и то же.</summary>
        string _textShown;

        /// <summary>Какой из двух слоёв портрета сейчас сверху.</summary>
        bool _frontIsA = true;

        /// <summary>Какой из двух слоёв фона сейчас сверху.</summary>
        bool _bgFrontIsA = true;

        /// <summary>Что показано прямо сейчас: нужно, чтобы не мигать одним и тем же.</summary>
        Texture2D _shown;

        /// <summary>Что лежит на нижнем слое (текущий портрет в полную яркость).</summary>
        Texture2D _baseShown;

        /// <summary>Фон, показанный сейчас. Смена суток сравнивается с ним.</summary>
        Texture2D _bgShown;

        readonly System.Random _rng = new System.Random();

        /// <summary>Панель построена, кошка найдена.</summary>
        public bool Active { get; private set; }

        public Nsg_NekoView()
        {
            var neko = Nsg_NekoRegistry.Assistant;
            if (neko == null)
            {
                // Кошки нет — слот остаётся пустым, как и был.
                style.display = DisplayStyle.None;
                return;
            }

            Active = true;
            Build(neko);
        }

        void Build(INsg_NekoAssistant neko)
        {
            style.flexGrow = 1;

            // Слот может быть и растянут, и сжат: минимальная высота не даёт
            // панели схлопнуться в полоску, если пользователь утащил разделитель.
            style.minHeight = ArtMinHeight + 96f;

            style.borderTopWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;
            style.borderRightWidth = 1;
            style.borderTopColor = new Color(0.28f, 0.30f, 0.34f);
            style.borderBottomColor = new Color(0.28f, 0.30f, 0.34f);
            style.borderLeftColor = new Color(0.28f, 0.30f, 0.34f);
            style.borderRightColor = new Color(0.28f, 0.30f, 0.34f);
            Nsg_Visual.Round(this, 4);
            style.overflow = Overflow.Hidden;

            // Базовый цвет: пока картинки фона нет, панель не должна быть
            // прозрачной дырой в окне.
            style.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

            // Фон — тоже двумя слоями: смена суток должна перетекать, а не
            // щёлкать. Рассвет/день/ночь меняются редко, поэтому переход
            // можно сделать заметно длиннее, чем у лица.
            //
            // Нижняя граница — по верх плашки с репликой. Раньше фон занимал
            // всю панель, и плашка резала картинку пополам: нижняя половина
            // фигуры торчала из-под текста. Теперь картинка вписывается ровно
            // в видимую часть, и резать нечего.
            _bgA = MakeArtLayer(BackgroundSizeType.Cover);
            _bgB = MakeArtLayer(BackgroundSizeType.Cover);
            _bgA.style.bottom = TextPlateHeight;
            _bgB.style.bottom = TextPlateHeight;
            _bgA.style.opacity = 0f;
            _bgB.style.opacity = 0f;
            Add(_bgA);
            Add(_bgB);

            // Портрет занимает всё свободное место: именно ради него кошке
            // и отдана отдельная колонка.
            _artHost = new VisualElement();
            _artHost.style.flexGrow = 1;
            _artHost.style.flexShrink = 1;
            _artHost.style.minHeight = ArtMinHeight;
            _artHost.style.marginTop = 4;
            _artHost.style.marginLeft = 4;
            _artHost.style.marginRight = 4;
            _artHost.pickingMode = PickingMode.Ignore;
            Add(_artHost);

            // Нижний слой — «подложка»: всегда текущий портрет в полную
            // яркость. Кроссфейд из двух полупрозрачных кадров на середине
            // перехода перекрывает друг друга лишь частично, и в просвет
            // пробивался тёмный фон панели — моргание читалось как вспышка
            // черноты, а расходящиеся края кадров давали тёмный контур.
            // Подложка закрывает этот просвет и держит яркость.
            _artBase = MakeArtLayer(BackgroundSizeType.Contain);
            _artHost.Add(_artBase);

            // Два слоя друг над другом: смена настроения идёт кроссфейдом.
            // backgroundImage в UI Toolkit не анимируется, а opacity — да,
            // поэтому «плавно» здесь означает «перекрыть вторым слоем».
            _artA = MakeArtLayer(BackgroundSizeType.Contain);
            _artB = MakeArtLayer(BackgroundSizeType.Contain);
            _artHost.Add(_artA);
            _artHost.Add(_artB);

            // Реплика — фиксированная полоса внизу: расти должна картинка,
            // а не текст.
            //
            // Плашка НЕПРОЗРАЧНАЯ. Фон окна — картинка во всю панель, и
            // полупрозрачная полоса резала её пополам: нижняя половина фигуры
            // просвечивала сквозь текст и читалась как обрезанное тело.
            // Цвет взят из хрома панели, чтобы полоса выглядела частью
            // интерфейса, а не чёрной заплатой.
            //
            // Внутри тоже два слоя: новая фраза проявляется поверх старой,
            // а не подменяет её рывком. Плавность здесь короткая — текст
            // читают, а не разглядывают, и задерживать его нельзя.
            _textBox = new VisualElement();
            _textBox.style.flexGrow = 0;
            _textBox.style.flexShrink = 0;
            _textBox.style.height = TextPlateHeight;
            _textBox.style.backgroundColor = new Color(0.11f, 0.12f, 0.15f, 1f);
            _textBox.style.overflow = Overflow.Hidden;
            Add(_textBox);

            _textA = MakeTextLayer();
            _textB = MakeTextLayer();
            _textBox.Add(_textA);
            _textBox.Add(_textB);

            RefreshArt(neko);
            RefreshBackground(neko);
            ScheduleBlink(BlinkFor(Nsg_NekoMood.Idle));

            // Тик: кошка решает, есть ли повод заговорить, а заодно проверяется,
            // не сменились ли сутки за окном.
            schedule.Execute(() =>
            {
                var now = Nsg_NekoRegistry.Assistant;
                if (now != null) RefreshBackground(now);
                Observe(Nsg_NekoEvent.Tick, 0);
            }).Every(TickMs);
        }

        /// <summary>
        /// Слой картинки. Их всегда два, чтобы работал кроссфейд:
        /// backgroundImage в UI Toolkit не анимируется, а opacity — да.
        /// </summary>
        static VisualElement MakeArtLayer(BackgroundSizeType fit)
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            e.style.left = 0f;
            e.style.top = 0f;
            e.style.right = 0f;
            e.style.bottom = 0f;
            e.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(fit));
            e.style.transitionProperty = new List<StylePropertyName> { "opacity" };
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>Слой реплики: две штуки, чтобы текст проявлялся, а не щёлкал.</summary>
        static Label MakeTextLayer()
        {
            var e = new Label(string.Empty);
            e.style.position = Position.Absolute;
            e.style.left = 0f;
            e.style.top = 0f;
            e.style.right = 0f;
            e.style.bottom = 0f;
            e.style.whiteSpace = WhiteSpace.Normal;
            e.style.fontSize = 11;
            e.style.paddingLeft = 8;
            e.style.paddingRight = 8;
            e.style.paddingTop = 4;
            e.style.paddingBottom = 6;
            e.style.unityTextAlign = Nsg_Rtl.TextAlign;
            e.style.color = new Color(0.94f, 0.95f, 0.97f);
            e.style.transitionProperty = new List<StylePropertyName> { "opacity" };
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>Показать реплику. Проявляется поверх предыдущей.</summary>
        void ShowText(string text)
        {
            if (string.IsNullOrEmpty(text) || text == _textShown) return;
            if (_textA == null || _textB == null) return;

            var front = _textFrontIsA ? _textA : _textB;
            var back = _textFrontIsA ? _textB : _textA;

            back.text = text;

            var duration = new List<TimeValue>
            {
                new TimeValue((float)TextFadeMs, TimeUnit.Millisecond)
            };
            back.style.transitionDuration = duration;
            front.style.transitionDuration = duration;

            back.style.opacity = 1f;
            front.style.opacity = 0f;

            _textFrontIsA = !_textFrontIsA;
            _textShown = text;
        }

        // ------------------------------------------------------------------
        // Показ картинки
        // ------------------------------------------------------------------

        /// <summary>
        /// Нижний слой: текущий портрет в полную яркость, без перехода.
        ///
        /// Кроссфейд двух кадров с альфой на середине перехода даёт всего 75%
        /// покрытия, и оставшиеся 25% просвечивают тёмным фоном панели. На
        /// расходящихся краях кадров это тем более заметно. Подложка держит
        /// полную яркость и убирает чёрную вспышку при моргании.
        /// </summary>
        void SetBaseArt(Texture2D tex)
        {
            if (_artBase == null || tex == null || tex == _baseShown) return;

            _artBase.style.backgroundImage = new StyleBackground(tex);
            _artBase.style.opacity = 1f;
            _baseShown = tex;
        }

        /// <summary>
        /// Показать портрет. fadeMs = 0 — мгновенная подмена (моргание),
        /// иначе кроссфейд (смена настроения).
        /// </summary>
        void ShowPortrait(Texture2D tex, long fadeMs)
        {
            if (tex == null || tex == _shown) return;
            if (_artA == null || _artB == null) return;

            var front = _frontIsA ? _artA : _artB;
            var back = _frontIsA ? _artB : _artA;

            back.style.backgroundImage = new StyleBackground(tex);

            var duration = new List<TimeValue>
            {
                new TimeValue((float)fadeMs, TimeUnit.Millisecond)
            };
            back.style.transitionDuration = duration;
            front.style.transitionDuration = duration;

            back.style.opacity = 1f;
            front.style.opacity = 0f;

            _frontIsA = !_frontIsA;
            _shown = tex;

            if (_placeholder != null)
            {
                _placeholder.RemoveFromHierarchy();
                _placeholder = null;
            }
        }

        /// <summary>
        /// Фон под текущее время суток. Меняется редко, поэтому переход
        /// длинный: смена рассвета на день должна читаться как «за окном
        /// стемнело», а не как подмена картинки.
        /// </summary>
        void RefreshBackground(INsg_NekoAssistant neko)
        {
            if (_bgA == null || _bgB == null || neko == null) return;

            var bg = neko.Background;
            if (bg == null || bg == _bgShown) return;

            var front = _bgFrontIsA ? _bgA : _bgB;
            var back = _bgFrontIsA ? _bgB : _bgA;

            back.style.backgroundImage = new StyleBackground(bg);

            var duration = new List<TimeValue>
            {
                new TimeValue((float)BackgroundFadeMs, TimeUnit.Millisecond)
            };
            back.style.transitionDuration = duration;
            front.style.transitionDuration = duration;

            back.style.opacity = 1f;
            front.style.opacity = 0f;

            _bgFrontIsA = !_bgFrontIsA;
            _bgShown = bg;
        }

        // ------------------------------------------------------------------
        // Моргание
        // ------------------------------------------------------------------

        /// <summary>
        /// Профиль моргания под настроение.
        ///
        /// Разная частота — это характер: встревоженная кошка моргает часто и
        /// коротко, сонная — редко и долго. Одинаковый ритм у всех настроений
        /// сразу выдавал бы автомат.
        /// </summary>
        static BlinkProfile BlinkFor(Nsg_NekoMood mood)
        {
            switch (mood)
            {
                case Nsg_NekoMood.Curious:
                    return new BlinkProfile { MinMs = 2000, MaxMs = 5000, HoldMs = 100 };
                case Nsg_NekoMood.Happy:
                    return new BlinkProfile { MinMs = 3000, MaxMs = 7000, HoldMs = 120 };
                case Nsg_NekoMood.Worried:
                    // Тревога: часто и коротко.
                    return new BlinkProfile { MinMs = 1400, MaxMs = 3400, HoldMs = 90 };
                case Nsg_NekoMood.Thinking:
                    return new BlinkProfile { MinMs = 3500, MaxMs = 8000, HoldMs = 170 };
                case Nsg_NekoMood.Sleepy:
                    // Сон: редко и долго, веко опускается медленнее.
                    return new BlinkProfile { MinMs = 4000, MaxMs = 9000, HoldMs = 330 };
            }
            return new BlinkProfile { MinMs = 2600, MaxMs = 6200, HoldMs = 110 };
        }

        void ScheduleBlink(BlinkProfile profile)
        {
            if (!Active) return;

            // Разброс обязателен: ровный ритм моргания сразу выдаёт автомат.
            int span = profile.MaxMs - profile.MinMs;
            int delay = profile.MinMs + (span > 0 ? _rng.Next(span) : 0);
            schedule.Execute(Blink).ExecuteLater(delay);
        }

        void Blink()
        {
            var neko = Nsg_NekoRegistry.Assistant;
            if (!Active || neko == null || panel == null) return;

            var profile = BlinkFor(neko.Mood);

            var blink = neko.Blink;
            if (blink == null)
            {
                // Кадра моргания нет — это не ошибка, просто кошка не моргает.
                // Пробуем позже: художник может положить файл в любой момент.
                ScheduleBlink(profile);
                return;
            }

            // Короткое перетекание, а не мгновенная подмена. Кадры нарисованы
            // отдельно и попиксельно не совпадают: резкий щелчок выдал бы это
            // как дёрганье всей картинки, а быстрый переход — нет.
            ShowPortrait(blink, BlinkCloseMs);

            schedule.Execute(() =>
            {
                var now = Nsg_NekoRegistry.Assistant;
                if (now != null) ShowPortrait(now.Portrait, BlinkOpenMs);

                ScheduleBlink(BlinkFor(now != null ? now.Mood : Nsg_NekoMood.Idle));
            }).ExecuteLater(profile.HoldMs);
        }

        // ------------------------------------------------------------------
        // Речь
        // ------------------------------------------------------------------

        public void Greet()
        {
            Say("greet", Nsg_NekoMood.Idle);
        }

        public void Say(string lineKey, Nsg_NekoMood mood)
        {
            var neko = Nsg_NekoRegistry.Assistant;
            if (!Active || neko == null || _textBox == null) return;

            string text = neko.Say(lineKey, mood);
            if (string.IsNullOrEmpty(text)) return;

            neko.Mood = mood;
            ShowText(text);
            RefreshArt(neko);
        }

        /// <summary>
        /// Объяснение проблемы. Сначала короткая реплика «думаю», потом ответ:
        /// без этой паузы ответ выглядит мгновенной подстановкой строки, и
        /// живость пропадает.
        /// </summary>
        public void Ask(NsgDiagnostic d, string csPath)
        {
            var neko = Nsg_NekoRegistry.Assistant;
            if (!Active || neko == null || d == null) return;

            var fact = Nsg_NekoFacts.From(d, csPath);
            if (fact == null) return;

            Say("thinking", Nsg_NekoMood.Thinking);

            schedule.Execute(() =>
            {
                if (_textBox == null) return;

                string text = neko.Explain(fact);
                if (string.IsNullOrEmpty(text))
                {
                    Say("noIdea", Nsg_NekoMood.Worried);
                    return;
                }

                ShowText(text);
                RefreshArt(neko);
            }).ExecuteLater(220);
        }

        /// <summary>
        /// Сообщает кошке о событии редактора. Реплику выбирает она сама:
        /// если возвращается null, кошка промолчала.
        /// </summary>
        public void Observe(Nsg_NekoEvent evt, int value)
        {
            var neko = Nsg_NekoRegistry.Assistant;
            if (!Active || neko == null || _textBox == null) return;
            if (panel == null) return;

            string text = neko.Observe(evt, value);
            if (string.IsNullOrEmpty(text)) return;

            ShowText(text);
            RefreshArt(neko);
        }

        /// <summary>Объяснить логику блока (вызывается из меню блока).</summary>
        public void ExplainBlock(NsgMethodGraph graph, NsgGraphNode node, Nsg_BlockLibrary lib)
        {
            var neko = Nsg_NekoRegistry.Assistant;
            if (!Active || neko == null || _textBox == null) return;

            var block = Nsg_NekoFacts.FromBlock(graph, node, lib);
            if (block == null) return;

            Say("thinking", Nsg_NekoMood.Thinking);

            schedule.Execute(() =>
            {
                if (_textBox == null) return;

                string text = neko.ExplainBlock(block);
                if (string.IsNullOrEmpty(text))
                {
                    Say("noIdea", Nsg_NekoMood.Worried);
                    return;
                }

                ShowText(text);
                RefreshArt(neko);
            }).ExecuteLater(220);
        }

        void RefreshArt(INsg_NekoAssistant neko)
        {
            if (_artHost == null || neko == null) return;

            var portrait = neko.Portrait;
            if (portrait != null)
            {
                // Подложка меняется без перехода: пока верхние слои в полную
                // яркость, она всё равно скрыта, а в момент кроссфейда именно
                // она не даёт картинке потемнеть.
                SetBaseArt(portrait);

                // Смена настроения — кроссфейдом: лицо «перетекает», а не
                // щёлкает. Резкая подмена читалась бы как сбой отрисовки.
                ShowPortrait(portrait, MoodFadeMs);
                return;
            }

            // Портрета ещё нет — заглушка, чтобы панель не выглядела сломанной.
            if (_placeholder == null)
            {
                _placeholder = new Label(Nsg_L10n.T("neko.noArt"));
                _placeholder.style.fontSize = 10;
                _placeholder.style.color = new Color(0.55f, 0.58f, 0.64f);
                _placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
                _placeholder.style.position = Position.Absolute;
                _placeholder.style.left = 0f;
                _placeholder.style.top = 0f;
                _placeholder.style.right = 0f;
                _placeholder.style.bottom = 0f;
                _artHost.Add(_placeholder);
            }
        }
    }
}
