using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NekoScriptGraph
{
    public enum NsgDocState
    {
        /// <summary>Рядом с .cs нет .nsg.json: свободный, обычный скрипт.</summary>
        Unmanaged,
        Synced,
        CsDirty,
        BlocksDirty,
        Conflict
    }

    /// <summary>
    /// Владеет одним управляемым файлом: Foo.cs + Foo.nsg.json.
    ///
    /// .cs никогда не ссылается на этот плагин. Здесь нет ни маркера, ни
    /// атрибута, ни хука partial-класса, поэтому удаление Foo.nsg.json
    /// возвращает Foo.cs в состояние полностью свободного скрипта.
    /// Расхождение определяется сравнением файла с текстом тел, записанным
    /// в .nsg.json.
    /// </summary>
    public class Nsg_Document
    {
        public string CsPath;
        public string NsgPath;
        public NsgFileModel Model;
        public NsgDocState State = NsgDocState.Unmanaged;

        /// <summary>Язык, которым обслуживается этот файл.</summary>
        public Nsg_LanguageEntry Entry;

        /// <summary>Движок языка: разбор, печать, карта блоков.</summary>
        public INsg_LanguageEngine Engine;

        /// <summary>Библиотека блоков этого языка.</summary>
        public Nsg_BlockLibrary Library;

        public readonly NsgDiagnostics Diagnostics = new NsgDiagnostics();

        public Nsg_Document(Nsg_LanguageEntry entry)
        {
            Entry = entry;
            if (entry != null)
            {
                Engine = entry.Language != null ? entry.Language.CreateEngine() : null;
            }
        }

        public bool IsManaged
        {
            get { return File.Exists(NsgPath); }
        }

        public string LanguageId
        {
            get { return Entry != null ? Entry.Id : "?"; }
        }

        public static string NsgPathFor(string csPath)
        {
            string dir = Path.GetDirectoryName(csPath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(csPath);
            string p = Path.Combine(dir, name + Nsg_Paths.ManagedExtension);
            return p.Replace('\\', '/');
        }

        public static string CsPathFor(string nsgPath)
        {
            string dir = Path.GetDirectoryName(nsgPath) ?? string.Empty;
            string name = Path.GetFileName(nsgPath);
            if (name.EndsWith(Nsg_Paths.ManagedExtension))
            {
                name = name.Substring(0, name.Length - Nsg_Paths.ManagedExtension.Length);
            }
            string p = Path.Combine(dir, name + ".cs");
            return p.Replace('\\', '/');
        }

        // ------------------------------------------------------------------
        // Загрузка / сохранение
        // ------------------------------------------------------------------

        public bool LoadModel()
        {
            if (!File.Exists(NsgPath)) return false;
            string json = File.ReadAllText(NsgPath);
            if (string.IsNullOrEmpty(json)) return false;
            Model = JsonUtility.FromJson<NsgFileModel>(json);
            if (Model == null) return false;
            Model.UnpackGraphs();
            return true;
        }

        public void SaveModel()
        {
            if (Model == null) return;
            Nsg_Paths.EnsureDirectory(Path.GetDirectoryName(NsgPath) ?? Nsg_Paths.Root);
            Model.PackGraphs();
            File.WriteAllText(NsgPath, JsonUtility.ToJson(Model, true));

            // Перезапись файла заставляет Unity переимпортировать его, а новый
            // объект ассета создаётся с нулевыми флагами. Поэтому «скрыт» надо
            // поставить заново — иначе файл всплывал бы после каждого сохранения.
            Nsg_FileVisibility.ApplyTo(NsgPath);
        }

        /// <summary>
        /// Разбирает все тела методов из .cs и пересобирает графы.
        /// Это единственное направление, превращающее код в блоки.
        /// </summary>
        public bool ImportFromCode(bool inheritLayout)
        {
            if (!File.Exists(CsPath))
            {
                Diagnostics.Error(NsgCodes.Unconvertible, Nsg_L10n.T("msg.noSource", CsPath));
                return false;
            }

            return ImportFromSource(File.ReadAllText(CsPath), inheritLayout);
        }

        /// <summary>
        /// То же самое, но текст приходит извне, а не с диска.
        ///
        /// Нужно агентскому интерфейсу: «план» и «канонизация» обязаны уметь
        /// разобрать предложенный текст ДО записи в проект. Это принципиально:
        /// .cs участвует в компиляции Unity, поэтому неверный текст сначала
        /// ломает домен, и только потом о нём узнал бы плагин. Собственный
        /// лексер плагина в компиляции не нуждается, поэтому разбор возможен
        /// всегда.
        /// </summary>
        public bool ImportFromSource(string src, bool inheritLayout)
        {
            if (Engine == null) return false;
            if (src == null) src = string.Empty;

            Diagnostics.Clear();
            var fresh = Engine.Split(src, Path.GetFileName(CsPath), Diagnostics);
            var freshMethods = fresh.AllMethods();

            var previous = new NsgMethodGraph[freshMethods.Count];
            if (inheritLayout && Model != null)
            {
                var oldMethods = Model.AllMethods();
                for (int i = 0; i < freshMethods.Count; i++)
                {
                    for (int j = 0; j < oldMethods.Count; j++)
                    {
                        if (oldMethods[j].name == freshMethods[i].name &&
                            oldMethods[j].header == freshMethods[i].header)
                        {
                            previous[i] = oldMethods[j].graph;
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < freshMethods.Count; i++)
            {
                var m = freshMethods[i];
                string inner = Engine.InnerBody(m.printed);

                int before = Diagnostics.Count;
                var body = Engine.ParseBody(inner, Diagnostics);

                int offset = m.line - 3;
                if (offset != 0)
                {
                    for (int k = before; k < Diagnostics.Count; k++)
                    {
                        var dg = Diagnostics.Items[k];
                        if (dg.Line >= 3) dg.Line += offset;
                    }
                }

                m.graph = Engine.ToGraph(Library, Diagnostics, body, previous[i]);
            }

            // Заранее предупреждаем, что импорт что-то переформатирует.
            var check = new NsgDiagnostics();
            int reformatted = 0;
            for (int i = 0; i < freshMethods.Count; i++)
            {
                var m = freshMethods[i];
                var ast = Engine.ToAst(Library, check, m.graph);
                string indent = Engine.BodyIndent(m.header);
                if (Engine.PrintBody(ast, indent) != m.printed) reformatted++;
            }
            if (reformatted > 0)
            {
                Diagnostics.Info(NsgCodes.Ambiguous, Nsg_L10n.T("msg.canonicalized", reformatted));
            }

            Model = fresh;
            return !Diagnostics.HasErrors;
        }

        // ------------------------------------------------------------------
        // Блоки -> код
        // ------------------------------------------------------------------

        public bool TryRender(out string text, out List<string> bodies, NsgDiagnostics diag)
        {
            text = null;
            bodies = new List<string>();

            if (Model == null)
            {
                diag.Error(NsgCodes.Internal, Nsg_L10n.T("msg.noModelRender"));
                return false;
            }

            var methods = Model.AllMethods();

            for (int i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                var ast = Engine.ToAst(Library, diag, m.graph);
                string indent = Engine.BodyIndent(m.header);
                bodies.Add(Engine.PrintBody(ast, indent));
            }

            text = Engine.Rebuild(Model, bodies);
            return !diag.HasErrors;
        }

        /// <summary>
        /// Обновляет каркас с диска, затем рендерит. Завершается ошибкой
        /// NSG0006, если набор методов на диске больше не совпадает с
        /// управляемой моделью, потому что угадывание молча потеряло бы код
        /// или сдвинуло его.
        /// </summary>
        public bool Generate(out string text, out List<string> bodies, NsgDiagnostics diag)
        {
            text = null;
            bodies = null;

            if (Model == null)
            {
                diag.Error(NsgCodes.Internal, Nsg_L10n.T("msg.noModelGenerate"));
                return false;
            }

            if (File.Exists(CsPath))
            {
                string current = File.ReadAllText(CsPath);
                var disk = Engine.Split(current, Path.GetFileName(CsPath), diag);
                var diskMethods = disk.AllMethods();
                var myMethods = Model.AllMethods();

                if (diskMethods.Count != myMethods.Count)
                {
                    diag.Error(NsgCodes.MetadataLoss,
                        Nsg_L10n.T("msg.methodCount", diskMethods.Count, myMethods.Count));
                    return false;
                }

                for (int i = 0; i < diskMethods.Count; i++)
                {
                    if (diskMethods[i].name != myMethods[i].name)
                    {
                        diag.Error(NsgCodes.MetadataLoss,
                            Nsg_L10n.T("msg.methodName", i + 1, diskMethods[i].name, myMethods[i].name));
                        return false;
                    }
                }

                // Диск даёт каркас; наши графы дают тела.
                for (int i = 0; i < diskMethods.Count; i++)
                {
                    diskMethods[i].graph = myMethods[i].graph;
                }
                Model = disk;
            }

            return TryRender(out text, out bodies, diag);
        }

        public void WriteCode(string text, List<string> bodies)
        {
            File.WriteAllText(CsPath, text);
            if (bodies != null)
            {
                var methods = Model.AllMethods();
                for (int i = 0; i < methods.Count && i < bodies.Count; i++)
                {
                    if (bodies[i] != null) methods[i].printed = bodies[i];
                }
            }
            SaveModel();
        }

        // ------------------------------------------------------------------
        // Определение расхождений
        // ------------------------------------------------------------------

        public void RefreshState()
        {
            if (Model == null || !File.Exists(NsgPath))
            {
                State = NsgDocState.Unmanaged;
                return;
            }

            if (!File.Exists(CsPath))
            {
                State = NsgDocState.CsDirty;
                return;
            }

            string disk = File.ReadAllText(CsPath);
            string expected = Engine.Rebuild(Model);
            bool csClean = disk == expected;

            bool blocksClean = true;
            var diag = new NsgDiagnostics();
            var methods = Model.AllMethods();

            for (int i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                var ast = Engine.ToAst(Library, diag, m.graph);
                string indent = Engine.BodyIndent(m.header);
                if (Engine.PrintBody(ast, indent) != m.printed)
                {
                    blocksClean = false;
                    break;
                }
            }

            if (csClean && blocksClean) State = NsgDocState.Synced;
            else if (!csClean && blocksClean) State = NsgDocState.CsDirty;
            else if (csClean && !blocksClean) State = NsgDocState.BlocksDirty;
            else State = NsgDocState.Conflict;
        }

        public string StateLabel()
        {
            switch (State)
            {
                case NsgDocState.Unmanaged: return Nsg_L10n.T("state.unmanaged");
                case NsgDocState.Synced: return Nsg_L10n.T("state.synced");
                case NsgDocState.CsDirty: return Nsg_L10n.T("state.csdirty");
                case NsgDocState.BlocksDirty: return Nsg_L10n.T("state.blocksdirty");
                case NsgDocState.Conflict: return Nsg_L10n.T("state.conflict");
            }
            return State.ToString();
        }

        /// <summary>Удаляет .nsg.json, возвращая .cs в состояние свободного скрипта.</summary>
        public void Unmanage()
        {
            if (File.Exists(NsgPath)) File.Delete(NsgPath);
            Model = null;
            State = NsgDocState.Unmanaged;
        }
    }
}
