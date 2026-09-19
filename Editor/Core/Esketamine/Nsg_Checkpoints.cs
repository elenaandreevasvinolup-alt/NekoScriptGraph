using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Три именованных слота сохранения на скрипт ПЛЮС автоматический.
    ///
    /// Они лежат в папке с точкой в начале, которую Unity никогда не
    /// импортирует, и эта папка пишет собственный .gitignore. Поэтому
    /// встроенные точки сохранения приватны для плагина и никогда не
    /// конфликтуют с историей репозитория.
    ///
    /// АВТОМАТИЧЕСКИЙ СЛОТ — не «четвёртый именованный». Его нельзя выбрать,
    /// назвать или занять вручную: он пишется САМ перед каждым необратимым
    /// действием и служит одной цели — дать точку возврата, о которой не надо
    /// было помнить. Именованные слоты для этого не годятся: пользователь
    /// вспоминает о них после того, как стало поздно.
    /// </summary>
    public static class Nsg_Checkpoints
    {
        public const int SlotCount = 3;

        /// <summary>Номер автоматического слота. Вне диапазона именованных.</summary>
        public const int AutoSlot = SlotCount;

        /// <summary>Последняя метка автосохранения — её показывает интерфейс.</summary>
        public static string LastAutoLabel { get; private set; }

        [Serializable]
        public class Slot
        {
            public int schemaVersion = 1;
            public string savedAt;
            public string label;
            public string csSource;

            // Хранится как текст JSON, а не как вложенный объект: так файл
            // остаётся достаточно плоским для сериализатора Unity и читается
            // даже если форма модели позже изменится.
            public string modelJson;
            public int methodCount;
        }

        public static string RootDir
        {
            get { return Nsg_Paths.Root + "/.checkpoints"; }
        }

        public static string DirFor(string csPath)
        {
            string name = (csPath ?? "unknown").Replace('\\', '/');
            if (name.StartsWith("Assets/")) name = name.Substring("Assets/".Length);
            name = Nsg_BlockLibrary.SafeFileName(name).Replace('/', '_');
            return RootDir + "/" + name;
        }

        static string FileFor(string csPath, int slot)
        {
            string name = slot == AutoSlot ? "auto" : slot.ToString();
            return DirFor(csPath) + "/" + name + ".json";
        }

        public static void EnsureGitIgnore()
        {
            Nsg_Paths.EnsureDirectory(RootDir);
            string gi = RootDir + "/.gitignore";
            if (!File.Exists(gi))
            {
                // Оставляем сам ignore-файл, чтобы правило выжило после клона.
                File.WriteAllText(gi, "# NekoScriptGraph built-in checkpoints are plugin-private.\n# They are intentionally kept out of git so they can never clash\n# with the repository's own history.\n*\n!.gitignore\n");
            }
        }

        public static bool Exists(string csPath, int slot)
        {
            return File.Exists(FileFor(csPath, slot));
        }

        public static string Describe(string csPath, int slot)
        {
            string path = FileFor(csPath, slot);
            if (!File.Exists(path)) return null;

            try
            {
                var s = JsonUtility.FromJson<Slot>(File.ReadAllText(path));
                if (s == null) return "?";
                return (s.savedAt ?? "?") + "  ·  " + s.methodCount + " " + Nsg_L10n.T("tree.method");
            }
            catch
            {
                return "?";
            }
        }

        public static void Save(string csPath, int slot, NsgFileModel model, string csSource, string label)
        {
            if (model == null) return;
            EnsureGitIgnore();
            Nsg_Paths.EnsureDirectory(DirFor(csPath));

            model.PackGraphs();

            var s = new Slot
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                label = label,
                csSource = csSource,
                modelJson = JsonUtility.ToJson(model, true),
                methodCount = model.MethodCount
            };

            File.WriteAllText(FileFor(csPath, slot), JsonUtility.ToJson(s, true));
        }

        /// <summary>
        /// Автосохранение перед НЕОБРАТИМЫМ действием.
        ///
        /// Модель клонируется: Save упаковывает графы (PackGraphs), а
        /// вызывающий обычно продолжает работать с той же моделью. Менять её
        /// ради резервной копии — значит портить то, что копируют.
        ///
        /// Сбой автосохранения НЕ отменяет само действие: он лишает точки
        /// возврата, но не является причиной отказываться от работы. Поэтому
        /// исключение ловится и уходит предупреждением.
        /// </summary>
        public static void AutoSave(string csPath, NsgFileModel model, string csSource, string label)
        {
            if (model == null || string.IsNullOrEmpty(csPath)) return;

            try
            {
                var copy = Nsg_Cloner.CloneModel(model);
                Save(csPath, AutoSlot, copy, csSource, label);
                LastAutoLabel = DateTime.Now.ToString("HH:mm:ss") + " — " + label;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[NekoScriptGraph] autosave failed: " + e.Message);
            }
        }

        public static Slot LoadAuto(string csPath)
        {
            return Load(csPath, AutoSlot);
        }

        public static Slot Load(string csPath, int slot)
        {
            string path = FileFor(csPath, slot);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<Slot>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        public static NsgFileModel ModelOf(Slot s)
        {
            if (s == null || string.IsNullOrEmpty(s.modelJson)) return null;
            try
            {
                var m = JsonUtility.FromJson<NsgFileModel>(s.modelJson);
                if (m != null) m.UnpackGraphs();
                return m;
            }
            catch
            {
                return null;
            }
        }

        public static void Delete(string csPath, int slot)
        {
            string path = FileFor(csPath, slot);
            if (File.Exists(path)) File.Delete(path);
        }

        public static List<string> ListFor(string csPath)
        {
            var list = new List<string>();
            for (int i = 0; i < SlotCount; i++) list.Add(Describe(csPath, i));
            return list;
        }
    }

    /// <summary>
    /// Минимальный мост к git. Он запускается только когда пользователь явно
    /// просит точку сохранения git, и индексирует лишь два файла текущего
    /// скрипта, поэтому не может помешать уже сложившемуся порядку коммитов.
    /// </summary>
    public static class Nsg_Git
    {
        static readonly string[] Candidates =
        {
            "/usr/bin/git",
            "/usr/local/bin/git",
            "/opt/homebrew/bin/git",
            "git"
        };

        static string _gitPath;

        static string GitPath()
        {
            if (_gitPath != null) return _gitPath;

            for (int i = 0; i < Candidates.Length; i++)
            {
                string c = Candidates[i];
                if (c == "git")
                {
                    _gitPath = "git";
                    return _gitPath;
                }
                if (File.Exists(c))
                {
                    _gitPath = c;
                    return _gitPath;
                }
            }

            _gitPath = "git";
            return _gitPath;
        }

        public static bool IsRepo(string anyPathInRepo, out string workDir)
        {
            workDir = null;
            string dir = File.Exists(anyPathInRepo)
                ? (Path.GetDirectoryName(anyPathInRepo) ?? string.Empty)
                : anyPathInRepo;

            string output;
            int code = Run(dir, new[] { "rev-parse", "--show-toplevel" }, out output);
            if (code != 0) return false;

            workDir = output.Trim();
            return workDir.Length > 0;
        }

        public static bool Commit(string repoRoot, List<string> assetPaths, string message, out string output)
        {
            output = string.Empty;
            if (string.IsNullOrEmpty(repoRoot) || assetPaths == null || assetPaths.Count == 0)
            {
                output = "nothing to commit";
                return false;
            }

            var add = new List<string> { "add", "--" };
            add.AddRange(assetPaths);

            string addOut;
            int c1 = Run(repoRoot, add, out addOut);

            var commit = new List<string> { "commit", "-m", message, "--" };
            commit.AddRange(assetPaths);

            int c2 = Run(repoRoot, commit, out output);

            output = (addOut + "\n" + output).Trim();
            return c1 == 0 && c2 == 0;
        }

        public static bool HasChanges(string repoRoot, List<string> assetPaths, out string output)
        {
            var args = new List<string> { "status", "--porcelain", "--" };
            args.AddRange(assetPaths);
            return Run(repoRoot, args, out output) == 0 && !string.IsNullOrEmpty(output.Trim());
        }

        static int Run(string workDir, string[] args, out string output)
        {
            return Run(workDir, new List<string>(args), out output);
        }

        static int Run(string workDir, List<string> args, out string output)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = GitPath();
                psi.WorkingDirectory = string.IsNullOrEmpty(workDir) ? "." : workDir;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                for (int i = 0; i < args.Count; i++) psi.ArgumentList.Add(args[i]);

                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    output = (stdout + stderr).Trim();
                    return p.ExitCode;
                }
            }
            catch (Exception e)
            {
                output = e.Message;
                return -1;
            }
        }
    }
}
