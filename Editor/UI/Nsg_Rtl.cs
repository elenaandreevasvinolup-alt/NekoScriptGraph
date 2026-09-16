using UnityEngine;
using UnityEngine.UIElements;

namespace NekoScriptGraph
{
    /// <summary>
    /// Зеркалирование интерфейса для языков, которые пишутся справа налево
    /// (арабский, иврит).
    ///
    /// Намеренно НЕ используется <c>style.direction = Direction.Rtl</c>:
    /// поддержка direction в UI Toolkit неполная и меняется от версии к
    /// версии, а требование сформулировано геометрически — палитра блоков
    /// слева, блоки растут влево. Поэтому логические «начало» и «конец»
    /// расставлены явно, и результат предсказуем на любой версии Unity.
    ///
    /// Что остаётся английским и не зеркалится: пункты меню Unity
    /// (NekoScriptGraph/...) — см. Nsg_MenuRuntime.
    /// </summary>
    public static class Nsg_Rtl
    {
        public static bool On
        {
            get { return Nsg_L10n.IsRtl; }
        }

        // ------------------------------------------------------------------
        // Ось строки
        // ------------------------------------------------------------------

        /// <summary>Направление строки: Row в LTR, RowReverse в RTL.</summary>
        public static FlexDirection Row
        {
            get { return On ? FlexDirection.RowReverse : FlexDirection.Row; }
        }

        /// <summary>
        /// Начало поперечной оси. Для колонки это левый край в LTR и правый
        /// в RTL — именно так блоки начинают расти слева.
        /// </summary>
        public static Align AlignStart
        {
            get { return On ? Align.FlexEnd : Align.FlexStart; }
        }

        /// <summary>Выравнивание текста по началу строки.</summary>
        public static TextAnchor TextAlign
        {
            get { return On ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft; }
        }

        // ------------------------------------------------------------------
        // Отступы
        // ------------------------------------------------------------------

        public static void PaddingStart(IStyle s, float v)
        {
            if (On) s.paddingRight = v; else s.paddingLeft = v;
        }

        public static void PaddingEnd(IStyle s, float v)
        {
            if (On) s.paddingLeft = v; else s.paddingRight = v;
        }

        public static void MarginStart(IStyle s, float v)
        {
            if (On) s.marginRight = v; else s.marginLeft = v;
        }

        public static void MarginEnd(IStyle s, float v)
        {
            if (On) s.marginLeft = v; else s.marginRight = v;
        }

        // ------------------------------------------------------------------
        // Линии
        // ------------------------------------------------------------------

        /// <summary>
        /// Толстая линия по началу строки. Ею рисуется C-образный отступ
        /// тела блока управления: слева в LTR, справа в RTL.
        /// </summary>
        public static void BorderStart(IStyle s, float width, Color color)
        {
            if (On)
            {
                s.borderRightWidth = width;
                s.borderRightColor = color;
            }
            else
            {
                s.borderLeftWidth = width;
                s.borderLeftColor = color;
            }
        }

        /// <summary>
        /// Тонкая линия по концу строки: разделитель между группами ленты.
        /// В LTR это правая граница, в RTL — левая.
        /// </summary>
        public static void BorderEnd(IStyle s, float width, Color color)
        {
            if (On)
            {
                s.borderLeftWidth = width;
                s.borderLeftColor = color;
            }
            else
            {
                s.borderRightWidth = width;
                s.borderRightColor = color;
            }
        }

        /// <summary>
        /// Индекс фиксированной панели в TwoPaneSplitView. Панель, которая
        /// должна сохранять ширину, стоит второй в LTR и первой в RTL, потому
        /// что порядок детей в RTL обратный.
        /// </summary>
        public static int SplitFixedIndex(bool fixedIsSecondInLtr)
        {
            if (!fixedIsSecondInLtr) return On ? 1 : 0;
            return On ? 0 : 1;
        }
    }
}
