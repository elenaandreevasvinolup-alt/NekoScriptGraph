using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Отдельное окно проблем. Главный редактор отправляет диагностику сюда
    /// после каждой операции, чтобы ошибки никогда не прятались за строкой
    /// состояния.
    /// </summary>
    public class Nsg_ErrorWindow : EditorWindow
    {
        static Nsg_ErrorWindow _instance;

        // Последняя диагностика, даже если окна ещё нет. Нужна, чтобы открытие
        // окна вручную показывало актуальное, а не пустой список.
        static NsgDiagnostics _lastDiag;
        static string _lastPath;

        NsgDiagnostics _diag;
        string _csPath;
        bool _filterErrors;
        bool _filterWarnings;

        ScrollView _list;
        Label _summary;

        // ---- кошка (необязательная часть окна) ----
        Nsg_NekoView _neko;

        /// <summary>Уже существующее окно или null. НЕ создаёт: иначе проверка
        /// «есть ли окно» сама бы его и открывала.</summary>
        static Nsg_ErrorWindow Instance()
        {
            if (_instance != null) return _instance;

            var all = Resources.FindObjectsOfTypeAll<Nsg_ErrorWindow>();
            if (all != null && all.Length > 0) _instance = all[0];
            return _instance;
        }

        /// <summary>Создаёт окно, если его ещё нет. GetWindow показывает окно,
        /// поэтому вызывается только тогда, когда показать действительно надо.</summary>
        static Nsg_ErrorWindow Ensure()
        {
            var w = Instance();
            if (w != null) return w;

            w = GetWindow<Nsg_ErrorWindow>();
            w.titleContent = new GUIContent(Nsg_L10n.T("err.title"));
            w.minSize = new Vector2(420, 240);
            _instance = w;
            return w;
        }

        public static void Open()
        {
            var w = Ensure();
            w._diag = _lastDiag;
            w._csPath = _lastPath;
            w.Show();
            w.Rebuild();
        }

        /// <summary>
        /// Обновить диагностику. Окно НЕ всплывает само: раньше Push вызывал
        /// Open, а Push зовётся при каждом открытии файла — окно вылезало
        /// наверх и забирало фокус на любом действии. Всплывает только
        /// ошибка: ровно ради этого окно и существует.
        /// </summary>
        public static void Push(NsgDiagnostics diag, string csPath)
        {
            _lastDiag = diag;
            _lastPath = csPath;

            var w = Instance();
            if (w == null)
            {
                // Окна нет. Показывать нечего — не создаём его молча; ошибка
                // ниже создаст и покажет.
                if (diag == null || !diag.HasErrors) return;
                w = Ensure();
            }

            w._diag = diag;
            w._csPath = csPath;
            w.Rebuild();

            if (diag != null && diag.HasErrors)
            {
                w.Show();
                w.NekoSay("react.error", Nsg_NekoMood.Worried);
            }
            else if (diag != null && diag.Count == 0)
            {
                w.NekoSay("react.clean", Nsg_NekoMood.Happy);
            }
            else if (diag != null)
            {
                w.NekoSay("react.success", Nsg_NekoMood.Happy);
            }
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

            var onlyErr = new Toggle(Nsg_L10n.T("err.error"));
            onlyErr.value = _filterErrors;
            onlyErr.RegisterValueChangedCallback(evt => { _filterErrors = evt.newValue; Rebuild(); });
            bar.Add(onlyErr);

            var onlyWarn = new Toggle(Nsg_L10n.T("err.warning"));
            onlyWarn.value = _filterWarnings;
            onlyWarn.RegisterValueChangedCallback(evt => { _filterWarnings = evt.newValue; Rebuild(); });
            bar.Add(onlyWarn);

            var clear = new Button(() => { if (_diag != null) { _diag.Clear(); Rebuild(); } });
            clear.text = Nsg_L10n.T("err.clear");
            Nsg_Rtl.MarginStart(clear.style, 8);
            bar.Add(clear);

            root.Add(bar);

            _summary = new Label(string.Empty);
            _summary.style.marginBottom = 4;
            _summary.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_summary);

            _list = new ScrollView();
            _list.style.flexGrow = 1;
            root.Add(_list);

            // Панель кошки: одна и та же, что в слоте главного окна.
            // Без ProgramNeko элемент сам прячется и места не занимает.
            _neko = new Nsg_NekoView();
            if (_neko.Active)
            {
                _neko.style.marginTop = 5;
                // В этом окне список должен тянуться, поэтому высоту панели
                // фиксируем, а не отдаём под flexGrow.
                _neko.style.flexGrow = 0;
                _neko.style.height = 172;
                root.Add(_neko);
            }

            Rebuild();
            NekoGreet();
        }

        void NekoGreet()
        {
            if (_neko != null) _neko.Greet();
        }

        void NekoSay(string lineKey, Nsg_NekoMood mood)
        {
            if (_neko != null) _neko.Say(lineKey, mood);
        }

        void AskNeko(NsgDiagnostic d)
        {
            if (_neko != null) _neko.Ask(d, _csPath);
        }

        void Rebuild()
        {
            if (_list == null) return;
            _list.Clear();

            int errors = 0, warnings = 0, infos = 0;

            if (_diag != null)
            {
                for (int i = 0; i < _diag.Items.Count; i++)
                {
                    var d = _diag.Items[i];
                    if (d.Severity == NsgSeverity.Error) errors++;
                    else if (d.Severity == NsgSeverity.Warning) warnings++;
                    else infos++;

                    // Два переключателя — это НАБОР фильтров, а не два
                    // последовательных запрета. Раньше здесь стояли два
                    // независимых continue, то есть логическое И: с обоими
                    // включёнными галочками не проходило ни одной строки —
                    // ошибка не проходила по «!= Warning», предупреждение по
                    // «!= Error». Теперь включённые галочки объединяются по ИЛИ,
                    // а при выключенных показывается всё.
                    if (_filterErrors || _filterWarnings)
                    {
                        bool pass = (_filterErrors && d.Severity == NsgSeverity.Error)
                                 || (_filterWarnings && d.Severity == NsgSeverity.Warning);
                        if (!pass) continue;
                    }

                    _list.Add(Row(d));
                }
            }

            if (_summary != null)
            {
                _summary.text = Nsg_L10n.T("err.count", errors, warnings, infos);
            }

            if (_list.childCount == 0)
            {
                var none = new Label(Nsg_L10n.T("err.none"));
                none.style.color = new Color(0.6f, 0.63f, 0.68f);
                _list.Add(none);
            }
        }

        VisualElement Row(NsgDiagnostic d)
        {
            var row = new VisualElement();
            row.style.flexDirection = Nsg_Rtl.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            Nsg_Visual.Round(row, 3);

            Color c;
            string tag;
            switch (d.Severity)
            {
                case NsgSeverity.Error:
                    c = Nsg_Palette.Error;
                    tag = Nsg_L10n.T("err.error");
                    break;
                case NsgSeverity.Warning:
                    c = Nsg_Palette.Warning;
                    tag = Nsg_L10n.T("err.warning");
                    break;
                default:
                    c = Nsg_Palette.Info;
                    tag = Nsg_L10n.T("err.info");
                    break;
            }

            row.style.backgroundColor = new Color(c.r, c.g, c.b, 0.12f);

            var sev = new Label(tag);
            sev.style.color = c;
            sev.style.unityFontStyleAndWeight = FontStyle.Bold;
            sev.style.minWidth = 60;
            row.Add(sev);

            var code = new Label(d.Code);
            code.style.color = new Color(0.75f, 0.78f, 0.84f);
            code.style.minWidth = 74;
            row.Add(code);

            var msg = new Label(d.Message);
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.flexGrow = 1;
            msg.style.unityTextAlign = Nsg_Rtl.TextAlign;
            msg.style.color = new Color(0.9f, 0.91f, 0.94f);
            row.Add(msg);

            if (d.Line > 0 && !string.IsNullOrEmpty(_csPath))
            {
                var go = new Button(() => GoTo(d.Line));
                go.text = Nsg_L10n.T("err.goto");
                Nsg_Rtl.MarginStart(go.style, 6);
                row.Add(go);

                var loc = new Label("L" + d.Line);
                loc.style.color = new Color(0.6f, 0.63f, 0.68f);
                Nsg_Rtl.MarginStart(loc.style, 4);
                row.Add(loc);
            }

            // Кнопка объяснения появляется ТОЛЬКО когда установлена кошка и
            // в её языке есть зачин для этого кода. Без кошки окно прежнее.
            var neko = Nsg_NekoRegistry.Assistant;
            if (neko != null && neko.CanExplain(d.Code))
            {
                var ask = new Button(() => AskNeko(d));
                ask.text = Nsg_L10n.T("err.explain");
                Nsg_Rtl.MarginStart(ask.style, 6);
                row.Add(ask);
            }

            return row;
        }

        void GoTo(int line)
        {
            if (string.IsNullOrEmpty(_csPath)) return;
            var obj = AssetDatabase.LoadAssetAtPath<Object>(_csPath);
            if (obj != null) AssetDatabase.OpenAsset(obj, line);
        }
    }
}
