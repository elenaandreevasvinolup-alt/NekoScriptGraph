using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace NekoScriptGraph.Roslyn
{
    /// <summary>
    /// Движок подсказок на Roslyn.
    ///
    /// Лежит ВНЕ пакета плагина и ставится по желанию. Поэтому здесь нет ни
    /// одной ссылки на сборки Roslyn времени компиляции: всё общение идёт
    /// рефлексией. Так пакет собирается даже там, где Roslyn нет, и честно
    /// сообщает «не готов», вместо того чтобы не собраться вовсе.
    ///
    /// Roslyn тоже берётся СНАРУЖИ: из папки, которую положил пользователь,
    /// или из самого редактора Unity. В пакете нет ни её копии, ни её
    /// зависимостей — иначе «необязательный движок» тихо превратился бы в
    /// обязательный.
    /// </summary>
    public class Nsg_RoslynProvider : INsg_CompletionProvider
    {
        public const string ProviderId = "completion.roslyn";

        /// <summary>Куда пользователь может положить Roslyn, если в редакторе его нет.</summary>
        public const string DropFolder = "Assets/NSG.Roslyn";

        const string CoreDll = "Microsoft.CodeAnalysis.dll";
        const string CSharpDll = "Microsoft.CodeAnalysis.CSharp.dll";

        static bool _probed;

        // Полное имя обязательно: UnityEditor.Compilation.Assembly — другой
        // тип с тем же коротким именем.
        static System.Reflection.Assembly _core;
        static System.Reflection.Assembly _csharp;
        static string _failure;

        // Кэш ссылок: список метаданных проекта меняется только при
        // перекомпиляции, а собирать его каждый раз дорого.
        static string[] _referencePaths;

        // Кэш разбора: одна и та же версия файла не должна разбираться дважды.
        static readonly object CacheGate = new object();
        static string _cacheKey;
        static List<Nsg_CompletionItem> _cacheItems = new List<Nsg_CompletionItem>();

        // ------------------------------------------------------------------

        public string Id
        {
            get { return ProviderId; }
        }

        public string TitleKey
        {
            get { return "ext.roslyn.name"; }
        }

        /// <summary>Внешний движок спрашивают после встроенного.</summary>
        public int Priority
        {
            get { return 100; }
        }

        public Nsg_ProviderState State
        {
            get
            {
                if (!Nsg_Advanced.Enabled) return Nsg_ProviderState.Missing;

                Probe();
                if (_failure != null) return Nsg_ProviderState.Failed;
                if (_core == null || _csharp == null) return Nsg_ProviderState.Warming;
                return Nsg_ProviderState.Ready;
            }
        }

        public string Status
        {
            get
            {
                if (!Nsg_Advanced.Enabled) return Nsg_L10n.T("adv.needAssistant");

                Probe();
                return _failure;
            }
        }

        public bool CanServe(Nsg_CompletionContext context)
        {
            return State == Nsg_ProviderState.Ready
                && context != null
                && !string.IsNullOrEmpty(context.SourceText)
                && context.CaretOffset >= 0;
        }

        public void Complete(Nsg_CompletionContext context, Action<List<Nsg_CompletionItem>> done)
        {
            if (done == null) return;

            var empty = new List<Nsg_CompletionItem>();
            if (!CanServe(context))
            {
                done(empty);
                return;
            }

            // Ссылки собираются ЗДЕСЬ, на главном потоке: CompilationPipeline —
            // редакторский API, и из фонового потока он не работает.
            string[] references;
            try
            {
                references = ReferencePaths();
            }
            catch (Exception e)
            {
                _failure = e.Message;
                done(empty);
                return;
            }

            // А разбор идёт в фоне: собранный проект на большом решении
            // собирается заметно дольше кадра, и морозить интерфейс нельзя.
            Task.Run(() =>
            {
                List<Nsg_CompletionItem> items;
                try
                {
                    items = Compute(context, references);
                }
                catch (Exception e)
                {
                    _failure = e.Message;
                    items = new List<Nsg_CompletionItem>();
                }
                done(items);
            });
        }

        // ------------------------------------------------------------------
        // Разбор
        // ------------------------------------------------------------------

        static List<Nsg_CompletionItem> Compute(Nsg_CompletionContext ctx, string[] paths)
        {
            string key = ctx.CaretOffset + "|" + (ctx.Prefix ?? string.Empty) + "|" +
                         ctx.SourceText.Length + "|" + ctx.SourceText.GetHashCode();

            lock (CacheGate)
            {
                if (key == _cacheKey) return _cacheItems;
            }

            var result = new List<Nsg_CompletionItem>();

            Type tSyntaxTree = _core.GetType("Microsoft.CodeAnalysis.SyntaxTree", true);
            Type tCSharpSyntaxTree = _csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree", true);
            Type tCompilation = _csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation", true);
            Type tMetadataReference = _core.GetType("Microsoft.CodeAnalysis.MetadataReference", true);
            Type tModel = _core.GetType("Microsoft.CodeAnalysis.SemanticModel", true);
            Type tSymbol = _core.GetType("Microsoft.CodeAnalysis.ISymbol", true);
            Type tNsOrType = _core.GetType("Microsoft.CodeAnalysis.INamespaceOrTypeSymbol", true);

            // --- дерево ---
            var parse = tCSharpSyntaxTree.GetMethod("ParseText",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (parse == null) throw new MissingMethodException("CSharpSyntaxTree.ParseText");

            object tree = parse.Invoke(null, new object[] { ctx.SourceText });

            // --- ссылки ---
            var treeList = MakeList(tSyntaxTree);
            treeList.Add(tree);

            var refList = MakeList(tMetadataReference);
            var createRef = tMetadataReference.GetMethod("CreateFromFile",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);

            // paths собран на главном потоке вызывающим: CompilationPipeline
            // доступен только там.
            for (int i = 0; i < paths.Length; i++)
            {
                try { refList.Add(createRef.Invoke(null, new object[] { paths[i] })); }
                catch { }
            }

            // --- компиляция ---
            var create = FindCreate(tCompilation, tSyntaxTree, tMetadataReference);
            if (create == null) throw new MissingMethodException("CSharpCompilation.Create");

            var parameters = create.GetParameters();
            var args = new object[parameters.Length];
            args[0] = "NekoScriptGraph.Snippet";
            args[1] = treeList;
            args[2] = refList;
            for (int i = 3; i < args.Length; i++) args[i] = null;

            object compilation = create.Invoke(null, args);

            // --- модель ---
            var getModel = tCompilation.GetMethod("GetSemanticModel", new[] { tSyntaxTree });
            if (getModel == null) throw new MissingMethodException("GetSemanticModel");

            object model = getModel.Invoke(compilation, new object[] { tree });

            // --- поиск символов ---
            var lookup = tModel.GetMethod("LookupSymbols",
                new[] { typeof(int), tNsOrType, typeof(string), typeof(bool) });
            if (lookup == null) throw new MissingMethodException("SemanticModel.LookupSymbols");

            string prefix = string.IsNullOrEmpty(ctx.Prefix) ? null : ctx.Prefix;
            object symbols = lookup.Invoke(model, new object[] { ctx.CaretOffset, null, prefix, false });

            var nameProp = tSymbol.GetProperty("Name");
            var kindProp = tSymbol.GetProperty("Kind");

            int limit = ctx.Limit > 0 ? ctx.Limit : 24;
            var seen = new HashSet<string>();

            foreach (var sym in (System.Collections.IEnumerable)symbols)
            {
                if (sym == null) continue;

                string name;
                string kind;
                try
                {
                    name = (string)nameProp.GetValue(sym, null);
                    kind = kindProp.GetValue(sym, null).ToString();
                }
                catch
                {
                    continue;
                }

                // Имена, придуманные компилятором, человеку не нужны.
                if (string.IsNullOrEmpty(name) || name.StartsWith("<", StringComparison.Ordinal)) continue;
                if (!seen.Add(name)) continue;

                result.Add(new Nsg_CompletionItem
                {
                    Kind = "symbol",
                    Display = name,
                    Insert = name,
                    Score = ScoreOf(name, ctx.Prefix, kind),
                    Detail = kind
                });

                if (result.Count >= limit * 4) break;
            }

            result.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (result.Count > limit) result.RemoveRange(limit, result.Count - limit);

            lock (CacheGate)
            {
                _cacheKey = key;
                _cacheItems = result;
            }
            return result;
        }

        /// <summary>Точное совпадение регистра и короткие имена — выше.</summary>
        static float ScoreOf(string name, string prefix, string kind)
        {
            float score = 1f;

            if (!string.IsNullOrEmpty(prefix) &&
                name.StartsWith(prefix, StringComparison.Ordinal)) score += 2f;

            if (kind == "Local" || kind == "Parameter") score += 1.5f;
            else if (kind == "Field" || kind == "Property" || kind == "Method") score += 1f;
            else if (kind == "NamedType") score += 0.5f;

            score += Mathf.Max(0f, 0.5f - name.Length * 0.02f);
            return score;
        }

        static System.Collections.IList MakeList(Type element)
        {
            return (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(element));
        }

        /// <summary>
        /// Ищет перегрузку Create, принимающую дерево, ссылки и (необязательно)
        /// параметры. Ищем по имени и числу аргументов: точные типы зависят от
        /// версии Roslyn, а имя и арность стабильны.
        /// </summary>
        static MethodInfo FindCreate(Type compilation, Type tree, Type reference)
        {
            var methods = compilation.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "Create") continue;

                var ps = methods[i].GetParameters();
                if (ps.Length < 3 || ps.Length > 4) continue;
                if (ps[0].ParameterType != typeof(string)) continue;
                if (!ps[1].ParameterType.IsGenericType) continue;

                return methods[i];
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Ссылки проекта
        // ------------------------------------------------------------------

        static string[] ReferencePaths()
        {
            if (_referencePaths != null) return _referencePaths;

            var set = new HashSet<string>();
            var list = new List<string>();

            try
            {
                var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Add(list, set, assemblies[i].assemblyPath);

                    var refs = assemblies[i].compilationReferences;
                    if (refs == null) continue;

                    for (int k = 0; k < refs.Length; k++) Add(list, set, refs[k]);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NekoScriptGraph] Roslyn: ссылки проекта не собраны: " + e.Message);
            }

            _referencePaths = list.ToArray();
            return _referencePaths;
        }

        static void Add(List<string> list, HashSet<string> set, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (set.Add(path)) list.Add(path);
        }

        // ------------------------------------------------------------------
        // Поиск Roslyn
        // ------------------------------------------------------------------

        static void Probe()
        {
            if (_probed) return;
            _probed = true;

            try
            {
                string dir = FindRoslynDir();
                if (dir == null)
                {
                    _failure = Nsg_L10n.T("ext.roslyn.notFound", DropFolder);
                    return;
                }

                _core = System.Reflection.Assembly.LoadFrom(Path.Combine(dir, CoreDll));
                _csharp = System.Reflection.Assembly.LoadFrom(Path.Combine(dir, CSharpDll));
            }
            catch (Exception e)
            {
                _core = null;
                _csharp = null;
                _failure = e.Message;
            }
        }

        /// <summary>
        /// Ищет папку с Roslyn: сначала там, куда положил пользователь, потом
        /// внутри самого редактора Unity.
        /// </summary>
        static string FindRoslynDir()
        {
            var candidates = new List<string>();

            // 1. Папка пользователя: лежит ВНЕ пакета плагина.
            try
            {
                if (Directory.Exists(DropFolder))
                {
                    candidates.Add(DropFolder);
                    var subs = Directory.GetDirectories(DropFolder);
                    for (int i = 0; i < subs.Length; i++) candidates.Add(subs[i]);
                }
            }
            catch { }

            // 2. Сам редактор Unity: Roslyn входит в его состав.
            try
            {
                string contents = EditorApplication.applicationContentsPath;
                candidates.Add(Path.Combine(contents, "DotNetSdkRoslyn"));
                candidates.Add(Path.Combine(contents, "Managed"));
                candidates.Add(Path.Combine(contents, "Managed", "UnityEngine"));
                candidates.Add(Path.Combine(contents, "Tools", "Roslyn"));
                candidates.Add(Path.Combine(contents, "..", "DotNetSdkRoslyn"));
            }
            catch { }

            for (int i = 0; i < candidates.Count; i++)
            {
                try
                {
                    string dir = Path.GetFullPath(candidates[i]);
                    if (File.Exists(Path.Combine(dir, CoreDll)) &&
                        File.Exists(Path.Combine(dir, CSharpDll)))
                    {
                        return dir;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>Сбрасывает кэши: после перекомпиляции проекта.</summary>
        public static void Invalidate()
        {
            lock (CacheGate)
            {
                _referencePaths = null;
                _cacheKey = null;
                _cacheItems = new List<Nsg_CompletionItem>();
            }
        }
    }

    /// <summary>
    /// Сброс кэшей после перекомпиляции. Ссылки проекта к этому моменту уже
    /// устарели, и держать их дальше значит подсказывать по старому набору.
    /// </summary>
    [InitializeOnLoad]
    internal static class Nsg_RoslynCacheBoot
    {
        static Nsg_RoslynCacheBoot()
        {
            AssemblyReloadEvents.afterAssemblyReload += Nsg_RoslynProvider.Invalidate;
        }
    }
}
