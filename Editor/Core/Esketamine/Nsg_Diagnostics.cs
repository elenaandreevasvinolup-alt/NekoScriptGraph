using System.Collections.Generic;

namespace NekoScriptGraph
{
    public enum NsgSeverity
    {
        Info,
        Warning,
        Error
    }

    public class NsgDiagnostic
    {
        public string Code;
        public NsgSeverity Severity;
        public string Message;
        public int Line;
        public int Col;

        public NsgDiagnostic(string code, NsgSeverity severity, string message, int line, int col)
        {
            Code = code;
            Severity = severity;
            Message = message;
            Line = line;
            Col = col;
        }

        public override string ToString()
        {
            return "[" + Code + "] " + Severity + " (" + Line + "," + Col + "): " + Message;
        }
    }

    /// <summary>
    /// Коды диагностики. Это часть контракта плагина: агент или внешний
    /// инструмент может ветвиться по ним, поэтому никогда не перенумеровывай
    /// уже существующие коды.
    /// </summary>
    public static class NsgCodes
    {
        public const string UnknownBlock = "NSG0001"; // блока с таким id нет в библиотеке
        public const string OutOfSubset = "NSG0002";  // синтаксис вне поддерживаемого подмножества
        public const string Ambiguous = "NSG0003";    // один блок, несколько исходных форм
        public const string Unresolved = "NSG0004";   // символ не удалось разрешить
        public const string Unconvertible = "NSG0005";// метод или файл целиком не конвертируется
        public const string MetadataLoss = "NSG0006"; // конвертация потеряла бы поля или атрибуты
        public const string HashMismatch = "NSG0007"; // .cs изменился под управляемым .nsg.json
        public const string Internal = "NSG0009";     // ошибка в самом плагине
    }

    public class NsgDiagnostics
    {
        public readonly List<NsgDiagnostic> Items = new List<NsgDiagnostic>();

        public int Count
        {
            get { return Items.Count; }
        }

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Severity == NsgSeverity.Error) return true;
                }
                return false;
            }
        }

        public void Add(NsgDiagnostic d)
        {
            if (d != null) Items.Add(d);
        }

        /// <summary>
        /// Есть ли уже ровно такая запись.
        ///
        /// Нужна там, где диагностика выдаётся из ОТРИСОВКИ: перерисовка
        /// повторяется десятки раз, а список живёт до следующего импорта —
        /// без этой проверки одна неизвестная деталь плодила неограниченный
        /// хвост одинаковых ошибок.
        /// </summary>
        public bool Has(string code, string message)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                var d = Items[i];
                if (d != null && d.Code == code && d.Message == message) return true;
            }
            return false;
        }

        public void Error(string code, string message, int line = 0, int col = 0)
        {
            Items.Add(new NsgDiagnostic(code, NsgSeverity.Error, message, line, col));
        }

        public void Warn(string code, string message, int line = 0, int col = 0)
        {
            Items.Add(new NsgDiagnostic(code, NsgSeverity.Warning, message, line, col));
        }

        public void Info(string code, string message, int line = 0, int col = 0)
        {
            Items.Add(new NsgDiagnostic(code, NsgSeverity.Info, message, line, col));
        }

        /// <summary>
        /// Откатывает диагностику, выданную пробным разбором
        /// (возврат назад при определении локального объявления).
        /// </summary>
        public void Truncate(int count)
        {
            if (count < 0) count = 0;
            while (Items.Count > count) Items.RemoveAt(Items.Count - 1);
        }

        public void Clear()
        {
            Items.Clear();
        }

        public string Summary()
        {
            int e = 0, w = 0, i = 0;
            for (int k = 0; k < Items.Count; k++)
            {
                if (Items[k].Severity == NsgSeverity.Error) e++;
                else if (Items[k].Severity == NsgSeverity.Warning) w++;
                else i++;
            }
            return Nsg_L10n.T("diag.summary", e, w, i);
        }
    }
}
