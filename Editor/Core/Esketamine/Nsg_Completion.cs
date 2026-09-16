using System;
using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Что именно нужно подсказать.
    ///
    /// Контекст намеренно нейтрален: ни Roslyn, ни UnityEditor. Внешний движок
    /// получает его и возвращает те же элементы, что и встроенный, поэтому
    /// интерфейс не знает, кто ответил.
    /// </summary>
    public class Nsg_CompletionContext
    {
        public string CsPath;
        public string MethodHeader;

        /// <summary>Граф метода, в котором стоит курсор. Может быть null.</summary>
        public NsgMethodGraph Graph;

        /// <summary>Узел, в чей слот пишут. Может быть null.</summary>
        public string NodeId;
        public int SlotIndex;

        /// <summary>Набранный префикс, например «transform.Tra». Может быть пустым.</summary>
        public string Prefix;

        /// <summary>Исходный текст файла. Нужен внешним движкам; может быть null.</summary>
        public string SourceText;

        /// <summary>Позиция курсора в SourceText. -1 — неизвестна.</summary>
        public int CaretOffset = -1;

        public NsgFileModel Model;
        public Nsg_BlockLibrary Library;

        /// <summary>Курсор в позиции оператора, а не значения.</summary>
        public bool StatementPosition;

        /// <summary>Сколько вариантов вернуть. Больше — дороже.</summary>
        public int Limit = 24;
    }

    /// <summary>
    /// Один вариант подсказки. Два вида: блок (вставляется в граф) и символ
    /// (вставляется текстом в слот). Один тип на оба вида — иначе интерфейсу
    /// пришлось бы знать, какой движок ответил.
    /// </summary>
    public class Nsg_CompletionItem
    {
        /// <summary>«block» — блок графа, «symbol» — имя для текстового слота.</summary>
        public string Kind = "block";

        /// <summary>Что показать в списке.</summary>
        public string Display;

        /// <summary>Что вставить: идентификатор блока или текст.</summary>
        public string Insert;

        /// <summary>Уверенность. Влияет на порядок и на слияние дублей.</summary>
        public float Score;

        /// <summary>Заполнено только для Kind == «block».</summary>
        public NsgBlockDef Def;

        /// <summary>Подсказка справа: «local», «member», «type», «keyword»…</summary>
        public string Detail;
    }

    /// <summary>Состояние движка. Нужно интерфейсу, чтобы не врать.</summary>
    public enum Nsg_ProviderState
    {
        /// <summary>Не найден вовсе — норма для встроенного набора.</summary>
        Missing,

        /// <summary>Найден, но ещё не готов: прогрев или тяжёлая инициализация.</summary>
        Warming,

        /// <summary>Готов отвечать.</summary>
        Ready,

        /// <summary>Найден, но не работает: нет библиотек, не тот API, упал.</summary>
        Failed
    }

    /// <summary>
    /// Источник подсказок.
    ///
    /// Встроенный набор данных регистрируется всегда и отвечает мгновенно.
    /// Внешние движки (например, Roslyn) приходят отдельным пакетом и
    /// находятся рефлексией: ядро не знает их типов, поэтому удаление пакета
    /// ничего не ломает.
    ///
    /// Ответ асинхронный: внешний движок не имеет права морозить интерфейс.
    /// Вызывающий сначала показывает встроенные варианты, а внешние
    /// ДОБАВЛЯЕТ, когда они придут.
    /// </summary>
    public interface INsg_CompletionProvider
    {
        /// <summary>Стабильный идентификатор: по нему ветвятся инструменты.</summary>
        string Id { get; }

        /// <summary>Ключ локализации названия для настроек.</summary>
        string TitleKey { get; }

        /// <summary>Чем больше, тем позже спрашивают. Встроенный набор — 0.</summary>
        int Priority { get; }

        Nsg_ProviderState State { get; }

        /// <summary>Готовая причина состояния для интерфейса. null — объяснять нечего.</summary>
        string Status { get; }

        bool CanServe(Nsg_CompletionContext context);

        /// <summary>
        /// Ответ обязан прийти через callback. Поток не гарантирован, поэтому
        /// вызывающий сам решает, как вернуться на главный.
        /// </summary>
        void Complete(Nsg_CompletionContext context, Action<List<Nsg_CompletionItem>> done);
    }

    /// <summary>
    /// Встроенный набор подсказок: библиотека блоков активного языка.
    ///
    /// Ничего не стоит, ничего не знает о языках и работает даже в batchmode.
    /// Это нижний этаж: если внешний движок не установлен, ответственный за
    /// подсказки всё равно есть.
    /// </summary>
    public class Nsg_DataCompletionProvider : INsg_CompletionProvider
    {
        public const string ProviderId = "completion.data";

        public string Id
        {
            get { return ProviderId; }
        }

        public string TitleKey
        {
            get { return "ext.completion.data"; }
        }

        public int Priority
        {
            get { return 0; }
        }

        public Nsg_ProviderState State
        {
            get { return Nsg_Advanced.Enabled ? Nsg_ProviderState.Ready : Nsg_ProviderState.Missing; }
        }

        public string Status
        {
            get { return Nsg_Advanced.Enabled ? null : Nsg_L10n.T("adv.needAssistant"); }
        }

        public bool CanServe(Nsg_CompletionContext context)
        {
            return Nsg_Advanced.Enabled && context != null && context.Library != null;
        }

        public void Complete(Nsg_CompletionContext context, Action<List<Nsg_CompletionItem>> done)
        {
            var result = new List<Nsg_CompletionItem>();

            if (CanServe(context))
            {
                var blocks = Nsg_Autocomplete.Suggest(context.Graph, context.Library,
                                                      context.StatementPosition, context.Limit);

                for (int i = 0; i < blocks.Count; i++)
                {
                    var s = blocks[i];
                    if (s == null || s.Def == null) continue;

                    result.Add(new Nsg_CompletionItem
                    {
                        Kind = "block",
                        Display = s.Def.Label(),
                        Insert = s.Def.id,
                        Score = s.Score,
                        Def = s.Def,
                        Detail = s.Def.CategoryLabel()
                    });
                }
            }

            if (done != null) done(result);
        }
    }

    /// <summary>
    /// Реестр источников подсказок.
    ///
    /// Устроен так же, как реестр кошки и реестр языков: встроенное
    /// регистрируется кодом, внешнее находится рефлексией. Три состояния
    /// обязаны быть безопасными — движка нет, движок битый, движок упал в
    /// ответе: во всех трёх случаях подсказки продолжают работать.
    /// </summary>
    public static class Nsg_CompletionRegistry
    {
        static readonly List<INsg_CompletionProvider> Registered = new List<INsg_CompletionProvider>();
        static bool _scanned;

        /// <summary>Все источники: встроенный первым, внешние по приоритету.</summary>
        public static List<INsg_CompletionProvider> Providers
        {
            get { Scan(); return Registered; }
        }

        /// <summary>Есть ли хотя бы один внешний движок.</summary>
        public static bool HasExternal
        {
            get
            {
                Scan();
                for (int i = 0; i < Registered.Count; i++)
                {
                    if (Registered[i].Id != Nsg_DataCompletionProvider.ProviderId) return true;
                }
                return false;
            }
        }

        /// <summary>Сбрасывает кэш: после установки или удаления пакета.</summary>
        public static void Rediscover()
        {
            _scanned = false;
            Registered.Clear();
        }

        /// <summary>
        /// Мгновенный ответ: только встроенные источники. Вызывается на каждое
        /// нажатие, поэтому не имеет права ничего ждать.
        /// </summary>
        public static List<Nsg_CompletionItem> CompleteNow(Nsg_CompletionContext context)
        {
            var merged = new List<Nsg_CompletionItem>();
            if (!Nsg_Advanced.Enabled || context == null) return merged;

            Scan();
            for (int i = 0; i < Registered.Count; i++)
            {
                var p = Registered[i];
                if (p.Priority != 0) continue;   // внешние спрашивают отдельно
                Merge(merged, Run(p, context), context.Limit);
            }
            return merged;
        }

        /// <summary>
        /// Догоняющий ответ: внешние движки. callback вызывается один раз;
        /// пустой список означает «ничего нового».
        /// </summary>
        public static void CompleteAsync(Nsg_CompletionContext context,
                                         Action<List<Nsg_CompletionItem>> done)
        {
            if (!Nsg_Advanced.Enabled || context == null)
            {
                if (done != null) done(new List<Nsg_CompletionItem>());
                return;
            }

            Scan();

            var external = new List<INsg_CompletionProvider>();
            for (int i = 0; i < Registered.Count; i++)
            {
                var p = Registered[i];
                if (p.Priority == 0) continue;
                if (p.State != Nsg_ProviderState.Ready || !p.CanServe(context)) continue;
                external.Add(p);
            }

            if (external.Count == 0)
            {
                if (done != null) done(new List<Nsg_CompletionItem>());
                return;
            }

            var merged = new List<Nsg_CompletionItem>();
            int remaining = external.Count;
            var gate = new object();

            for (int i = 0; i < external.Count; i++)
            {
                var p = external[i];
                RunAsync(p, context, list =>
                {
                    lock (gate)
                    {
                        Merge(merged, list, context.Limit);
                        remaining--;
                        if (remaining > 0) return;

                        if (done != null) done(merged);
                    }
                });
            }
        }

        static List<Nsg_CompletionItem> Run(INsg_CompletionProvider p, Nsg_CompletionContext ctx)
        {
            var result = new List<Nsg_CompletionItem>();
            if (p == null || p.State != Nsg_ProviderState.Ready || !p.CanServe(ctx)) return result;

            try
            {
                p.Complete(ctx, list => { if (list != null) result = list; });
            }
            catch (Exception e)
            {
                // Упавший движок не должен уносить подсказки с собой.
                UnityEngine.Debug.LogWarning("[NekoScriptGraph] completion provider '" +
                                             p.Id + "' failed: " + e.Message);
            }
            return result;
        }

        static void RunAsync(INsg_CompletionProvider p, Nsg_CompletionContext ctx,
                             Action<List<Nsg_CompletionItem>> done)
        {
            try
            {
                p.Complete(ctx, list =>
                {
                    if (done == null) return;
                    done(list ?? new List<Nsg_CompletionItem>());
                });
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[NekoScriptGraph] completion provider '" +
                                             p.Id + "' failed: " + e.Message);
                if (done != null) done(new List<Nsg_CompletionItem>());
            }
        }

        /// <summary>
        /// Слияние без дублей: один блок или один символ — одна строка.
        /// При совпадении выживает больший балл, потому что балл и есть
        /// уверенность.
        /// </summary>
        public static void Merge(List<Nsg_CompletionItem> into, List<Nsg_CompletionItem> from, int limit)
        {
            if (into == null || from == null) return;

            for (int i = 0; i < from.Count; i++)
            {
                var item = from[i];
                if (item == null) continue;

                string key = KeyOf(item);
                if (string.IsNullOrEmpty(key)) continue;

                bool found = false;
                for (int k = 0; k < into.Count; k++)
                {
                    if (into[k] != null && KeyOf(into[k]) == key)
                    {
                        if (item.Score > into[k].Score) into[k] = item;
                        found = true;
                        break;
                    }
                }
                if (!found) into.Add(item);
            }

            if (limit > 0 && into.Count > limit)
            {
                into.Sort((a, b) => b.Score.CompareTo(a.Score));
                into.RemoveRange(limit, into.Count - limit);
            }
        }

        static string KeyOf(Nsg_CompletionItem item)
        {
            if (item == null) return null;
            string kind = string.IsNullOrEmpty(item.Kind) ? "block" : item.Kind;

            if (kind == "block")
            {
                return item.Def != null ? "block:" + item.Def.id : null;
            }
            return kind + ":" + (item.Insert ?? string.Empty);
        }

        // ------------------------------------------------------------------

        static void Scan()
        {
            if (_scanned) return;
            _scanned = true;

            Registered.Add(new Nsg_DataCompletionProvider());

            var own = typeof(Nsg_CompletionRegistry).Assembly;
            var iface = typeof(INsg_CompletionProvider);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int a = 0; a < assemblies.Length; a++)
            {
                var asm = assemblies[a];
                if (asm == own) continue;   // встроенный уже добавлен

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    // Библиотек движка может не быть — часть типов не загрузится.
                    // Берём те, что загрузились, вместо падения.
                    types = e.Types;
                }
                catch
                {
                    continue;
                }
                if (types == null) continue;

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!iface.IsAssignableFrom(t)) continue;

                    try
                    {
                        var inst = Activator.CreateInstance(t) as INsg_CompletionProvider;
                        if (inst != null && !string.IsNullOrEmpty(inst.Id)) Registered.Add(inst);
                    }
                    catch (Exception e)
                    {
                        // Движок установлен, но не собрался: это состояние
                        // «Failed», а не повод уронить редактор.
                        UnityEngine.Debug.LogWarning(
                            "[NekoScriptGraph] completion provider '" + t.FullName +
                            "' could not be created: " + e.Message);
                    }
                }
            }

            Registered.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }
}
