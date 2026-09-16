using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Движок для любого C-подобного языка. Отличие от C# ровно одно —
    /// профиль, из которого берутся наборы ключевых слов. Печататель и карта
    /// блоков общие: идентификаторы блоков не зависят от языка, а формат
    /// C-семейства совпадает (Allman, отступ 4).
    ///
    /// Лежит в ядре, рядом с остальной общей машиной C-подобных языков:
    /// профиль приносят сами языки, а грамматика у семейства одна.
    ///
    /// Вторая часть объявлена в Nsg_CStyleAgentSpec.cs — описание письменного
    /// набора для агента. Одно на всё C-семейство, потому что печататель и
    /// правила канонической формы у семейства общие.
    /// </summary>
    public partial class Nsg_CStyleEngine : INsg_LanguageEngine
    {
        readonly Nsg_LanguageProfile _profile;

        public Nsg_CStyleEngine(Nsg_LanguageProfile profile)
        {
            _profile = profile;
        }

        public Nsg_LanguageProfile Profile
        {
            get { return _profile; }
        }

        public NsgFileModel Split(string source, string fileName, NsgDiagnostics diag)
        {
            return NsgFileSplitter.Split(source, fileName, diag, _profile);
        }

        public string Rebuild(NsgFileModel model)
        {
            return NsgFileSplitter.Rebuild(model);
        }

        public string Rebuild(NsgFileModel model, List<string> bodies)
        {
            return NsgFileSplitter.Rebuild(model, bodies);
        }

        public string BodyIndent(string header)
        {
            return NsgFileSplitter.BodyIndent(header);
        }

        public string InnerBody(string printed)
        {
            return NsgFileSplitter.InnerBody(printed);
        }

        public string MethodName(string header)
        {
            return NsgFileSplitter.ExtractMethodName(header);
        }

        public List<NsgStmt> ParseBody(string innerBody, NsgDiagnostics diag)
        {
            return NsgParser.ParseBodyFragment(innerBody, diag, _profile).Statements;
        }

        public NsgMethodGraph ToGraph(Nsg_BlockLibrary library, NsgDiagnostics diag,
                                      List<NsgStmt> statements, NsgMethodGraph previous)
        {
            return new NsgCodeMap(library, diag).ToGraph(statements, previous);
        }

        public List<NsgStmt> ToAst(Nsg_BlockLibrary library, NsgDiagnostics diag, NsgMethodGraph graph)
        {
            return new NsgCodeMap(library, diag).ToAst(graph);
        }

        public string PrintBody(List<NsgStmt> statements, string indent)
        {
            var printer = new NsgPrinter();
            printer.Profile = _profile;
            return printer.PrintMethodBodyRaw(statements, indent);
        }

        public int GenerateApiBlocks(string folder, NsgDiagnostics diag)
        {
            var result = Nsg_ApiGenerator.GenerateFromFiles(folder, _profile, diag);
            return result.Generated;
        }
    }

    /// <summary>
    /// Общая часть для всех C-подобных языков, включая устанавливаемые.
    ///
    /// Наследник приносит только профиль (словарь) и, при необходимости,
    /// свои идиомы через <see cref="AddLanguageBlocks"/>.
    /// </summary>
    public abstract class Nsg_CStyleLanguageBase : INsg_Language
    {
        protected abstract Nsg_LanguageProfile Profile { get; }

        public string Id
        {
            get { return Profile.Id; }
        }

        public string DisplayName
        {
            get { return Profile.DisplayName; }
        }

        public string IconName
        {
            get { return Profile.IconName; }
        }

        public string[] Extensions
        {
            get { return Profile.Extensions; }
        }

        public virtual bool BuiltIn
        {
            get { return false; }
        }

        public int ApiVersion
        {
            get { return Nsg_LanguageApi.Version; }
        }

        public INsg_LanguageEngine CreateEngine()
        {
            return new Nsg_CStyleEngine(Profile);
        }

        public Nsg_BlockLibrary CreateLibrary(string blocksFolder)
        {
            var lib = new Nsg_BlockLibrary();
            var all = Nsg_BlockLibrary.CreateDefaults();

            for (int i = 0; i < all.Count; i++)
            {
                if (!SupportsBlock(all[i].id)) continue;
                lib.Blocks.Add(all[i]);
            }

            int before = lib.Blocks.Count;
            AddLanguageBlocks(lib.Blocks);

            // Идиомы языка — отдельная группа в палитре. Без этого они
            // попадают в общую «API» и визуально неотличимы от чужих блоков:
            // именно поэтому HLSL выглядел копией C#.
            Nsg_CStyleBlocks.MarkLanguageGroup(lib.Blocks, before, Id, DisplayName);

            lib.Rebuild();
            return lib;
        }

        /// <summary>Есть ли в языке этот общий блок.</summary>
        protected virtual bool SupportsBlock(string blockId)
        {
            if (!Profile.HasForeach && blockId == "stmt.foreach") return false;
            if (!Profile.HasNew && blockId == "expr.new") return false;
            return true;
        }

        /// <summary>Идиомы языка. Переопределяется наследником.</summary>
        protected virtual void AddLanguageBlocks(List<NsgBlockDef> into)
        {
        }
    }
}
