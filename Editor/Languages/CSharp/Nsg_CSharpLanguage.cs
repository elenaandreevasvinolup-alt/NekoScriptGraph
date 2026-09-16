using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Встроенный язык C#. Тонкая обёртка над существующим движком: он уже
    /// проверен приёмочными тестами, поэтому здесь только приведение к общему
    /// контракту <see cref="INsg_Language"/>.
    /// </summary>
    public class Nsg_CSharpLanguage : INsg_Language
    {
        public string Id
        {
            get { return "csharp"; }
        }

        public string DisplayName
        {
            get { return "C#"; }
        }

        public string IconName
        {
            get { return "C#"; }
        }

        public string[] Extensions
        {
            get { return new[] { ".cs" }; }
        }

        public bool BuiltIn
        {
            get { return true; }
        }

        public int ApiVersion
        {
            get { return Nsg_LanguageApi.Version; }
        }

        public INsg_LanguageEngine CreateEngine()
        {
            return new Nsg_CSharpEngine();
        }

        public Nsg_BlockLibrary CreateLibrary(string blocksFolder)
        {
            var lib = new Nsg_BlockLibrary();
            lib.Blocks.AddRange(Nsg_BlockLibrary.CreateDefaults());
            lib.Rebuild();
            return lib;
        }
    }

    /// <summary>
    /// Движок C#: делегирует в лексер, разборщик, печататель, разделитель
    /// файла и карту блоков.
    ///
    /// Вторая часть объявлена в Nsg_CSharpAgentSpec.cs — там описание
    /// письменного набора для агента. Оно рядом с движком, потому что набор
    /// и есть проекция его печатателя.
    /// </summary>
    public partial class Nsg_CSharpEngine : INsg_LanguageEngine
    {
        public NsgFileModel Split(string source, string fileName, NsgDiagnostics diag)
        {
            return NsgFileSplitter.Split(source, fileName, diag);
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
            return NsgParser.ParseBodyFragment(innerBody, diag).Statements;
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
            return new NsgPrinter().PrintMethodBodyRaw(statements, indent);
        }

        public int GenerateApiBlocks(string folder, NsgDiagnostics diag)
        {
            var result = Nsg_ApiGenerator.Generate(folder, diag);
            return result.Generated;
        }
    }
}
