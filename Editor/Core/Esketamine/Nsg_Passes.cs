using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Вид прохода. Диагностика только читает модель и сообщает о находках,
    /// оптимизатор имеет право её менять.
    /// </summary>
    public enum NsgPassKind
    {
        Diagnostic,
        Optimizer
    }

    /// <summary>
    /// Всё, что нужно проходу. Проход не должен сам читать диск: модель уже
    /// загружена и передана сюда, поэтому проходы детерминированы и тестируемы
    /// вне редактора.
    /// </summary>
    public class Nsg_PassContext
    {
        public NsgFileModel Model;
        public Nsg_BlockLibrary Library;
        public string CsPath;

        /// <summary>Если задано — проход ограничен одним методом.</summary>
        public NsgStructNode Method;
        public NsgMethodGraph Graph;

        public List<NsgStructNode> Methods()
        {
            if (Method != null) return new List<NsgStructNode> { Method };
            return Model != null ? Model.AllMethods() : new List<NsgStructNode>();
        }
    }

    /// <summary>
    /// Один проход по модели блоков.
    ///
    /// Это и есть точка расширения: оптимизация (свёртка констант, слияние
    /// сообщений, чистка мёртвого кода) и всё, что появится вместе с блоками
    /// низкого уровня, регистрируется как <see cref="NsgPassKind.Optimizer"/>.
    /// Движку не нужно ничего менять — достаточно вызвать
    /// <see cref="Nsg_PassRegistry.RunAll"/>.
    /// </summary>
    public interface INsg_Pass
    {
        /// <summary>Стабильный идентификатор. Не переименовывать: по нему ветвятся инструменты.</summary>
        string Id { get; }

        NsgPassKind Kind { get; }

        /// <summary>Ключ локализации для названия в интерфейсе.</summary>
        string TitleKey { get; }

        /// <summary>
        /// Диагностика: только добавляет сообщения.
        /// Оптимизатор: меняет модель и добавляет сообщения о том, что сделал.
        /// Возвращает true, если проход что-то нашёл или изменил.
        /// </summary>
        bool Run(Nsg_PassContext context, NsgDiagnostics diagnostics);
    }

    /// <summary>
    /// Реестр проходов. Заполняется статически; порядок регистрации задаёт
    /// порядок выполнения.
    /// </summary>
    public static class Nsg_PassRegistry
    {
        static readonly List<INsg_Pass> Registered = new List<INsg_Pass>();

        public static void Register(INsg_Pass pass)
        {
            if (pass == null || string.IsNullOrEmpty(pass.Id)) return;
            for (int i = 0; i < Registered.Count; i++)
            {
                if (Registered[i].Id == pass.Id) return; // без дублей
            }
            Registered.Add(pass);
        }

        public static void Unregister(string id)
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                if (Registered[i].Id == id)
                {
                    Registered.RemoveAt(i);
                    return;
                }
            }
        }

        public static int Count
        {
            get { return Registered.Count; }
        }

        public static List<INsg_Pass> OfKind(NsgPassKind kind)
        {
            var list = new List<INsg_Pass>();
            for (int i = 0; i < Registered.Count; i++)
            {
                if (Registered[i].Kind == kind) list.Add(Registered[i]);
            }
            return list;
        }

        public static bool HasOptimizers
        {
            get { return OfKind(NsgPassKind.Optimizer).Count > 0; }
        }

        /// <summary>Прогоняет все проходы заданного вида. Возвращает число сработавших.</summary>
        public static int RunAll(Nsg_PassContext context, NsgPassKind kind, NsgDiagnostics diagnostics)
        {
            if (context == null) return 0;

            // Оптимизатор меняет код пользователя. Такое разрешено только
            // когда установлена кошка: правку обязан кто-то объяснить.
            // Диагностика — только чтение, поэтому работает всегда.
            if (kind == NsgPassKind.Optimizer && !Nsg_Advanced.Enabled) return 0;

            int fired = 0;
            var list = OfKind(kind);
            for (int i = 0; i < list.Count; i++)
            {
                int before = diagnostics != null ? diagnostics.Count : 0;
                bool changed = false;

                try
                {
                    changed = list[i].Run(context, diagnostics);
                }
                catch (System.Exception e)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.Error(NsgCodes.Internal,
                            list[i].Id + ": " + e.Message);
                    }
                }

                if (changed) fired++;
                else if (diagnostics != null && diagnostics.Count == before) { /* ничего не найдено */ }
            }
            return fired;
        }

        /// <summary>Список идентификаторов зарегистрированных проходов (для диагностики).</summary>
        public static List<string> Ids()
        {
            var list = new List<string>();
            for (int i = 0; i < Registered.Count; i++) list.Add(Registered[i].Id);
            return list;
        }
    }
}
