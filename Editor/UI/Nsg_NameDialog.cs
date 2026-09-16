using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Маленькое модальное окно для ввода имени. EditorUtility.DisplayDialog
    /// текстового ввода не умеет, поэтому нужен свой.
    /// </summary>
    public class Nsg_NameDialog : EditorWindow
    {
        string _value = string.Empty;
        Action<string> _onOk;
        TextField _field;

        public static void Show(string title, string initial, Action<string> onOk)
        {
            var w = CreateInstance<Nsg_NameDialog>();
            w._value = initial ?? string.Empty;
            w._onOk = onOk;
            w.titleContent = new GUIContent(title);

            var mouse = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            w.position = new Rect(mouse.x, mouse.y, 340f, 96f);
            w.ShowUtility();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;

            _field = new TextField();
            _field.value = _value;
            _field.RegisterValueChangedCallback(evt => _value = evt.newValue);
            _field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) { Accept(); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); }
            });
            root.Add(_field);

            var row = new VisualElement();
            row.style.flexDirection = Nsg_Rtl.Row;
            row.style.marginTop = 6;

            var ok = new Button(Accept);
            ok.text = Nsg_L10n.T("confirm.ok");
            ok.style.flexGrow = 1;
            row.Add(ok);

            var cancel = new Button(Close);
            cancel.text = Nsg_L10n.T("confirm.cancel");
            cancel.style.flexGrow = 1;
            row.Add(cancel);

            root.Add(row);

            EditorApplication.delayCall += () =>
            {
                if (_field != null)
                {
                    _field.Focus();
                    _field.SelectAll();
                }
            };
        }

        void Accept()
        {
            var cb = _onOk;
            _onOk = null;
            string value = _value;
            Close();
            if (cb != null) cb(value);
        }
    }
}
