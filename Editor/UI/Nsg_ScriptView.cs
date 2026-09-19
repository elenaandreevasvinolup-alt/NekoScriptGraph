using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Полотно всего скрипта: один экран на скрипт.
    ///
    /// Рисует дерево структуры (рамки namespace / class / метода) и для каждого
    /// метода — вертикально сложенную стопку блоков. Все правки проходят через
    /// здешние помощники изменения, которые сначала снимают снимок для отмены.
    /// </summary>
    public class Nsg_ScriptView : VisualElement
    {
        public Nsg_Document Doc;
        public Nsg_BlockLibrary Library;
        public System.Action Changed;

        /// <summary>
        /// Библиотека блоков изменилась изнутри полотна (создан свой блок).
        /// Окно по этому сигналу перечитывает библиотеку и пересобирает палитру.
        /// </summary>
        public System.Action LibraryChanged;

        /// <summary>
        /// Просьба объяснить блок. Обрабатывает окно: у него есть кошка, а у
        /// полотна её быть не должно.
        /// </summary>
        public System.Action<NsgMethodGraph, NsgGraphNode> ExplainBlockRequested;

        /// <summary>Есть ли кому и чем описывать логику этого блока.</summary>
        public bool CanExplainBlock(NsgGraphNode node, NsgStructNode method)
        {
            var neko = Nsg_NekoRegistry.Assistant;
            if (neko == null || node == null || method == null || method.graph == null) return false;

            var block = Nsg_NekoFacts.FromBlock(method.graph, node, Library);
            return block != null;
        }

        public void ExplainBlock(NsgGraphNode node, NsgStructNode method)
        {
            if (node == null || method == null) return;
            if (ExplainBlockRequested != null) ExplainBlockRequested(method.graph, node);
        }

        /// <summary>
        /// Объяснить первый выделенный блок. Вызывается горячей клавишей:
        /// пользователю не нужно целиться в конкретный блок, достаточно того,
        /// что он выделен.
        /// </summary>
        public void ExplainSelectedBlock()
        {
            if (_selected.Count == 0) return;

            string id = null;
            foreach (var s in _selected) { id = s; break; }
            if (string.IsNullOrEmpty(id)) return;

            NsgGraphNode node;
            NsgStructNode method;
            if (!FindNodeAndMethod(id, out node, out method)) return;

            ExplainBlock(node, method);
        }

        /// <summary>Ищет узел и его метод по идентификатору: выделение хранит только id.</summary>
        bool FindNodeAndMethod(string id, out NsgGraphNode node, out NsgStructNode method)
        {
            node = null;
            method = null;

            if (Doc == null || Doc.Model == null || string.IsNullOrEmpty(id)) return false;

            var methods = Doc.Model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i].graph;
                if (g == null) continue;

                var n = g.Find(id);
                if (n == null) continue;

                node = n;
                method = methods[i];
                return true;
            }
            return false;
        }

        public readonly Nsg_UndoStack Undo = new Nsg_UndoStack();

        VisualElement _content;
        VisualElement _viewport;
        VisualElement _zoomRoot;
        int _uid;

        /// <summary>Полотна чертежа текущей отрисовки — чтобы выделение доходило и до них.</summary>
        readonly List<Nsg_BlueprintView> _bpViews = new List<Nsg_BlueprintView>();

        /// <summary>Режим рисования: стопка (Scratch) или чертёж (UE Blueprints).</summary>
        public NsgViewMode Mode
        {
            get { return Nsg_Settings.Instance.Mode; }
        }

        /// <summary>
        /// Масштаб полотна. Перетаскивание в чертеже делит экранную дельту на
        /// него: иначе при уменьшении карточка убегала бы быстрее курсора.
        /// </summary>
        public float Zoom
        {
            get { return _zoom; }
        }

        float _zoom = 1f;
        Vector2 _pan;

        // Сетка фона. Рисуется на слое ПОД масштабируемым содержимым, но
        // внутри области просмотра: так она не растягивается вместе с блоками,
        // зато честно уезжает при переносе и меняет шаг при масштабе.
        VisualElement _gridLayer;

        /// <summary>Шаг сетки в координатах полотна.</summary>
        const float GridStep = 24f;

        /// <summary>Каждая пятая линия ярче: без этого сетка превращается в шум.</summary>
        const int GridMajorEvery = 5;

        static readonly Color GridMinorColor = new Color(1f, 1f, 1f, 0.022f);
        static readonly Color GridMajorColor = new Color(1f, 1f, 1f, 0.055f);
        bool _panning;
        Vector2 _panStart;
        Vector2 _panOrigin;
        Label _zoomLabel;

        const float ZoomMin = 0.25f;
        const float ZoomMax = 2.5f;
        const float ZoomStep = 0.9f;

        static readonly string[] CommonBases =
        {
            "MonoBehaviour", "ScriptableObject", "Object", "Component", "Behaviour",
            "StateMachineBehaviour", "EditorWindow", "Editor", "PropertyDrawer",
            "GraphView", "Node", "ScriptableWizard", "EditorWindow"
        };

        public Nsg_ScriptView()
        {
            style.flexGrow = 1;
            BuildShell();
        }

        void BuildShell()
        {
            // Область просмотра обрезает содержимое, а вложенный слой несёт
            // масштаб и смещение. Такой способ даёт масштабирование колесом
            // с якорем под курсором, как в Shader Graph.
            _viewport = new VisualElement();
            _viewport.style.flexGrow = 1;
            _viewport.style.overflow = Overflow.Hidden;
            _viewport.style.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            Add(_viewport);

            // Сетка идёт первой: в UI Toolkit дети рисуются по порядку, поэтому
            // она окажется под содержимым.
            _gridLayer = new VisualElement();
            _gridLayer.style.position = Position.Absolute;
            _gridLayer.style.left = 0f;
            _gridLayer.style.top = 0f;
            _gridLayer.style.right = 0f;
            _gridLayer.style.bottom = 0f;
            _gridLayer.pickingMode = PickingMode.Ignore;
            _gridLayer.generateVisualContent += OnGenerateGrid;
            _viewport.Add(_gridLayer);

            _zoomRoot = new VisualElement();
            _zoomRoot.style.position = Position.Absolute;
            _zoomRoot.style.left = 0f;
            _zoomRoot.style.top = 0f;
            _zoomRoot.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(0f, 0f));
            _viewport.Add(_zoomRoot);

            _content = new VisualElement();
            _content.style.paddingLeft = 12;
            _content.style.paddingRight = 12;
            _content.style.paddingTop = 8;
            _content.style.paddingBottom = 80;
            _zoomRoot.Add(_content);

            _viewport.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _viewport.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                _panning = false;

                // Захват потерян (окно потеряло фокус, Alt+Tab, пересборка
                // дерева). Неважно, начался ли уже перенос: снять нужно и
                // состояние кандидата, иначе первое же движение мыши над
                // полотном начнёт «фантомный» перенос.
                if (_blockDragging || !string.IsNullOrEmpty(_dragId)) CancelBlockDrag();
            });

            // Отмена указателя (перетаскивание перехватила система).
            _viewport.RegisterCallback<PointerCancelEvent>(evt => CancelBlockDrag());

            // Перетаскивание блоков внутри полотна.
            _viewport.RegisterCallback<PointerMoveEvent>(OnBlockDragMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnBlockDragUp);

            // Клик по пустому месту снимает выделение.
            _content.RegisterCallback<PointerDownEvent>(evt =>
            {
                var t = evt.target as VisualElement;
                if (t != null && t.GetFirstAncestorOfType<Nsg_BlockView>() != null) return;
                // Карточка чертежа — тоже не пустое место: иначе нажатие с
                // Shift сбрасывало бы накопленное выделение до выбора карточки.
                if (Nsg_BlueprintView.InsideCard(t)) return;
                ClearSelection();
            }, TrickleDown.TrickleDown);

            ApplyTransform();
        }

        // ------------------------------------------------------------------
        // Масштаб и панорамирование
        // ------------------------------------------------------------------

        void OnWheel(WheelEvent evt)
        {
            float old = _zoom;
            float next = Mathf.Clamp(_zoom * (evt.delta.y > 0f ? ZoomStep : 1f / ZoomStep),
                                     ZoomMin, ZoomMax);
            if (Mathf.Approximately(old, next)) return;

            // Точка под курсором остаётся на месте:
            //   экран = pan + точка * zoom, значит
            //   pan' = экран - (экран - pan) * (zoom' / zoom)
            Vector2 anchor = evt.localMousePosition;
            _pan = anchor - (anchor - _pan) * (next / old);
            _zoom = next;

            ApplyTransform();
            evt.StopPropagation();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            bool panButton = evt.button == 2 || evt.button == 1 ||
                             (evt.button == 0 && (evt.altKey || evt.shiftKey));
            if (!panButton) return;

            _panning = true;
            _panStart = evt.position;
            _panOrigin = _pan;
            _viewport.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_panning) return;
            Vector2 delta = (Vector2)evt.position - _panStart;
            _pan = _panOrigin + delta;
            ApplyTransform();
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_panning) return;
            _panning = false;
            if (_viewport.HasPointerCapture(evt.pointerId)) _viewport.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void ApplyTransform()
        {
            if (_zoomRoot == null) return;
            _zoomRoot.transform.scale = new Vector3(_zoom, _zoom, 1f);
            _zoomRoot.transform.position = new Vector3(_pan.x, _pan.y, 0f);
            if (_zoomLabel != null) _zoomLabel.text = Mathf.RoundToInt(_zoom * 100f) + "%";

            // Сетка рисуется по текущему смещению и масштабу, поэтому её нужно
            // перерисовать вместе с содержимым.
            if (_gridLayer != null) _gridLayer.MarkDirtyRepaint();
        }

        /// <summary>
        /// Фоновая сетка. Линии собираются в один путь: по отдельному Stroke на
        /// каждую линию было бы в разы дороже, а линий здесь сотни.
        /// </summary>
        void OnGenerateGrid(MeshGenerationContext mgc)
        {
            if (_gridLayer == null) return;

            float step = GridStep * _zoom;

            // Ниже этого шага линии сливаются в заливку, и рисовать нечего.
            if (step < 6f) return;

            Vector2 size = _gridLayer.layout.size;
            if (size.x < 2f || size.y < 2f) return;

            var p = mgc.painter2D;
            p.lineWidth = 1f;

            // Остаток берётся со знаком, поэтому сдвигаем его в (-step, 0]:
            // иначе при отрицательном смещении первая линия уезжала бы вправо.
            float ox = _pan.x % step;
            if (ox > 0f) ox -= step;
            float oy = _pan.y % step;
            if (oy > 0f) oy -= step;

            p.strokeColor = GridMinorColor;
            p.BeginPath();
            for (float x = ox; x < size.x; x += step)
            {
                p.MoveTo(new Vector2(x, 0f));
                p.LineTo(new Vector2(x, size.y));
            }
            for (float y = oy; y < size.y; y += step)
            {
                p.MoveTo(new Vector2(0f, y));
                p.LineTo(new Vector2(size.x, y));
            }
            p.Stroke();

            float big = step * GridMajorEvery;
            if (big < 12f) return;

            float bx = _pan.x % big;
            if (bx > 0f) bx -= big;
            float by = _pan.y % big;
            if (by > 0f) by -= big;

            p.strokeColor = GridMajorColor;
            p.BeginPath();
            for (float x = bx; x < size.x; x += big)
            {
                p.MoveTo(new Vector2(x, 0f));
                p.LineTo(new Vector2(x, size.y));
            }
            for (float y = by; y < size.y; y += big)
            {
                p.MoveTo(new Vector2(0f, y));
                p.LineTo(new Vector2(size.x, y));
            }
            p.Stroke();
        }

        /// <summary>Вписать содержимое в окно.</summary>
        public void FitToView()
        {
            if (_content == null || _viewport == null) return;

            float contentHeight = _content.layout.height;
            float viewHeight = _viewport.layout.height;
            if (contentHeight <= 1f || viewHeight <= 1f) return;

            _zoom = Mathf.Clamp(viewHeight / contentHeight, ZoomMin, 1f);
            _pan = new Vector2(0f, 0f);
            ApplyTransform();
        }

        public void SetZoom(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, ZoomMin, ZoomMax);
            ApplyTransform();
        }

        public void SetZoomLabel(Label label)
        {
            _zoomLabel = label;
            ApplyTransform();
        }

        // ==================================================================
        // Отрисовка
        // ==================================================================

        public void Rebuild()
        {
            if (_content == null) BuildShell();

            // Масштаб и смещение сохраняются: пересборка не должна дёргать вид.
            _views.Clear();
            _bpViews.Clear();
            _content.Clear();

            if (Doc == null || Doc.Model == null)
            {
                var hint = new Label(Nsg_L10n.T("win.noDoc"));
                hint.style.color = new Color(0.6f, 0.63f, 0.68f);
                hint.style.marginTop = 20;
                _content.Add(hint);
                return;
            }

            SeedUid();
            _firstScratch = null;
            // Ссылка на прежний граф: иначе после смены документа запасная
            // цель броска указывала бы в исчезнувшую модель.
            _lastStack = null;

            // Подсветка рисков должна быть готова ДО построения блоков: вид
            // блока спрашивает находку в своём конструкторе.
            RefreshHealth();

            var model = Doc.Model;
            for (int i = 0; i < model.roots.Count; i++)
            {
                var el = BuildStructNode(model.roots[i], 0);
                if (el != null) _content.Add(el);
            }

            // Фон скрипта тоже принимает блоки: иначе брошенное ниже последнего
            // метода просто пропадало. Кладём в отвал первого метода.
            _content.userData = _firstScratch;

            // Выделение не должно ссылаться на исчезнувшие узлы.
            var alive = new HashSet<string>(_views.Keys);
            _selected.RemoveWhere(id => !alive.Contains(id));
            RefreshSelection();
        }

        void Refresh()
        {
            Rebuild();
            if (Changed != null) Changed();
        }

        void SeedUid()
        {
            _uid = 0;
            var methods = Doc.Model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i].graph;
                if (g == null) continue;
                for (int k = 0; k < g.nodes.Count; k++)
                {
                    string id = g.nodes[k].id;
                    if (string.IsNullOrEmpty(id) || id[0] != 'u') continue;
                    int v;
                    if (int.TryParse(id.Substring(1), out v) && v > _uid) _uid = v;
                }
            }
        }

        VisualElement BuildStructNode(NsgStructNode node, int depth)
        {
            if (node.kind == "raw") return BuildRawBlock(node, depth);
            if (node.kind == "field") return BuildFieldBlock(node, depth);
            if (node.kind == "method") return BuildMethodFrame(node, depth);
            return BuildContainerFrame(node, depth);
        }

        // ------------------------------------------------------------------
        // Член с семантикой (HLSL): float4 vertex : POSITION;
        // ------------------------------------------------------------------

        static readonly string[] CommonSemantics =
        {
            "POSITION", "NORMAL", "TANGENT", "BINORMAL", "COLOR",
            "TEXCOORD0", "TEXCOORD1", "TEXCOORD2", "TEXCOORD3",
            "BLENDWEIGHTS", "BLENDINDICES", "PSIZE", "FOG",
            "SV_Position", "SV_Target0", "SV_Target1", "SV_Depth",
            "SV_VertexID", "SV_InstanceID", "SV_GroupID", "SV_GroupThreadID"
        };

        VisualElement BuildFieldBlock(NsgStructNode node, int depth)
        {
            var box = new VisualElement();
            box.style.flexDirection = Nsg_Rtl.Row;
            box.style.alignItems = Align.Center;
            Nsg_Rtl.MarginStart(box.style, depth * 12);
            box.style.marginBottom = 2;

            var typeField = new TextField();
            typeField.isDelayed = true;
            typeField.value = node.headerPrefix ?? string.Empty;
            typeField.style.width = 88;
            typeField.RegisterValueChangedCallback(evt => WriteField(node, evt.newValue, null, null));
            box.Add(typeField);

            var nameField = new TextField();
            nameField.isDelayed = true;
            nameField.value = node.name ?? string.Empty;
            nameField.style.width = 88;
            nameField.RegisterValueChangedCallback(evt => WriteField(node, null, evt.newValue, null));
            box.Add(nameField);

            box.Add(Nsg_Visual.Text(":", new Color(0.70f, 0.73f, 0.78f)));

            var semField = new TextField();
            semField.isDelayed = true;
            semField.value = node.baseList ?? string.Empty;
            semField.style.width = 104;
            semField.RegisterValueChangedCallback(evt => WriteField(node, null, null, evt.newValue));
            box.Add(semField);

            var choices = new List<string>(CommonSemantics);
            // Незнакомая семантика остаётся первой в списке, иначе список
            // показывал бы POSITION там, где на самом деле что-то другое.
            if (!string.IsNullOrEmpty(node.baseList) && !choices.Contains(node.baseList))
            {
                choices.Insert(0, node.baseList);
            }

            int pick = choices.IndexOf(node.baseList ?? string.Empty);
            var dd = new DropdownField(choices, pick < 0 ? 0 : pick);
            dd.style.width = 108;
            Nsg_Rtl.MarginStart(dd.style, 3);
            dd.RegisterValueChangedCallback(evt => WriteField(node, null, null, evt.newValue));
            box.Add(dd);

            return box;
        }

        void WriteField(NsgStructNode node, string type, string name, string semantic)
        {
            if (Doc == null || Doc.Model == null) return;

            Undo.PushFull(Doc.Model, "field");

            if (type != null) node.headerPrefix = type.Trim();
            if (name != null) node.name = name.Trim();
            if (semantic != null) node.baseList = semantic.Trim();

            node.text = node.headerPrefix + " " + node.name + " : " + node.baseList + ";";
            Refresh();
        }

        // ------------------------------------------------------------------
        // Дословный член
        // ------------------------------------------------------------------

        VisualElement BuildRawBlock(NsgStructNode node, int depth)
        {
            var box = new VisualElement();
            Nsg_Rtl.MarginStart(box.style, depth * 12);
            box.style.marginBottom = 3;
            box.style.backgroundColor = new Color(0.22f, 0.23f, 0.26f);
            Nsg_Rtl.PaddingStart(box.style, 6);
            Nsg_Rtl.PaddingEnd(box.style, 6);
            box.style.paddingTop = 3;
            box.style.paddingBottom = 3;
            Nsg_Visual.Round(box, 4);

            var row = new VisualElement();
            row.style.flexDirection = Nsg_Rtl.Row;
            row.style.alignItems = Align.Center;
            box.Add(row);

            var tag = new Label(Nsg_L10n.T("tree.raw"));
            tag.style.color = new Color(0.62f, 0.66f, 0.72f);
            Nsg_Rtl.MarginEnd(tag.style, 6);
            tag.style.fontSize = 10;
            row.Add(tag);

            var preview = new Label(Shorten(node.text));
            preview.style.color = new Color(0.80f, 0.83f, 0.87f);
            preview.style.flexGrow = 1;
            preview.style.whiteSpace = WhiteSpace.NoWrap;
            preview.style.overflow = Overflow.Hidden;
            row.Add(preview);

            var field = new TextField();
            field.multiline = true;
            field.style.display = DisplayStyle.None;
            field.style.marginTop = 3;
            field.value = node.text ?? string.Empty;
            box.Add(field);

            var edit = new Button(() =>
            {
                bool showing = field.style.display == DisplayStyle.Flex;
                field.style.display = showing ? DisplayStyle.None : DisplayStyle.Flex;
                preview.style.display = showing ? DisplayStyle.Flex : DisplayStyle.None;
                if (showing) ApplyRawEdit(node, field.value);
            });
            edit.text = "✎";
            edit.style.width = 20;
            edit.style.height = 16;
            Nsg_Rtl.MarginStart(edit.style, 4);
            row.Add(edit);

            var del = new Button(() => DeleteRawMember(node));
            del.text = "×";
            del.style.width = 20;
            del.style.height = 16;
            Nsg_Rtl.MarginStart(del.style, 2);
            row.Add(del);

            return box;
        }

        void ApplyRawEdit(NsgStructNode node, string value)
        {
            if (Doc == null || Doc.Model == null) return;
            if ((node.text ?? string.Empty) == (value ?? string.Empty)) return;
            Undo.PushFull(Doc.Model, "raw");
            node.text = value;
            Refresh();
        }

        void DeleteRawMember(NsgStructNode node)
        {
            if (Doc == null || Doc.Model == null) return;
            Undo.PushFull(Doc.Model, "delete member");
            RemoveChildOf(Doc.Model.roots, node);
            Refresh();
        }

        static bool RemoveChildOf(List<NsgStructNode> list, NsgStructNode target)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == target)
                {
                    // Не даём тексту исчезнуть молча: он уходит вместе с узлом.
                    list.RemoveAt(i);
                    return true;
                }
                if (RemoveChildOf(list[i].children, target)) return true;
            }
            return false;
        }

        static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "…" : s;
        }

        // ------------------------------------------------------------------
        // Рамки
        // ------------------------------------------------------------------

        class Frame
        {
            public VisualElement Root;
            public VisualElement Header;
            public VisualElement Body;
            public Button Toggle;
            public string Key;
        }

        /// <summary>
        /// Свёрнутые рамки структур: class / namespace / void.
        ///
        /// Ключ строится по СОДЕРЖИМОМУ узла, а не по ссылке на объект: вид
        /// пересобирается после каждой правки, и без сохранения состояния
        /// свёрнутая рамка тут же развернулась бы обратно.
        /// </summary>
        readonly HashSet<string> _collapsed = new HashSet<string>();

        static string FrameKey(NsgStructNode node)
        {
            if (node == null) return string.Empty;
            return (node.kind ?? string.Empty) + "|" + (node.name ?? string.Empty)
                 + "|" + (node.header ?? string.Empty);
        }

        static void ApplyCollapsed(VisualElement body, Button toggle, bool collapsed)
        {
            if (body != null) body.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (toggle != null) toggle.text = collapsed ? "▸" : "▾";
        }

        Frame MakeFrame(int depth, Color color, NsgStructNode node)
        {
            var root = new VisualElement();
            Nsg_Rtl.MarginStart(root.style, depth * 12);
            root.style.marginBottom = 5;

            var header = new VisualElement();
            header.style.flexDirection = Nsg_Rtl.Row;
            header.style.alignItems = Align.Center;
            header.style.flexWrap = Wrap.Wrap;
            header.style.backgroundColor = color;
            Nsg_Rtl.PaddingStart(header.style, 6);
            Nsg_Rtl.PaddingEnd(header.style, 4);
            header.style.paddingTop = 3;
            header.style.paddingBottom = 3;
            header.style.minHeight = 22;
            Nsg_Visual.RoundTop(header, 5);
            Nsg_Visual.ApplySprite(header, "header", color);

            // Переключатель идёт первым: заголовок наполняет вызывающий, и его
            // содержимое должно оказаться после кнопки.
            string key = FrameKey(node);
            var toggle = new Button();
            toggle.style.width = 14;
            toggle.style.height = 14;
            toggle.style.fontSize = 9;
            toggle.style.paddingLeft = 0;
            toggle.style.paddingRight = 0;
            toggle.style.paddingTop = 0;
            toggle.style.paddingBottom = 0;
            toggle.style.marginRight = 4;
            toggle.style.flexShrink = 0;
            toggle.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
            toggle.style.color = Nsg_Palette.TextOn(color);
            toggle.style.unityTextAlign = TextAnchor.MiddleCenter;
            Nsg_Visual.Round(toggle, 3);
            header.Add(toggle);

            // Правая кнопка на заголовке — превратить метод или тип в свой блок.
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return;
                ShowMacroMenu(node);
                evt.StopPropagation();
            });

            root.Add(header);

            var body = new VisualElement();
            Nsg_Rtl.BorderStart(body.style, Nsg_Settings.Instance.bodyIndent, color);
            Nsg_Rtl.PaddingStart(body.style, 6);
            body.style.paddingTop = 4;
            body.style.paddingBottom = 2;
            root.Add(body);

            var footer = new VisualElement();
            footer.style.height = 8;
            footer.style.backgroundColor = color;
            Nsg_Visual.RoundBottom(footer, 5);
            Nsg_Visual.ApplySprite(footer, "footer", color);
            root.Add(footer);

            var frame = new Frame { Root = root, Header = header, Body = body, Toggle = toggle, Key = key };

            if (string.IsNullOrEmpty(key))
            {
                // У узла без стабильного ключа сворачивать нечего: кнопку прячем,
                // иначе она выглядела бы рабочей, но состояние не сохранялось бы.
                toggle.style.display = DisplayStyle.None;
            }
            else
            {
                ApplyCollapsed(body, toggle, _collapsed.Contains(key));
                toggle.clicked += () =>
                {
                    bool collapsed = !_collapsed.Contains(key);
                    if (collapsed) _collapsed.Add(key); else _collapsed.Remove(key);
                    ApplyCollapsed(body, toggle, collapsed);
                };
            }

            return frame;
        }

        VisualElement BuildMethodFrame(NsgStructNode node, int depth)
        {
            var color = Nsg_Palette.Frame;
            var frame = MakeFrame(depth, color, node);

            var sig = new Label(string.IsNullOrEmpty(node.name) ? node.header.Trim() : node.name);
            sig.style.color = Nsg_Palette.TextOn(color);
            sig.style.unityFontStyleAndWeight = FontStyle.Bold;
            sig.tooltip = node.header.Trim();
            frame.Header.Add(sig);

            if (node.graph == null) node.graph = new NsgMethodGraph();

            var scratch = new ScratchTarget { Graph = node.graph, Method = node };

            if (Mode == NsgViewMode.Blueprint)
            {
                // В чертеже узлы стоят свободно и видны все, поэтому отвал не
                // нужен: «брошенных мимо стопки» здесь не существует.
                var bp = new Nsg_BlueprintView(this, node.graph, node, Library);

                // Всё полотно — цель для броска из палитры: брошенный блок
                // дописывается в конец цепочки метода. Без этого в чертеже
                // блоки нельзя было бы добавлять вообще.
                var dropTarget = new StackTarget(node.graph, null, null, node);
                bp.userData = dropTarget;

                // Запасная цель для броска мимо полотна: в чертеже отвала нет,
                // поэтому «брошено не туда» должно значить «в этот метод».
                if (_lastStack == null) _lastStack = dropTarget;

                _bpViews.Add(bp);
                frame.Body.Add(bp);
            }
            else
            {
                frame.Body.Add(BuildStack(new StackTarget(node.graph, null, null, node), depth + 1));
                frame.Body.Add(BuildScratchPad(scratch, depth + 1));
            }

            // Всё тело метода — зона отвала. Брошенное мимо стопки не пропадает:
            // раньше целью считалась только узкая полоска отвала, и блок,
            // отпущенный рядом с ней, просто исчезал.
            // Стопка выигрывает, потому что она ближе по дереву.
            frame.Body.userData = scratch;
            frame.Root.userData = scratch;

            return frame.Root;
        }

        /// <summary>
        /// Отвал: сюда падают блоки, брошенные мимо стопки. Они сохраняются, их
        /// можно перетащить обратно, но в код они не попадают — печатается
        /// только то, что достижимо из entry метода.
        /// </summary>
        VisualElement BuildScratchPad(ScratchTarget scratch, int depth)
        {
            var method = scratch != null ? scratch.Method : null;
            if (method == null || method.graph == null) return new VisualElement();

            var reachable = ReachableSet(method.graph);

            var orphans = new List<NsgGraphNode>();
            for (int i = 0; i < method.graph.nodes.Count; i++)
            {
                var n = method.graph.nodes[i];
                if (!reachable.Contains(n.id)) orphans.Add(n);
            }

            var pad = new VisualElement();
            pad.userData = scratch;
            scratch.Container = pad;
            if (_firstScratch == null) _firstScratch = scratch;
            pad.style.marginTop = 6;
            pad.style.marginLeft = 0f;
            pad.style.borderTopWidth = 1;
            pad.style.borderTopColor = new Color(0.35f, 0.36f, 0.40f);
            pad.style.paddingTop = 4;
            pad.style.paddingLeft = 4;
            pad.style.paddingRight = 4;
            pad.style.minHeight = 48;

            var hint = new Label(Nsg_L10n.T("scratch.hint"));
            hint.style.fontSize = 10;
            hint.style.color = new Color(0.55f, 0.58f, 0.63f);
            pad.Add(hint);

            if (orphans.Count == 0) return pad;

            var grid = new VisualElement();
            grid.style.flexDirection = Nsg_Rtl.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginTop = 3;
            pad.Add(grid);

            for (int i = 0; i < orphans.Count; i++)
            {
                var def = Library != null ? Library.Get(orphans[i].block) : null;
                if (def == null) continue;

                var bv = new Nsg_BlockView(orphans[i], def, this, false, method.graph, method, 1, null);
                Nsg_Rtl.MarginEnd(bv.style, 6);
                bv.style.marginBottom = 4;
                bv.style.opacity = 0.75f;
                grid.Add(bv);
            }

            return pad;
        }

        static HashSet<string> ReachableSet(NsgMethodGraph g)
        {
            var seen = new HashSet<string>();
            if (g != null) CollectReachable(g, g.entry, seen);
            return seen;
        }

        /// <summary>
        /// Всё, что достижимо из точки входа метода. Ровно это и печатается в
        /// код; остальные узлы графа — отвал.
        /// </summary>
        static void CollectReachable(NsgMethodGraph g, string head, HashSet<string> seen)
        {
            string cur = head;
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;

                CollectReachable(g, n.body, seen);
                CollectReachable(g, n.els, seen);

                for (int i = 0; i < n.args.Count; i++)
                {
                    if (n.args[i] != null) CollectReachable(g, n.args[i].link, seen);
                }
                for (int i = 0; i < n.extra.Count; i++)
                {
                    if (n.extra[i] != null) CollectReachable(g, n.extra[i].link, seen);
                }

                cur = n.next;
            }
        }

        VisualElement BuildContainerFrame(NsgStructNode node, int depth)
        {
            var color = Nsg_Palette.Frame;
            var frame = MakeFrame(depth, color, node);
            var textColor = Nsg_Palette.TextOn(color);

            if (node.kind == "namespace")
            {
                frame.Header.Add(Nsg_Visual.Text(Nsg_L10n.T("tree.namespace"), textColor));

                var nameField = new TextField();
                nameField.isDelayed = true;
                nameField.value = node.name ?? string.Empty;
                nameField.style.width = 180;
                nameField.RegisterValueChangedCallback(evt => SetNamespaceName(node, evt.newValue));
                frame.Header.Add(nameField);
            }
            else
            {
                frame.Header.Add(Nsg_Visual.Text((node.headerPrefix ?? string.Empty).Trim(), textColor));

                var nameField = new TextField();
                nameField.isDelayed = true;
                nameField.value = node.name ?? string.Empty;
                nameField.style.width = 170;
                nameField.RegisterValueChangedCallback(evt => SetTypeName(node, evt.newValue));
                frame.Header.Add(nameField);

                // У контейнеров ShaderLab (Shader, Properties, Pass, Tags...)
                // базового типа не бывает — список наследования был бы мусором.
                if (!node.isContainer)
                {
                    frame.Header.Add(Nsg_Visual.Text(":", textColor));

                    var baseField = new TextField();
                    baseField.isDelayed = true;
                    baseField.value = node.baseList ?? string.Empty;
                    baseField.style.width = 130;
                    baseField.RegisterValueChangedCallback(evt => SetBaseList(node, evt.newValue));
                    frame.Header.Add(baseField);

                    var choices = new List<string>();
                    choices.Add(Nsg_L10n.T("tree.noBase"));
                    choices.AddRange(CommonBases);
                    var dd = new DropdownField(choices, 0);
                    dd.style.width = 120;
                    Nsg_Rtl.MarginStart(dd.style, 3);
                    dd.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue == Nsg_L10n.T("tree.noBase")) SetBaseList(node, string.Empty);
                        else SetBaseList(node, evt.newValue);
                    });
                    frame.Header.Add(dd);
                }
            }

            for (int i = 0; i < node.children.Count; i++)
            {
                var child = BuildStructNode(node.children[i], depth + 1);
                if (child != null) frame.Body.Add(child);
            }

            if (node.children.Count == 0)
            {
                var empty = new Label(Nsg_L10n.T("tree.bodyEmpty"));
                empty.style.color = new Color(0.55f, 0.58f, 0.62f);
                frame.Body.Add(empty);
            }

            return frame.Root;
        }

        void SetTypeName(NsgStructNode node, string value)
        {
            if (Doc == null || Doc.Model == null) return;
            value = (value ?? string.Empty).Trim();
            if (value == (node.name ?? string.Empty)) return;
            Undo.PushFull(Doc.Model, "rename");
            node.name = value;
            Refresh();
        }

        void SetNamespaceName(NsgStructNode node, string value)
        {
            if (Doc == null || Doc.Model == null) return;
            value = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(value) || value == (node.name ?? string.Empty)) return;

            string header = node.header ?? string.Empty;
            int at = header.IndexOf(node.name ?? string.Empty, System.StringComparison.Ordinal);
            if (at < 0) return;

            Undo.PushFull(Doc.Model, "namespace");
            node.header = header.Substring(0, at) + value + header.Substring(at + (node.name ?? string.Empty).Length);
            node.name = value;
            Refresh();
        }

        void SetBaseList(NsgStructNode node, string value)
        {
            if (Doc == null || Doc.Model == null) return;
            value = (value ?? string.Empty).Trim();
            if (value == (node.baseList ?? string.Empty)) return;

            Undo.PushFull(Doc.Model, "base");
            if (string.IsNullOrEmpty(value))
            {
                node.baseList = string.Empty;
                node.headerBasePrefix = string.Empty;
            }
            else
            {
                if (string.IsNullOrEmpty(node.headerBasePrefix)) node.headerBasePrefix = " : ";
                node.baseList = value;
            }
            Refresh();
        }

        // ==================================================================
        // Стопки операторов
        // ==================================================================

        public class StackTarget
        {
            public NsgMethodGraph Graph;
            public NsgGraphNode OwnerNode;
            public string Field; // "next" | "body" | "els"
            public NsgStructNode Method;

            /// <summary>Контейнер стопки — нужен для показа призрака вставки.</summary>
            public VisualElement Container;

            public StackTarget(NsgMethodGraph graph, NsgGraphNode owner, string field, NsgStructNode method)
            {
                Graph = graph;
                OwnerNode = owner;
                Field = field;
                Method = method;
            }

            public string GetHead()
            {
                if (Graph == null) return null;
                if (OwnerNode == null) return Graph.entry;
                if (Field == "body") return OwnerNode.body;
                if (Field == "els") return OwnerNode.els;
                return OwnerNode.next;
            }

            public void SetHead(string id)
            {
                if (Graph == null) return;
                if (OwnerNode == null)
                {
                    Graph.entry = id;
                    return;
                }
                if (Field == "body") OwnerNode.body = id;
                else if (Field == "els") OwnerNode.els = id;
                else OwnerNode.next = id;
            }
        }

        public VisualElement BuildStack(StackTarget target, int depth)
        {
            var box = new VisualElement();
            box.style.marginTop = 1;

            // Метка цели перетаскивания: палитра ищет её, поднимаясь по
            // предкам элемента под курсором.
            box.userData = target;
            target.Container = box;

            if (target.Graph != null)
            {
                var seen = new HashSet<string>();
                string cur = target.GetHead();

                while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
                {
                    var n = target.Graph.Find(cur);
                    if (n == null) break;

                    var def = Library != null ? Library.Get(n.block) : null;
                    if (def == null)
                    {
                        box.Add(UnknownBlockNotice(n.block));
                        break;
                    }

                    box.Add(new Nsg_BlockView(n, def, this, false, target.Graph, target.Method, depth, target));
                    cur = n.next;
                }
            }

            var add = new Button(() => PickStatement(target));
            add.text = "+ " + Nsg_L10n.T("pick.clickToAdd");
            add.style.height = 18;
            add.style.marginTop = 3;
            add.style.fontSize = 10;
            add.style.alignSelf = Nsg_Rtl.AlignStart;
            Nsg_Rtl.PaddingStart(add.style, 8);
            Nsg_Rtl.PaddingEnd(add.style, 8);
            add.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
            add.style.color = new Color(0.60f, 0.64f, 0.70f);
            add.style.borderTopWidth = 1;
            add.style.borderBottomWidth = 1;
            add.style.borderLeftWidth = 1;
            add.style.borderRightWidth = 1;
            add.style.borderTopColor = new Color(1f, 1f, 1f, 0.18f);
            add.style.borderBottomColor = new Color(1f, 1f, 1f, 0.18f);
            add.style.borderLeftColor = new Color(1f, 1f, 1f, 0.18f);
            add.style.borderRightColor = new Color(1f, 1f, 1f, 0.18f);
            Nsg_Visual.Round(add, 4);
            box.Add(add);

            // Подсказка показывается только у главной цепочки метода: в каждой
            // вложенной ветке она превратилась бы в постоянный шум.
            if (target.OwnerNode == null) box.Add(BuildSuggestStrip(target));

            return box;
        }

        // ==================================================================
        // Свои блоки
        // ==================================================================

        /// <summary>
        /// Меню заголовка структурного узла. Свой блок делается из метода
        /// (печатает вызов и обратим) и из типа (объявляет переменную и
        /// создаёт объект — это шаблон, потому что параметров конструктора
        /// мы не знаем).
        /// </summary>
        void ShowMacroMenu(NsgStructNode node)
        {
            if (node == null) return;

            var menu = new GenericMenu();

            if (node.kind != "method" && node.kind != "type")
            {
                menu.AddDisabledItem(new GUIContent(Nsg_L10n.T("macro.cannot")));
                menu.ShowAsContext();
                return;
            }

            menu.AddItem(new GUIContent(Nsg_L10n.T("macro.create")), false, () =>
            {
                string initial = string.IsNullOrEmpty(node.name) ? "MyBlock" : node.name;

                Nsg_NameDialog.Show(Nsg_L10n.T("macro.create"), initial, name =>
                {
                    var def = Nsg_Macro.Build(node, name);
                    if (def == null)
                    {
                        Debug.LogWarning("[NekoScriptGraph] " + Nsg_L10n.T("macro.cannot"));
                        return;
                    }

                    string path = Nsg_Macro.Save(def);
                    Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("macro.saved", path));

                    if (LibraryChanged != null) LibraryChanged();
                });
            });

            menu.ShowAsContext();
        }

        // ==================================================================
        // Подсказка следующего блока
        // ==================================================================

        const int SuggestCount = 5;

        /// <summary>
        /// Смещение курсора по горизонтали, после которого список закрывается.
        /// Нужна мёртвая зона: без неё список схлопывался бы от дрожания руки.
        /// </summary>
        const float SuggestDeadZone = 40f;

        /// <summary>
        /// Полоса подсказок в конце стопки: полупрозрачный «призрак» самого
        /// вероятного блока, а по наведению — список вариантов.
        ///
        /// Призрак не является частью модели: он не печатается и не сохраняется,
        /// это только предположение.
        /// </summary>
        /// <summary>
        /// Блок, после которого вставляют — для переходной подсказки.
        ///
        /// Полоса стоит в КОНЦЕ списка, поэтому «сосед» — хвост этого списка,
        /// а не владелец. Если список пуст, ориентир — блок, который его
        /// открыл: подсказать, что положить внутрь if, тоже полезно.
        /// </summary>
        static string PrecedingBlock(StackTarget target)
        {
            if (target == null || target.Graph == null) return null;

            var tail = TailOf(target.Graph, target.GetHead());
            if (tail != null) return tail.block;

            return target.OwnerNode != null ? target.OwnerNode.block : null;
        }

        static NsgGraphNode TailOf(NsgMethodGraph g, string head)
        {
            if (g == null || string.IsNullOrEmpty(head)) return null;

            var guard = new HashSet<string>();
            NsgGraphNode last = null;
            string cur = head;

            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;
                last = n;
                cur = n.next;
            }
            return last;
        }

        VisualElement BuildSuggestStrip(StackTarget target)
        {
            var box = new VisualElement();
            if (target == null || target.Graph == null || Library == null) return box;

            var list = Nsg_Autocomplete.Suggest(target.Graph, Library, true, SuggestCount,
                                               PrecedingBlock(target));
            if (list.Count == 0) return box;

            box.style.marginTop = 2;
            box.style.alignSelf = Nsg_Rtl.AlignStart;

            var first = list[0].Def;
            Color firstColor = first.BlockColor();

            var ghost = new Button(() => AppendStatement(target, first.id));
            ghost.text = first.Label();
            ghost.style.height = 18;
            ghost.style.fontSize = 10;
            ghost.style.alignSelf = Nsg_Rtl.AlignStart;
            ghost.style.opacity = 0.40f;
            ghost.style.backgroundColor = firstColor;
            ghost.style.color = Nsg_Palette.TextOn(firstColor);
            ghost.style.unityTextAlign = Nsg_Rtl.TextAlign;
            Nsg_Rtl.PaddingStart(ghost.style, 8);
            Nsg_Rtl.PaddingEnd(ghost.style, 8);
            ghost.tooltip = first.id;
            Nsg_Visual.Round(ghost, 4);
            box.Add(ghost);

            var panel = new VisualElement();
            panel.style.display = DisplayStyle.None;
            panel.style.marginTop = 2;
            panel.style.alignSelf = Nsg_Rtl.AlignStart;
            panel.style.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
            panel.style.borderTopWidth = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderTopColor = new Color(0.35f, 0.38f, 0.44f);
            panel.style.borderBottomColor = new Color(0.35f, 0.38f, 0.44f);
            panel.style.borderLeftColor = new Color(0.35f, 0.38f, 0.44f);
            panel.style.borderRightColor = new Color(0.35f, 0.38f, 0.44f);
            panel.style.paddingLeft = 3;
            panel.style.paddingRight = 3;
            panel.style.paddingTop = 3;
            panel.style.paddingBottom = 3;
            panel.focusable = true;
            Nsg_Visual.Round(panel, 4);
            box.Add(panel);

            var items = new List<Button>();
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                var item = new Button(() => AppendStatement(target, list[index].Def.id));
                item.text = list[i].Def.Label();
                item.style.height = 17;
                item.style.fontSize = 10;
                item.style.marginBottom = 1;
                item.style.unityTextAlign = Nsg_Rtl.TextAlign;
                item.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
                item.style.color = new Color(0.80f, 0.84f, 0.89f);
                items.Add(item);
                panel.Add(item);
            }

            int highlight = 0;

            System.Action refresh = () =>
            {
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].style.backgroundColor = i == highlight
                        ? new Color(0.28f, 0.42f, 0.62f)
                        : new Color(1f, 1f, 1f, 0.04f);
                }
            };
            refresh();

            System.Action accept = () =>
            {
                if (highlight >= 0 && highlight < list.Count)
                {
                    AppendStatement(target, list[highlight].Def.id);
                }
            };

            System.Action hide = () =>
            {
                panel.style.display = DisplayStyle.None;
            };

            // Наведение раскрывает список.
            float entryX = 0f;
            ghost.RegisterCallback<PointerEnterEvent>(evt =>
            {
                entryX = evt.position.x;
                panel.style.display = DisplayStyle.Flex;
                panel.Focus();
            });

            // Уход курсора в сторону закрывает: так список не мешает, когда
            // пользователь просто ведёт мышь к другому блоку.
            box.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (panel.style.display.value != DisplayStyle.Flex) return;
                if (Mathf.Abs(evt.position.x - entryX) > SuggestDeadZone) hide();
            });

            box.RegisterCallback<PointerLeaveEvent>(evt => hide());

            // Клавиатура: как в списке подсказок ввода.
            panel.RegisterCallback<KeyDownEvent>(evt =>
            {
                switch (evt.keyCode)
                {
                    case KeyCode.DownArrow:
                        highlight = (highlight + 1) % items.Count;
                        refresh();
                        evt.StopPropagation();
                        break;
                    case KeyCode.UpArrow:
                        highlight = (highlight - 1 + items.Count) % items.Count;
                        refresh();
                        evt.StopPropagation();
                        break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        accept();
                        evt.StopPropagation();
                        break;
                    case KeyCode.Escape:
                        hide();
                        evt.StopPropagation();
                        break;
                }
            });

            // Наведение на пункт списка переставляет подсветку: мышь и
            // клавиатура должны указывать на одно и то же.
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                items[i].RegisterCallback<PointerEnterEvent>(evt =>
                {
                    highlight = index;
                    refresh();
                });
            }

            return box;
        }

        VisualElement UnknownBlockNotice(string blockId)
        {
            var box = new VisualElement();
            box.style.backgroundColor = new Color(0.35f, 0.15f, 0.15f);
            box.style.paddingLeft = 6;
            box.style.paddingRight = 6;
            box.style.paddingTop = 3;
            box.style.paddingBottom = 3;
            Nsg_Visual.Round(box, 4);

            var l = new Label(NsgCodes.UnknownBlock + ": " + blockId);
            l.style.color = new Color(1f, 0.7f, 0.7f);
            box.Add(l);

            if (Doc != null)
            {
                // Этот код выполняется при каждой пересборке стека, а список
                // диагностики чистится только при импорте: без проверки одна
                // неизвестная деталь добавляла бы по одинаковой ошибке на
                // каждый Rebuild.
                string hint = Nsg_L10n.T("msg.unknownBlockHint", blockId);
                if (!Doc.Diagnostics.Has(NsgCodes.UnknownBlock, hint))
                    Doc.Diagnostics.Error(NsgCodes.UnknownBlock, hint);
            }

            return box;
        }

        // ==================================================================
        // Выделение и буфер обмена
        // ==================================================================

        readonly Dictionary<string, Nsg_BlockView> _views = new Dictionary<string, Nsg_BlockView>();
        readonly HashSet<string> _selected = new HashSet<string>();
        StackTarget _lastStack;

        /// <summary>Общий буфер: переживает пересборку окна.</summary>
        public class NsgClipboard
        {
            public readonly List<NsgGraphNode> Nodes = new List<NsgGraphNode>();
            public readonly List<string> Roots = new List<string>();

            public bool IsEmpty
            {
                get { return Nodes.Count == 0; }
            }

            public int Count
            {
                get { return Nodes.Count; }
            }
        }

        public static NsgClipboard Clipboard = new NsgClipboard();

        public void RegisterView(string id, Nsg_BlockView view)
        {
            if (!string.IsNullOrEmpty(id) && view != null) _views[id] = view;
        }

        public int SelectionCount
        {
            get { return _selected.Count; }
        }

        public void OnBlockPointerDown(string id, StackTarget target, bool additive, PointerDownEvent evt)
        {
            if (target != null) _lastStack = target;
            SelectNode(id, additive);
        }

        public void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            RefreshSelection();
        }

        void RefreshSelection()
        {
            foreach (var kv in _views)
            {
                if (kv.Value != null) kv.Value.SetSelected(_selected.Contains(kv.Key));
            }

            // Полотна чертежа держат свои карточки сами: они не Nsg_BlockView,
            // поэтому выделение им передаётся отдельно.
            for (int i = 0; i < _bpViews.Count; i++)
            {
                if (_bpViews[i] != null) _bpViews[i].ApplySelection(_selected);
            }
        }

        /// <summary>Выделение узла. Общее для стопки и чертежа.</summary>
        public void SelectNode(string id, bool additive)
        {
            if (string.IsNullOrEmpty(id)) return;

            if (additive)
            {
                if (!_selected.Add(id)) _selected.Remove(id);
            }
            else
            {
                if (_selected.Count == 1 && _selected.Contains(id)) return;
                _selected.Clear();
                _selected.Add(id);
            }

            RefreshSelection();
        }

        public bool IsSelected(string id)
        {
            return !string.IsNullOrEmpty(id) && _selected.Contains(id);
        }

        // ==================================================================
        // Риски: подсветка и предложения по исправлению
        // ==================================================================

        /// <summary>
        /// Порог, выше которого подсветка рисков отключается: полный анализ
        /// гигиены на каждую пересборку заметно тормозил бы редактор на
        /// больших файлах. Число — компромисс, а не физический предел.
        /// </summary>
        const int HealthNodeLimit = 1500;

        readonly Dictionary<string, Nsg_HealthFinding> _findings =
            new Dictionary<string, Nsg_HealthFinding>();

        VisualElement _fixPopup;

        /// <summary>Находка гигиены для узла, если она есть.</summary>
        public Nsg_HealthFinding FindingOf(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            Nsg_HealthFinding f;
            return _findings.TryGetValue(nodeId, out f) ? f : null;
        }

        void RefreshHealth()
        {
            _findings.Clear();
            if (Doc == null || Doc.Model == null || Library == null) return;

            int nodes = 0;
            var methods = Doc.Model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i].graph;
                if (g != null && g.nodes != null) nodes += g.nodes.Count;
            }
            if (nodes > HealthNodeLimit) return;

            var report = Nsg_Health.Analyze(Doc.Model, Library, null);
            for (int i = 0; i < report.Findings.Count; i++)
            {
                var f = report.Findings[i];
                if (f == null || string.IsNullOrEmpty(f.NodeId)) continue;

                // На узле одна рамка, поэтому оставляем самую серьёзную находку.
                Nsg_HealthFinding cur;
                if (_findings.TryGetValue(f.NodeId, out cur) && cur.Severity >= f.Severity) continue;
                _findings[f.NodeId] = f;
            }
        }

        // ------------------------------------------------------------------
        // Всплывающая подсказка с исправлением
        // ------------------------------------------------------------------

        public void ShowFixPopup(VisualElement anchor, Nsg_HealthFinding finding)
        {
            HideFixPopup();
            if (anchor == null || finding == null || _content == null) return;

            Color accent = finding.Severity == NsgSeverity.Error
                ? new Color(0.95f, 0.25f, 0.25f)
                : new Color(0.95f, 0.60f, 0.20f);

            var popup = new VisualElement();
            popup.style.position = Position.Absolute;
            popup.style.backgroundColor = new Color(0.14f, 0.15f, 0.18f);
            popup.style.borderTopWidth = 1;
            popup.style.borderBottomWidth = 1;
            popup.style.borderLeftWidth = 1;
            popup.style.borderRightWidth = 1;
            popup.style.borderTopColor = accent;
            popup.style.borderBottomColor = accent;
            popup.style.borderLeftColor = accent;
            popup.style.borderRightColor = accent;
            popup.style.paddingLeft = 8;
            popup.style.paddingRight = 8;
            popup.style.paddingTop = 6;
            popup.style.paddingBottom = 6;
            popup.style.maxWidth = 340;
            Nsg_Visual.Round(popup, 4);

            var title = new Label(Nsg_L10n.T("health.fix"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 10;
            title.style.color = accent;
            popup.Add(title);

            var msg = new Label(finding.Message);
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.fontSize = 11;
            msg.style.marginTop = 3;
            msg.style.color = new Color(0.80f, 0.84f, 0.89f);
            popup.Add(msg);

            string fixLabel = FixLabelFor(finding);
            if (string.IsNullOrEmpty(fixLabel))
            {
                var none = new Label(Nsg_L10n.T("health.fixNone"));
                none.style.fontSize = 10;
                none.style.marginTop = 5;
                none.style.color = new Color(0.58f, 0.62f, 0.68f);
                popup.Add(none);
            }
            else
            {
                var apply = new Button(() =>
                {
                    HideFixPopup();
                    ApplyFix(finding);
                });
                apply.text = Nsg_L10n.T("health.fixApply") + ": " + fixLabel;
                apply.style.marginTop = 5;
                apply.style.fontSize = 10;
                apply.style.unityTextAlign = Nsg_Rtl.TextAlign;
                popup.Add(apply);
            }

            // Ставим под блоком, в координатах содержимого полотна.
            var rect = anchor.worldBound;
            Vector2 pos = _content.WorldToLocal(new Vector2(rect.xMin, rect.yMax));
            popup.style.left = pos.x;
            popup.style.top = pos.y + 4f;

            _content.Add(popup);
            _fixPopup = popup;
        }

        public void HideFixPopup()
        {
            if (_fixPopup == null) return;
            _fixPopup.RemoveFromHierarchy();
            _fixPopup = null;
        }

        static string FixLabelFor(Nsg_HealthFinding f)
        {
            if (f == null) return null;

            switch (f.RuleId)
            {
                case "graph.cycle": return Nsg_L10n.T("health.fixBreakLink");
                case "input.dangling": return Nsg_L10n.T("health.fixFillZero");
                case "resource.leak": return Nsg_L10n.T("health.fixAddRelease");
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Автоматические исправления
        // ------------------------------------------------------------------

        public void ApplyFix(Nsg_HealthFinding f)
        {
            if (f == null || Doc == null || Doc.Model == null || Library == null) return;

            var graph = FindGraphOfId(f.NodeId);
            var node = graph != null ? graph.Find(f.NodeId) : null;
            if (node == null) return;

            bool changed;
            switch (f.RuleId)
            {
                case "graph.cycle": changed = BreakCycle(graph, node); break;
                case "input.dangling": changed = FillDangling(graph, node); break;
                case "resource.leak": changed = AppendRelease(graph, node); break;
                default: return;
            }

            if (changed) Refresh();
        }

        /// <summary>
        /// Разрывает обратную ссылку: ищет слот, который ссылается на сам узел,
        /// и очищает его. Именно эта ссылка и делает граф непечатаемым.
        /// </summary>
        static bool BreakCycle(NsgMethodGraph g, NsgGraphNode node)
        {
            var hit = FindSlotTo(g, node, node.id);
            if (hit == null) return false;

            hit.Slots[hit.Index].link = null;
            return true;
        }

        /// <summary>
        /// Заполняет обязательный пустой вход. Вставляется НАСТОЯЩИЙ литерал,
        /// а не текст в слот: только узел гарантированно печатается как
        /// выражение. Значение — ноль, пользователь потом заменит.
        /// </summary>
        bool FillDangling(NsgMethodGraph g, NsgGraphNode node)
        {
            int index = FindEmptyRequired(node);
            if (index < 0) return false;

            var literal = new NsgGraphNode { id = NewNodeId(g, "fix"), block = "expr.literal" };
            literal.EnsureArg(0).text = "0";
            g.nodes.Add(literal);

            var slot = node.EnsureArg(index);
            slot.link = literal.id;
            slot.text = null;
            return true;
        }

        /// <summary>
        /// Дописывает освобождение сразу после оператора с выделением.
        /// Проверка утечки грубая, и правка тоже: без анализа потока управления
        /// нельзя понять, где именно освобождать.
        /// </summary>
        bool AppendRelease(NsgMethodGraph g, NsgGraphNode acquire)
        {
            string release = Nsg_Health.ReleaseFor(acquire.block);
            if (string.IsNullOrEmpty(release)) return false;
            if (Library.Get(release) == null) return false;

            var owner = FindStatementOwner(g, acquire.id);
            if (owner == null) return false;

            var rel = new NsgGraphNode { id = NewNodeId(g, "fix"), block = release };
            rel.EnsureArg(0).link = acquire.id;
            g.nodes.Add(rel);

            rel.next = owner.next;
            owner.next = rel.id;
            return true;
        }

        int FindEmptyRequired(NsgGraphNode node)
        {
            var def = Library != null ? Library.Get(node.block) : null;
            if (def == null || def.sockets == null) return -1;

            for (int s = 0; s < def.sockets.Length; s++)
            {
                var socket = def.sockets[s];
                if (socket == null || !socket.required || socket.kind != "expr") continue;

                var slot = s < node.args.Count ? node.args[s] : null;
                if (slot != null && (!string.IsNullOrEmpty(slot.link) || !string.IsNullOrEmpty(slot.text))) continue;
                return s;
            }
            return -1;
        }

        class SlotRef
        {
            public List<NsgSlot> Slots;
            public int Index;
        }

        static SlotRef FindSlotTo(NsgMethodGraph g, NsgGraphNode from, string target)
        {
            if (g == null || from == null || string.IsNullOrEmpty(target)) return null;
            return FindSlotWalk(g, from, target, new HashSet<string>());
        }

        static SlotRef FindSlotWalk(NsgMethodGraph g, NsgGraphNode n, string target, HashSet<string> seen)
        {
            if (n == null || string.IsNullOrEmpty(n.id) || !seen.Add(n.id)) return null;

            var hit = FindInList(n.args, target);
            if (hit != null) return hit;

            hit = FindInList(n.extra, target);
            if (hit != null) return hit;

            return WalkSlotFind(g, n.args, target, seen) ?? WalkSlotFind(g, n.extra, target, seen);
        }

        static SlotRef WalkSlotFind(NsgMethodGraph g, List<NsgSlot> slots, string target, HashSet<string> seen)
        {
            if (slots == null) return null;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || string.IsNullOrEmpty(s.link)) continue;

                var hit = FindSlotWalk(g, g.Find(s.link), target, seen);
                if (hit != null) return hit;
            }
            return null;
        }

        static SlotRef FindInList(List<NsgSlot> slots, string target)
        {
            if (slots == null) return null;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.link != target) continue;
                return new SlotRef { Slots = slots, Index = i };
            }
            return null;
        }

        /// <summary>Оператор, в поддереве которого находится узел.</summary>
        static NsgGraphNode FindStatementOwner(NsgMethodGraph g, string nodeId)
        {
            if (g == null || string.IsNullOrEmpty(nodeId)) return null;

            var statements = new List<NsgGraphNode>();
            CollectChainNodes(g, g.entry, new HashSet<string>(), statements);

            for (int i = 0; i < statements.Count; i++)
            {
                if (ContainsNode(g, statements[i].id, nodeId, new HashSet<string>())) return statements[i];
            }
            return null;
        }

        static void CollectChainNodes(NsgMethodGraph g, string head, HashSet<string> seen,
                                      List<NsgGraphNode> into)
        {
            string cur = head;
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;

                into.Add(n);
                CollectChainNodes(g, n.body, seen, into);
                CollectChainNodes(g, n.els, seen, into);
                cur = n.next;
            }
        }

        static bool ContainsNode(NsgMethodGraph g, string rootId, string wanted, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(rootId) || !seen.Add(rootId)) return false;
            if (rootId == wanted) return true;

            var n = g.Find(rootId);
            if (n == null) return false;

            if (FindInList(n.args, wanted) != null) return true;
            if (FindInList(n.extra, wanted) != null) return true;

            return WalkContains(g, n.args, wanted, seen) || WalkContains(g, n.extra, wanted, seen);
        }

        static bool WalkContains(NsgMethodGraph g, List<NsgSlot> slots, string wanted, HashSet<string> seen)
        {
            if (slots == null) return false;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || string.IsNullOrEmpty(s.link)) continue;
                if (ContainsNode(g, s.link, wanted, seen)) return true;
            }
            return false;
        }

        static string NewNodeId(NsgMethodGraph g, string prefix)
        {
            for (int i = 0; i < 100000; i++)
            {
                string id = prefix + i;
                if (g.Find(id) == null) return id;
            }
            return prefix + System.Guid.NewGuid().ToString("N");
        }

        NsgMethodGraph FindGraphOfId(string id)
        {
            if (Doc == null || Doc.Model == null) return null;

            var methods = Doc.Model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i].graph;
                if (g != null && g.Find(id) != null) return g;
            }
            return null;
        }

        NsgStructNode FindMethodOfGraph(NsgMethodGraph g)
        {
            if (Doc == null || Doc.Model == null) return null;

            var methods = Doc.Model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                if (methods[i].graph == g) return methods[i];
            }
            return null;
        }

        Dictionary<NsgMethodGraph, List<string>> SelectionByGraph()
        {
            var map = new Dictionary<NsgMethodGraph, List<string>>();

            foreach (var id in _selected)
            {
                var g = FindGraphOfId(id);
                if (g == null) continue;

                List<string> list;
                if (!map.TryGetValue(g, out list))
                {
                    list = new List<string>();
                    map[g] = list;
                }
                list.Add(id);
            }
            return map;
        }

        /// <summary>Порядок узлов в обходе — нужен для стабильной сортировки корней.</summary>
        static Dictionary<string, int> OrderIndex(NsgMethodGraph g)
        {
            var order = new Dictionary<string, int>();
            int counter = 0;
            CollectOrder(g, g.entry, order, ref counter);
            return order;
        }

        static void CollectOrder(NsgMethodGraph g, string head, Dictionary<string, int> order, ref int counter)
        {
            var seen = new HashSet<string>();
            string cur = head;
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;
                if (!order.ContainsKey(cur)) order[cur] = counter++;

                CollectOrder(g, n.body, order, ref counter);
                CollectOrder(g, n.els, order, ref counter);

                cur = n.next;
            }
        }

        /// <summary>Выделенные узлы, у которых предок не выделен — корни поддеревьев.</summary>
        List<string> RootsOf(NsgMethodGraph g, List<string> ids)
        {
            var set = new HashSet<string>(ids);
            var roots = new List<string>();

            for (int i = 0; i < ids.Count; i++)
            {
                var incoming = FindIncoming(g, ids[i]);
                if (incoming != null && incoming.Node != null && set.Contains(incoming.Node.id)) continue;
                roots.Add(ids[i]);
            }

            var order = OrderIndex(g);
            roots.Sort((a, b) =>
            {
                int ia, ib;
                order.TryGetValue(a, out ia);
                order.TryGetValue(b, out ib);
                return ia.CompareTo(ib);
            });

            return roots;
        }

        // ------------------------------------------------------------------
        // Копировать / вставить / дублировать / удалить
        // ------------------------------------------------------------------

        public void CopySelection()
        {
            var map = SelectionByGraph();
            if (map.Count == 0) return;

            // Берём первую группу: выделение почти всегда внутри одного метода.
            NsgMethodGraph graph = null;
            List<string> ids = null;
            foreach (var kv in map)
            {
                graph = kv.Key;
                ids = kv.Value;
                break;
            }

            var roots = RootsOf(graph, ids);
            if (roots.Count == 0) return;

            Clipboard.Nodes.Clear();
            Clipboard.Roots.Clear();

            for (int i = 0; i < roots.Count; i++)
            {
                Clipboard.Roots.Add(roots[i]);

                var subtree = new HashSet<string>();
                CollectSubtreeNoNext(graph, roots[i], subtree);

                foreach (var id in subtree)
                {
                    var src = graph.Find(id);
                    if (src != null) Clipboard.Nodes.Add(Nsg_Cloner.CloneNode(src));
                }
            }
        }

        public bool PasteClipboard()
        {
            if (Clipboard == null || Clipboard.IsEmpty) return false;
            return InsertNodes(Clipboard.Nodes, Clipboard.Roots, "paste");
        }

        /// <summary>
        /// Готовит набор из выделения. Набор обязан быть ОДНОЙ связной группой:
        /// иначе при вставке непонятно, что с чем соединять. Возвращает null и
        /// причину, если условие не выполнено.
        /// </summary>
        public Nsg_Preset BuildPresetFromSelection(out string error)
        {
            error = null;

            var map = SelectionByGraph();
            if (map.Count == 0)
            {
                error = Nsg_L10n.T("preset.noSelection");
                return null;
            }
            if (map.Count > 1)
            {
                error = Nsg_L10n.T("preset.multiGraph");
                return null;
            }

            NsgMethodGraph graph = null;
            List<string> ids = null;
            foreach (var kv in map)
            {
                graph = kv.Key;
                ids = kv.Value;
                break;
            }

            var roots = RootsOf(graph, ids);
            if (roots.Count != 1)
            {
                error = Nsg_L10n.T("preset.notConnected", roots.Count);
                return null;
            }

            string root = roots[0];
            var collected = new HashSet<string>();
            CollectSubtree(graph, root, collected);

            if (collected.Count > Nsg_Presets.MaxBlocks)
            {
                error = Nsg_L10n.T("preset.tooBig", collected.Count, Nsg_Presets.MaxBlocks);
                return null;
            }

            var preset = new Nsg_Preset();
            preset.rootId = root;
            preset.languageId = Doc != null ? Doc.LanguageId : null;

            foreach (var id in collected)
            {
                var n = graph.Find(id);
                if (n != null) preset.nodes.Add(Nsg_Cloner.CloneNode(n));
            }

            preset.blockCount = preset.nodes.Count;
            return preset;
        }

        public bool PastePreset(Nsg_Preset preset)
        {
            if (preset == null || preset.nodes == null || preset.nodes.Count == 0) return false;

            return InsertNodes(preset.nodes, PresetRoots(preset), "preset");
        }

        /// <summary>
        /// Вставляет набор в конкретную цель броска. Так набор можно положить
        /// в нужную ветку, а не только в конец последней стопки.
        /// </summary>
        public bool DropPreset(DropTarget drop, Nsg_Preset preset)
        {
            if (preset == null || preset.nodes == null || preset.nodes.Count == 0) return false;
            if (drop == null || drop.Kind == DropKind.None) return PastePreset(preset);

            if (drop.Kind == DropKind.Stack && drop.Stack != null)
            {
                return InsertNodes(preset.nodes, PresetRoots(preset), "preset", drop.Stack);
            }

            if (drop.Kind == DropKind.Scratch && drop.Scratch != null)
            {
                var t = new StackTarget(drop.Scratch.Graph, null, null, drop.Scratch.Method);
                return InsertNodes(preset.nodes, PresetRoots(preset), "preset", t);
            }

            return PastePreset(preset);
        }

        static List<string> PresetRoots(Nsg_Preset preset)
        {
            var roots = new List<string>();
            if (preset != null && !string.IsNullOrEmpty(preset.rootId)) roots.Add(preset.rootId);
            return roots;
        }

        /// <summary>
        /// Вставляет узлы в конец текущей стопки (или стопки первого метода),
        /// с новыми идентификаторами, и выделяет вставленное.
        /// </summary>
        bool InsertNodes(List<NsgGraphNode> nodes, List<string> roots, string label)
        {
            return InsertNodes(nodes, roots, label, null);
        }

        /// <summary>
        /// preferred — цель броска. При перетаскивании набора из панели он
        /// должен попасть туда, куда его бросили, а не в последнюю стопку.
        /// </summary>
        bool InsertNodes(List<NsgGraphNode> nodes, List<string> roots, string label, StackTarget preferred)
        {
            if (nodes == null || nodes.Count == 0) return false;

            var target = preferred;
            if (target == null || target.Graph == null) target = _lastStack;
            if (target == null || target.Graph == null) target = FirstStack();
            if (target == null) return false;

            Push(target.Method, label);

            var map = new Dictionary<string, string>();
            for (int i = 0; i < nodes.Count; i++) map[nodes[i].id] = NextId();

            for (int i = 0; i < nodes.Count; i++)
            {
                var c = Nsg_Cloner.CloneNode(nodes[i]);
                c.id = map[c.id];
                c.next = Remap(c.next, map);
                c.body = Remap(c.body, map);
                c.els = Remap(c.els, map);

                for (int k = 0; k < c.args.Count; k++)
                {
                    if (c.args[k] != null) c.args[k].link = Remap(c.args[k].link, map);
                }
                for (int k = 0; k < c.extra.Count; k++)
                {
                    if (c.extra[k] != null) c.extra[k].link = Remap(c.extra[k].link, map);
                }

                target.Graph.nodes.Add(c);
            }

            var pastedRoots = new List<string>();
            if (roots != null)
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    string id = Remap(roots[i], map);
                    if (!string.IsNullOrEmpty(id) && target.Graph.Find(id) != null) pastedRoots.Add(id);
                }
            }
            if (pastedRoots.Count == 0) { Refresh(); return false; }

            for (int i = 0; i < pastedRoots.Count - 1; i++)
            {
                var n = target.Graph.Find(pastedRoots[i]);
                if (n != null) n.next = pastedRoots[i + 1];
            }

            string head = pastedRoots[0];
            string stackHead = target.GetHead();

            if (string.IsNullOrEmpty(stackHead))
            {
                target.SetHead(head);
            }
            else
            {
                var last = LastInChain(target.Graph, stackHead);
                if (last != null) last.next = head;
                else target.SetHead(head);
            }

            _selected.Clear();
            CollectSubtree(target.Graph, head, _selected);
            Refresh();
            return true;
        }

        /// <summary>
        /// Cmd/Ctrl+D: копирует выделение, вставляет рядом, снимает выделение со
        /// старых и выделяет вставленные.
        /// </summary>
        public bool DuplicateSelection()
        {
            if (_selected.Count == 0) return false;

            CopySelection();
            if (Clipboard.IsEmpty) return false;

            return PasteClipboard();
        }

        public bool DeleteSelection()
        {
            if (_selected.Count == 0) return false;

            var map = SelectionByGraph();
            if (map.Count == 0) return false;

            foreach (var kv in map)
            {
                var g = kv.Key;
                Push(FindMethodOfGraph(g), "delete");

                var doomed = new HashSet<string>();
                var roots = RootsOf(g, kv.Value);
                for (int i = 0; i < roots.Count; i++) CollectSubtreeNoNext(g, roots[i], doomed);

                // Всё, что вело в удаляемый узел, переводим на живого преемника.
                for (int i = 0; i < g.nodes.Count; i++)
                {
                    var n = g.nodes[i];
                    if (doomed.Contains(n.id)) continue;

                    Redirect(g, n, "next", doomed);
                    Redirect(g, n, "body", doomed);
                    Redirect(g, n, "els", doomed);

                    for (int k = 0; k < n.args.Count; k++)
                    {
                        if (n.args[k] != null && doomed.Contains(n.args[k].link)) n.args[k].link = null;
                    }
                    for (int k = 0; k < n.extra.Count; k++)
                    {
                        if (n.extra[k] != null && doomed.Contains(n.extra[k].link)) n.extra[k].link = null;
                    }
                }

                if (doomed.Contains(g.entry)) g.entry = SurvivingSuccessor(g, g.entry, doomed);

                foreach (var d in doomed) g.Remove(d);
            }

            _selected.Clear();
            Refresh();
            return true;
        }

        void Redirect(NsgMethodGraph g, NsgGraphNode n, string field, HashSet<string> doomed)
        {
            string value = field == "next" ? n.next : (field == "body" ? n.body : n.els);
            if (string.IsNullOrEmpty(value) || !doomed.Contains(value)) return;

            string replacement = SurvivingSuccessor(g, value, doomed);
            if (field == "next") n.next = replacement;
            else if (field == "body") n.body = replacement;
            else n.els = replacement;
        }

        static string SurvivingSuccessor(NsgMethodGraph g, string id, HashSet<string> doomed)
        {
            var seen = new HashSet<string>();
            string cur = id;

            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                if (!doomed.Contains(cur)) return cur;
                var n = g.Find(cur);
                if (n == null) return null;
                cur = n.next;
            }
            return null;
        }

        /// <summary>Стопка первого метода — цель вставки по умолчанию.</summary>
        StackTarget FirstStack()
        {
            if (Doc == null || Doc.Model == null) return null;

            var methods = Doc.Model.AllMethods();
            if (methods.Count == 0) return null;

            if (methods[0].graph == null) methods[0].graph = new NsgMethodGraph();
            return new StackTarget(methods[0].graph, null, null, methods[0]);
        }

        public void ResetClipboard()
        {
            Clipboard = new NsgClipboard();
        }

        // ==================================================================
        // Перетаскивание внутри полотна
        // ==================================================================

        string _dragId;
        Vector2 _dragStart;
        bool _blockDragging;
        int _dragPointerId = -1;
        VisualElement _blockGhost;

        /// <summary>
        /// Внешняя цель перетаскивания (зона наборов). Возвращает true, если
        /// окно само обработало сброс.
        /// </summary>
        public System.Func<Vector2, bool> ExternalDrop;

        /// <summary>Вызывается из блока при нажатии: кандидат на перетаскивание.</summary>
        public void BeginBlockDragCandidate(string id, PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            // Alt+ЛКМ — это панорамирование полотна, а не перенос блока.
            if (evt.altKey) return;

            _dragId = id;
            _dragStart = evt.position;
            _dragPointerId = evt.pointerId;
            _blockDragging = false;

            // Захватываем указатель СРАЗУ, ещё до начала переноса. Иначе
            // PointerUp, отпущенный вне полотна (палитра, вкладки, другое окно
            // редактора), до полотна не дойдёт: кандидат останется висеть, и
            // первое же движение мыши над полотном начнёт «фантомный» перенос,
            // а призрак «N блоков» будет следовать за курсором.
            //
            // Поля ввода, кнопки и списки внутрь блока сюда не доходят — их
            // отсекает Nsg_BlockView.IsInteractiveTarget, поэтому фокус и
            // выделение текста не страдают.
            if (_viewport != null) _viewport.CapturePointer(evt.pointerId);
        }

        void OnBlockDragMove(PointerMoveEvent evt)
        {
            if (string.IsNullOrEmpty(_dragId)) return;

            // Полотно панорамируется — перенос не начинаем.
            if (_panning) return;

            if (!_blockDragging)
            {
                float distance = ((Vector2)evt.position - _dragStart).magnitude;
                if (distance < 6f) return;

                if (_selected.Count == 0) return;

                _blockDragging = true;
                MakeBlockGhost();
                SetDropHighlight(true);
            }

            MoveBlockGhost(evt.position);

            var p = this.panel;
            if (p != null)
            {
                var target = ResolveDrop(p.Pick(evt.position));
                ShowDropPreview(target, null);
            }
        }

        void OnBlockDragUp(PointerUpEvent evt)
        {
            string id = _dragId;
            _dragId = null;

            int pointerId = _dragPointerId >= 0 ? _dragPointerId : evt.pointerId;
            _dragPointerId = -1;

            if (!_blockDragging)
            {
                ReleaseDragCapture(pointerId);
                return;
            }
            _blockDragging = false;

            ClearBlockGhost();
            ClearDropPreview();
            SetDropHighlight(false);

            // Захват снимаем ПОСЛЕ сброса состояния: обработчик
            // PointerCaptureOutEvent не должен принять это за потерю захвата
            // и отменить сброс, который вот-вот произойдёт.
            ReleaseDragCapture(pointerId);

            if (string.IsNullOrEmpty(id)) return;

            var p = this.panel;
            var target = p != null ? ResolveDrop(p.Pick(evt.position)) : new DropTarget { Kind = DropKind.None };

            // Зона наборов живёт в окне: отдаём сброс туда.
            if (target.Kind == DropKind.Preset || target.Kind == DropKind.None)
            {
                if (ExternalDrop != null && ExternalDrop(evt.position)) return;
            }

            if (target.Kind == DropKind.Stack)
            {
                MoveSelectionTo(target.Stack);
            }
            else if (target.Kind == DropKind.Scratch)
            {
                MoveSelectionToScratch(target.Scratch);
            }
        }

        /// <summary>Снимает захват указателя, если он наш.</summary>
        void ReleaseDragCapture(int pointerId)
        {
            if (pointerId < 0 || _viewport == null) return;
            if (_viewport.HasPointerCapture(pointerId)) _viewport.ReleasePointer(pointerId);
        }

        /// <summary>
        /// Аварийно снимает перетаскивание вместе с захватом указателя: потерян
        /// захват или пришла отмена указателя. Призрак и подсветка не должны
        /// оставаться на экране. Выделение не трогаем — блок остаётся выделенным
        /// там, где был.
        /// </summary>
        void CancelBlockDrag()
        {
            int pointerId = _dragPointerId;
            _dragPointerId = -1;
            _dragId = null;
            _blockDragging = false;

            ClearBlockGhost();
            ClearDropPreview();
            SetDropHighlight(false);

            // Состояние сброшено ДО ReleasePointer, поэтому обработчик
            // PointerCaptureOutEvent сюда повторно не войдёт.
            ReleaseDragCapture(pointerId);
        }

        void MakeBlockGhost()
        {
            var ghost = new Label(Nsg_L10n.T("drag.blocks", _selected.Count));
            ghost.style.position = Position.Absolute;
            ghost.style.paddingLeft = 8;
            ghost.style.paddingRight = 8;
            ghost.style.paddingTop = 4;
            ghost.style.paddingBottom = 4;
            ghost.style.backgroundColor = new Color(0.95f, 0.72f, 0.22f, 0.92f);
            ghost.style.color = new Color(0.12f, 0.12f, 0.14f);
            ghost.pickingMode = PickingMode.Ignore;
            Nsg_Visual.Round(ghost, 4);
            _blockGhost = ghost;
            Add(ghost);
        }

        void MoveBlockGhost(Vector2 panelPosition)
        {
            if (_blockGhost == null) return;
            var local = this.WorldToLocal(panelPosition);
            _blockGhost.style.left = local.x + 12;
            _blockGhost.style.top = local.y + 10;
        }

        void ClearBlockGhost()
        {
            if (_blockGhost != null)
            {
                _blockGhost.RemoveFromHierarchy();
                _blockGhost = null;
            }
        }

        // ------------------------------------------------------------------
        // Перенос выделения
        // ------------------------------------------------------------------

        /// <summary>
        /// Отцепляет выделенные группы от их мест и возвращает корни в порядке
        /// следования. Возвращает false, если выделение нельзя переносить.
        /// </summary>
        bool DetachSelection(out NsgMethodGraph graph, out List<string> roots, out List<string> moving)
        {
            graph = null;
            roots = null;
            moving = null;

            var map = SelectionByGraph();
            if (map.Count != 1) return false;

            List<string> ids = null;
            foreach (var kv in map)
            {
                graph = kv.Key;
                ids = kv.Value;
                break;
            }

            var order = OrderIndex(graph);

            // Сначала всё, что переносится.
            var initialRoots = RootsOf(graph, ids);
            moving = new List<string>();
            var movingSet = new HashSet<string>();

            for (int i = 0; i < initialRoots.Count; i++)
            {
                var sub = new HashSet<string>();
                CollectSubtree(graph, initialRoots[i], sub);
                foreach (var s in sub) if (movingSet.Add(s)) moving.Add(s);
            }

            // Корни переноса — те, чей предок НЕ переносится.
            roots = new List<string>();
            for (int i = 0; i < moving.Count; i++)
            {
                var incoming = FindIncoming(graph, moving[i]);

                // Предок тоже переносится, значит корнем будет он, а не этот узел.
                if (incoming != null && incoming.Node != null && movingSet.Contains(incoming.Node.id)) continue;

                // Узел без входящей связи — это либо точка входа, либо блок из
                // отвала. Оба обязаны быть корнями переноса.
                //
                // Здесь раньше стоял пропуск «уже отвал», и из-за него блок из
                // отвала нельзя было вернуть в стопку: список корней выходил
                // пустым, перенос молча отменялся, а блок выглядел исчезнувшим.
                roots.Add(moving[i]);
            }

            roots.Sort((a, b) =>
            {
                int ia, ib;
                order.TryGetValue(a, out ia);
                order.TryGetValue(b, out ib);
                return ia.CompareTo(ib);
            });

            return roots.Count > 0;
        }

        /// <summary>Конец цепочки внутри переносимого набора.</summary>
        static string TailWithin(NsgMethodGraph g, string root, HashSet<string> movingSet)
        {
            string tail = root;
            var seen = new HashSet<string>();

            while (!string.IsNullOrEmpty(tail) && seen.Add(tail))
            {
                var n = g.Find(tail);
                if (n == null) break;
                if (string.IsNullOrEmpty(n.next) || !movingSet.Contains(n.next)) break;
                tail = n.next;
            }
            return tail;
        }

        public bool MoveSelectionTo(StackTarget target)
        {
            if (target == null || target.Graph == null) return false;

            NsgMethodGraph graph;
            List<string> roots;
            List<string> moving;

            if (!DetachSelection(out graph, out roots, out moving)) return false;

            // Перенос между методами идёт через отвал (там узлы клонируются с
            // новыми id). Прямая подвеска сломала бы граф: узлы остались бы в
            // исходном графе, а целевой ссылался бы на несуществующие id, и
            // цепочка молча пропала бы при печати.
            if (target.Graph != graph) return false;

            // В стопку подвешиваются только блоки-операторы. У блока выражения
            // формы оператора нет: подвесив его через next, мы бы отцепили его
            // из слота, а напечатать вместо него было бы нечего — снаружи это
            // выглядит как «блок исчез при отпускании».
            for (int i = 0; i < roots.Count; i++)
            {
                var rootNode = graph.Find(roots[i]);
                var rootDef = rootNode != null && Library != null ? Library.Get(rootNode.block) : null;
                if (rootDef == null || rootDef.IsExpression) return false;
            }

            var movingSet = new HashSet<string>(moving);

            // Нельзя положить группу внутрь самой себя — получится цикл.
            string targetHead = target.GetHead();
            if (!string.IsNullOrEmpty(targetHead) && movingSet.Contains(targetHead)) return false;

            Push(FindMethodOfGraph(graph), "move");

            // Отцепляем: прежнее место получает продолжение за хвостом группы.
            for (int i = 0; i < roots.Count; i++)
            {
                string tail = TailWithin(graph, roots[i], movingSet);
                var tailNode = graph.Find(tail);
                string continuation = tailNode != null ? tailNode.next : null;

                var incoming = FindIncoming(graph, roots[i]);
                if (incoming != null) incoming.Set(continuation);
                else if (graph.entry == roots[i]) graph.entry = continuation;

                if (tailNode != null) tailNode.next = null;
            }

            // Пристраиваем к концу целевой стопки.
            for (int i = 0; i < roots.Count - 1; i++)
            {
                var n = graph.Find(roots[i]);
                if (n != null) n.next = roots[i + 1];
            }

            string head = roots[0];
            string stackHead = target.GetHead();

            if (string.IsNullOrEmpty(stackHead))
            {
                target.SetHead(head);
            }
            else
            {
                var last = LastInChain(target.Graph, stackHead);
                if (last != null) last.next = head;
                else target.SetHead(head);
            }

            _selected.Clear();
            CollectSubtree(graph, head, _selected);
            _lastStack = target;
            Refresh();
            return true;
        }

        /// <summary>Перенос выделения в отвал метода.</summary>
        public bool MoveSelectionToScratch(ScratchTarget scratch)
        {
            if (scratch == null || scratch.Graph == null) return false;

            NsgMethodGraph graph;
            List<string> roots;
            List<string> moving;

            if (!DetachSelection(out graph, out roots, out moving)) return false;

            // Отвал того же метода: отцепляем, но никуда не подвешиваем.
            if (graph == scratch.Graph)
            {
                Push(scratch.Method, "detach");
                var movingSet = new HashSet<string>(moving);

                for (int i = 0; i < roots.Count; i++)
                {
                    string tail = TailWithin(graph, roots[i], movingSet);
                    var tailNode = graph.Find(tail);
                    string continuation = tailNode != null ? tailNode.next : null;

                    var incoming = FindIncoming(graph, roots[i]);
                    if (incoming != null) incoming.Set(continuation);
                    else if (graph.entry == roots[i]) graph.entry = continuation;

                    if (tailNode != null) tailNode.next = null;
                }

                Refresh();
                return true;
            }

            // Другой метод: переносим в его отвал.
            Push(scratch.Method, "detach");

            var map = new Dictionary<string, string>();
            for (int i = 0; i < moving.Count; i++) map[moving[i]] = NextId();

            for (int i = 0; i < moving.Count; i++)
            {
                var src = graph.Find(moving[i]);
                if (src == null) continue;

                var c = Nsg_Cloner.CloneNode(src);
                c.id = map[c.id];
                c.next = Remap(c.next, map);
                c.body = Remap(c.body, map);
                c.els = Remap(c.els, map);
                for (int k = 0; k < c.args.Count; k++) if (c.args[k] != null) c.args[k].link = Remap(c.args[k].link, map);
                for (int k = 0; k < c.extra.Count; k++) if (c.extra[k] != null) c.extra[k].link = Remap(c.extra[k].link, map);
                scratch.Graph.nodes.Add(c);
            }

            var doomed = new HashSet<string>(moving);
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                var n = graph.nodes[i];
                if (doomed.Contains(n.id)) continue;
                if (doomed.Contains(n.next)) n.next = SurvivingSuccessor(graph, n.next, doomed);
                if (doomed.Contains(n.body)) n.body = SurvivingSuccessor(graph, n.body, doomed);
                if (doomed.Contains(n.els)) n.els = SurvivingSuccessor(graph, n.els, doomed);
                for (int k = 0; k < n.args.Count; k++) if (n.args[k] != null && doomed.Contains(n.args[k].link)) n.args[k].link = null;
                for (int k = 0; k < n.extra.Count; k++) if (n.extra[k] != null && doomed.Contains(n.extra[k].link)) n.extra[k].link = null;
            }
            if (doomed.Contains(graph.entry)) graph.entry = SurvivingSuccessor(graph, graph.entry, doomed);
            foreach (var d in doomed) graph.Remove(d);

            _selected.Clear();
            Refresh();
            return true;
        }

        // ==================================================================
        // Перетаскивание из палитры
        // ==================================================================

        /// <summary>
        /// Отвал метода: блоки, которые ни к чему не подключены. Они хранятся в
        /// графе (значит, сохраняются в .nsg.json), но недостижимы из entry,
        /// поэтому НЕ печатаются в код и не участвуют в трансляции.
        /// </summary>
        public class ScratchTarget
        {
            public NsgMethodGraph Graph;
            public NsgStructNode Method;
            public VisualElement Container;
        }

        public enum DropKind
        {
            None,
            Stack,
            Scratch,
            /// <summary>Зона слотов наборов — живёт в окне, не в полотне.</summary>
            Preset
        }

        /// <summary>Метка зоны наборов. Полотно про неё знает только по типу.</summary>
        public class PresetZoneMarker
        {
        }

        public class DropTarget
        {
            public DropKind Kind;
            public StackTarget Stack;
            public ScratchTarget Scratch;
        }

        /// <summary>Определяет, куда попадёт блок: в стопку, в отвал или в наборы.</summary>
        public static DropTarget ResolveDrop(VisualElement element)
        {
            var e = element;
            while (e != null)
            {
                var stack = e.userData as StackTarget;
                if (stack != null) return new DropTarget { Kind = DropKind.Stack, Stack = stack };

                var scratch = e.userData as ScratchTarget;
                if (scratch != null) return new DropTarget { Kind = DropKind.Scratch, Scratch = scratch };

                if (e.userData is PresetZoneMarker) return new DropTarget { Kind = DropKind.Preset };

                e = e.parent;
            }
            return new DropTarget { Kind = DropKind.None };
        }

        // ------------------------------------------------------------------
        // Призрак вставки
        // ------------------------------------------------------------------

        VisualElement _dropPreview;

        /// <summary>Отвал первого метода — запасная цель для фона скрипта.</summary>
        ScratchTarget _firstScratch;

        public ScratchTarget FirstScratch
        {
            get { return _firstScratch; }
        }

        /// <summary>
        /// Запасная цель, когда точка отпускания ничего не дала. Блок не должен
        /// пропадать ни при каких обстоятельствах, поэтому всегда возвращаем
        /// хоть какое-то место.
        /// </summary>
        public DropTarget FallbackDrop()
        {
            if (_firstScratch != null)
            {
                return new DropTarget { Kind = DropKind.Scratch, Scratch = _firstScratch };
            }

            // В чертеже отвала нет, поэтому запасная цель — последняя стопка.
            // Без этого блок, брошенный мимо полотна метода, пропадал бы.
            if (_lastStack != null)
            {
                return new DropTarget { Kind = DropKind.Stack, Stack = _lastStack };
            }

            return new DropTarget { Kind = DropKind.None };
        }

        public void ClearDropPreview()
        {
            if (_dropPreview != null)
            {
                _dropPreview.RemoveFromHierarchy();
                _dropPreview = null;
            }
        }

        /// <summary>
        /// Показывает полупрозрачную «пустую» копию блока ровно там, где он
        /// окажется после отпускания. Это точнее, чем подсветка всей стопки.
        /// </summary>
        public void ShowDropPreview(DropTarget target, string blockId)
        {
            ShowDropPreview(target, blockId, null);
        }

        /// <summary>
        /// fallbackLabel — что показать, если блока с таким id нет. Нужно для
        /// наборов: у них нет отдельного определения блока, только имя.
        /// </summary>
        public void ShowDropPreview(DropTarget target, string blockId, string fallbackLabel)
        {
            if (target == null || target.Kind == DropKind.None)
            {
                ClearDropPreview();
                return;
            }

            var container = target.Kind == DropKind.Stack
                ? (target.Stack != null ? target.Stack.Container : null)
                : (target.Scratch != null ? target.Scratch.Container : null);

            if (container == null)
            {
                ClearDropPreview();
                return;
            }

            // Уже стоит в нужном месте — перерисовывать нечего.
            if (_dropPreview != null && _dropPreview.parent == container)
            {
                if (target.Kind == DropKind.Stack) return;
            }

            ClearDropPreview();

            var def = Library != null ? Library.Get(blockId) : null;
            Color color = def != null ? def.BlockColor() : Nsg_Palette.Frame;

            _dropPreview = new VisualElement();
            // Призрак не должен участвовать в попадании: иначе он сам
            // оказывается под курсором и цель определяется неверно.
            _dropPreview.pickingMode = PickingMode.Ignore;
            _dropPreview.style.height = 22;
            _dropPreview.style.marginTop = 2;
            _dropPreview.style.marginBottom = 2;
            _dropPreview.style.backgroundColor = new Color(color.r, color.g, color.b, 0.22f);
            _dropPreview.style.borderTopWidth = 1;
            _dropPreview.style.borderBottomWidth = 1;
            _dropPreview.style.borderLeftWidth = 1;
            _dropPreview.style.borderRightWidth = 1;
            _dropPreview.style.borderTopColor = color;
            _dropPreview.style.borderBottomColor = color;
            _dropPreview.style.borderLeftColor = color;
            _dropPreview.style.borderRightColor = color;
            Nsg_Visual.Round(_dropPreview, 4);

            var label = new Label(def != null ? def.Label() : (fallbackLabel ?? blockId));
            label.style.color = new Color(color.r, color.g, color.b, 0.85f);
            label.style.fontSize = 11;
            Nsg_Rtl.MarginStart(label.style, 6);
            label.style.unityTextAlign = Nsg_Rtl.TextAlign;
            _dropPreview.Add(label);

            if (target.Kind == DropKind.Stack)
            {
                // AppendStatement кладёт блок в конец стопки, значит призрак
                // должен стоять прямо перед кнопкой «+».
                int index = Mathf.Max(0, container.childCount - 1);
                container.Insert(index, _dropPreview);
            }
            else
            {
                container.Add(_dropPreview);
            }
        }

        /// <summary>Кладёт блок туда, куда показал призрак.</summary>
        public void DropBlock(DropTarget target, string blockId)
        {
            ClearDropPreview();
            if (target == null || string.IsNullOrEmpty(blockId)) return;

            if (target.Kind == DropKind.Stack && target.Stack != null)
            {
                AppendStatement(target.Stack, blockId);
                return;
            }

            if (target.Kind == DropKind.Scratch && target.Scratch != null)
            {
                AddOrphan(target.Scratch, blockId);
            }
        }

        /// <summary>
        /// Добавляет блок в отвал. Он попадает в граф (и в .nsg.json), но
        /// недостижим из entry, поэтому в код не печатается.
        /// </summary>
        public void AddOrphan(ScratchTarget scratch, string blockId)
        {
            if (scratch == null || scratch.Graph == null) return;

            Push(scratch.Method, "detach");
            var n = NewNode(blockId);
            scratch.Graph.nodes.Add(n);
            Refresh();
        }

        /// <summary>Подсветка стопок на время перетаскивания (в дополнение к призраку).</summary>
        public void SetDropHighlight(bool on)
        {
            if (_content == null) return;
            Highlight(_content, on);
        }

        static void Highlight(VisualElement e, bool on)
        {
            if (e.userData is StackTarget)
            {
                e.style.backgroundColor = on ? new Color(0.35f, 0.55f, 0.85f, 0.18f) : Color.clear;
            }

            for (int i = 0; i < e.childCount; i++) Highlight(e[i], on);
        }

        // ==================================================================
        // Изменения
        // ==================================================================

        string NextId()
        {
            _uid++;
            return "u" + _uid;
        }

        NsgGraphNode NewNode(string blockId)
        {
            var n = new NsgGraphNode();
            n.id = NextId();
            n.block = blockId;

            var def = Library != null ? Library.Get(blockId) : null;
            if (def != null)
            {
                for (int i = 0; i < def.SocketCount; i++) n.args.Add(new NsgSlot());
                if (def.VariadicIndex >= 0) n.extra.Add(new NsgSlot());
            }
            return n;
        }

        void Push(NsgStructNode method, string label)
        {
            if (method != null) Undo.PushMethod(method, label);
            else if (Doc != null) Undo.PushFull(Doc.Model, label);
        }

        static NsgGraphNode LastInChain(NsgMethodGraph g, string head)
        {
            NsgGraphNode last = null;
            var seen = new HashSet<string>();
            string cur = head;
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;
                last = n;
                cur = n.next;
            }
            return last;
        }

        class LinkRef
        {
            public NsgGraphNode Node;
            public string Field;
            public int Index;

            public void Set(string value)
            {
                if (Node == null) return;
                if (Field == "next") Node.next = value;
                else if (Field == "body") Node.body = value;
                else if (Field == "els") Node.els = value;
                else if (Field == "arg") Node.EnsureArg(Index).link = value;
                else if (Field == "extra")
                {
                    while (Node.extra.Count <= Index) Node.extra.Add(new NsgSlot());
                    Node.extra[Index].link = value;
                }
            }
        }

        static LinkRef FindIncoming(NsgMethodGraph g, string target)
        {
            if (g == null || string.IsNullOrEmpty(target)) return null;

            for (int i = 0; i < g.nodes.Count; i++)
            {
                var n = g.nodes[i];
                if (n.next == target) return new LinkRef { Node = n, Field = "next" };
                if (n.body == target) return new LinkRef { Node = n, Field = "body" };
                if (n.els == target) return new LinkRef { Node = n, Field = "els" };

                for (int k = 0; k < n.args.Count; k++)
                {
                    if (n.args[k] != null && n.args[k].link == target)
                        return new LinkRef { Node = n, Field = "arg", Index = k };
                }
                for (int k = 0; k < n.extra.Count; k++)
                {
                    if (n.extra[k] != null && n.extra[k].link == target)
                        return new LinkRef { Node = n, Field = "extra", Index = k };
                }
            }
            return null;
        }

        static void CollectSubtree(NsgMethodGraph g, string id, HashSet<string> set)
        {
            if (g == null || string.IsNullOrEmpty(id) || !set.Add(id)) return;
            var n = g.Find(id);
            if (n == null) return;

            CollectSubtree(g, n.next, set);
            CollectSubtree(g, n.body, set);
            CollectSubtree(g, n.els, set);
            for (int i = 0; i < n.args.Count; i++)
            {
                if (n.args[i] != null) CollectSubtree(g, n.args[i].link, set);
            }
            for (int i = 0; i < n.extra.Count; i++)
            {
                if (n.extra[i] != null) CollectSubtree(g, n.extra[i].link, set);
            }
        }

        static void CollectSubtreeNoNext(NsgMethodGraph g, string id, HashSet<string> set)
        {
            if (g == null || string.IsNullOrEmpty(id) || !set.Add(id)) return;
            var n = g.Find(id);
            if (n == null) return;

            CollectSubtreeNoNext(g, n.body, set);
            CollectSubtreeNoNext(g, n.els, set);
            for (int i = 0; i < n.args.Count; i++)
            {
                if (n.args[i] != null) CollectSubtreeNoNext(g, n.args[i].link, set);
            }
            for (int i = 0; i < n.extra.Count; i++)
            {
                if (n.extra[i] != null) CollectSubtreeNoNext(g, n.extra[i].link, set);
            }
        }

        // ---- операторы ----

        void AppendStatement(StackTarget t, string blockId)
        {
            if (t.Graph == null) return;
            Push(t.Method, "add " + blockId);

            var n = NewNode(blockId);
            t.Graph.nodes.Add(n);

            string head = t.GetHead();
            if (string.IsNullOrEmpty(head))
            {
                t.SetHead(n.id);
            }
            else
            {
                var last = LastInChain(t.Graph, head);
                if (last != null) last.next = n.id;
                else t.SetHead(n.id);
            }
            Refresh();
        }

        void InsertStatementAfter(StackTarget t, string afterId, string blockId)
        {
            if (t.Graph == null) return;
            Push(t.Method, "insert " + blockId);

            var n = NewNode(blockId);
            t.Graph.nodes.Add(n);

            if (string.IsNullOrEmpty(afterId))
            {
                n.next = t.GetHead();
                t.SetHead(n.id);
            }
            else
            {
                var prev = t.Graph.Find(afterId);
                if (prev != null)
                {
                    n.next = prev.next;
                    prev.next = n.id;
                }
                else
                {
                    n.next = t.GetHead();
                    t.SetHead(n.id);
                }
            }
            Refresh();
        }

        public void MoveStatement(StackTarget t, string id, int delta)
        {
            if (t.Graph == null) return;

            var ids = new List<string>();
            var seen = new HashSet<string>();
            string cur = t.GetHead();
            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = t.Graph.Find(cur);
                if (n == null) break;
                ids.Add(cur);
                cur = n.next;
            }

            int idx = ids.IndexOf(id);
            int j = idx + delta;
            if (idx < 0 || j < 0 || j >= ids.Count) return;

            Push(t.Method, "move");
            string tmp = ids[idx];
            ids[idx] = ids[j];
            ids[j] = tmp;

            for (int i = 0; i < ids.Count; i++)
            {
                var n = t.Graph.Find(ids[i]);
                if (n != null) n.next = (i + 1 < ids.Count) ? ids[i + 1] : null;
            }
            t.SetHead(ids[0]);
            Refresh();
        }

        public void DeleteStatement(StackTarget t, string id)
        {
            if (t.Graph == null) return;
            var n = t.Graph.Find(id);
            if (n == null) return;

            Push(t.Method, "delete");
            string successor = n.next;

            var incoming = FindIncoming(t.Graph, id);
            if (incoming == null) t.SetHead(successor);
            else incoming.Set(successor);

            var doomed = new HashSet<string>();
            CollectSubtreeNoNext(t.Graph, id, doomed);
            foreach (var d in doomed) t.Graph.Remove(d);
            Refresh();
        }

        public void DuplicateStatement(StackTarget t, string id)
        {
            if (t.Graph == null) return;
            var src = t.Graph.Find(id);
            if (src == null) return;

            Push(t.Method, "duplicate");

            var ids = new HashSet<string>();
            CollectSubtree(t.Graph, id, ids);

            var map = new Dictionary<string, string>();
            foreach (var old in ids) map[old] = NextId();

            foreach (var old in ids)
            {
                var s = t.Graph.Find(old);
                if (s == null) continue;

                var c = Nsg_Cloner.CloneNode(s);
                c.id = map[old];
                c.next = Remap(c.next, map);
                c.body = Remap(c.body, map);
                c.els = Remap(c.els, map);
                for (int i = 0; i < c.args.Count; i++)
                {
                    if (c.args[i] != null) c.args[i].link = Remap(c.args[i].link, map);
                }
                for (int i = 0; i < c.extra.Count; i++)
                {
                    if (c.extra[i] != null) c.extra[i].link = Remap(c.extra[i].link, map);
                }
                t.Graph.nodes.Add(c);
            }

            var dup = t.Graph.Find(map[id]);
            if (dup != null)
            {
                dup.next = src.next;
                src.next = dup.id;
            }
            Refresh();
        }

        static string Remap(string id, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(id)) return id;
            string v;
            return map.TryGetValue(id, out v) ? v : id;
        }

        // ---- выражения ----

        public void PickStatement(StackTarget t)
        {
            var pos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            Nsg_PickerWindow.Show(pos, Library, false, id => AppendStatement(t, id));
        }

        public void PickExpression(NsgGraphNode parent, int socketIndex, NsgStructNode method)
        {
            var pos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            Nsg_PickerWindow.Show(pos, Library, true, id => SetExpr(parent, socketIndex, id, method));
        }

        public void PickExtra(NsgGraphNode parent, int index, NsgStructNode method)
        {
            var pos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            Nsg_PickerWindow.Show(pos, Library, true, id => SetExtra(parent, index, id, method));
        }

        public void PickReplace(NsgGraphNode n, NsgStructNode method)
        {
            var graph = FindGraphOf(n);
            bool expression = Library != null && Library.Get(n.block) != null && Library.Get(n.block).IsExpression;
            var pos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);

            Nsg_PickerWindow.Show(pos, Library, expression, id =>
            {
                if (graph != null) ReplaceBlock(graph, n, id, method);
            });
        }

        NsgMethodGraph FindGraphOf(NsgGraphNode n)
        {
            if (Doc == null || Doc.Model == null) return null;
            var methods = Doc.Model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                var g = methods[i].graph;
                if (g != null && g.Find(n.id) != null) return g;
            }
            return null;
        }

        void SetExpr(NsgGraphNode parent, int socketIndex, string blockId, NsgStructNode method)
        {
            var graph = FindGraphOf(parent);
            if (graph == null) return;

            Push(method, "set expr");

            var slot = parent.EnsureArg(socketIndex);
            if (!string.IsNullOrEmpty(slot.link))
            {
                var doomed = new HashSet<string>();
                CollectSubtree(graph, slot.link, doomed);
                foreach (var d in doomed) graph.Remove(d);
            }

            var n = NewNode(blockId);
            graph.nodes.Add(n);
            slot.link = n.id;
            slot.text = null;
            Refresh();
        }

        void SetExtra(NsgGraphNode parent, int index, string blockId, NsgStructNode method)
        {
            var graph = FindGraphOf(parent);
            if (graph == null) return;

            Push(method, "set arg");

            while (parent.extra.Count <= index) parent.extra.Add(new NsgSlot());
            var slot = parent.extra[index];
            if (!string.IsNullOrEmpty(slot.link))
            {
                var doomed = new HashSet<string>();
                CollectSubtree(graph, slot.link, doomed);
                foreach (var d in doomed) graph.Remove(d);
            }

            var n = NewNode(blockId);
            graph.nodes.Add(n);
            slot.link = n.id;
            slot.text = null;
            Refresh();
        }

        void ReplaceBlock(NsgMethodGraph graph, NsgGraphNode n, string blockId, NsgStructNode method)
        {
            Push(method, "replace");

            var doomed = new HashSet<string>();
            for (int i = 0; i < n.args.Count; i++)
            {
                if (n.args[i] != null) CollectSubtree(graph, n.args[i].link, doomed);
            }
            for (int i = 0; i < n.extra.Count; i++)
            {
                if (n.extra[i] != null) CollectSubtree(graph, n.extra[i].link, doomed);
            }
            CollectSubtree(graph, n.body, doomed);
            CollectSubtree(graph, n.els, doomed);
            foreach (var d in doomed) graph.Remove(d);

            n.block = blockId;
            n.args = new List<NsgSlot>();
            n.extra = new List<NsgSlot>();
            n.body = null;
            n.els = null;

            var def = Library != null ? Library.Get(blockId) : null;
            if (def != null)
            {
                for (int i = 0; i < def.SocketCount; i++) n.args.Add(new NsgSlot());
                if (def.VariadicIndex >= 0) n.extra.Add(new NsgSlot());
            }
            Refresh();
        }

        /// <summary>
        /// Переключение внутри группы вариантов. Значения сохранившихся слотов
        /// переносятся, поддеревья исчезнувших — удаляются.
        /// </summary>
        public void SwitchVariant(NsgGraphNode n, string newBlockId, NsgStructNode method)
        {
            var graph = FindGraphOf(n);
            if (graph == null) return;

            Push(method, "variant");
            n.block = newBlockId;

            var def = Library != null ? Library.Get(newBlockId) : null;
            int count = def != null ? def.SocketCount : 0;

            var old = n.args;
            var fresh = new List<NsgSlot>();
            for (int i = 0; i < count; i++)
            {
                fresh.Add(i < old.Count && old[i] != null ? old[i] : new NsgSlot());
            }

            for (int i = count; i < old.Count; i++)
            {
                if (old[i] == null || !old[i].IsLinked) continue;
                var doomed = new HashSet<string>();
                CollectSubtree(graph, old[i].link, doomed);
                foreach (var d in doomed) graph.Remove(d);
            }

            n.args = fresh;
            Refresh();
        }

        public void DeleteInline(NsgGraphNode n, NsgStructNode method)
        {
            var graph = FindGraphOf(n);
            if (graph == null) return;

            Push(method, "delete expr");

            var incoming = FindIncoming(graph, n.id);
            if (incoming != null) incoming.Set(null);

            var doomed = new HashSet<string>();
            CollectSubtree(graph, n.id, doomed);
            foreach (var d in doomed) graph.Remove(d);
            Refresh();
        }

        public void AddArg(NsgGraphNode parent, NsgStructNode method)
        {
            Push(method, "add arg");
            parent.extra.Add(new NsgSlot());
            Refresh();
        }

        public void RemoveArg(NsgGraphNode parent, int index, NsgStructNode method)
        {
            if (index < 0 || index >= parent.extra.Count) return;

            var graph = FindGraphOf(parent);
            Push(method, "remove arg");

            var slot = parent.extra[index];
            if (slot != null && !string.IsNullOrEmpty(slot.link) && graph != null)
            {
                var doomed = new HashSet<string>();
                CollectSubtree(graph, slot.link, doomed);
                foreach (var d in doomed) graph.Remove(d);
            }
            parent.extra.RemoveAt(index);
            Refresh();
        }

        public void OnSlotTextChanged(NsgGraphNode parent, int socketIndex, string text, NsgStructNode method)
        {
            if (parent == null) return;
            var slot = parent.EnsureArg(socketIndex);
            if (slot.text == text) return;

            Push(method, "edit text");
            slot.text = text;
            slot.link = null;

            // Без пересборки: поле уже показывает новое значение, а пересборка
            // украла бы фокус посреди правки.
            if (Changed != null) Changed();
        }

        // ==================================================================
        // Отмена / повтор
        // ==================================================================

        public bool PerformUndo()
        {
            if (Doc == null || Doc.Model == null || !Undo.CanUndo) return false;
            Doc.Model = Undo.Undo(Doc.Model);
            Refresh();
            return true;
        }

        public bool PerformRedo()
        {
            if (Doc == null || Doc.Model == null || !Undo.CanRedo) return false;
            Doc.Model = Undo.Redo(Doc.Model);
            Refresh();
            return true;
        }

        public void ResetUndo()
        {
            Undo.Clear();
        }
    }
}
