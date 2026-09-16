using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Неинтерактивный вход агентского интерфейса.
    ///
    /// Запуск из командной строки:
    ///
    ///   Unity -batchmode -quit -projectPath &lt;P&gt; \
    ///         -executeMethod NekoScriptGraph.Nsg_AgentCli.Main \
    ///         -nsgRequest req.json -nsgResponse resp.json
    ///
    /// Коды возврата:
    ///   0 — операция выполнена (ok = true)
    ///   1 — операция отклонена (ok = false, причина в resp.error)
    ///   2 — не удалось даже прочитать запрос
    ///
    /// Почему отдельный вход, а не пункт меню: все пункты меню Nsg_Menu
    /// показывают EditorUtility.DisplayDialog, а модальное окно в batchmode
    /// вешает процесс навсегда. Здесь ни одного модального вызова.
    /// </summary>
    public static class Nsg_AgentCli
    {
        const string RequestFlag = "-nsgRequest";
        const string ResponseFlag = "-nsgResponse";

        public static void Main()
        {
            int code = Run();
            EditorApplication.Exit(code);
        }

        /// <summary>Разбор и выполнение без выхода из процесса (нужно тестам).</summary>
        public static int Run()
        {
            HookEditor();

            string reqPath = Flag(RequestFlag);
            string respPath = Flag(ResponseFlag);

            if (string.IsNullOrEmpty(reqPath))
            {
                Debug.LogError("[NekoScriptGraph] agent: " + RequestFlag + " <path> is required");
                return 2;
            }

            Nsg_AgentRequest req;
            try
            {
                req = JsonUtility.FromJson<Nsg_AgentRequest>(File.ReadAllText(reqPath));
            }
            catch (Exception e)
            {
                Debug.LogError("[NekoScriptGraph] agent: cannot read request: " + e.Message);
                return 2;
            }

            if (req == null)
            {
                Debug.LogError("[NekoScriptGraph] agent: request is empty");
                return 2;
            }

            var res = Nsg_AgentApi.Run(req);
            string json = JsonUtility.ToJson(res, true);

            if (!string.IsNullOrEmpty(respPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(respPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(respPath, json);
                }
                catch (Exception e)
                {
                    Debug.LogError("[NekoScriptGraph] agent: cannot write response: " + e.Message);
                    return 2;
                }
            }

            if (res.ok)
            {
                Debug.Log("[NekoScriptGraph] agent: " + req.op + " ok — " + json);
                return 0;
            }

            Debug.LogError("[NekoScriptGraph] agent: " + req.op + " rejected — " + json);
            return 1;
        }

        /// <summary>
        /// Связывает ядро с редактором. Ровно те вещи, ради которых ядру
        /// понадобился бы UnityEditor: GUID ассета, импорт, кэш библиотек и
        /// файлы .nsg.json.
        /// </summary>
        static void HookEditor()
        {
            Nsg_EditorHooks.Install();
        }

        static string Flag(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        // ==================================================================
        // Пункт меню: выгрузить письменный набор
        // ==================================================================

        /// <summary>
        /// Пункт меню намеренно НЕ попадает в Nsg_MenuRuntime: по требованию
        /// подписи меню Unity остаются английскими.
        /// </summary>
        [MenuItem(Nsg_Menu.Root + "Write Agent Writing Set", false, 300)]
        public static void WriteWritingSet()
        {
            string folder = Nsg_Paths.Root + "/AgentSpec";
            Nsg_Paths.EnsureDirectory(folder);

            var written = new List<string>();
            var entries = Nsg_LanguageRegistry.All;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Language == null) continue;

                var req = new Nsg_AgentRequest
                {
                    op = Nsg_AgentOps.Spec,
                    specLanguage = entry.Id,
                    specOut = folder + "/" + entry.Id + ".md"
                };

                var res = Nsg_AgentApi.Run(req);
                if (res.ok && res.wrote) written.Add(entry.Id);
            }

            AssetDatabase.Refresh();
            Debug.Log("[NekoScriptGraph] agent writing set: " + written.Count +
                      " language(s) → " + folder + " [" + string.Join(", ", written.ToArray()) + "]");
        }
    }
}
