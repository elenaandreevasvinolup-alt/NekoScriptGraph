using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Настройки плагина и кошки.
    ///
    /// Окно намеренно одно: «поставить/снять расширение» и «сделать кошку
    /// тише» — это одно и то же действие пользователя, и разводить их по
    /// двум окнам значит заставлять искать.
    ///
    /// Вкладка кошки строится ПО ОПИСАНИЮ, которое даёт сама кошка. Ядро не
    /// знает ни одного её параметра: удалите ProgramNeko — вкладка исчезнет
    /// вместе с ней, а окно останется целым.
    /// </summary>
    public class Nsg_SettingsWindow : EditorWindow
    {
        enum Tab
        {
            General,
            Assistant,
            Extensions,
            Advanced
        }

        Tab _tab = Tab.General;

        VisualElement _tabs;
        VisualElement _body;
        Label _status;

        /// <summary>Результат пробного запуска внешнего движка.</summary>
        string _testResult;

        public static void Open()
        {
            var w = GetWindow<Nsg_SettingsWindow>();
            w.titleContent = new GUIContent(Nsg_L10n.T("set.title"));
            w.minSize = new Vector2(460, 380);
            w.Show();
        }

        /// <summary>Открыть сразу на вкладке кошки.</summary>
        public static void OpenAssistant()
        {
            OpenAt(Tab.Assistant);
        }

        /// <summary>
        /// Вкладка расширений, а если управлять нечем — общая. Так пункт меню
        /// не открывает пустую вкладку в сборке без кошки и без языков.
        /// </summary>
        public static void OpenExtensions()
        {
            OpenAt(Nsg_Extensions.HasAnything ? Tab.Extensions : Tab.General);
        }

        /// <summary>Открыть сразу на вкладке продвинутых возможностей.</summary>
        public static void OpenAdvanced()
        {
            OpenAt(Tab.Advanced);
        }

        static void OpenAt(Tab tab)
        {
            Open();
            var w = GetWindow<Nsg_SettingsWindow>();
            w._tab = tab;
            w.Rebuild();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;

            _status = new Label(string.Empty);
            _status.style.fontSize = 11;
            _status.style.marginBottom = 6;
            _status.style.unityTextAlign = Nsg_Rtl.TextAlign;
            root.Add(_status);

            _tabs = new VisualElement();
            _tabs.style.flexDirection = Nsg_Rtl.Row;
            _tabs.style.marginBottom = 6;
            root.Add(_tabs);

            _body = new VisualElement();
            _body.style.flexGrow = 1;
            root.Add(_body);

            // Две версии рядом: ядро и плагин выпускаются независимо, и
            // разница между ними должна быть видна, а не выясняться опытом.
            var ver = new Label(Nsg_L10n.T("set.versions",
                                           Nsg_Version.Plugin, Nsg_Core.Version, Nsg_Core.Name));
            ver.style.fontSize = 9;
            ver.style.marginTop = 6;
            ver.style.color = new Color(0.52f, 0.55f, 0.60f);
            ver.style.unityTextAlign = Nsg_Rtl.TextAlign;
            root.Add(ver);

            Rebuild();
        }

        // ------------------------------------------------------------------

        void Rebuild()
        {
            if (_tabs == null || _body == null) return;

            // Сначала выясняем, что в ЭТОЙ сборке вообще есть. Вкладка
            // существует, только если у неё есть содержимое: в сборке без
            // кошки, без языковых пакетов и без каталога расширений остаётся
            // одна вкладка «Общее» — ровно то, что в ней и есть.
            var packs = Nsg_LocalePacks.All();
            bool assistant = Nsg_AssistantPack.Installed || Nsg_AssistantPack.Disabled;
            var engines = Nsg_ExtensionCatalog.All();

            bool hasExtensions = packs.Count > 0 || assistant || engines.Count > 0;
            bool hasAdvanced = Nsg_Advanced.Enabled;

            if (_tab == Tab.Assistant && !assistant) _tab = Tab.General;
            if (_tab == Tab.Extensions && !hasExtensions) _tab = Tab.General;
            if (_tab == Tab.Advanced && !hasAdvanced) _tab = Tab.General;

            _tabs.Clear();
            AddTab("set.tab.general", Tab.General);
            if (assistant) AddTab("set.tab.assistant", Tab.Assistant);
            if (hasExtensions) AddTab("set.tab.extensions", Tab.Extensions);
            if (hasAdvanced) AddTab("set.tab.advanced", Tab.Advanced);

            _body.Clear();
            switch (_tab)
            {
                case Tab.Assistant: BuildAssistant(); break;
                case Tab.Extensions: BuildExtensions(packs, assistant, engines); break;
                case Tab.Advanced: BuildAdvanced(); break;
                default: BuildGeneral(); break;
            }

            RefreshStatus(assistant);
        }

        /// <summary>
        /// Строка состояния говорит только о том, что существует. Нет кошки —
        /// нет и разговора о продвинутых возможностях: в такой сборке их
        /// просто нет, и обещать их было бы враньём.
        /// </summary>
        void RefreshStatus(bool assistantExists)
        {
            if (_status == null) return;

            if (!assistantExists)
            {
                _status.text = string.Empty;
                return;
            }

            bool on = Nsg_Advanced.Enabled;
            _status.text = Nsg_L10n.T(on ? "adv.enabled" : "adv.disabled");
            _status.style.color = on
                ? new Color(0.55f, 0.80f, 0.58f)
                : new Color(0.78f, 0.72f, 0.55f);
        }

        void AddTab(string key, Tab tab)
        {
            var b = new Button(() => { _tab = tab; Rebuild(); });
            b.text = Nsg_L10n.T(key);
            b.style.marginRight = 4;
            b.style.fontSize = 11;
            if (_tab == tab) b.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tabs.Add(b);
        }

        static Label Section(string key)
        {
            var l = new Label(Nsg_L10n.T(key));
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 10;
            l.style.marginBottom = 4;
            l.style.fontSize = 11;
            l.style.color = new Color(0.78f, 0.82f, 0.88f);
            l.style.unityTextAlign = Nsg_Rtl.TextAlign;
            return l;
        }

        static Label Note(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 3;
            l.style.color = new Color(0.60f, 0.64f, 0.70f);
            l.style.unityTextAlign = Nsg_Rtl.TextAlign;
            return l;
        }

        /// <summary>
        /// Подпись параметра. Кошка может отдать готовую строку из своего
        /// языкового пакета — тогда ядро в чужие переводы не заглядывает.
        /// </summary>
        static string TitleOf(Nsg_NekoSettingDef def)
        {
            if (def == null) return string.Empty;
            if (!string.IsNullOrEmpty(def.Title)) return def.Title;
            return string.IsNullOrEmpty(def.TitleKey) ? string.Empty : Nsg_L10n.T(def.TitleKey);
        }

        static string DescOf(Nsg_NekoSettingDef def)
        {
            if (def == null) return null;
            if (!string.IsNullOrEmpty(def.Desc)) return def.Desc;
            return string.IsNullOrEmpty(def.DescKey) ? null : Nsg_L10n.T(def.DescKey);
        }

        // ------------------------------------------------------------------
        // Общее
        // ------------------------------------------------------------------

        void BuildGeneral()
        {
            var scroll = new ScrollView();
            _body.Add(scroll);

            scroll.Add(Section("set.general.view"));

            var settings = Nsg_Settings.Instance;

            var bp = new Toggle(Nsg_L10n.T("view.blueprint"));
            bp.value = settings.Mode == NsgViewMode.Blueprint;
            bp.RegisterValueChangedCallback(evt =>
            {
                settings.Mode = evt.newValue ? NsgViewMode.Blueprint : NsgViewMode.Stack;
                settings.Save();
            });
            scroll.Add(bp);

            var hide = new Toggle(Nsg_L10n.T("files.btnHide"));
            hide.value = settings.hideBlockFiles;
            hide.RegisterValueChangedCallback(evt =>
            {
                settings.hideBlockFiles = evt.newValue;
                settings.Save();
                Nsg_FileVisibility.ApplyAll(true);
            });
            scroll.Add(hide);

            scroll.Add(Section("set.general.api"));
            var api = new TextField(Nsg_L10n.T("api.folder"));
            api.value = settings.apiOutputFolder;
            api.RegisterValueChangedCallback(evt =>
            {
                settings.apiOutputFolder = evt.newValue;
                settings.Save();
            });
            scroll.Add(api);

            // Выбор языка нужен, только если языков больше одного: английский
            // встроен всегда, и список из одной строки — это не выбор.
            if (Nsg_L10n.LanguageCount > 1)
            {
                scroll.Add(Section("win.lang"));
                BuildLanguagePicker(scroll);
            }
        }

        void BuildLanguagePicker(VisualElement host)
        {
            int count = Nsg_L10n.LanguageCount;
            var choices = new List<string>(count);
            for (int i = 0; i < count; i++) choices.Add(Nsg_L10n.LanguageName(i));

            var dd = new DropdownField(Nsg_L10n.T("win.lang"), choices, Nsg_L10n.Lang);
            dd.RegisterValueChangedCallback(evt =>
            {
                int idx = choices.IndexOf(evt.newValue);
                if (idx < 0 || idx == Nsg_L10n.Lang) return;

                Nsg_L10n.Lang = idx;
                Nsg_MenuRuntime.Rebuild();
                Rebuild();
            });
            host.Add(dd);

            host.Add(Note(Nsg_L10n.T("set.general.langNote")));
        }

        // ------------------------------------------------------------------
        // Кошка
        // ------------------------------------------------------------------

        void BuildAssistant()
        {
            var scroll = new ScrollView();
            _body.Add(scroll);

            var s = Nsg_NekoSettingsRegistry.Current;
            if (s == null)
            {
                scroll.Add(Section("set.assistant.missing"));
                scroll.Add(Note(Nsg_L10n.T("set.assistant.missingNote")));

                var go = new Button(() => { _tab = Tab.Extensions; Rebuild(); });
                go.text = Nsg_L10n.T("set.tab.extensions");
                go.style.marginTop = 6;
                go.style.width = 220;
                scroll.Add(go);
                return;
            }

            var schema = s.Schema;
            if (schema == null || schema.Count == 0)
            {
                scroll.Add(Note(Nsg_L10n.T("set.assistant.noSchema")));
                return;
            }

            // Наборы идут первыми: настроить характер одним нажатием проще,
            // чем подбирать пять ползунков.
            var presets = s.Presets;
            if (presets != null && presets.Count > 0)
            {
                scroll.Add(Section("set.assistant.presets"));

                var row = new VisualElement();
                row.style.flexDirection = Nsg_Rtl.Row;
                for (int i = 0; i < presets.Count; i++)
                {
                    var p = presets[i];
                    var b = new Button(() =>
                    {
                        s.ApplyPreset(p);
                        s.Save();
                        Rebuild();
                    });
                    b.text = Nsg_L10n.T(p.TitleKey);
                    b.style.marginRight = 4;
                    b.style.fontSize = 11;
                    row.Add(b);
                }
                scroll.Add(row);
            }

            scroll.Add(Section("set.assistant.persona"));

            for (int i = 0; i < schema.Count; i++)
            {
                var def = schema[i];
                if (def == null || string.IsNullOrEmpty(def.Key)) continue;

                switch (def.Kind)
                {
                    case Nsg_NekoSettingKind.Info:
                        scroll.Add(Note(TitleOf(def)));
                        break;

                    case Nsg_NekoSettingKind.Toggle:
                    {
                        var t = new Toggle(TitleOf(def));
                        t.value = s.Get(def.Key) >= 0.5f;
                        t.RegisterValueChangedCallback(evt =>
                        {
                            s.Set(def.Key, evt.newValue ? 1f : 0f);
                            s.Save();
                        });
                        scroll.Add(t);
                        break;
                    }

                    case Nsg_NekoSettingKind.Choice:
                    {
                        var keys = def.ChoiceKeys ?? new string[0];
                        var labels = new List<string>(keys.Length);
                        for (int k = 0; k < keys.Length; k++) labels.Add(Nsg_L10n.T(keys[k]));

                        int cur = Mathf.Clamp(Mathf.RoundToInt(s.Get(def.Key)), 0,
                                              Mathf.Max(0, labels.Count - 1));
                        if (labels.Count == 0) break;

                        var dd = new DropdownField(TitleOf(def), labels, cur);
                        dd.RegisterValueChangedCallback(evt =>
                        {
                            int idx = labels.IndexOf(evt.newValue);
                            if (idx < 0) return;

                            float v = def.ChoiceValues != null && idx < def.ChoiceValues.Length
                                ? def.ChoiceValues[idx]
                                : idx;
                            s.Set(def.Key, v);
                            s.Save();
                        });
                        scroll.Add(dd);
                        break;
                    }

                    default:
                    {
                        var sl = new Slider(TitleOf(def), def.Min, def.Max);
                        sl.showInputField = true;
                        if (def.Step > 0f) sl.pageSize = def.Step;
                        sl.value = Mathf.Clamp(s.Get(def.Key), def.Min, def.Max);

                        // Значение применяется сразу, а на диск пишется по
                        // отпусканию: иначе перетаскивание писало бы файл
                        // десятки раз в секунду.
                        sl.RegisterValueChangedCallback(evt => s.Set(def.Key, evt.newValue));
                        sl.RegisterCallback<PointerUpEvent>(evt => s.Save());
                        scroll.Add(sl);
                        break;
                    }
                }

                string desc = DescOf(def);
                if (!string.IsNullOrEmpty(desc))
                {
                    scroll.Add(Note(desc));
                }
            }
        }

        // ------------------------------------------------------------------
        // Расширения
        // ------------------------------------------------------------------

        void BuildExtensions(List<Nsg_PackInfo> packs, bool assistant,
                             List<Nsg_ExtensionEntry> engines)
        {
            var scroll = new ScrollView();
            _body.Add(scroll);

            // --- языковые пакеты ---
            // Раздел появляется, только если пакеты действительно есть:
            // в сборке без папок Locale о языках сообщать нечего.
            if (packs.Count > 0)
            {
                scroll.Add(Section("set.ext.locales"));
                scroll.Add(Note(Nsg_L10n.T("set.ext.localesNote")));
            }

            for (int i = 0; i < packs.Count; i++)
            {
                var p = packs[i];
                var row = new VisualElement();
                row.style.flexDirection = Nsg_Rtl.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                var name = new Label(p.Name + "  (" + p.Code + ")  " + p.SizeText);
                name.style.flexGrow = 1;
                name.style.fontSize = 11;
                name.style.unityTextAlign = Nsg_Rtl.TextAlign;
                if (!p.Enabled) name.style.color = new Color(0.55f, 0.58f, 0.62f);
                row.Add(name);

                string folder = p.Folder;
                string code = p.Code;
                bool enabled = p.Enabled;
                var b = new Button(() =>
                {
                    if (enabled) Nsg_LocalePacks.Disable(folder, code);
                    else Nsg_LocalePacks.Enable(folder, code);
                    Rebuild();
                });
                b.text = Nsg_L10n.T(enabled ? "set.ext.disable" : "set.ext.enable");
                b.style.fontSize = 10;
                b.style.width = 90;
                row.Add(b);

                scroll.Add(row);
            }

            // --- кошка ---
            // Папки ProgramNeko нет — значит и раздела нет: снимать и
            // возвращать нечего.
            if (assistant)
            {
                scroll.Add(Section("set.ext.assistant"));

                var arow = new VisualElement();
                arow.style.flexDirection = Nsg_Rtl.Row;
                arow.style.alignItems = Align.Center;

                bool installed = Nsg_AssistantPack.Installed;
                bool disabled = Nsg_AssistantPack.Disabled;

                var alabel = new Label(Nsg_L10n.T(installed ? "set.ext.assistantOn" :
                                                  disabled ? "set.ext.assistantOff" : "set.ext.assistantAbsent"));
                alabel.style.flexGrow = 1;
                alabel.style.fontSize = 11;
                arow.Add(alabel);

                if (installed || disabled)
                {
                    var ab = new Button(() =>
                    {
                        if (installed) Nsg_AssistantPack.Disable();
                        else Nsg_AssistantPack.Enable();
                        Rebuild();
                    });
                    ab.text = Nsg_L10n.T(installed ? "set.ext.disable" : "set.ext.enable");
                    ab.style.fontSize = 10;
                    ab.style.width = 90;
                    arow.Add(ab);
                }

                scroll.Add(arow);
                scroll.Add(Note(Nsg_L10n.T("set.ext.assistantNote")));
            }

            // --- внешние движки ---
            // Каталога нет — нет и раздела: ставить нечего.
            if (engines.Count > 0)
            {
                scroll.Add(Section("set.ext.engines"));
                scroll.Add(Note(Nsg_L10n.T("set.ext.enginesNote")));
            }

            for (int i = 0; i < engines.Count; i++)
            {
                var e = engines[i];
                if (e == null || string.IsNullOrEmpty(e.id)) continue;

                var box = new VisualElement();
                box.style.marginBottom = 8;
                box.style.paddingLeft = 6;
                box.style.paddingRight = 6;
                box.style.paddingTop = 4;
                box.style.paddingBottom = 4;
                box.style.backgroundColor = new Color(0.18f, 0.19f, 0.22f);
                Nsg_Visual.Round(box, 4);

                var title = new Label(Nsg_L10n.T(e.nameKey) + "  " + (e.version ?? string.Empty));
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.fontSize = 11;
                box.Add(title);

                box.Add(Note(Nsg_L10n.T(e.descKey)));

                bool present = Nsg_ExtInstaller.IsInstalled(e.id);
                var state = new Label(Nsg_L10n.T(present ? "set.ext.installed" : "set.ext.notInstalled"));
                state.style.fontSize = 10;
                state.style.color = present
                    ? new Color(0.55f, 0.80f, 0.58f)
                    : new Color(0.62f, 0.65f, 0.70f);
                box.Add(state);

                var row = new VisualElement();
                row.style.flexDirection = Nsg_Rtl.Row;
                row.style.marginTop = 3;

                string id = e.id;
                bool isPresent = present;

                var act = new Button(() =>
                {
                    if (isPresent) Nsg_ExtInstaller.Remove(id);
                    else Nsg_ExtInstaller.Install(id);
                    Rebuild();
                });
                act.text = Nsg_L10n.T(isPresent ? "set.ext.uninstall" : "set.ext.install");
                act.style.fontSize = 10;
                act.style.width = 120;
                act.SetEnabled(!Nsg_ExtInstaller.IsBusy);
                row.Add(act);

                var info = new Button(() =>
                {
                    EditorUtility.RevealInFinder(
                        System.IO.Path.GetFullPath(Nsg_ExtensionCatalog.CatalogPath));
                });
                info.text = Nsg_L10n.T("set.ext.catalog");
                info.style.fontSize = 10;
                Nsg_Rtl.MarginStart(info.style, 4);
                row.Add(info);

                box.Add(row);

                if (!string.IsNullOrEmpty(Nsg_ExtInstaller.LastError))
                {
                    var err = Note(Nsg_ExtInstaller.LastError);
                    err.style.color = new Color(0.90f, 0.55f, 0.50f);
                    box.Add(err);
                }

                scroll.Add(box);
            }

            var refresh = new Button(() =>
            {
                Nsg_ExtensionCatalog.Reload();
                Nsg_CompletionRegistry.Rediscover();
                Rebuild();
            });
            refresh.text = Nsg_L10n.T("set.ext.refresh");
            refresh.style.marginTop = 4;
            refresh.style.width = 160;
            scroll.Add(refresh);
        }

        // ------------------------------------------------------------------
        // Продвинутое
        // ------------------------------------------------------------------

        void BuildAdvanced()
        {
            var scroll = new ScrollView();
            _body.Add(scroll);

            scroll.Add(Section("set.adv.gate"));

            var gate = new Label(Nsg_L10n.T(Nsg_Advanced.StatusKey));
            gate.style.fontSize = 11;
            gate.style.whiteSpace = WhiteSpace.Normal;
            gate.style.color = Nsg_Advanced.Enabled
                ? new Color(0.55f, 0.80f, 0.58f)
                : new Color(0.78f, 0.72f, 0.55f);
            scroll.Add(gate);

            if (!Nsg_Advanced.Enabled)
            {
                scroll.Add(Note(Nsg_L10n.T("adv.needAssistant")));

                var go = new Button(() => { _tab = Tab.Extensions; Rebuild(); });
                go.text = Nsg_L10n.T("set.tab.extensions");
                go.style.marginTop = 6;
                go.style.width = 220;
                scroll.Add(go);
            }

            scroll.Add(Section("set.adv.providers"));

            var providers = Nsg_CompletionRegistry.Providers;
            for (int i = 0; i < providers.Count; i++)
            {
                var p = providers[i];
                string line = Nsg_L10n.T(p.TitleKey) + "  [" + p.Id + "]  " +
                              Nsg_L10n.T("set.adv.priority") + " " + p.Priority + "  ·  " +
                              StateText(p.State);

                var l = new Label(line);
                l.style.fontSize = 10;
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.unityTextAlign = Nsg_Rtl.TextAlign;
                scroll.Add(l);

                if (!string.IsNullOrEmpty(p.Status)) scroll.Add(Note(p.Status));
            }

            if (providers.Count == 0)
            {
                scroll.Add(Note(Nsg_L10n.T("set.adv.noProviders")));
            }

            // Пробный запуск нужен, только если внешний движок действительно
            // установлен: иначе кнопке нечего проверять.
            if (Nsg_CompletionRegistry.HasExternal)
            {
                var test = new Button(RunSmokeTest);
                test.text = Nsg_L10n.T("set.adv.test");
                test.style.marginTop = 6;
                test.style.width = 220;
                scroll.Add(test);

                if (!string.IsNullOrEmpty(_testResult)) scroll.Add(Note(_testResult));
            }

            // Оптимизаторов может не быть вовсе — тогда и раздела нет.
            int optimizers = Nsg_PassRegistry.OfKind(NsgPassKind.Optimizer).Count;
            if (optimizers > 0)
            {
                scroll.Add(Section("set.adv.optimizers"));
                scroll.Add(Note(Nsg_L10n.T("set.adv.optimizerCount", optimizers)));

                var ids = Nsg_PassRegistry.Ids();
                for (int i = 0; i < ids.Count; i++) scroll.Add(Note("· " + ids[i]));
            }
        }

        static string StateText(Nsg_ProviderState state)
        {
            switch (state)
            {
                case Nsg_ProviderState.Ready: return Nsg_L10n.T("set.adv.state.ready");
                case Nsg_ProviderState.Warming: return Nsg_L10n.T("set.adv.state.warming");
                case Nsg_ProviderState.Failed: return Nsg_L10n.T("set.adv.state.failed");
            }
            return Nsg_L10n.T("set.adv.state.missing");
        }

        // ------------------------------------------------------------------
        // Пробный запуск
        // ------------------------------------------------------------------

        /// <summary>
        /// Гоняет внешние движки на выделенном скрипте. Позиция берётся
        /// грубо — сразу после последней открывающей скобки: цель пробы в том,
        /// чтобы проверить всю цепочку, а не получить полезную подсказку.
        /// </summary>
        void RunSmokeTest()
        {
            var ctx = BuildSmokeContext();
            if (ctx == null)
            {
                _testResult = Nsg_L10n.T("set.adv.testNoFile");
                Rebuild();
                return;
            }

            _testResult = Nsg_L10n.T("set.adv.testRunning");
            Rebuild();

            Nsg_CompletionRegistry.CompleteAsync(ctx, items =>
            {
                // Ответ приходит из чужого потока: в интерфейс можно только
                // через главный.
                EditorApplication.delayCall += () =>
                {
                    int count = items != null ? items.Count : 0;
                    _testResult = Nsg_L10n.T("set.adv.testDone", count) + Preview(items);
                    Rebuild();
                };
            });
        }

        static string Preview(List<Nsg_CompletionItem> items)
        {
            if (items == null || items.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder(": ");
            int shown = 0;
            for (int i = 0; i < items.Count && shown < 8; i++)
            {
                var it = items[i];
                if (it == null) continue;
                if (shown > 0) sb.Append(", ");
                sb.Append(it.Display);
                shown++;
            }
            return sb.ToString();
        }

        static Nsg_CompletionContext BuildSmokeContext()
        {
            var obj = Selection.activeObject;
            string path = obj != null ? AssetDatabase.GetAssetPath(obj) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs")) return null;

            string text;
            try { text = File.ReadAllText(path); }
            catch { return null; }

            int caret = text.LastIndexOf('{');
            caret = caret < 0 ? 0 : caret + 1;

            return new Nsg_CompletionContext
            {
                CsPath = path,
                SourceText = text,
                CaretOffset = caret,
                Limit = 12
            };
        }
    }
}
