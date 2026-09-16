using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Полотно одного метода в режиме чертежа (UE Blueprints).
    ///
    /// Отличие от стопки: узлы не вложены друг в друга, а стоят в собственных
    /// координатах node.x/node.y, а связи нарисованы проводами.
    ///
    /// Что здесь является чем:
    ///   • execution-провод — next (следующий оператор), body и els (ветки);
    ///   • data-провод      — args[i].link и extra[i].link.
    ///
    /// Связи данных уже были в модели (NsgSlot.link), их просто никто не
    /// рисовал: стопке они не нужны. Поэтому чертёж — это только отрисовка и
    /// перетаскивание, а не новая модель данных.
    ///
    /// Позиции карточек считаются аналитически из фиксированных размеров, а не
    /// измеряются у VisualElement. Так провода можно рисовать до первой
    /// раскладки и не зависеть от порядка прохода layout.
    /// </summary>
    public class Nsg_BlueprintView : VisualElement
    {
        public Nsg_ScriptView Owner;
        public NsgMethodGraph Graph;
        public NsgStructNode Method;
        public Nsg_BlockLibrary Library;

        /// <summary>Метка карточки: по ней полотно отличает карточку от пустого места.</summary>
        public const string CardClass = "nsg-bp-card";

        // Размеры карточки. Держим их константами: от них зависит и отрисовка
        // проводов, и попадание по пинам.
        const float CardW = 240f;
        const float HeaderH = 24f;
        const float RowH = 20f;
        const float PadBottom = 8f;
        const float Margin = 28f;
        const float Nub = 9f;

        /// <summary>Невидимая площадка вокруг пина: по 9 пикселей тонким кружком не попасть.</summary>
        const float PinPad = 5f;

        /// <summary>Цвет проводов данных. Исполнение красится цветом блока.</summary>
        static readonly Color DataWireColor = new Color(0.58f, 0.64f, 0.72f);
        static readonly Color DragWireColor = new Color(1f, 0.85f, 0.35f);
        static readonly Color ExecInColor = new Color(0.75f, 0.78f, 0.82f);

        // ------------------------------------------------------------------
        // Модель карточки и пина
        // ------------------------------------------------------------------

        enum PinKind
        {
            /// <summary>Вход исполнения: сюда приходит next/body/els.</summary>
            ExecIn,
            /// <summary>Выход исполнения. Он же — выход значения для проводa данных.</summary>
            ExecOut,
            Body,
            Els,
            /// <summary>Вход данных: args[index].</summary>
            ArgIn
        }

        class PinRef
        {
            public string NodeId;
            public PinKind Kind;
            public int Index;
        }

        class Card
        {
            public NsgGraphNode Node;
            public NsgBlockDef Def;
            public VisualElement Root;
            public float X;
            public float Y;
            public float H;
        }

        class Wire
        {
            public Vector2 From;
            public Vector2 To;
            public Color Color;
        }

        readonly List<Card> _cards = new List<Card>();
        readonly List<Wire> _wires = new List<Wire>();
        readonly Dictionary<string, Card> _byId = new Dictionary<string, Card>();

        VisualElement _wireLayer;
        VisualElement _cardLayer;
        float _boundsHeight;

        // --- перетаскивание карточки ---
        Card _dragCard;
        Vector2 _dragStart;
        bool _dragMoved;
        int _dragPointer = -1;
        readonly Dictionary<Card, Vector2> _dragOrigins = new Dictionary<Card, Vector2>();

        // --- протягивание провода ---
        PinRef _wireFrom;
        Vector2 _wireTo;

        public Nsg_BlueprintView(Nsg_ScriptView owner, NsgMethodGraph graph,
                                 NsgStructNode method, Nsg_BlockLibrary lib)
        {
            Owner = owner;
            Graph = graph;
            Method = method;
            Library = lib;

            style.marginTop = 4;
            style.marginBottom = 4;

            // Обработчики ставятся один раз: Build() вызывается повторно при
            // пересборке связей, и дубли коллбэков копились бы.
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(evt => EndDrag());

            // Указатель ушёл с полотна — протягивание провода отменяется,
            // иначе провод остался бы висеть за курсором.
            RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (_wireFrom == null) return;
                _wireFrom = null;
                MarkWiresDirty();
            });

            Nsg_BpLayout.Arrange(Graph);
            Build();
        }

        // ------------------------------------------------------------------
        // Построение
        // ------------------------------------------------------------------

        void Build()
        {
            Clear();
            _cards.Clear();
            _wires.Clear();
            _byId.Clear();

            if (Graph == null) return;

            for (int i = 0; i < Graph.nodes.Count; i++)
            {
                var n = Graph.nodes[i];
                if (n == null) continue;

                var def = Library != null ? Library.Get(n.block) : null;
                if (def == null) continue;

                var card = new Card
                {
                    Node = n,
                    Def = def,
                    X = Mathf.Max(0f, n.x),
                    Y = Mathf.Max(0f, n.y)
                };
                card.H = HeightOf(def);
                card.Root = BuildCard(card);

                _cards.Add(card);
                _byId[n.id] = card;
            }

            float w = 360f;
            float h = 220f;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].X + CardW + Margin > w) w = _cards[i].X + CardW + Margin;
                if (_cards[i].Y + _cards[i].H + Margin > h) h = _cards[i].Y + _cards[i].H + Margin;
            }
            style.width = w;
            style.height = h;
            _boundsHeight = h;

            // Слой проводов идёт первым: в UI Toolkit дети рисуются по порядку,
            // поэтому провода окажутся под карточками.
            _wireLayer = new VisualElement();
            Fill(_wireLayer);
            _wireLayer.pickingMode = PickingMode.Ignore;
            _wireLayer.generateVisualContent += OnGenerateWires;
            Add(_wireLayer);

            _cardLayer = new VisualElement();
            Fill(_cardLayer);
            Add(_cardLayer);

            for (int i = 0; i < _cards.Count; i++) _cardLayer.Add(_cards[i].Root);

            AddCornerButtons();
            BuildWires();
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0f;
            e.style.top = 0f;
            e.style.right = 0f;
            e.style.bottom = 0f;
        }

        static float HeightOf(NsgBlockDef def)
        {
            int rows = def != null ? def.SocketCount : 0;
            // Управлению нужны ещё две строки под пины веток body/els: иначе
            // они налезали бы на строки аргументов.
            if (def != null && def.IsControl) rows += 2;
            return HeaderH + rows * RowH + PadBottom;
        }

        /// <summary>Середина строки ветки body: считается от числа аргументов.</summary>
        static float BodyPinY(NsgBlockDef def)
        {
            int rows = def != null ? def.SocketCount : 0;
            return HeaderH + (rows + 0.5f) * RowH;
        }

        /// <summary>Середина строки ветки els.</summary>
        static float ElsPinY(NsgBlockDef def)
        {
            int rows = def != null ? def.SocketCount : 0;
            return HeaderH + (rows + 1.5f) * RowH;
        }

        VisualElement BuildCard(Card card)
        {
            var n = card.Node;
            var def = card.Def;
            Color color = def.BlockColor();

            var root = new VisualElement();
            // Класс-метка нужна полотну: по нему оно отличает клик по карточке
            // от клика по пустому месту, чтобы не сбрасывать выделение.
            root.AddToClassList(CardClass);
            root.style.position = Position.Absolute;
            root.style.left = card.X;
            root.style.top = card.Y;
            root.style.width = CardW;
            root.style.height = card.H;
            root.style.backgroundColor = new Color(0.19f, 0.20f, 0.23f);
            Nsg_Visual.Round(root, 6);
            SetBorder(root, RiskColor(n.id), 2f);

            if (Owner != null && Owner.FindingOf(n.id) != null)
            {
                var finding = Owner.FindingOf(n.id);
                root.RegisterCallback<PointerEnterEvent>(evt => Owner.ShowFixPopup(root, finding));
                root.RegisterCallback<PointerLeaveEvent>(evt => Owner.HideFixPopup());
            }

            // ---- шапка ----
            var header = new VisualElement();
            header.style.position = Position.Absolute;
            header.style.left = 0f;
            header.style.right = 0f;
            header.style.top = 0f;
            header.style.height = HeaderH;
            header.style.backgroundColor = color;
            header.style.flexDirection = Nsg_Rtl.Row;
            header.style.alignItems = Align.Center;
            Nsg_Rtl.PaddingStart(header.style, 14);
            Nsg_Rtl.PaddingEnd(header.style, 14);
            Nsg_Visual.RoundTop(header, 6);
            header.pickingMode = PickingMode.Ignore;

            var title = new Label(HeaderText(n, def));
            title.style.color = Nsg_Palette.TextOn(color);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.overflow = Overflow.Hidden;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.flexGrow = 1;
            title.style.unityTextAlign = Nsg_Rtl.TextAlign;
            header.Add(title);
            root.Add(header);

            // ---- строки слотов ----
            int rows = def.SocketCount;
            for (int i = 0; i < rows; i++)
            {
                var socket = def.sockets[i];
                string name = socket != null && !string.IsNullOrEmpty(socket.name) ? socket.name : i.ToString();

                var row = new VisualElement();
                row.style.position = Position.Absolute;
                row.style.left = 0f;
                row.style.right = 0f;
                row.style.top = HeaderH + i * RowH;
                row.style.height = RowH;
                row.style.flexDirection = Nsg_Rtl.Row;
                row.style.alignItems = Align.Center;
                Nsg_Rtl.PaddingStart(row.style, 14);
                Nsg_Rtl.PaddingEnd(row.style, 10);
                row.pickingMode = PickingMode.Ignore;

                var key = new Label(name);
                key.style.color = new Color(0.55f, 0.58f, 0.64f);
                key.style.fontSize = 10;
                Nsg_Rtl.MarginEnd(key.style, 6);
                row.Add(key);

                var value = new Label(SlotText(n, i));
                value.style.color = new Color(0.86f, 0.89f, 0.93f);
                value.style.flexGrow = 1;
                value.style.overflow = Overflow.Hidden;
                value.style.whiteSpace = WhiteSpace.NoWrap;
                value.style.unityTextAlign = Nsg_Rtl.TextAlign;
                row.Add(value);

                // Пин слота стоит центром ровно на левой кромке карточки —
                // тогда провод данных попадает в него без поправок.
                bool expr = socket != null && socket.kind == "expr";
                row.Add(MakePin(new PinRef { NodeId = n.id, Kind = PinKind.ArgIn, Index = i },
                                -Nub * 0.5f, (RowH - Nub) * 0.5f,
                                expr ? DataWireColor : new Color(0.35f, 0.37f, 0.41f)));

                root.Add(row);
            }

            // ---- пины исполнения: центром на кромке ----
            root.Add(MakePin(new PinRef { NodeId = n.id, Kind = PinKind.ExecIn },
                             -Nub * 0.5f, (HeaderH - Nub) * 0.5f, ExecInColor));

            // Выход общий: и продолжение исполнения, и значение для провода.
            root.Add(MakePin(new PinRef { NodeId = n.id, Kind = PinKind.ExecOut },
                             CardW - Nub * 0.5f, (HeaderH - Nub) * 0.5f, color));

            if (def.IsControl)
            {
                root.Add(MakePin(new PinRef { NodeId = n.id, Kind = PinKind.Body },
                                 CardW - Nub * 0.5f, BodyPinY(def) - Nub * 0.5f, color));
                if (def.id == "stmt.if")
                {
                    root.Add(MakePin(new PinRef { NodeId = n.id, Kind = PinKind.Els },
                                     CardW - Nub * 0.5f, ElsPinY(def) - Nub * 0.5f, color));
                }
            }

            root.RegisterCallback<PointerDownEvent>(evt => OnCardPointerDown(card, evt));
            return root;
        }

        static void SetBorder(VisualElement e, Color c, float w)
        {
            e.style.borderTopWidth = w;
            e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w;
            e.style.borderRightWidth = w;
            e.style.borderTopColor = c;
            e.style.borderBottomColor = c;
            e.style.borderLeftColor = c;
            e.style.borderRightColor = c;
        }

        /// <summary>
        /// Пин: тонкий кружок плюс невидимая площадка вокруг. Площадка нужна,
        /// потому что по кружку в 9 пикселей невозможно попасть курсором.
        /// </summary>
        VisualElement MakePin(PinRef pin, float left, float top, Color color)
        {
            var hit = new VisualElement();
            hit.userData = pin;
            hit.style.position = Position.Absolute;
            hit.style.left = left - PinPad;
            hit.style.top = top - PinPad;
            hit.style.width = Nub + PinPad * 2f;
            hit.style.height = Nub + PinPad * 2f;
            hit.style.alignItems = Align.Center;
            hit.style.justifyContent = Justify.Center;

            var dot = new VisualElement();
            dot.style.width = Nub;
            dot.style.height = Nub;
            dot.style.backgroundColor = color;
            dot.style.borderTopLeftRadius = Nub * 0.5f;
            dot.style.borderTopRightRadius = Nub * 0.5f;
            dot.style.borderBottomLeftRadius = Nub * 0.5f;
            dot.style.borderBottomRightRadius = Nub * 0.5f;
            dot.pickingMode = PickingMode.Ignore;
            hit.Add(dot);

            hit.RegisterCallback<PointerDownEvent>(evt => OnPinPointerDown(pin, evt));
            return hit;
        }

        /// <summary>Подпись шапки: шаблон блока с подставленными текстами слотов.</summary>
        static string HeaderText(NsgGraphNode n, NsgBlockDef def)
        {
            var values = new string[def.SocketCount];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = SlotText(n, i);
            }
            string text = def.RenderLabel(values);
            return string.IsNullOrEmpty(text) ? def.id : text;
        }

        /// <summary>Текст слота: либо значение, либо ссылка на связанный блок.</summary>
        static string SlotText(NsgGraphNode n, int index)
        {
            if (n == null || n.args == null || index < 0 || index >= n.args.Count) return string.Empty;
            var slot = n.args[index];
            if (slot == null) return string.Empty;
            if (!string.IsNullOrEmpty(slot.link)) return "→ " + slot.link;
            return slot.text ?? string.Empty;
        }

        void AddCornerButtons()
        {
            var row = new VisualElement();
            row.style.position = Position.Absolute;
            row.style.left = 4f;
            row.style.top = Mathf.Max(4f, _boundsHeight - 26f);
            row.style.flexDirection = Nsg_Rtl.Row;
            row.style.alignItems = Align.Center;

            var add = new Button(() =>
            {
                if (Owner != null && Method != null)
                {
                    Owner.PickStatement(new Nsg_ScriptView.StackTarget(Graph, null, null, Method));
                }
            });
            add.text = "+ " + Nsg_L10n.T("pick.clickToAdd");
            StyleCornerButton(add);
            row.Add(add);

            var tidy = new Button(() =>
            {
                // Полная раскладка заново: единственный способ разгрести
                // полотно, если узлы растащили вручную.
                PushUndo();
                Nsg_BpLayout.ArrangeAll(Graph);
                RebuildSoon();
            });
            tidy.text = Nsg_L10n.T("bp.tidy");
            Nsg_Rtl.MarginStart(tidy.style, 4);
            StyleCornerButton(tidy);
            row.Add(tidy);

            Add(row);
        }

        static void StyleCornerButton(Button b)
        {
            b.style.height = 20f;
            b.style.fontSize = 10;
            b.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            b.style.color = new Color(0.62f, 0.66f, 0.72f);
        }

        // ------------------------------------------------------------------
        // Провода
        // ------------------------------------------------------------------

        void BuildWires()
        {
            _wires.Clear();
            if (Graph == null) return;

            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                var n = card.Node;
                Color exec = card.Def != null ? card.Def.BlockColor() : Nsg_Palette.Frame;

                if (!string.IsNullOrEmpty(n.next)) AddExecWire(card, HeaderH * 0.5f, n.next, exec);
                if (!string.IsNullOrEmpty(n.body)) AddExecWire(card, BodyPinY(card.Def), n.body, exec);
                if (!string.IsNullOrEmpty(n.els)) AddExecWire(card, ElsPinY(card.Def), n.els, exec);

                AddDataWires(card, n.args);
                AddDataWires(card, n.extra);
            }

            MarkWiresDirty();
        }

        void AddDataWires(Card card, List<NsgSlot> slots)
        {
            if (slots == null) return;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.link)) continue;

                Card src;
                if (!_byId.TryGetValue(slot.link, out src)) continue;

                // У вариативных блоков слотов в графе может быть больше, чем
                // нарисованных строк. Провод всё равно должен прийти в карточку,
                // а не повиснуть ниже неё.
                float rowY = HeaderH + RowH * (i + 0.5f);
                if (rowY > card.H - RowH * 0.5f) rowY = card.H - RowH * 0.5f;

                _wires.Add(new Wire
                {
                    From = new Vector2(src.X + CardW, src.Y + HeaderH * 0.5f),
                    To = new Vector2(card.X, card.Y + rowY),
                    Color = DataWireColor
                });
            }
        }

        void AddExecWire(Card from, float fromLocalY, string toId, Color color)
        {
            Card to;
            if (!_byId.TryGetValue(toId, out to)) return;

            _wires.Add(new Wire
            {
                From = new Vector2(from.X + CardW, from.Y + fromLocalY),
                To = new Vector2(to.X, to.Y + HeaderH * 0.5f),
                Color = color
            });
        }

        void MarkWiresDirty()
        {
            if (_wireLayer != null) _wireLayer.MarkDirtyRepaint();
        }

        void OnGenerateWires(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            p.lineWidth = 2f;

            for (int i = 0; i < _wires.Count; i++)
            {
                DrawWire(p, _wires[i].From, _wires[i].To, _wires[i].Color);
            }

            // Провод, который сейчас тянут: от пина к курсору.
            if (_wireFrom != null)
            {
                Vector2 a;
                if (PinPoint(_wireFrom, out a))
                {
                    DrawWire(p, a, _wireTo, DragWireColor);
                }
            }
        }

        static void DrawWire(Painter2D p, Vector2 from, Vector2 to, Color color)
        {
            float dx = Mathf.Max(36f, Mathf.Abs(to.x - from.x) * 0.5f);

            p.strokeColor = color;
            p.BeginPath();
            p.MoveTo(from);
            p.BezierCurveTo(new Vector2(from.x + dx, from.y),
                            new Vector2(to.x - dx, to.y),
                            to);
            p.Stroke();
        }

        /// <summary>Центр пина в координатах полотна.</summary>
        bool PinPoint(PinRef pin, out Vector2 point)
        {
            point = Vector2.zero;
            if (pin == null) return false;

            Card card;
            if (!_byId.TryGetValue(pin.NodeId, out card)) return false;

            switch (pin.Kind)
            {
                case PinKind.ArgIn:
                    point = new Vector2(card.X, card.Y + HeaderH + RowH * (pin.Index + 0.5f));
                    return true;
                case PinKind.Body:
                    point = new Vector2(card.X + CardW, card.Y + BodyPinY(card.Def));
                    return true;
                case PinKind.Els:
                    point = new Vector2(card.X + CardW, card.Y + ElsPinY(card.Def));
                    return true;
                case PinKind.ExecIn:
                    point = new Vector2(card.X, card.Y + HeaderH * 0.5f);
                    return true;
                default:
                    point = new Vector2(card.X + CardW, card.Y + HeaderH * 0.5f);
                    return true;
            }
        }

        // ------------------------------------------------------------------
        // Связывание проводов
        // ------------------------------------------------------------------

        /// <summary>Пины-источники: выход исполнения и ветки управления.</summary>
        static bool IsExecOutPin(PinKind kind)
        {
            return kind == PinKind.ExecOut || kind == PinKind.Body || kind == PinKind.Els;
        }

        void OnPinPointerDown(PinRef pin, PointerDownEvent evt)
        {
            if (evt.button != 0 || pin == null) return;

            if (pin.Kind == PinKind.ArgIn)
            {
                // С входа данных тянуть нечего, если он пуст.
                if (!HasLink(pin)) return;
            }
            else if (pin.Kind == PinKind.ExecIn)
            {
                // С входа исполнения — только если туда что-то приходит.
                if (!HasIncomingExec(pin.NodeId)) return;
            }
            else if (!IsExecOutPin(pin.Kind))
            {
                return;
            }

            // Что именно получится — исполнение или значение — решает не
            // источник, а пин, на который бросят. Так же устроен UE: выход
            // один, а провод получается разный.
            _wireFrom = pin;
            Vector2 a;
            if (PinPoint(pin, out a)) _wireTo = a;

            MarkWiresDirty();
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            // Два разных перетаскивания на одном полотне: провод и карточка.
            if (_wireFrom != null)
            {
                _wireTo = this.WorldToLocal(evt.position);
                MarkWiresDirty();
                return;
            }

            if (_dragCard != null) OnDragMove(evt);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (_wireFrom != null)
            {
                var from = _wireFrom;
                _wireFrom = null;

                // Цель ищется по элементу под курсором: захвата указателя при
                // протягивании нет, поэтому evt.target — это именно пин.
                ApplyWire(from, FindPin(evt.target as VisualElement));

                MarkWiresDirty();
                evt.StopPropagation();
                return;
            }

            if (_dragCard != null)
            {
                OnDragUp();
                evt.StopPropagation();
            }
        }

        /// <summary>Поднимается от элемента под курсором до пина.</summary>
        static PinRef FindPin(VisualElement e)
        {
            while (e != null)
            {
                var pin = e.userData as PinRef;
                if (pin != null) return pin;
                e = e.parent;
            }
            return null;
        }

        /// <summary>
        /// Раскладывает протянутый провод по модели. Направление протягивания
        /// не важно: связь всегда приводится к виду «источник → приёмник».
        /// </summary>
        void ApplyWire(PinRef from, PinRef to)
        {
            // ---- данные ----
            if (from.Kind == PinKind.ExecOut && to != null && to.Kind == PinKind.ArgIn)
            {
                ConnectData(from, to);
                return;
            }
            if (from.Kind == PinKind.ArgIn && to != null && to.Kind == PinKind.ExecOut)
            {
                ConnectData(to, from);
                return;
            }
            if (from.Kind == PinKind.ArgIn && to == null)
            {
                // Потянули от входа данных и бросили в пустоту — связь снята.
                ClearLink(from);
                return;
            }

            // ---- исполнение ----
            if (IsExecOutPin(from.Kind) && to != null && to.Kind == PinKind.ExecIn)
            {
                ConnectExec(from, to.NodeId);
                return;
            }
            if (from.Kind == PinKind.ExecIn && to != null && IsExecOutPin(to.Kind))
            {
                ConnectExec(to, from.NodeId);
                return;
            }
            if (from.Kind == PinKind.ExecIn && to == null)
            {
                DetachExec(from.NodeId);
                return;
            }

            // Бросили в пустоту или в несовместимый пин: ничего не делаем.
        }

        // ------------------------------------------------------------------
        // Провода данных
        // ------------------------------------------------------------------

        void ConnectData(PinRef src, PinRef dst)
        {
            if (!CanConnect(src, dst)) return;

            var source = Graph.Find(src.NodeId);
            var target = Graph.Find(dst.NodeId);
            if (source == null || target == null) return;

            PushUndo();

            var slot = target.EnsureArg(dst.Index);
            slot.link = source.id;
            slot.text = null;

            // Связанный узел мог лежать в отвале — раскладываем заново, чтобы
            // провод не уходил за пределы полотна.
            Nsg_BpLayout.Arrange(Graph);
            RebuildSoon();
        }

        // ------------------------------------------------------------------
        // Провода исполнения
        // ------------------------------------------------------------------

        void ConnectExec(PinRef src, string targetId)
        {
            if (src == null || string.IsNullOrEmpty(targetId)) return;
            if (src.NodeId == targetId) return;

            var source = Graph.Find(src.NodeId);
            var target = Graph.Find(targetId);
            if (source == null || target == null) return;

            // Вход метода перепривязать нельзя: без entry вся цепочка станет
            // отвалом и метод перестанет печататься.
            if (Graph.entry == targetId) return;

            // В исполнение может встать только оператор, не выражение.
            var targetDef = DefOf(targetId);
            if (targetDef == null || targetDef.shape == "expression") return;

            // Ветки body/els бывают только у блока управления.
            if (src.Kind != PinKind.ExecOut)
            {
                var srcDef = DefOf(src.NodeId);
                if (srcDef == null || !srcDef.IsControl) return;
            }

            // Цикл: приёмник не должен уже вести обратно к источнику.
            if (ReachesViaExec(targetId, source.id)) return;

            PushUndo();

            // Освобождаем прежний вход цели: у оператора в цепочке ровно один
            // предшественник, иначе он печатался бы дважды.
            DetachIncoming(targetId);

            if (src.Kind == PinKind.Body) source.body = targetId;
            else if (src.Kind == PinKind.Els) source.els = targetId;
            else source.next = targetId;

            Nsg_BpLayout.Arrange(Graph);
            RebuildSoon();
        }

        void DetachExec(string nodeId)
        {
            if (Graph == null || string.IsNullOrEmpty(nodeId)) return;

            // Первый оператор метода не отцепляем: это обнулило бы весь метод.
            if (Graph.entry == nodeId) return;

            // Снимок берётся до правки, иначе отмена вернула бы уже отцепленное.
            if (!HasIncomingExec(nodeId)) return;
            PushUndo();

            if (!DetachIncoming(nodeId)) return;
            RebuildSoon();
        }

        /// <summary>Снимает входящую связь исполнения. Возвращает false, если её не было.</summary>
        bool DetachIncoming(string targetId)
        {
            if (Graph == null || string.IsNullOrEmpty(targetId)) return false;

            string field;
            var pred = FindPredecessor(targetId, out field);
            if (pred == null) return false;

            switch (field)
            {
                case "body": pred.body = null; break;
                case "els": pred.els = null; break;
                default: pred.next = null; break;
            }
            return true;
        }

        NsgGraphNode FindPredecessor(string targetId, out string field)
        {
            field = null;
            if (Graph == null || Graph.nodes == null) return null;

            for (int i = 0; i < Graph.nodes.Count; i++)
            {
                var n = Graph.nodes[i];
                if (n == null) continue;

                if (n.next == targetId) { field = "next"; return n; }
                if (n.body == targetId) { field = "body"; return n; }
                if (n.els == targetId) { field = "els"; return n; }
            }
            return null;
        }

        bool HasIncomingExec(string nodeId)
        {
            string field;
            return FindPredecessor(nodeId, out field) != null;
        }

        /// <summary>Ведёт ли исполнение от узла к другому узлу (проверка цикла).</summary>
        bool ReachesViaExec(string fromId, string wanted)
        {
            if (Graph == null || string.IsNullOrEmpty(fromId)) return false;

            var seen = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(fromId);

            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                if (string.IsNullOrEmpty(cur)) continue;
                if (cur == wanted) return true;
                if (!seen.Add(cur)) continue;

                var n = Graph.Find(cur);
                if (n == null) continue;

                stack.Push(n.next);
                stack.Push(n.body);
                stack.Push(n.els);
            }
            return false;
        }

        /// <summary>
        /// Пересборка отложена до конца обработки события: менять дерево прямо
        /// внутри коллбэка, который по нему же и разошёлся, небезопасно.
        /// </summary>
        void RebuildSoon()
        {
            schedule.Execute(() =>
            {
                if (panel == null) return;
                Build();
            });
        }

        void ClearLink(PinRef pin)
        {
            var node = Graph != null ? Graph.Find(pin.NodeId) : null;
            if (node == null || pin.Index < 0 || pin.Index >= node.args.Count) return;

            var slot = node.args[pin.Index];
            if (slot == null || string.IsNullOrEmpty(slot.link)) return;

            PushUndo();
            slot.link = null;
            RebuildSoon();
        }

        bool CanConnect(PinRef src, PinRef dst)
        {
            if (src == null || dst == null) return false;
            if (src.NodeId == dst.NodeId) return false;

            var srcDef = DefOf(src.NodeId);
            if (srcDef == null || srcDef.shape != "expression") return false;

            var dstDef = DefOf(dst.NodeId);
            if (dstDef == null || dstDef.sockets == null) return false;
            if (dst.Index < 0 || dst.Index >= dstDef.sockets.Length) return false;

            var socket = dstDef.sockets[dst.Index];
            if (socket == null || socket.kind != "expr") return false;

            // Цикл: источник не должен сам зависеть от приёмника.
            return !DependsOn(src.NodeId, dst.NodeId);
        }

        /// <summary>Зависит ли узел <paramref name="nodeId"/> от значения другого узла.</summary>
        bool DependsOn(string nodeId, string dependency)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(nodeId);

            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                if (cur == dependency) return true;
                if (!seen.Add(cur)) continue;

                var n = Graph != null ? Graph.Find(cur) : null;
                if (n == null) continue;

                PushLinks(n.args, stack);
                PushLinks(n.extra, stack);
            }
            return false;
        }

        static void PushLinks(List<NsgSlot> slots, Stack<string> stack)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null && !string.IsNullOrEmpty(s.link)) stack.Push(s.link);
            }
        }

        bool HasLink(PinRef pin)
        {
            var node = Graph != null ? Graph.Find(pin.NodeId) : null;
            if (node == null || pin.Index < 0 || pin.Index >= node.args.Count) return false;
            var slot = node.args[pin.Index];
            return slot != null && !string.IsNullOrEmpty(slot.link);
        }

        bool IsExpressionNode(string id)
        {
            var def = DefOf(id);
            return def != null && def.shape == "expression";
        }

        NsgBlockDef DefOf(string id)
        {
            var node = Graph != null ? Graph.Find(id) : null;
            if (node == null) return null;
            return Library != null ? Library.Get(node.block) : null;
        }

        void PushUndo()
        {
            if (Owner != null && Owner.Doc != null && Owner.Doc.Model != null)
            {
                Owner.Undo.PushFull(Owner.Doc.Model, "wire");
            }
        }

        // ------------------------------------------------------------------
        // Выделение
        // ------------------------------------------------------------------

        /// <summary>Цвет рамки карточки по риску. Выделение его перебивает.</summary>
        Color RiskColor(string nodeId)
        {
            var f = Owner != null ? Owner.FindingOf(nodeId) : null;
            if (f == null) return Color.clear;

            return f.Severity == NsgSeverity.Error
                ? new Color(0.95f, 0.25f, 0.25f)
                : new Color(0.95f, 0.60f, 0.20f);
        }

        public void ApplySelection(HashSet<string> selected)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                bool on = selected != null && card.Node != null && selected.Contains(card.Node.id);

                // Выделение важнее риска: иначе не понять, что выбрано.
                SetBorder(card.Root, on ? new Color(1f, 0.85f, 0.35f) : RiskColor(card.Node.id), 2f);
            }
        }

        /// <summary>Лежит ли элемент внутри карточки чертежа (с учётом предков).</summary>
        public static bool InsideCard(VisualElement e)
        {
            while (e != null)
            {
                if (e.ClassListContains(CardClass)) return true;
                e = e.parent;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Перетаскивание карточки
        // ------------------------------------------------------------------

        void OnCardPointerDown(Card card, PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            bool additive = evt.shiftKey || evt.ctrlKey || evt.commandKey;
            if (Owner != null) Owner.SelectNode(card.Node.id, additive);

            _dragCard = card;
            _dragStart = evt.position;
            _dragMoved = false;

            _dragOrigins.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                bool selected = Owner == null || Owner.IsSelected(c.Node.id);
                if (c == card || selected) _dragOrigins[c] = new Vector2(c.X, c.Y);
            }

            _dragPointer = evt.pointerId;
            // Захват указателя — расширение PointerCaptureHelper, поэтому
            // вызывается только через экземпляр, а не по имени.
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnDragMove(PointerMoveEvent evt)
        {
            float zoom = Owner != null ? Owner.Zoom : 1f;
            if (zoom <= 0.01f) zoom = 1f;

            Vector2 delta = ((Vector2)evt.position - _dragStart) / zoom;
            if (!_dragMoved)
            {
                if (delta.magnitude < 3f) return;
                _dragMoved = true;

                // Снимок делается до первого сдвига: иначе отмена вернула бы
                // уже сдвинутое состояние.
                PushUndo();
            }

            foreach (var kv in _dragOrigins)
            {
                var card = kv.Key;
                MoveCard(card, Mathf.Max(0f, kv.Value.x + delta.x), Mathf.Max(0f, kv.Value.y + delta.y));
            }

            BuildWires();
            evt.StopPropagation();
        }

        void OnDragUp()
        {
            bool moved = _dragMoved;
            EndDrag();
            if (moved && Owner != null && Owner.Changed != null) Owner.Changed();
        }

        void EndDrag()
        {
            // Поля сбрасываются ДО ReleasePointer: отпускание захвата вызывает
            // PointerCaptureOut, который снова пришёл бы сюда.
            int pointer = _dragPointer;
            _dragPointer = -1;
            _dragCard = null;
            _dragMoved = false;
            _dragOrigins.Clear();

            if (pointer >= 0 && this.HasPointerCapture(pointer)) this.ReleasePointer(pointer);
        }

        static void MoveCard(Card card, float x, float y)
        {
            card.X = x;
            card.Y = y;
            card.Node.x = x;
            card.Node.y = y;
            card.Root.style.left = x;
            card.Root.style.top = y;
        }
    }
}
