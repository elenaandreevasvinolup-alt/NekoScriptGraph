using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Выбор блока с поиском. Открывается нажатием "+" в стопке операторов
    /// или на пустом слоте значения.
    /// </summary>
    public class Nsg_PickerWindow : EditorWindow
    {
        Nsg_BlockLibrary _lib;
        bool _expressionOnly;
        Action<string> _onPick;

        TextField _search;
        ScrollView _list;

        public static void Show(Vector2 screenPos, Nsg_BlockLibrary lib, bool expressionOnly, Action<string> onPick)
        {
            var w = CreateInstance<Nsg_PickerWindow>();
            w._lib = lib;
            w._expressionOnly = expressionOnly;
            w._onPick = onPick;
            w.titleContent = new GUIContent(Nsg_L10n.T(expressionOnly ? "pick.expression" : "pick.statement"));

            float x = Mathf.Clamp(screenPos.x, 40f, Mathf.Max(60f, Screen.currentResolution.width - 360f));
            float y = Mathf.Clamp(screenPos.y, 40f, Mathf.Max(60f, Screen.currentResolution.height - 460f));
            w.position = new Rect(x, y, 340, 440);
            w.ShowUtility();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;
            root.style.paddingTop = 4;

            _search = new TextField();
            _search.style.marginBottom = 4;
            _search.RegisterValueChangedCallback(evt =>
            {
                RebuildList();
            });
            root.Add(_search);

            _list = new ScrollView();
            _list.style.flexGrow = 1;
            root.Add(_list);

            RebuildList();

            EditorApplication.delayCall += () =>
            {
                if (_search != null) _search.Focus();
            };
        }

        void OnLostFocus()
        {
            // Выбор, который остаётся открытым после клика в стороне, раздражает.
            if (this != null) Close();
        }

        void RebuildList()
        {
            if (_list == null) return;
            _list.Clear();

            if (_lib == null) return;

            string q = (_search != null ? _search.value : null) ?? string.Empty;
            q = q.Trim().ToLowerInvariant();

            var categories = _lib.Categories();
            int shown = 0;

            for (int c = 0; c < categories.Count; c++)
            {
                string cat = categories[c];
                var blocks = _lib.InCategory(cat);

                VisualElement group = null;
                for (int i = 0; i < blocks.Count; i++)
                {
                    var b = blocks[i];
                    if (b.IsExpression != _expressionOnly) continue;

                    if (q.Length > 0)
                    {
                        string hay = (b.id + " " + b.label + " " + b.labelEn + " " + b.labelRu +
                                      " " + b.Label() + " " + b.CategoryLabel()).ToLowerInvariant();
                        if (hay.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                    }

                    if (group == null)
                    {
                        group = new VisualElement();
                        var head = new Label(cat.StartsWith("cat.") ? Nsg_L10n.T(cat) : cat);
                        head.style.unityFontStyleAndWeight = FontStyle.Bold;
                        head.style.marginTop = 6;
                        head.style.marginBottom = 2;
                        head.style.color = new Color(0.75f, 0.78f, 0.85f);
                        _list.Add(head);

                        Nsg_Rtl.MarginStart(group.style, 4);
                        _list.Add(group);
                    }

                    var def = b;
                    var btn = new Button(() => Pick(def.id));
                    btn.text = def.Label();
                    btn.tooltip = def.id + "\n" + def.manual;
                    btn.style.unityTextAlign = Nsg_Rtl.TextAlign;
                    btn.style.marginBottom = 1;
                    btn.style.backgroundColor = def.BlockColor();
                    btn.style.color = Nsg_Palette.TextOn(def.BlockColor());
                    group.Add(btn);
                    shown++;
                }
            }

            if (shown == 0)
            {
                var none = new Label(Nsg_L10n.T("pick.noMatch"));
                none.style.marginTop = 8;
                none.style.color = new Color(0.6f, 0.6f, 0.6f);
                _list.Add(none);
            }
        }

        void Pick(string id)
        {
            var cb = _onPick;
            _onPick = null;
            Close();
            if (cb != null) cb(id);
        }
    }
}
