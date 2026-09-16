using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>Небольшие помощники оформления, общие для всех видов блоков.</summary>
    public static class Nsg_Visual
    {
        public static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        public static void RoundTop(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
        }

        public static void RoundBottom(VisualElement e, float r)
        {
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        public static void ApplySprite(VisualElement e, string kind, Color tint)
        {
            var s = Nsg_Settings.Instance.SpriteFor(kind);
            if (s == null) return;
            e.style.backgroundImage = new StyleBackground(s);
            e.style.unityBackgroundImageTintColor = tint;
        }

        public static Label Text(string s, Color c)
        {
            var l = new Label(s);
            l.style.color = c;
            Nsg_Rtl.MarginEnd(l.style, 3);
            l.style.unityTextAlign = Nsg_Rtl.TextAlign;
            return l;
        }
    }

    /// <summary>
    /// Один отрисованный блок.
    ///
    /// Блоки-операторы и блоки управления рисуются стопками в стиле Scratch:
    /// цветная шапка, необязательное C-образное тело, необязательная ветка
    /// «иначе» и замыкающая полоса. Блоки-выражения рисуются встроенно, чтобы
    /// читаться как вставки внутри слота.
    /// </summary>
    public class Nsg_BlockView : VisualElement
    {
        public NsgGraphNode Data;
        public NsgBlockDef Def;
        public Nsg_ScriptView Owner;
        public Nsg_ScriptView.StackTarget Target;
        public NsgMethodGraph Graph;
        public NsgStructNode Method;
        public bool Inline;
        public int Depth;

        VisualElement _header;
        VisualElement _bodyStack;
        VisualElement _elseStack;
        VisualElement _footer;

        Color _color;

        /// <summary>Находка гигиены для этого узла: красная/оранжевая рамка.</summary>
        Nsg_HealthFinding _finding;

        public Nsg_BlockView(NsgGraphNode data, NsgBlockDef def, Nsg_ScriptView owner,
                             bool inline, NsgMethodGraph graph, NsgStructNode method, int depth,
                             Nsg_ScriptView.StackTarget target)
        {
            Data = data;
            Def = def;
            Owner = owner;
            Inline = inline;
            Graph = graph;
            Method = method;
            Depth = depth;
            Target = target;

            _color = def != null ? def.BlockColor() : Nsg_Palette.Frame;
            Build();
            RegisterInOwner();

            // Риск подсвечивается рамкой, а наведении показывается подсказка
            // с исправлением. Обработчики вешаются только на проблемные блоки:
            // держать их на каждом блоке дорого.
            _finding = Owner != null ? Owner.FindingOf(Data.id) : null;
            ApplyBorder();

            if (_finding != null)
            {
                RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (Owner != null) Owner.ShowFixPopup(this, _finding);
                });
                RegisterCallback<PointerLeaveEvent>(evt =>
                {
                    if (Owner != null) Owner.HideFixPopup();
                });
            }
        }

        /// <summary>Регистрируется в полотне, чтобы выделение находило вид по id.</summary>
        void RegisterInOwner()
        {
            if (Owner != null) Owner.RegisterView(Data.id, this);
        }

        // ------------------------------------------------------------------
        // Построение
        // ------------------------------------------------------------------

        void Build()
        {
            style.marginBottom = 2;
            // Начало поперечной оси: в LTR блоки прижаты влево, в RTL — вправо,
            // и растут они влево, к началу строки.
            style.alignSelf = Nsg_Rtl.AlignStart;

            // Рамка выделения присутствует всегда, меняется только цвет. Иначе
            // при выделении менялась бы толщина и весь блок дёргался.
            style.borderTopWidth = 2;
            style.borderBottomWidth = 2;
            style.borderLeftWidth = 2;
            style.borderRightWidth = 2;
            style.borderTopColor = Color.clear;
            style.borderBottomColor = Color.clear;
            style.borderLeftColor = Color.clear;
            style.borderRightColor = Color.clear;

            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

            var settings = Nsg_Settings.Instance;
            float radius = settings.cornerRadius;

            _header = new VisualElement();
            // RowReverse в RTL: подпись читается справа налево, а вложенные
            // блоки-выражения уезжают влево, вглубь строки.
            _header.style.flexDirection = Nsg_Rtl.Row;
            _header.style.alignItems = Align.Center;
            _header.style.flexWrap = Wrap.Wrap;
            _header.style.backgroundColor = _color;
            Nsg_Rtl.PaddingStart(_header.style, 6);
            Nsg_Rtl.PaddingEnd(_header.style, 3);
            _header.style.paddingTop = 3;
            _header.style.paddingBottom = 3;
            _header.style.minHeight = 22;
            Nsg_Visual.Round(_header, radius);
            Nsg_Visual.ApplySprite(_header, Inline ? "expr" : "header", _color);

            BuildHeader();
            AddVariantDropdown();

            var menu = new Button(ShowMenu);
            menu.text = "⋮";
            menu.style.width = 18;
            menu.style.height = 16;
            Nsg_Rtl.MarginStart(menu.style, 4);
            menu.style.paddingLeft = 0f;
            menu.style.paddingRight = 0f;
            menu.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
            menu.style.color = Nsg_Palette.TextOn(_color);
            menu.style.borderTopWidth = 0f;
            menu.style.borderBottomWidth = 0f;
            menu.style.borderLeftWidth = 0f;
            menu.style.borderRightWidth = 0f;
            _header.Add(menu);

            Add(_header);

            if (Inline) return;
            if (Def == null || !Def.IsControl) return;

            // ---- C-образное тело ----
            // Отступ-«скоба» стоит со стороны начала строки: слева в LTR,
            // справа в RTL, иначе тело блока читалось бы наоборот.
            _bodyStack = new VisualElement();
            Nsg_Rtl.BorderStart(_bodyStack.style, settings.bodyIndent, _color);
            Nsg_Rtl.PaddingStart(_bodyStack.style, 6);
            _bodyStack.style.paddingTop = 3;
            Add(_bodyStack);

            var body = Owner != null
                ? Owner.BuildStack(new Nsg_ScriptView.StackTarget(Graph, Data, "body", Method), Depth + 1)
                : null;
            if (body != null) _bodyStack.Add(body);

            // ---- ветка «иначе» ----
            if (Def.id == "stmt.if")
            {
                var elseRow = new VisualElement();
                elseRow.style.flexDirection = Nsg_Rtl.Row;
                elseRow.style.alignItems = Align.Center;
                elseRow.style.backgroundColor = new Color(_color.r, _color.g, _color.b, 0.55f);
                Nsg_Rtl.PaddingStart(elseRow.style, 6);
                elseRow.style.minHeight = 18;
                elseRow.Add(Nsg_Visual.Text(Nsg_L10n.T("blk.else"), Nsg_Palette.TextOn(_color)));
                Add(elseRow);

                _elseStack = new VisualElement();
                Nsg_Rtl.BorderStart(_elseStack.style, settings.bodyIndent, _color);
                Nsg_Rtl.PaddingStart(_elseStack.style, 6);
                _elseStack.style.paddingTop = 3;
                Add(_elseStack);

                var els = Owner != null
                    ? Owner.BuildStack(new Nsg_ScriptView.StackTarget(Graph, Data, "els", Method), Depth + 1)
                    : null;
                if (els != null) _elseStack.Add(els);
            }

            // ---- замыкающая полоса ----
            _footer = new VisualElement();
            _footer.style.height = settings.footerHeight;
            _footer.style.backgroundColor = _color;
            Nsg_Visual.RoundBottom(_footer, radius);
            Nsg_Visual.ApplySprite(_footer, "footer", _color);
            Add(_footer);
        }

        void BuildHeader()
        {
            var label = Def != null ? Def.Label() : Data.block;
            var segs = SplitLabel(label);

            for (int i = 0; i < segs.Count; i++)
            {
                var seg = segs[i];
                if (seg.Slot < 0)
                {
                    if (!string.IsNullOrEmpty(seg.Text))
                        _header.Add(Nsg_Visual.Text(seg.Text, Nsg_Palette.TextOn(_color)));
                    continue;
                }

                AddSocket(seg.Slot);
            }
        }

        class LabelSeg
        {
            public string Text;
            public int Slot = -1;
        }

        static List<LabelSeg> SplitLabel(string label)
        {
            var list = new List<LabelSeg>();
            if (string.IsNullOrEmpty(label)) return list;

            var sb = new StringBuilder();
            int i = 0;
            while (i < label.Length)
            {
                if (label[i] == '{')
                {
                    int close = label.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        int n;
                        if (int.TryParse(label.Substring(i + 1, close - i - 1).Trim(), out n))
                        {
                            if (sb.Length > 0)
                            {
                                list.Add(new LabelSeg { Text = sb.ToString() });
                                sb.Length = 0;
                            }
                            list.Add(new LabelSeg { Slot = n });
                            i = close + 1;
                            continue;
                        }
                    }
                }
                sb.Append(label[i]);
                i++;
            }
            if (sb.Length > 0) list.Add(new LabelSeg { Text = sb.ToString() });
            return list;
        }

        /// <summary>
        /// Группа вариантов рисуется одним блоком с выпадающим списком: шесть
        /// блоков присваивания не занимают шесть мест в палитре.
        /// </summary>
        void AddVariantDropdown()
        {
            if (Def == null || string.IsNullOrEmpty(Def.variantGroup)) return;
            if (Owner == null || Owner.Library == null) return;

            var variants = Owner.Library.InVariantGroup(Def.variantGroup);
            if (variants.Count < 2) return;

            var labels = new List<string>();
            int index = 0;
            for (int i = 0; i < variants.Count; i++)
            {
                var v = variants[i];
                labels.Add(string.IsNullOrEmpty(v.variantLabel) ? v.Label() : v.variantLabel);
                if (v.id == Def.id) index = i;
            }

            var dd = new DropdownField(labels, index);
            dd.style.width = 58;
            dd.style.height = 16;
            Nsg_Rtl.MarginStart(dd.style, 3);
            Nsg_Rtl.MarginEnd(dd.style, 2);
            dd.style.marginTop = 0f;
            dd.style.marginBottom = 0f;
            dd.RegisterValueChangedCallback(evt =>
            {
                int pick = labels.IndexOf(evt.newValue);
                if (pick < 0 || pick >= variants.Count) return;
                if (variants[pick].id == Data.block) return;
                if (Owner != null) Owner.SwitchVariant(Data, variants[pick].id, Method);
            });
            _header.Add(dd);
        }

        void AddSocket(int index)
        {
            if (Def.sockets == null || index < 0 || index >= Def.sockets.Length) return;
            var sock = Def.sockets[index];

            if (sock.variadic)
            {
                _header.Add(BuildVariadic(index));
                return;
            }

            if (sock.kind == "expr")
            {
                _header.Add(BuildExprSlot(index));
                return;
            }

            _header.Add(BuildTextField(index, sock));
        }

        VisualElement BuildTextField(int index, NsgSocketDef sock)
        {
            var slot = Data.Arg(index);
            string current = slot != null ? (slot.text ?? string.Empty) : string.Empty;

            // Слот с фиксированным набором значений — список, а не поле ввода.
            if (sock.choices != null && sock.choices.Length > 0)
            {
                var labels = new List<string>();
                int pick = 0;
                for (int i = 0; i < sock.choices.Length; i++)
                {
                    labels.Add(sock.choices[i]);
                    if (sock.choices[i] == current) pick = i;
                }
                if (string.IsNullOrEmpty(current) && labels.Count > 0)
                {
                    // Пустой слот получает первое значение: без оператора
                    // выражение не напечатать.
                    Data.EnsureArg(index).text = labels[0];
                }

                var dd = new DropdownField(labels, pick);
                dd.style.width = 54;
                dd.style.height = 16;
                Nsg_Rtl.MarginEnd(dd.style, 2);
                dd.style.marginTop = 0f;
                dd.style.marginBottom = 0f;

                int captured = index;
                dd.RegisterValueChangedCallback(evt =>
                {
                    if (Owner != null) Owner.OnSlotTextChanged(Data, captured, evt.newValue, Method);
                });
                return dd;
            }

            var tf = new TextField();
            tf.isDelayed = true;
            tf.value = current;
            tf.style.width = sock.kind == "var" ? 76 : 68;
            tf.style.height = 16;
            Nsg_Rtl.MarginEnd(tf.style, 2);
            Nsg_Rtl.MarginStart(tf.style, 0f);
            tf.style.marginTop = 0f;
            tf.style.marginBottom = 0f;

            int captured2 = index;
            tf.RegisterValueChangedCallback(evt =>
            {
                if (Owner != null) Owner.OnSlotTextChanged(Data, captured2, evt.newValue, Method);
            });

            return tf;
        }

        VisualElement BuildExprSlot(int index)
        {
            var slot = Data.Arg(index);
            if (slot != null && slot.IsLinked && Graph != null)
            {
                var child = Graph.Find(slot.link);
                var childDef = child != null && Owner != null && Owner.Library != null
                    ? Owner.Library.Get(child.block)
                    : null;

                if (child != null && childDef != null)
                {
                    return new Nsg_BlockView(child, childDef, Owner, true, Graph, Method, Depth + 1, null);
                }
            }

            var pill = new Button(() =>
            {
                if (Owner != null) Owner.PickExpression(Data, index, Method);
            });
            pill.text = Nsg_L10n.T("pick.empty");
            pill.style.height = 17;
            Nsg_Rtl.MarginEnd(pill.style, 2);
            Nsg_Rtl.PaddingStart(pill.style, 6);
            Nsg_Rtl.PaddingEnd(pill.style, 6);
            pill.style.fontSize = 10;
            pill.style.backgroundColor = new Color(1f, 1f, 1f, 0.16f);
            pill.style.color = Nsg_Palette.TextOn(_color);
            pill.style.borderTopWidth = 1;
            pill.style.borderBottomWidth = 1;
            pill.style.borderLeftWidth = 1;
            pill.style.borderRightWidth = 1;
            pill.style.borderTopColor = new Color(1f, 1f, 1f, 0.45f);
            pill.style.borderBottomColor = new Color(1f, 1f, 1f, 0.45f);
            pill.style.borderLeftColor = new Color(1f, 1f, 1f, 0.45f);
            pill.style.borderRightColor = new Color(1f, 1f, 1f, 0.45f);
            Nsg_Visual.Round(pill, 8);

            int captured = index;
            pill.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(Nsg_L10n.T("pick.expression"),
                    a => { if (Owner != null) Owner.PickExpression(Data, captured, Method); });
            }));

            return pill;
        }

        VisualElement BuildVariadic(int index)
        {
            var box = new VisualElement();
            box.style.flexDirection = Nsg_Rtl.Row;
            box.style.alignItems = Align.Center;
            box.style.flexWrap = Wrap.Wrap;

            int count = Data.extra != null ? Data.extra.Count : 0;
            for (int k = 0; k < count; k++)
            {
                if (k > 0) box.Add(Nsg_Visual.Text(", ", Nsg_Palette.TextOn(_color)));
                box.Add(BuildExtraSlot(k));
            }

            var add = new Button(() =>
            {
                if (Owner != null) Owner.AddArg(Data, Method);
            });
            add.text = "+";
            add.style.width = 20;
            add.style.height = 16;
            Nsg_Rtl.MarginStart(add.style, 2);
            add.style.paddingLeft = 0f;
            add.style.paddingRight = 0f;
            add.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
            add.style.color = Nsg_Palette.TextOn(_color);
            box.Add(add);

            return box;
        }

        VisualElement BuildExtraSlot(int k)
        {
            var slot = Data.extra != null && k < Data.extra.Count ? Data.extra[k] : null;

            if (slot != null && slot.IsLinked && Graph != null)
            {
                var child = Graph.Find(slot.link);
                var childDef = child != null && Owner != null && Owner.Library != null
                    ? Owner.Library.Get(child.block)
                    : null;

                if (child != null && childDef != null)
                {
                    return new Nsg_BlockView(child, childDef, Owner, true, Graph, Method, Depth + 1, null);
                }
            }

            var pill = new Button();
            pill.text = Nsg_L10n.T("pick.empty");
            pill.style.height = 17;
            Nsg_Rtl.MarginEnd(pill.style, 2);
            Nsg_Rtl.PaddingStart(pill.style, 6);
            Nsg_Rtl.PaddingEnd(pill.style, 6);
            pill.style.fontSize = 10;
            pill.style.backgroundColor = new Color(1f, 1f, 1f, 0.16f);
            pill.style.color = Nsg_Palette.TextOn(_color);
            Nsg_Visual.Round(pill, 8);

            int captured = k;
            pill.clicked += () =>
            {
                if (Owner != null) Owner.PickExtra(Data, captured, Method);
            };

            pill.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(Nsg_L10n.T("blk.del"),
                    a => { if (Owner != null) Owner.RemoveArg(Data, captured, Method); });
            }));

            return pill;
        }

        // ------------------------------------------------------------------
        // Выделение
        // ------------------------------------------------------------------

        bool _selected;

        public bool Selected
        {
            get { return _selected; }
        }

        /// <summary>
        /// Клик выделяет блок. Cmd/Ctrl или Shift добавляют к выделению.
        /// Событие не поглощаем: поля ввода должны получить фокус как обычно.
        /// </summary>
        void OnPointerDown(PointerDownEvent evt)
        {
            if (Owner == null) return;

            // Правая кнопка открывает меню. Раньше здесь стоял общий возврат
            // для всех кнопок кроме левой, и контекстное меню по правой кнопке
            // не открывалось вообще — оно висело только на кнопке «⋮».
            if (evt.button == 1)
            {
                Owner.OnBlockPointerDown(Data.id, Target, false, evt);
                ShowMenu();
                evt.StopPropagation();
                return;
            }

            if (evt.button != 0) return;

            bool additive = evt.shiftKey || evt.commandKey || evt.ctrlKey;
            Owner.OnBlockPointerDown(Data.id, Target, additive, evt);

            // Кандидат на перетаскивание. Только для блоков-операторов: блок
            // выражения нельзя подвесить в стопку через next.
            //
            // Условие именно "!Inline", а не "Target != null": у блока из отвала
            // стопки нет (Target пуст), но перетаскивать его обязательно нужно —
            // иначе отцепленный блок не вернуть обратно в стопку.
            //
            // Поле ввода, кнопка и выпадающий список внутри блока перетаскивание
            // не начинают: иначе выделение текста мышью превращалось бы в перенос
            // блока, а нажатие на «⋮» — в попытку его утащить.
            if (!additive && !Inline && !IsInteractiveTarget(evt.target as VisualElement))
                Owner.BeginBlockDragCandidate(Data.id, evt);
        }

        /// <summary>
        /// Есть ли на пути от цели события до этого блока интерактивный элемент.
        /// Обход идёт ровно по поддереву ЭТОГО блока: у вложенного блока-выражения
        /// свои поля, но перетаскивание начинает внешний блок, поэтому его поля
        /// тоже нужно учитывать.
        /// </summary>
        bool IsInteractiveTarget(VisualElement e)
        {
            while (e != null && e != this)
            {
                if (e is TextField || e is Button || e is DropdownField || e is Toggle) return true;
                e = e.parent;
            }
            return false;
        }

        /// <summary>
        /// Рамка блока. Выделение важнее риска, поэтому у выделенного блока
        /// красная подсветка уступает место жёлтой: иначе было бы не понять,
        /// что именно сейчас выбрано.
        /// </summary>
        void ApplyBorder()
        {
            Color c = Color.clear;

            if (_selected)
            {
                c = new Color(1f, 0.78f, 0.25f, 1f);
            }
            else if (_finding != null)
            {
                c = _finding.Severity == NsgSeverity.Error
                    ? new Color(0.95f, 0.25f, 0.25f, 1f)
                    : new Color(0.95f, 0.60f, 0.20f, 1f);
            }

            style.borderTopColor = c;
            style.borderBottomColor = c;
            style.borderLeftColor = c;
            style.borderRightColor = c;
        }

        public void SetSelected(bool on)
        {
            if (_selected == on) return;
            _selected = on;
            ApplyBorder();

            if (_header != null)
            {
                _header.style.unityBackgroundImageTintColor = on
                    ? new Color(1.15f, 1.15f, 1.15f, 1f)
                    : Color.white;
            }
        }

        // ------------------------------------------------------------------
        // Меню
        // ------------------------------------------------------------------

        void ShowMenu()
        {
            var menu = new GenericMenu();
            if (Owner == null) { menu.ShowAsContext(); return; }

            if (!Inline && Target != null)
            {
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.up")), false,
                    () => Owner.MoveStatement(Target, Data.id, -1));
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.down")), false,
                    () => Owner.MoveStatement(Target, Data.id, 1));
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.dup")), false,
                    () => Owner.DuplicateStatement(Target, Data.id));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.replace")), false,
                    () => Owner.PickReplace(Data, Method));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.del")), false,
                    () => Owner.DeleteStatement(Target, Data.id));
            }
            else
            {
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.replace")), false,
                    () => Owner.PickReplace(Data, Method));
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.del")), false,
                    () => Owner.DeleteInline(Data, Method));
            }

            // Пункт кошки появляется только когда она установлена и умеет
            // описывать блоки этого вида.
            if (Owner != null && Owner.CanExplainBlock(Data, Method))
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent(Nsg_L10n.T("blk.explain")), false,
                    () => Owner.ExplainBlock(Data, Method));
            }

            menu.ShowAsContext();
        }
    }
}
