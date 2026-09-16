using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Окно редактора блоков: вкладки как в VS Code, палитра блоков справа,
    /// полотно скрипта в центре.
    /// </summary>
    public class Nsg_Window : EditorWindow
    {
        Nsg_ScriptView _view;
        Nsg_Document _doc;

        readonly List<Nsg_Document> _tabs = new List<Nsg_Document>();
        int _active = -1;

        VisualElement _tabBar;
        VisualElement _palette;

        /// <summary>Строка поиска живёт ВНЕ списка блоков: список пересобирается
        /// на каждое нажатие клавиши, и пересоздание поля сбрасывало бы фокус.</summary>
        VisualElement _paletteSearchHost;
        string _paletteQuery = string.Empty;

        /// <summary>Свёрнутые секции палитры. Состояние переживает пересборку.</summary>
        readonly HashSet<string> _paletteCollapsed = new HashSet<string>();

        /// <summary>Развёрнутые вручную буквенные индексы: по умолчанию они свёрнуты.</summary>
        readonly HashSet<string> _paletteExpanded = new HashSet<string>();
        VisualElement _presetPanel;
        VisualElement _rightPane;
        VisualElement _presetPane;

        // ---- кошка в нижнем слоте (необязательная часть) ----
        Nsg_NekoView _neko;
        bool _nekoGreeted;
        int _nekoLastDiagnostics = -1;
        Label _status;
        Button _undoBtn;
        Button _redoBtn;

        string _dragBlockId;
        /// <summary>Перетаскивается набор, а не отдельный блок: одно из двух.</summary>
        Nsg_Preset _dragPreset;
        VisualElement _dragGhost;
        bool _dragging;
        Nsg_ScriptView.DropTarget _hoverTarget;

        bool _saveScheduled;

        public static void Open()
        {
            var w = GetWindow<Nsg_Window>();
            w.titleContent = new GUIContent(Nsg_L10n.T("win.title"));
            w.minSize = new Vector2(980, 600);
            w.Show();
        }

        public void CreateGUI()
        {
            BuildUI();
        }

        void OnDisable()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] != null && _tabs[i].Model != null) _tabs[i].SaveModel();
            }

            // Запоминаем пропорции трёх областей. fixedPaneDimension наружу не
            // отдаётся, поэтому читаем фактический размер панели.
            var settings = Nsg_Settings.Instance;
            if (_rightPane != null && _rightPane.layout.width > 1f) settings.rightPaneWidth = _rightPane.layout.width;
            if (_presetPane != null && _presetPane.layout.height > 1f) settings.presetPaneHeight = _presetPane.layout.height;
            settings.Save();
        }

        // ==================================================================
        // Интерфейс
        // ==================================================================

        void BuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1;

            // Страховка для перетаскивания из палитры: если захват указателя
            // потерян (окно потеряло фокус, Alt+Tab, пересборка дерева), призрак
            // нельзя оставлять на экране — убрать его будет нечем.
            rootVisualElement.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (_dragging) CancelDrag();
            });

            // Полотно создаётся до панели инструментов: панель подписывается
            // на него (индикатор масштаба, «вписать в окно»).
            _view = new Nsg_ScriptView();
            _view.Library = ActiveLibrary();
            _view.Changed = OnViewChanged;
            _view.ExternalDrop = TryExternalDrop;
            // Свой блок, созданный из полотна, требует перечитать библиотеку.
            _view.LibraryChanged = ReloadLibraries;

            // Логику блока объясняет кошка: полотно только сообщает о просьбе.
            _view.ExplainBlockRequested = (graph, node) =>
            {
                if (_neko != null) _neko.ExplainBlock(graph, node, _view.Library);
            };

            _tabBar = new VisualElement();
            _tabBar.style.flexDirection = Nsg_Rtl.Row;
            _tabBar.style.flexWrap = Wrap.NoWrap;
            Nsg_Rtl.PaddingStart(_tabBar.style, 4);
            Nsg_Rtl.PaddingEnd(_tabBar.style, 4);
            _tabBar.style.paddingTop = 3;
            _tabBar.style.backgroundColor = new Color(0.15f, 0.16f, 0.18f);
            rootVisualElement.Add(_tabBar);

            rootVisualElement.Add(BuildRibbon());

            var middle = new VisualElement();
            middle.style.flexDirection = FlexDirection.Row;
            middle.style.flexGrow = 1;

            _view.style.flexGrow = 1;

            var right = new VisualElement();
            right.style.flexDirection = FlexDirection.Column;
            // Разделитель обращён к полотну, а полотно в RTL слева.
            Nsg_Rtl.BorderStart(right.style, 1, new Color(0.25f, 0.26f, 0.29f));
            right.style.backgroundColor = new Color(0.18f, 0.19f, 0.21f);

            var paletteScroll = new ScrollView();
            paletteScroll.style.flexGrow = 1;
            paletteScroll.style.paddingLeft = 5;
            paletteScroll.style.paddingRight = 5;
            paletteScroll.style.paddingTop = 5;

            _paletteSearchHost = new VisualElement();
            _paletteSearchHost.style.marginBottom = 4;
            var searchField = new TextField();
            searchField.value = _paletteQuery ?? string.Empty;
            searchField.tooltip = Nsg_L10n.T("palette.search");
            searchField.RegisterValueChangedCallback(evt =>
            {
                _paletteQuery = evt.newValue ?? string.Empty;
                RebuildPalette();
            });
            _paletteSearchHost.Add(searchField);
            paletteScroll.Add(_paletteSearchHost);

            _palette = new VisualElement();
            paletteScroll.Add(_palette);

            var presetScroll = new ScrollView();
            presetScroll.style.paddingLeft = 5;
            presetScroll.style.paddingRight = 5;
            presetScroll.style.paddingTop = 5;

            // Нижний слот: панель кошки, если установлен ProgramNeko.
            // Без него слот остаётся пустым, как и был задумано.
            _neko = new Nsg_NekoView();
            if (_neko.Active)
            {
                presetScroll.Add(_neko);
                _nekoGreeted = false;
            }

            // Пропорции трёх областей: полотно | (блоки над наборами).
            // TwoPaneSplitView даёт готовую перетаскиваемую разделительную полосу.
            var settings = Nsg_Settings.Instance;

            var rightSplit = new TwoPaneSplitView(
                1, Mathf.Max(60f, settings.presetPaneHeight),
                TwoPaneSplitViewOrientation.Vertical);
            rightSplit.style.flexGrow = 1;
            rightSplit.Add(paletteScroll);
            rightSplit.Add(presetScroll);
            _presetPane = presetScroll;

            right.Add(rightSplit);

            var mainSplit = new TwoPaneSplitView(
                Nsg_Rtl.SplitFixedIndex(true), Mathf.Max(140f, settings.rightPaneWidth),
                TwoPaneSplitViewOrientation.Horizontal);
            mainSplit.style.flexGrow = 1;

            // Фиксированная панель — колонка блоков. В LTR она вторая (справа),
            // в RTL первая (слева): требование «палитра блоков слева».
            if (Nsg_Rtl.On)
            {
                mainSplit.Add(right);
                mainSplit.Add(_view);
            }
            else
            {
                mainSplit.Add(_view);
                mainSplit.Add(right);
            }
            _rightPane = right;

            middle.Add(mainSplit);
            rootVisualElement.Add(middle);

            var bar = new VisualElement();
            bar.style.flexDirection = Nsg_Rtl.Row;
            bar.style.alignItems = Align.Center;
            Nsg_Rtl.PaddingStart(bar.style, 8);
            Nsg_Rtl.PaddingEnd(bar.style, 8);
            bar.style.paddingTop = 3;
            bar.style.paddingBottom = 3;
            bar.style.borderTopWidth = 1;
            bar.style.borderTopColor = new Color(0.25f, 0.26f, 0.29f);

            _status = new Label(string.Empty);
            _status.style.flexGrow = 1;
            _status.style.unityTextAlign = Nsg_Rtl.TextAlign;
            bar.Add(_status);

            var errBtn = new Button(() => Nsg_ErrorWindow.Push(
                _doc != null ? _doc.Diagnostics : null,
                _doc != null ? _doc.CsPath : null));
            errBtn.text = Nsg_L10n.T("btn.errors");
            bar.Add(errBtn);

            rootVisualElement.Add(bar);

            // Клавиши в UI Toolkit приходят в элемент, который в фокусе. Без
            // этого корень окна не получал KeyDown и Ctrl/Cmd+Z не работал:
            // фокусировать было нечего.
            rootVisualElement.focusable = true;
            rootVisualElement.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // Кошка следит за происходящим. Движения мыши сами по себе её не
            // будят: они лишь копятся как признак «елозит, но ничего не делает».
            rootVisualElement.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_neko != null) _neko.Observe(Nsg_NekoEvent.PointerMove, 1);
            });
            rootVisualElement.schedule.Execute(() => rootVisualElement.Focus()).ExecuteLater(50);

            RebuildTabs();
            // RebuildPalette сам наполняет секцию наборов, поэтому отдельный
            // вызов RebuildPresets здесь не нужен.
            RebuildPalette();

            if (_doc != null)
            {
                // Язык переключился, но открытый файл должен остаться открытым:
                // перепривязываем документ, НЕ сбрасывая историю отмены.
                AttachDoc(false);
            }
            else
            {
                RefreshStatus();
                RefreshUndoButtons();
                _view.Rebuild();
            }
        }

        // ==================================================================
        // Лента команд в стиле Word: вкладки сверху, группы с подписями снизу
        // ==================================================================

        /// <summary>Активная вкладка ленты: 0 — начало, 1 — управление, 2 — инструменты, 3 — вид.</summary>
        int _ribbonTab;

        VisualElement _ribbonBody;
        readonly List<Button> _ribbonTabs = new List<Button>();

        VisualElement BuildRibbon()
        {
            // Список вкладок пересобирается вместе с окном: без очистки при
            // смене языка в нём копились бы дубликаты старых кнопок.
            _ribbonTabs.Clear();

            var root = new VisualElement();
            root.style.backgroundColor = new Color(0.17f, 0.18f, 0.21f);
            root.style.borderBottomWidth = 1;
            root.style.borderBottomColor = new Color(0.26f, 0.27f, 0.31f);

            // ---- полоса вкладок ----
            var strip = new VisualElement();
            strip.style.flexDirection = Nsg_Rtl.Row;
            strip.style.backgroundColor = new Color(0.13f, 0.14f, 0.16f);
            Nsg_Rtl.PaddingStart(strip.style, 4);
            strip.style.paddingTop = 3;

            AddRibbonTab(strip, "ribbon.tab.home", 0);
            AddRibbonTab(strip, "ribbon.tab.manage", 1);
            AddRibbonTab(strip, "ribbon.tab.tools", 2);
            AddRibbonTab(strip, "ribbon.tab.view", 3);
            root.Add(strip);

            // ---- содержимое активной вкладки ----
            _ribbonBody = new VisualElement();
            _ribbonBody.style.flexDirection = Nsg_Rtl.Row;
            _ribbonBody.style.alignItems = Align.FlexStart;
            _ribbonBody.style.flexWrap = Wrap.Wrap;
            root.Add(_ribbonBody);

            RebuildRibbon();
            return root;
        }

        void AddRibbonTab(VisualElement strip, string titleKey, int index)
        {
            var tab = new Button(() =>
            {
                if (_ribbonTab == index) return;
                _ribbonTab = index;
                RebuildRibbon();
            });

            tab.text = Nsg_L10n.T(titleKey);
            tab.style.fontSize = 11;
            tab.style.height = 20;
            tab.style.marginRight = 2;
            Nsg_Rtl.PaddingStart(tab.style, 10);
            Nsg_Rtl.PaddingEnd(tab.style, 10);
            Nsg_Visual.RoundTop(tab, 4);

            strip.Add(tab);
            _ribbonTabs.Add(tab);
        }

        void RebuildRibbon()
        {
            if (_ribbonBody == null) return;

            // Метка масштаба живёт в полотне. При пересборке ссылку нужно снять,
            // иначе она осталась бы указывать на удалённый элемент.
            if (_view != null) _view.SetZoomLabel(null);
            _undoBtn = null;
            _redoBtn = null;

            _ribbonBody.Clear();

            switch (_ribbonTab)
            {
                case 1: BuildRibbonManage(_ribbonBody); break;
                case 2: BuildRibbonTools(_ribbonBody); break;
                case 3: BuildRibbonView(_ribbonBody); break;
                default: BuildRibbonHome(_ribbonBody); break;
            }

            RefreshRibbonTabs();
            RefreshUndoButtons();
        }

        void RefreshRibbonTabs()
        {
            for (int i = 0; i < _ribbonTabs.Count; i++)
            {
                bool on = i == _ribbonTab;
                var b = _ribbonTabs[i];

                b.style.backgroundColor = on
                    ? new Color(0.17f, 0.18f, 0.21f)
                    : new Color(0.13f, 0.14f, 0.16f);
                b.style.color = on ? Color.white : new Color(0.66f, 0.70f, 0.76f);
                b.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        /// <summary>
        /// Группа ленты: кнопки сверху, подпись снизу, разделитель справа — как
        /// в Word. Возвращает контейнер, в который складываются кнопки.
        /// </summary>
        VisualElement AddRibbonGroup(VisualElement body, string titleKey)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Column;
            group.style.marginTop = 4;
            group.style.marginBottom = 3;
            Nsg_Rtl.PaddingEnd(group.style, 9);
            Nsg_Rtl.MarginEnd(group.style, 9);
            Nsg_Rtl.BorderEnd(group.style, 1, new Color(1f, 1f, 1f, 0.09f));

            var items = new VisualElement();
            items.style.flexDirection = Nsg_Rtl.Row;
            items.style.alignItems = Align.Center;
            items.style.flexWrap = Wrap.Wrap;
            group.Add(items);

            var caption = new Label(Nsg_L10n.T(titleKey));
            caption.style.fontSize = 9;
            caption.style.color = new Color(0.50f, 0.54f, 0.60f);
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            caption.style.marginTop = 2;
            group.Add(caption);

            body.Add(group);
            return items;
        }

        static Button RibbonButton(string text, System.Action action)
        {
            var b = new Button(action);
            b.text = text;
            b.style.fontSize = 11;
            b.style.height = 20;
            b.style.marginRight = 3;
            Nsg_Rtl.PaddingStart(b.style, 8);
            Nsg_Rtl.PaddingEnd(b.style, 8);
            Nsg_Visual.Round(b, 3);
            return b;
        }

        void BuildRibbonHome(VisualElement body)
        {
            var script = AddRibbonGroup(body, "ribbon.group.script");
            script.Add(RibbonButton(Nsg_L10n.T("btn.open"), OpenFromSelection));
            script.Add(RibbonButton(Nsg_L10n.T("btn.manage"), OnManage));
            script.Add(RibbonButton(Nsg_L10n.T("btn.import"), OnImport));
            script.Add(RibbonButton(Nsg_L10n.T("btn.generate"), OnGenerate));
            script.Add(RibbonButton(Nsg_L10n.T("btn.preview"), OnPreview));

            var edit = AddRibbonGroup(body, "ribbon.group.edit");
            _undoBtn = RibbonButton(Nsg_L10n.T("btn.undo"), OnUndo);
            _redoBtn = RibbonButton(Nsg_L10n.T("btn.redo"), OnRedo);
            edit.Add(_undoBtn);
            edit.Add(_redoBtn);

            var zoom = AddRibbonGroup(body, "ribbon.group.zoom");
            zoom.Add(RibbonButton("−", () =>
            {
                if (_view != null) _view.SetZoom(_view.Zoom / 1.25f);
            }));
            zoom.Add(RibbonButton("1:1", () =>
            {
                if (_view != null) _view.SetZoom(1f);
            }));
            zoom.Add(RibbonButton(Nsg_L10n.T("btn.fit"), () =>
            {
                if (_view != null) _view.FitToView();
            }));

            var zoomLabel = new Label("100%");
            zoomLabel.style.minWidth = 44;
            zoomLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            zoomLabel.style.color = new Color(0.65f, 0.68f, 0.74f);
            zoom.Add(zoomLabel);
            if (_view != null) _view.SetZoomLabel(zoomLabel);
        }

        void BuildRibbonManage(VisualElement body)
        {
            var file = AddRibbonGroup(body, "ribbon.group.file");
            file.Add(RibbonButton(Nsg_L10n.T("btn.manage"), OnManage));
            file.Add(RibbonButton(Nsg_L10n.T("btn.unmanage"), OnUnmanage));

            var folder = AddRibbonGroup(body, "ribbon.group.folder");
            folder.Add(RibbonButton(Nsg_L10n.T("btn.manageFolder"), OnManageFolder));
            folder.Add(RibbonButton(Nsg_L10n.T("btn.unmanageFolder"), OnUnmanageFolder));

            var project = AddRibbonGroup(body, "ribbon.group.project");
            project.Add(RibbonButton(Nsg_L10n.T("btn.manageAll"), OnManageProject));
            project.Add(RibbonButton(Nsg_L10n.T("btn.unmanageAll"), OnUnmanageProject));

            // Видимость служебных файлов: подпись показывает, что произойдёт
            // по нажатию, поэтому лента пересобирается после переключения.
            var files = AddRibbonGroup(body, "ribbon.group.files");
            var toggle = RibbonButton(
                Nsg_FileVisibility.Hidden ? Nsg_L10n.T("files.btnShow") : Nsg_L10n.T("files.btnHide"),
                () =>
                {
                    Nsg_FileVisibility.Toggle();
                    RebuildRibbon();
                });
            toggle.tooltip = Nsg_L10n.T("files.hotkeyHint");
            files.Add(toggle);
        }

        void BuildRibbonTools(VisualElement body)
        {
            var api = AddRibbonGroup(body, "ribbon.group.api");
            api.Add(RibbonButton(Nsg_L10n.T("btn.api"), OnApiFolder));
            api.Add(RibbonButton(Nsg_L10n.T("btn.apiAll"), OnApiProject));

            var inspect = AddRibbonGroup(body, "ribbon.group.inspect");
            inspect.Add(RibbonButton(Nsg_L10n.T("health.btn"), OnHealth));
            inspect.Add(RibbonButton(Nsg_L10n.T("btn.selftest"), OnSelfTest));

            var version = AddRibbonGroup(body, "ribbon.group.version");
            version.Add(RibbonButton(Nsg_L10n.T("btn.checkpoint"), ShowCheckpointMenu));
            version.Add(RibbonButton(Nsg_L10n.T("btn.git"), OnGitCheckpoint));

            var library = AddRibbonGroup(body, "ribbon.group.library");
            library.Add(RibbonButton(Nsg_L10n.T("btn.library"), OnReloadLibrary));
        }

        void BuildRibbonView(VisualElement body)
        {
            var lang = AddRibbonGroup(body, "ribbon.group.lang");
            lang.Add(BuildLanguageSlot());

            var mode = AddRibbonGroup(body, "ribbon.group.mode");
            mode.Add(BuildViewModeSlot());
        }

        void OnGenerateShell()
        {
            // Выбор管线 и генерация оболочки — часть языка HLSL, поэтому живут
            // в его папке в LanguageSupport, а не в ядре плагина.
        }

        void OnGenerateShellUnused()
        {
            //Nsg_Menu.GenerateShaderShellFor(_doc);
            RebuildPalette();
            if (_view != null) _view.Rebuild();
        }

        VisualElement BuildLanguageSlot()
        {
            var slot = new VisualElement();

            // Кроме английского языков нет — выбирать нечего. Английский
            // встроен в код, поэтому список из одной строки это не выбор, а
            // пустая строка в ленте: слот не занимает места.
            if (Nsg_L10n.LanguageCount <= 1)
            {
                slot.style.display = DisplayStyle.None;
                return slot;
            }

            slot.style.flexDirection = Nsg_Rtl.Row;
            slot.style.alignItems = Align.Center;
            Nsg_Rtl.MarginStart(slot.style, 8);
            slot.Add(new Label(Nsg_L10n.T("win.lang")));

            var langs = new List<string>();
            for (int i = 0; i < Nsg_L10n.LanguageCount; i++) langs.Add(Nsg_L10n.LanguageName(i));

            var dd = new DropdownField(langs, (int)Nsg_L10n.Lang);
            dd.style.width = 130;
            dd.RegisterValueChangedCallback(evt =>
            {
                int idx = langs.IndexOf(evt.newValue);
                if (idx < 0) return;
                Nsg_L10n.Lang = idx;

                // Меню Unity строится отдельно от окна.
                Nsg_MenuRuntime.Rebuild();

                BuildUI();
            });
            slot.Add(dd);
            return slot;
        }

        /// <summary>
        /// Переключатель режима рисования. Меняет только отрисовку: обе модели
        /// читают один и тот же граф, поэтому переключение ничего не теряет.
        /// </summary>
        VisualElement BuildViewModeSlot()
        {
            var slot = new VisualElement();
            slot.style.flexDirection = Nsg_Rtl.Row;
            slot.style.alignItems = Align.Center;
            Nsg_Rtl.MarginStart(slot.style, 8);
            slot.Add(new Label(Nsg_L10n.T("view.mode")));

            var modes = new List<string>
            {
                Nsg_L10n.T("view.stack"),
                Nsg_L10n.T("view.blueprint")
            };

            var dd = new DropdownField(modes, (int)Nsg_Settings.Instance.Mode);
            dd.style.width = 130;
            dd.RegisterValueChangedCallback(evt =>
            {
                int idx = modes.IndexOf(evt.newValue);
                if (idx < 0) return;

                var mode = idx == (int)NsgViewMode.Blueprint
                    ? NsgViewMode.Blueprint
                    : NsgViewMode.Stack;
                if (Nsg_Settings.Instance.Mode == mode) return;

                Nsg_Settings.Instance.Mode = mode;
                Nsg_Settings.Instance.Save();

                // Пересобираем только полотно: масштаб и смещение сохраняются,
                // поэтому вид не прыгает при переключении.
                if (_view != null) _view.Rebuild();
            });
            slot.Add(dd);
            return slot;
        }

        // ==================================================================
        // Вкладки
        // ==================================================================

        void RebuildTabs()
        {
            if (_tabBar == null) return;
            _tabBar.Clear();

            for (int i = 0; i < _tabs.Count; i++)
            {
                int index = i;
                var doc = _tabs[i];
                bool active = index == _active;

                var tab = new VisualElement();
                tab.style.flexDirection = Nsg_Rtl.Row;
                tab.style.alignItems = Align.Center;
                Nsg_Rtl.PaddingStart(tab.style, 6);
                Nsg_Rtl.PaddingEnd(tab.style, 4);
                tab.style.paddingTop = 3;
                tab.style.paddingBottom = 3;
                Nsg_Rtl.MarginEnd(tab.style, 2);
                tab.style.backgroundColor = active
                    ? new Color(0.28f, 0.30f, 0.34f)
                    : new Color(0.20f, 0.21f, 0.24f);
                Nsg_Visual.Round(tab, 4);

                // Иконка языка: Dependencies/Editor/Sprite/<IconName>.png
                var icon = new VisualElement();
                icon.style.width = 15;
                icon.style.height = 15;
                Nsg_Rtl.MarginEnd(icon.style, 5);
                var sprite = Nsg_LanguageRegistry.LoadIcon(doc.Entry);
                if (sprite != null)
                {
                    // Иконки квадратные, поэтому растяжение в квадратный
                    // контейнер ничего не портит.
                    icon.style.backgroundImage = new StyleBackground(sprite);
                }
                tab.Add(icon);

                var name = new Label(Path.GetFileName(doc.CsPath));
                name.style.color = active ? Color.white : new Color(0.72f, 0.75f, 0.80f);
                Nsg_Rtl.MarginEnd(name.style, 6);
                tab.Add(name);

                var close = new Button(() => CloseTab(index));
                close.text = "×";
                close.style.width = 16;
                close.style.height = 15;
                close.style.paddingLeft = 0f;
                close.style.paddingRight = 0f;
                close.style.fontSize = 11;
                close.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
                tab.Add(close);

                tab.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    ActivateTab(index);
                });

                _tabBar.Add(tab);
            }
        }

        void ActivateTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            if (index == _active) return;

            SaveActiveTab();
            _active = index;
            _doc = _tabs[index];
            AttachDoc();
            RebuildTabs();
            RebuildPalette();
        }

        void CloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;

            var doc = _tabs[index];
            if (doc != null && doc.Model != null) doc.SaveModel();

            _tabs.RemoveAt(index);
            Nsg_Manager.Instance.Close(doc);

            if (_tabs.Count == 0)
            {
                _active = -1;
                _doc = null;
            }
            else
            {
                if (_active > index) _active--;
                else if (_active == index) _active = Mathf.Clamp(index, 0, _tabs.Count - 1);
                _doc = _tabs[_active];
            }

            AttachDoc();
            RebuildTabs();
            RebuildPalette();
        }

        void SaveActiveTab()
        {
            if (_doc == null || _doc.Model == null) return;
            _doc.SaveModel();
        }

        // ==================================================================
        // Открытие
        // ==================================================================

        void OpenFromSelection()
        {
            string path = null;
            var obj = Selection.activeObject;
            if (obj != null) path = AssetDatabase.GetAssetPath(obj);

            // Проверяем не «есть ли расширение», а «знает ли язык это
            // расширение»: иначе открывался бы любой текстовый файл, а .py,
            // .rs и .go — не открывались бы как чужие.
            string ext = string.IsNullOrEmpty(path) ? null : Path.GetExtension(path);
            bool known = !string.IsNullOrEmpty(ext) &&
                         Nsg_Manager.Instance.RegisteredExtensions().Contains(ext);

            if (!known)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectSource"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            OpenPath(path);
        }

        /// <summary>Открывает файл в новой вкладке (или переключается на неё).</summary>
        public void OpenPath(string path)
        {
            var doc = Nsg_Manager.Instance.Open(path);

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] == doc)
                {
                    ActivateTab(i);
                    return;
                }
            }

            if (!doc.IsManaged)
            {
                bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                    Path.GetFileName(path) + "\n\n" + Nsg_L10n.T("confirm.manage") + "\n" +
                    Path.GetFileName(doc.NsgPath),
                    Nsg_L10n.T("confirm.manageYes"), Nsg_L10n.T("confirm.cancel"));
                if (!ok) return;

                doc.Diagnostics.Clear();
                if (!Nsg_Manager.Instance.Manage(doc))
                {
                    ReportError(Nsg_L10n.T("err.manageFailed"), doc);
                    return;
                }
            }

            SaveActiveTab();
            _tabs.Add(doc);
            _active = _tabs.Count - 1;
            _doc = doc;
            AttachDoc();
            RebuildTabs();
            RebuildPalette();
        }

        /// <summary>Совместимость со старым именем.</summary>
        public void OpenCs(string path)
        {
            OpenPath(path);
        }

        void AttachDoc()
        {
            AttachDoc(true);
        }

        void AttachDoc(bool resetUndo)
        {
            if (_view != null)
            {
                _view.Doc = _doc;
                _view.Library = ActiveLibrary();
                if (resetUndo) _view.ResetUndo();
                _view.Rebuild();
            }
            RefreshStatus();
            RefreshUndoButtons();
            PushDiagnostics();
        }

        Nsg_BlockLibrary ActiveLibrary()
        {
            if (_doc != null && _doc.Library != null) return _doc.Library;
            return Nsg_Manager.Instance.Library;
        }

        // ==================================================================
        // Палитра блоков
        // ==================================================================

        /// <summary>Категория автогенерируемых API-блоков.</summary>
        const string ApiCategory = "cat.api";

        /// <summary>Ключ индекса «всё, что не латиница». Сортируется после Z.</summary>
        const string OtherIndexKey = "~";

        void RebuildPalette()
        {
            if (_palette == null) return;
            _palette.Clear();

            // Ссылка на прежнюю секцию наборов больше не валидна: контейнер
            // только что отцепили. Иначе сохранение набора дописывало бы
            // строки в отсоединённый элемент.
            _presetPanel = null;

            var lib = ActiveLibrary();
            if (lib == null)
            {
                _palette.Add(new Label(Nsg_L10n.T("palette.empty")));
                return;
            }

            string query = (_paletteQuery ?? string.Empty).Trim();
            if (query.Length > 0)
            {
                RebuildPaletteSearch(lib, query);
                return;
            }

            var shownGroups = new HashSet<string>();
            var categories = lib.Categories();

            // 1) Обычные категории: каждая — сворачиваемая секция.
            for (int c = 0; c < categories.Count; c++)
            {
                string cat = categories[c];
                if (cat == ApiCategory) continue;

                var blocks = UniqueVariants(lib.InCategory(cat), shownGroups);
                if (blocks.Count == 0) continue;

                var content = AddPaletteSection(_palette, "cat:" + cat, CategoryTitle(cat), 0);
                for (int i = 0; i < blocks.Count; i++) content.Add(BuildPaletteItem(blocks[i]));
            }

            // 2) Наборы: такая же секция, как остальные, а не отдельная панель.
            //    Метка зоны остаётся на содержимом: перетаскивание блоков сюда
            //    по-прежнему сохраняет выделение как набор.
            _presetPanel = AddPaletteSection(_palette, "presets", Nsg_L10n.T("preset.title"), 0);
            _presetPanel.userData = new Nsg_ScriptView.PresetZoneMarker();
            RebuildPresets();

            // 3) API: одна общая секция, а внутри — индекс по первой букве.
            //    В проекте бывают тысячи API-блоков, и без индекса список
            //    превращается в простыню.
            var apiBlocks = UniqueVariants(lib.InCategory(ApiCategory), shownGroups);
            if (apiBlocks.Count > 0)
            {
                var apiContent = AddPaletteSection(_palette, "api", CategoryTitle(ApiCategory), 0);

                var byLetter = new SortedDictionary<string, List<NsgBlockDef>>(System.StringComparer.Ordinal);
                for (int i = 0; i < apiBlocks.Count; i++)
                {
                    string letter = IndexLetterOf(apiBlocks[i]);

                    List<NsgBlockDef> bucket;
                    if (!byLetter.TryGetValue(letter, out bucket))
                    {
                        bucket = new List<NsgBlockDef>();
                        byLetter[letter] = bucket;
                    }
                    bucket.Add(apiBlocks[i]);
                }

                foreach (var kv in byLetter)
                {
                    // Глубина 2, а не 1: буквенный индекс должен читаться как
                    // вложенный в API, иначе он выглядит таким же разделом.
                    var letterContent = AddPaletteSection(apiContent, "api:" + kv.Key, IndexTitle(kv.Key), 2);
                    for (int i = 0; i < kv.Value.Count; i++) letterContent.Add(BuildPaletteItem(kv.Value[i]));
                }
            }

            if (_palette.childCount == 0)
            {
                _palette.Add(new Label(Nsg_L10n.T("palette.empty")));
            }
        }

        /// <summary>Плоский список результатов поиска: сворачивать в нём нечего.</summary>
        void RebuildPaletteSearch(Nsg_BlockLibrary lib, string query)
        {
            var hits = lib.Search(query, 200);

            var head = new Label(Nsg_L10n.T("palette.found", hits.Count));
            head.style.fontSize = 10;
            head.style.color = new Color(0.62f, 0.66f, 0.72f);
            head.style.marginBottom = 4;
            _palette.Add(head);

            if (hits.Count == 0)
            {
                _palette.Add(new Label(Nsg_L10n.T("palette.noMatch")));
                return;
            }

            for (int i = 0; i < hits.Count; i++) _palette.Add(BuildPaletteItem(hits[i]));
        }

        static string CategoryTitle(string cat)
        {
            return !string.IsNullOrEmpty(cat) && cat.StartsWith("cat.") ? Nsg_L10n.T(cat) : cat;
        }

        /// <summary>Оставляет по одному представителю на группу вариантов.</summary>
        static List<NsgBlockDef> UniqueVariants(List<NsgBlockDef> blocks, HashSet<string> shownGroups)
        {
            var list = new List<NsgBlockDef>();
            for (int i = 0; i < blocks.Count; i++)
            {
                var def = blocks[i];
                if (def == null) continue;
                if (!string.IsNullOrEmpty(def.variantGroup) && !shownGroups.Add(def.variantGroup)) continue;
                list.Add(def);
            }
            return list;
        }

        /// <summary>
        /// Буква индекса: первая буква имени типа. Всё, что не латиница
        /// (подчёркивание, кириллица, CJK), уходит в «прочее».
        /// </summary>
        static string IndexLetterOf(NsgBlockDef def)
        {
            string name = def != null ? def.category : null;
            if (!string.IsNullOrEmpty(name))
            {
                char ch = name[0];
                if (ch >= 'a' && ch <= 'z') return char.ToUpperInvariant(ch).ToString();
                if (ch >= 'A' && ch <= 'Z') return ch.ToString();
            }
            return OtherIndexKey;
        }

        static string IndexTitle(string letter)
        {
            return letter == OtherIndexKey ? Nsg_L10n.T("palette.other") : letter;
        }

        bool PaletteSectionCollapsed(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            // Буквенные индексы свёрнуты по умолчанию: их смысл — экономить место.
            if (key.StartsWith("api:")) return !_paletteExpanded.Contains(key);
            return _paletteCollapsed.Contains(key);
        }

        void SetPaletteSectionCollapsed(string key, bool collapsed)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (key.StartsWith("api:"))
            {
                if (collapsed) _paletteExpanded.Remove(key);
                else _paletteExpanded.Add(key);
                return;
            }

            if (collapsed) _paletteCollapsed.Add(key);
            else _paletteCollapsed.Remove(key);
        }

        /// <summary>Сворачиваемая секция палитры. Возвращает контейнер содержимого.</summary>
        VisualElement AddPaletteSection(VisualElement parent, string key, string title, int depth)
        {
            var box = new VisualElement();

            var head = new Button();
            head.style.unityTextAlign = TextAnchor.MiddleLeft;
            head.style.fontSize = 11;
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.color = new Color(0.72f, 0.76f, 0.83f);
            head.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
            head.style.marginTop = depth == 0 ? 6 : 2;
            head.style.marginBottom = 2;
            head.style.height = 20;
            Nsg_Rtl.PaddingStart(head.style, 4 + depth * 8);
            Nsg_Visual.Round(head, 3);

            var content = new VisualElement();
            Nsg_Rtl.MarginStart(content.style, depth * 8);

            bool collapsed = PaletteSectionCollapsed(key);
            head.text = (collapsed ? "▸ " : "▾ ") + title;
            content.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;

            head.clicked += () =>
            {
                collapsed = !collapsed;
                SetPaletteSectionCollapsed(key, collapsed);
                content.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
                head.text = (collapsed ? "▸ " : "▾ ") + title;
            };

            box.Add(head);
            box.Add(content);
            parent.Add(box);
            return content;
        }

        VisualElement BuildPaletteItem(NsgBlockDef def)
        {
            Color color = def.BlockColor();

            var item = new VisualElement();
            item.style.flexDirection = Nsg_Rtl.Row;
            item.style.alignItems = Align.Center;
            item.style.backgroundColor = color;
            item.style.paddingLeft = 6;
            item.style.paddingRight = 6;
            item.style.paddingTop = 3;
            item.style.paddingBottom = 3;
            item.style.marginBottom = 2;
            item.tooltip = def.id + "\n" + def.manual;
            Nsg_Visual.Round(item, 4);
            Nsg_Visual.ApplySprite(item, def.IsExpression ? "expr" : "header", color);

            var label = new Label(def.Label());
            label.style.color = Nsg_Palette.TextOn(color);
            label.style.fontSize = 11;
            item.Add(label);

            string blockId = def.id;
            item.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                BeginDrag(blockId, evt, item);
            });
            item.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_dragging) return;
                MoveGhost(evt.position);
                evt.StopPropagation();
            });
            item.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_dragging) return;
                EndDrag(evt.position, item, evt.pointerId);
                evt.StopPropagation();
            });

            return item;
        }

        // ------------------------------------------------------------------
        // Перетаскивание
        // ------------------------------------------------------------------

        void BeginDrag(string blockId, PointerDownEvent evt, VisualElement source)
        {
            _dragBlockId = blockId;
            _dragPreset = null;
            _dragging = true;

            _dragGhost = MakeGhost(blockId);
            rootVisualElement.Add(_dragGhost);
            MoveGhost(evt.position);

            if (_view != null) _view.SetDropHighlight(true);
            source.CapturePointer(evt.pointerId);
        }

        /// <summary>
        /// Перетаскивание набора из панели. Набор — это не блок с id, поэтому
        /// путь отдельный: у него своё имя и своя вставка.
        /// </summary>
        /// <summary>
        /// Начало перетаскивания набора. Принимает id указателя и координату, а
        /// не событие: бросок начинается из PointerMove (после порога смещения),
        /// и тип события здесь был бы только помехой.
        /// </summary>
        void BeginPresetDrag(Nsg_Preset preset, int pointerId, Vector2 panelPosition, VisualElement source)
        {
            if (preset == null || source == null) return;

            _dragPreset = preset;
            _dragBlockId = null;
            _dragging = true;

            _dragGhost = MakePresetGhost(preset);
            rootVisualElement.Add(_dragGhost);
            MoveGhost(panelPosition);

            if (_view != null) _view.SetDropHighlight(true);
            source.CapturePointer(pointerId);
        }

        VisualElement MakePresetGhost(Nsg_Preset preset)
        {
            var ghost = new Label(Nsg_L10n.T("preset.title") + ": " + preset.name);
            ghost.style.position = Position.Absolute;
            ghost.style.paddingLeft = 7;
            ghost.style.paddingRight = 7;
            ghost.style.paddingTop = 3;
            ghost.style.paddingBottom = 3;
            ghost.style.backgroundColor = Nsg_Palette.Macro;
            ghost.style.color = Nsg_Palette.TextOn(Nsg_Palette.Macro);
            ghost.style.opacity = 0.9f;
            // Иначе призрак перехватывал бы Pick и цель не находилась.
            ghost.pickingMode = PickingMode.Ignore;
            Nsg_Visual.Round(ghost, 4);
            return ghost;
        }

        VisualElement MakeGhost(string blockId)
        {
            var lib = ActiveLibrary();
            var def = lib != null ? lib.Get(blockId) : null;
            Color color = def != null ? def.BlockColor() : Nsg_Palette.Frame;

            var ghost = new Label(def != null ? def.Label() : blockId);
            ghost.style.position = Position.Absolute;
            ghost.style.paddingLeft = 7;
            ghost.style.paddingRight = 7;
            ghost.style.paddingTop = 3;
            ghost.style.paddingBottom = 3;
            ghost.style.backgroundColor = color;
            ghost.style.color = Nsg_Palette.TextOn(color);
            ghost.style.opacity = 0.9f;
            // Иначе призрак перехватывал бы Pick и цель не находилась.
            ghost.pickingMode = PickingMode.Ignore;
            Nsg_Visual.Round(ghost, 4);
            return ghost;
        }

        void MoveGhost(Vector2 panelPosition)
        {
            if (_dragGhost == null) return;
            var local = rootVisualElement.WorldToLocal(panelPosition);

            // В RTL призрак висит слева от курсора: справа теперь полотно,
            // и призрак закрывал бы то, что под ним.
            if (Nsg_Rtl.On)
            {
                float w = _dragGhost.layout.width;
                _dragGhost.style.left = local.x - 14f - (w > 1f ? w : 70f);
            }
            else
            {
                _dragGhost.style.left = local.x + 14;
            }
            _dragGhost.style.top = local.y + 10;

            UpdateDropPreview(panelPosition);
        }

        void UpdateDropPreview(Vector2 panelPosition)
        {
            if (_view == null) return;

            var panel = rootVisualElement.panel;
            if (panel == null) return;

            var picked = panel.Pick(panelPosition);
            _hoverTarget = Nsg_ScriptView.ResolveDrop(picked);

            // У набора нет определения блока, поэтому подписью служит его имя.
            _view.ShowDropPreview(_hoverTarget, _dragBlockId,
                _dragPreset != null ? _dragPreset.name : null);
        }

        void EndDrag(Vector2 panelPosition, VisualElement source, int pointerId)
        {
            _dragging = false;
            string blockId = _dragBlockId;
            var preset = _dragPreset;
            _dragBlockId = null;
            _dragPreset = null;

            if (_dragGhost != null)
            {
                _dragGhost.RemoveFromHierarchy();
                _dragGhost = null;
            }
            if (_view != null)
            {
                _view.ClearDropPreview();
                _view.SetDropHighlight(false);
            }
            if (source != null && source.HasPointerCapture(pointerId)) source.ReleasePointer(pointerId);

            if (_view == null) return;
            if (preset == null && string.IsNullOrEmpty(blockId)) return;

            // Сначала берём то, что показал призрак: что видел — то и получишь.
            var target = _hoverTarget;
            _hoverTarget = null;

            if (target == null || target.Kind == Nsg_ScriptView.DropKind.None)
            {
                var panel = rootVisualElement.panel;
                var picked = panel != null ? panel.Pick(panelPosition) : null;
                target = Nsg_ScriptView.ResolveDrop(picked);
            }

            // Точка не дала цели — отдаём блоку запасное место. Раньше здесь
            // стоял return, и блок просто исчезал.
            if (target == null || target.Kind == Nsg_ScriptView.DropKind.None)
            {
                target = _view.FallbackDrop();
            }

            if (target == null || target.Kind == Nsg_ScriptView.DropKind.None) return;

            if (preset != null) _view.DropPreset(target, preset);
            else _view.DropBlock(target, blockId);

            RefreshUndoButtons();
            RefreshStatus();
        }

        /// <summary>
        /// Аварийно снимает перетаскивание из палитры: потерян захват указателя.
        /// Ничего не кладём — блок остаётся в палитре, а призрак убирается.
        /// </summary>
        void CancelDrag()
        {
            if (!_dragging && _dragGhost == null) return;

            _dragging = false;
            _dragBlockId = null;
            _dragPreset = null;
            _hoverTarget = null;

            if (_dragGhost != null)
            {
                _dragGhost.RemoveFromHierarchy();
                _dragGhost = null;
            }

            if (_view != null)
            {
                _view.ClearDropPreview();
                _view.SetDropHighlight(false);
            }
        }

        // ==================================================================
        // Слоты наборов
        // ==================================================================

        void RebuildPresets()
        {
            if (_presetPanel == null) return;
            _presetPanel.Clear();

            // Заголовок рисует сворачиваемая секция палитры, поэтому здесь
            // сразу идёт кнопка сохранения.
            var save = new Button(OnSavePreset);
            save.text = Nsg_L10n.T("preset.save");
            save.style.marginBottom = 5;
            _presetPanel.Add(save);

            var list = Nsg_Presets.Sorted();
            if (list.Count == 0)
            {
                var none = new Label(Nsg_L10n.T("preset.none"));
                none.style.fontSize = 10;
                none.style.color = new Color(0.55f, 0.58f, 0.63f);
                _presetPanel.Add(none);
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var preset = list[i];

                var row = new VisualElement();
                row.style.flexDirection = Nsg_Rtl.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                // Кнопка заменена на обычный элемент: Button съедал бы нажатие
                // под себя, и перетаскивание набора не начиналось бы вовсе.
                // Клик и перетаскивание различаются по порогу смещения.
                var insert = new VisualElement();
                insert.style.flexGrow = 1;
                insert.style.flexDirection = Nsg_Rtl.Row;
                insert.style.alignItems = Align.Center;
                insert.style.paddingLeft = 6;
                insert.style.paddingRight = 6;
                insert.style.paddingTop = 3;
                insert.style.paddingBottom = 3;
                insert.style.backgroundColor = Nsg_Palette.Macro;
                insert.tooltip = preset.name;
                Nsg_Visual.Round(insert, 4);

                var caption = new Label(preset.name + "  (" + preset.blockCount + ")");
                caption.style.color = Nsg_Palette.TextOn(Nsg_Palette.Macro);
                caption.style.fontSize = 11;
                caption.pickingMode = PickingMode.Ignore;
                insert.Add(caption);

                Vector2 downPos = Vector2.zero;
                bool moved = false;
                int pointer = -1;

                insert.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;

                    downPos = evt.position;
                    moved = false;
                    pointer = evt.pointerId;

                    // Захват берётся сразу: иначе при быстром движении указатель
                    // успевает уйти с элемента, PointerMove до нас не доходит, и
                    // перетаскивание просто не начинается.
                    insert.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                });

                insert.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (pointer < 0) return;

                    if (!moved)
                    {
                        // Порог нужен, иначе обычный клик превращался бы в бросок.
                        if (((Vector2)evt.position - downPos).magnitude < 4f) return;
                        moved = true;
                        BeginPresetDrag(preset, evt.pointerId, evt.position, insert);
                    }

                    MoveGhost(evt.position);
                    evt.StopPropagation();
                });

                insert.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (pointer < 0) return;
                    int id = pointer;
                    pointer = -1;

                    if (moved)
                    {
                        EndDrag(evt.position, insert, id);
                    }
                    else
                    {
                        if (insert.HasPointerCapture(id)) insert.ReleasePointer(id);
                        OnInsertPreset(preset);
                    }

                    evt.StopPropagation();
                });

                row.Add(insert);

                var del = new Button(() =>
                {
                    // Удаление набора необратимо, поэтому спрашиваем.
                    if (!EditorUtility.DisplayDialog(Nsg_L10n.T("preset.title"),
                            Nsg_L10n.T("preset.confirmDelete", preset.name),
                            Nsg_L10n.T("confirm.ok"), Nsg_L10n.T("confirm.cancel")))
                    {
                        return;
                    }

                    Nsg_Presets.Remove(preset.name);
                    RebuildPresets();
                });
                del.text = "×";
                del.style.width = 18;
                del.style.height = 16;
                Nsg_Rtl.MarginStart(del.style, 2);
                row.Add(del);

                _presetPanel.Add(row);
            }
        }

        void OnSavePreset()
        {
            if (_view == null) return;

            string error;
            var preset = _view.BuildPresetFromSelection(out error);

            if (preset == null)
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("preset.title"), error, Nsg_L10n.T("confirm.ok"));
                return;
            }

            Nsg_NameDialog.Show(Nsg_L10n.T("preset.namePrompt"), string.Empty, name =>
            {
                preset.name = (name ?? string.Empty).Trim();

                string err;
                if (Nsg_Presets.Add(preset, out err))
                {
                    RebuildPresets();
                }
                else
                {
                    EditorUtility.DisplayDialog(Nsg_L10n.T("preset.title"), err, Nsg_L10n.T("confirm.ok"));
                }
            });
        }

        void OnInsertPreset(Nsg_Preset preset)
        {
            if (_view == null || preset == null) return;
            if (!_view.PastePreset(preset)) return;

            RefreshUndoButtons();
            RefreshStatus();
        }

        /// <summary>
        /// Сброс перетаскивания в зону наборов: сохраняем выделение как набор.
        /// Возвращает true, если сброс обработан здесь.
        /// </summary>
        bool TryExternalDrop(Vector2 panelPosition)
        {
            var panel = rootVisualElement.panel;
            if (panel == null) return false;

            var e = panel.Pick(panelPosition);
            while (e != null)
            {
                if (e.userData is Nsg_ScriptView.PresetZoneMarker)
                {
                    OnSavePreset();
                    return true;
                }
                e = e.parent;
            }
            return false;
        }

        // ==================================================================
        // Операции синхронизации
        // ==================================================================

        void OnManage()
        {
            if (_doc == null) { OpenFromSelection(); return; }
            if (_doc.IsManaged) return;

            _doc.Diagnostics.Clear();
            if (!Nsg_Manager.Instance.Manage(_doc)) ReportError(Nsg_L10n.T("err.manageFailed"), _doc);
            AttachDoc();
        }

        void OnUnmanage()
        {
            if (_doc == null) return;

            bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                Nsg_L10n.T("confirm.unmanage"), Nsg_L10n.T("confirm.unmanageYes"), Nsg_L10n.T("confirm.cancel"));
            if (!ok) return;

            Nsg_Manager.Instance.Unmanage(_doc);

            int index = _tabs.IndexOf(_doc);
            if (index >= 0) CloseTab(index);
            else AttachDoc();
        }

        void OnImport()
        {
            if (_doc == null) return;

            if (_doc.State == NsgDocState.BlocksDirty || _doc.State == NsgDocState.Conflict)
            {
                bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                    Nsg_L10n.T("confirm.reimportConflict"),
                    Nsg_L10n.T("confirm.continue"), Nsg_L10n.T("confirm.cancel"));
                if (!ok) return;
            }

            _doc.Diagnostics.Clear();
            if (!Nsg_Manager.Instance.ReimportCode(_doc)) ReportError(Nsg_L10n.T("err.reimportFailed"), _doc);
            AttachDoc();
        }

        void OnGenerate()
        {
            if (_doc == null) return;

            if (_doc.State == NsgDocState.Conflict)
            {
                bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                    Nsg_L10n.T("confirm.overwriteConflict"),
                    Nsg_L10n.T("confirm.continue"), Nsg_L10n.T("confirm.cancel"));
                if (!ok) return;
            }

            _doc.Diagnostics.Clear();
            if (!Nsg_Manager.Instance.OverwriteCode(_doc)) ReportError(Nsg_L10n.T("err.generateFailed"), _doc);
            AttachDoc();
        }

        void OnPreview()
        {
            if (_doc == null) return;

            string text;
            if (Nsg_Manager.Instance.Preview(_doc, out text))
                Debug.Log("[NekoScriptGraph] " + Path.GetFileName(_doc.CsPath) + "\n" + text);
            else
                ReportError(Nsg_L10n.T("err.generateFailed"), _doc);

            PushDiagnostics();
        }

        // ==================================================================
        // Отмена / повтор
        // ==================================================================

        /// <summary>
        /// Возвращает фокус корню окна, чтобы горячие клавиши работали и после
        /// клика по полотну. Поля ввода и списки не трогаем.
        /// </summary>
        void OnRootPointerDown(PointerDownEvent evt)
        {
            var t = evt.target as VisualElement;
            if (t == null) return;
            if (t is TextField || t is DropdownField || t is Toggle) return;
            if (t.GetFirstAncestorOfType<TextField>() != null) return;
            if (t.GetFirstAncestorOfType<DropdownField>() != null) return;

            if (rootVisualElement.focusController != null &&
                rootVisualElement.focusController.focusedElement != rootVisualElement)
            {
                rootVisualElement.Focus();
            }
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            bool mod = evt.ctrlKey || evt.commandKey;
            if (!mod) return;

            var t = evt.target as VisualElement;
            if (t is TextField) return;
            if (t != null && t.GetFirstAncestorOfType<TextField>() != null) return;

            bool handled = false;

            if (evt.keyCode == KeyCode.Z)
            {
                handled = evt.shiftKey ? (_view != null && _view.PerformRedo())
                                       : (_view != null && _view.PerformUndo());
            }
            else if (evt.keyCode == KeyCode.D)
            {
                // Cmd/Ctrl+D — сразу скопировать и выделить копии.
                handled = _view != null && _view.DuplicateSelection();
            }
            else if (evt.keyCode == KeyCode.C)
            {
                if (_view != null) { _view.CopySelection(); handled = true; }
            }
            else if (evt.keyCode == KeyCode.V)
            {
                handled = _view != null && _view.PasteClipboard();
            }
            else if (evt.keyCode == KeyCode.Backspace || evt.keyCode == KeyCode.Delete)
            {
                handled = _view != null && _view.DeleteSelection();
            }

            if (!handled) return;

            evt.StopPropagation();
            evt.StopImmediatePropagation();
            RefreshUndoButtons();
            RefreshStatus();
        }

        void OnUndo()
        {
            if (_view == null || !_view.PerformUndo()) return;
            RefreshUndoButtons();
            RefreshStatus();
        }

        void OnRedo()
        {
            if (_view == null || !_view.PerformRedo()) return;
            RefreshUndoButtons();
            RefreshStatus();
        }

        void RefreshUndoButtons()
        {
            bool canUndo = _view != null && _view.Undo.CanUndo;
            bool canRedo = _view != null && _view.Undo.CanRedo;

            if (_undoBtn != null)
            {
                _undoBtn.SetEnabled(canUndo);
                _undoBtn.tooltip = canUndo ? ("Ctrl/Cmd+Z — " + _view.Undo.NextUndoLabel) : "Ctrl/Cmd+Z";
            }
            if (_redoBtn != null)
            {
                _redoBtn.SetEnabled(canRedo);
                _redoBtn.tooltip = canRedo ? ("Ctrl/Cmd+Shift+Z — " + _view.Undo.NextRedoLabel) : "Ctrl/Cmd+Shift+Z";
            }
        }

        // ==================================================================
        // Точки сохранения
        // ==================================================================

        void ShowCheckpointMenu()
        {
            if (_doc == null) return;

            var menu = new GenericMenu();
            for (int i = 0; i < Nsg_Checkpoints.SlotCount; i++)
            {
                int slot = i;
                string desc = Nsg_Checkpoints.Describe(_doc.CsPath, slot);

                menu.AddItem(new GUIContent(desc == null
                        ? Nsg_L10n.T("cp.save", slot + 1)
                        : Nsg_L10n.T("cp.overwrite", slot + 1, desc)),
                    false, () => SaveCheckpoint(slot));

                if (desc != null)
                {
                    menu.AddItem(new GUIContent(Nsg_L10n.T("cp.load", slot + 1, desc)),
                        false, () => LoadCheckpoint(slot));
                    menu.AddItem(new GUIContent(Nsg_L10n.T("cp.delete", slot + 1)),
                        false, () => { Nsg_Checkpoints.Delete(_doc.CsPath, slot); });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent(Nsg_L10n.T("cp.empty", slot + 1)));
                }

                menu.AddSeparator(string.Empty);
            }

            menu.AddDisabledItem(new GUIContent(Nsg_L10n.T("git.note")));
            menu.ShowAsContext();
        }

        void SaveCheckpoint(int slot)
        {
            if (_doc == null || _doc.Model == null) return;
            string cs = File.Exists(_doc.CsPath) ? File.ReadAllText(_doc.CsPath) : null;
            Nsg_Checkpoints.Save(_doc.CsPath, slot, _doc.Model, cs, Path.GetFileName(_doc.CsPath));
            Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("cp.saved", slot + 1));
        }

        void LoadCheckpoint(int slot)
        {
            if (_doc == null) return;

            bool ok = EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                Nsg_L10n.T("cp.confirmLoad"), Nsg_L10n.T("confirm.continue"), Nsg_L10n.T("confirm.cancel"));
            if (!ok) return;

            var cp = Nsg_Checkpoints.Load(_doc.CsPath, slot);
            if (cp == null) return;

            if (!string.IsNullOrEmpty(cp.csSource)) File.WriteAllText(_doc.CsPath, cp.csSource);

            var model = Nsg_Checkpoints.ModelOf(cp);
            if (model != null)
            {
                model.sourceGuid = AssetDatabase.AssetPathToGUID(_doc.CsPath);
                _doc.Model = model;
                _doc.SaveModel();
            }

            _doc.Diagnostics.Clear();
            _doc.RefreshState();
            AttachDoc();
            AssetDatabase.Refresh();
            Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("cp.loaded", slot + 1));
        }

        // ==================================================================
        // Git
        // ==================================================================

        void OnGitCheckpoint()
        {
            if (_doc == null) return;

            string repoRoot;
            if (!Nsg_Git.IsRepo(_doc.CsPath, out repoRoot))
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("git.notRepo"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            var paths = new List<string> { _doc.CsPath };
            if (_doc.IsManaged) paths.Add(_doc.NsgPath);

            string status;
            if (!Nsg_Git.HasChanges(repoRoot, paths, out status))
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("git.noChanges"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            string message = "NekoScriptGraph checkpoint: " + Path.GetFileName(_doc.CsPath);
            string output;
            if (Nsg_Git.Commit(repoRoot, paths, message, out output))
            {
                Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("git.done") + "\n" + output);
            }
            else
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"),
                    Nsg_L10n.T("git.failed") + "\n" + output, Nsg_L10n.T("confirm.ok"));
            }
        }

        // ==================================================================
        // Разное
        // ==================================================================

        void OnSelfTest()
        {
            Nsg_SelfTest.Run();
        }

        void OnHealth()
        {
            Nsg_HealthWindow.Open(_doc);
        }

        // ------------------------------------------------------------------
        // Массовые операции
        // ------------------------------------------------------------------

        string ActiveFolder()
        {
            if (_doc == null || string.IsNullOrEmpty(_doc.CsPath)) return null;
            string dir = Path.GetDirectoryName(_doc.CsPath);
            return string.IsNullOrEmpty(dir) ? null : dir.Replace('\\', '/');
        }

        void OnApiFolder()
        {
            string folder = ActiveFolder();
            if (string.IsNullOrEmpty(folder))
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectFolder"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            Nsg_Menu.GenerateApiForFolder(folder);
            RebuildPalette();
            if (_view != null) _view.Rebuild();
        }

        void OnApiProject()
        {
            Nsg_Menu.GenerateApiForProject();
            RebuildPalette();
            if (_view != null) _view.Rebuild();
        }

        void OnManageFolder()
        {
            string folder = ActiveFolder();
            if (string.IsNullOrEmpty(folder))
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectFolder"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            Nsg_Menu.ManageAllIn(folder);
            AttachDoc(false);
            RebuildPalette();
        }

        void OnManageProject()
        {
            Nsg_Menu.ManageAllIn(null);
            AttachDoc(false);
            RebuildPalette();
        }

        void OnUnmanageFolder()
        {
            string folder = ActiveFolder();
            if (string.IsNullOrEmpty(folder))
            {
                EditorUtility.DisplayDialog(Nsg_L10n.T("confirm.title"), Nsg_L10n.T("confirm.selectFolder"),
                                            Nsg_L10n.T("confirm.ok"));
                return;
            }

            Nsg_Menu.UnmanageAllIn(folder);
            DropUnmanagedTabs();
        }

        void OnUnmanageProject()
        {
            Nsg_Menu.UnmanageAllIn(null);
            DropUnmanagedTabs();
        }

        /// <summary>
        /// Закрывает вкладки, чьи .nsg.json только что удалили.
        ///
        /// Обычный CloseTab здесь не годится: он сохраняет модель и тем самым
        /// воскресил бы только что удалённый файл. Поэтому документ забывается
        /// молча, без записи на диск.
        /// </summary>
        void DropUnmanagedTabs()
        {
            for (int i = _tabs.Count - 1; i >= 0; i--)
            {
                var doc = _tabs[i];
                if (doc != null && File.Exists(doc.NsgPath)) continue;

                if (doc != null)
                {
                    doc.Model = null;
                    doc.State = NsgDocState.Unmanaged;
                }
                _tabs.RemoveAt(i);
            }

            if (_tabs.Count == 0)
            {
                _active = -1;
                _doc = null;
            }
            else
            {
                if (_active >= _tabs.Count) _active = _tabs.Count - 1;
                if (_active < 0) _active = 0;
                _doc = _tabs[_active];
            }

            AttachDoc(false);
            RebuildTabs();
            RebuildPalette();
        }

        void OnReloadLibrary()
        {
            ReloadLibraries();
            Debug.Log("[NekoScriptGraph] " + Nsg_L10n.T("info.libraryReloaded",
                Nsg_Manager.Instance.Library.Blocks.Count));
        }

        /// <summary>
        /// Перечитывает библиотеки и ПЕРЕПРИВЯЗЫВАЕТ их к открытым документам.
        ///
        /// Без перепривязки документ остался бы со старым объектом библиотеки:
        /// менеджер кэш сбрасывает, а ссылку в документе — нет, и новый блок
        /// в палитре так и не появился бы.
        /// </summary>
        void ReloadLibraries()
        {
            Nsg_Manager.Instance.ReloadLibrary();

            for (int i = 0; i < _tabs.Count; i++)
            {
                var doc = _tabs[i];
                if (doc == null || doc.Entry == null) continue;
                doc.Library = Nsg_Manager.Instance.GetLibrary(doc.Entry);
            }

            if (_view != null)
            {
                _view.Library = ActiveLibrary();
                _view.Rebuild();
            }

            RebuildPalette();
        }

        void OnViewChanged()
        {
            // Правка модели: для кошки это признак, что работа идёт.
            if (_neko != null) _neko.Observe(Nsg_NekoEvent.Edit, 1);

            if (_doc == null || _doc.Model == null) return;
            if (_saveScheduled) return;

            _saveScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _saveScheduled = false;
                if (_doc == null || _doc.Model == null) return;
                _doc.SaveModel();
                _doc.RefreshState();
                RefreshStatus();
                RefreshUndoButtons();
            };
        }

        void ReportError(string prefix, Nsg_Document doc)
        {
            Debug.LogError("[NekoScriptGraph] " + prefix + ": " +
                           (doc != null ? doc.Diagnostics.Summary() : "?"));
            PushDiagnostics();
        }

        void PushDiagnostics()
        {
            Nsg_ErrorWindow.Push(_doc != null ? _doc.Diagnostics : null,
                                 _doc != null ? _doc.CsPath : null);
        }

        // ==================================================================
        // Кошка
        // ==================================================================

        /// <summary>
        /// Объяснить выделенный блок. Точка входа для горячей клавиши:
        /// пункт меню не знает про открытые окна, поэтому ищем существующее
        /// и НЕ создаём новое — иначе сочетание клавиш открывало бы окно.
        /// </summary>
        public static void ExplainSelectedBlock()
        {
            var all = Resources.FindObjectsOfTypeAll<Nsg_Window>();
            if (all == null || all.Length == 0) return;

            var w = all[0];
            if (w != null && w._view != null) w._view.ExplainSelectedBlock();
        }

        /// <summary>Здоровается один раз на сборку окна, а не на каждый статус.</summary>
        void NekoGreetOnce()
        {
            if (_nekoGreeted || _neko == null || !_neko.Active) return;

            _nekoGreeted = true;
            _neko.Greet();
        }

        /// <summary>
        /// Сообщает кошке о числе диагностик. Решение «говорить или молчать»
        /// принимает она сама: при появлении ошибки — зовёт немедленно, при
        /// разборе всех — радуется.
        ///
        /// Отправляем только при ИЗМЕНЕНИИ числа: иначе на каждой перерисовке
        /// статуса кошка получала бы одно и то же событие.
        /// </summary>
        void NekoReactToDiagnostics()
        {
            if (_neko == null || !_neko.Active) return;

            int count = _doc != null && _doc.Diagnostics != null
                ? _doc.Diagnostics.Items.Count
                : 0;

            if (count == _nekoLastDiagnostics) return;
            _nekoLastDiagnostics = count;

            _neko.Observe(Nsg_NekoEvent.Diagnostics, count);
        }

        void RefreshStatus()
        {
            if (_status == null) return;

            NekoGreetOnce();
            NekoReactToDiagnostics();

            if (_doc == null)
            {
                _status.text = Nsg_L10n.T("win.noDoc");
                return;
            }

            _doc.RefreshState();
            _status.text = _doc.LanguageId + "   ·   " + _doc.StateLabel();

            switch (_doc.State)
            {
                case NsgDocState.Synced:
                    _status.style.color = new Color(0.45f, 0.85f, 0.45f);
                    break;
                case NsgDocState.CsDirty:
                    _status.style.color = new Color(0.95f, 0.78f, 0.35f);
                    break;
                case NsgDocState.BlocksDirty:
                    _status.style.color = new Color(0.5f, 0.72f, 1f);
                    break;
                case NsgDocState.Conflict:
                    _status.style.color = new Color(1f, 0.45f, 0.45f);
                    break;
                default:
                    _status.style.color = Color.gray;
                    break;
            }

            if (_doc.Diagnostics.Items.Count > 0)
            {
                _status.text += "   ·   " + _doc.Diagnostics.Summary();
            }
        }
    }
}
