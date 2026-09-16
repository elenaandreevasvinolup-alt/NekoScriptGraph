using System.Collections.Generic;
using UnityEditor;

namespace NekoScriptGraph
{
    /// <summary>
    /// Единственное место, где ядро соединяется с редактором Unity.
    ///
    /// Ядро (Editor/Core) намеренно не ссылается на UnityEditor: тот же код
    /// работает и в batchmode, и в автономных тестах. Всё редакторское —
    /// GUID ассета, импорт, кэш библиотек, файлы .nsg.json — приходит сюда
    /// через хуки Nsg_AgentApi.
    ///
    /// Установка идемпотентна и вызывается из двух мест: Nsg_EditorBoot
    /// (обычный редактор) и Nsg_AgentCli (batchmode, где InitializeOnLoad тоже
    /// срабатывает, но полагаться на порядок не стоит).
    /// </summary>
    internal static class Nsg_EditorHooks
    {
        static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            Nsg_AgentApi.GuidOf = path =>
            {
                try { return AssetDatabase.AssetPathToGUID(path); }
                catch { return string.Empty; }
            };

            Nsg_AgentApi.FileWritten = path =>
            {
                try
                {
                    AssetDatabase.ImportAsset(path);
                    AssetDatabase.Refresh();
                }
                catch
                {
                    // вне редактора импортировать нечего
                }
            };

            Nsg_AgentApi.LibraryOf = entry => Nsg_Manager.Instance.GetLibrary(entry);

            Nsg_AgentApi.ListManagedConfigs = folder =>
                Nsg_Manager.Instance.FindManagedConfigs(folder).ToArray();

            Nsg_AgentApi.ReleaseConfigs = folder =>
            {
                int failed;
                int done = Nsg_Manager.Instance.UnmanageAll(folder, out failed);
                return new Nsg_ReleaseResult { released = done, failed = failed };
            };
        }
    }
}
