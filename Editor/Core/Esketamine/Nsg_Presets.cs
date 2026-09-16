using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Один сохранённый набор блоков.
    ///
    /// Набор — это всегда ОДНА связная группа: один корень и всё, что из него
    /// достижимо. Иначе при вставке было бы непонятно, что с чем соединять.
    /// </summary>
    [System.Serializable]
    public class Nsg_Preset
    {
        public string name;
        public string createdAt;
        public int blockCount;
        public string languageId;

        public List<NsgGraphNode> nodes = new List<NsgGraphNode>();
        public string rootId;

        [System.NonSerialized] public string sortKey;
    }

    [System.Serializable]
    public class Nsg_PresetFile
    {
        public int schemaVersion = 1;
        public List<Nsg_Preset> presets = new List<Nsg_Preset>();
    }

    /// <summary>Индекс инициали: порядок 符号 → 数字 → 英文 → 拼音首字母.</summary>
    public static class Nsg_IndexKey
    {
        public const int ClassSymbol = 0;
        public const int ClassDigit = 1;
        public const int ClassLatin = 2;
        public const int ClassCjk = 3;

        /// <summary>Ключ сортировки: "класс|значение".</summary>
        public static string Build(string name)
        {
            if (string.IsNullOrEmpty(name)) return ClassSymbol + "|";

            char c = name[0];
            string rest = name.Substring(1);

            if (char.IsDigit(c))
            {
                // Числа сортируем численно, а не как строку.
                return ClassDigit + "|" + (c - '0').ToString("D4") + rest.ToLowerInvariant();
            }

            if (IsLatin(c))
            {
                return ClassLatin + "|" + char.ToLowerInvariant(c) + rest.ToLowerInvariant();
            }

            if (IsCjk(c))
            {
                return ClassCjk + "|" + Nsg_Pinyin.Initial(c) + name;
            }

            return ClassSymbol + "|" + c + rest;
        }

        public static int ClassOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return ClassSymbol;
            int bar = key.IndexOf('|');
            if (bar <= 0) return ClassSymbol;
            int v;
            return int.TryParse(key.Substring(0, bar), out v) ? v : ClassSymbol;
        }

        static bool IsLatin(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        static bool IsCjk(char c)
        {
            return c >= 0x4E00 && c <= 0x9FFF;
        }
    }

    /// <summary>
    /// Таблица «иероглиф → инициал пиньиня» для распространённых знаков.
    ///
    /// Полная таблица — это десятки тысяч знаков, что для сортировки списка
    /// избыточно. Здесь покрыты частые в именах блоков знаки; для остальных
    /// используется запасная буква, поэтому непокрытый знак просто окажется
    /// ближе к концу группы, а не сломает порядок.
    /// </summary>
    public static class Nsg_Pinyin
    {
        public const char Fallback = 'z';

        /// <summary>
        /// Знаки сгруппированы по инициалу: первый символ строки — инициал,
        /// дальше идут сами знаки. Так таблица компактна и, что важнее, не
        /// может содержать повторяющихся ключей — а повтор в инициализаторе
        /// Dictionary падает уже во время выполнения.
        /// </summary>
        static readonly string[] Groups =
        {
            "a安按暗",
            "b把白百绑包保暴背本比笔变表别播不部",
            "c查差产常场超朝车成程池持充冲出初除处触传创此次从粗存错",
            "d打大带单当到道得等地点第电掉调定丢动都读度断队对多",
            "e额而二",
            "f发反方放飞分封否服复返范防访非废费风浮父负附",
            "g改概高告个给跟更工公攻共构关观管光广轨过",
            "h还海含好号合和黑很红后候忽画话环换回会活火获",
            "j击机基激级即计记加假间检简件建键将交角脚接结解界进近经静就局举具决绝",
            "k开看考可空控快框扩",
            "l来类冷离里力例连联量列临流路逻落率",
            "m马满慢没每密面描明命模默某目",
            "n拿那内能你年",
            "p判跑配碰批偏片频平评破普爬排派盘",
            "q期其起气前强切清情求区取全确群",
            "r让热人任日如入软若",
            "s三色森删上少设深什生声省时识实使始世事是收手输属数双水顺说死四速算随缩所杀山闪伤商社神失施十石适首受刷谁司思送",
            "t他它台太态弹探特提体天条跳听停通同头透图退托",
            "w外万完往望网为维委卫温文稳问我无五舞位物",
            "x西系下先现相想向项消小效写新信行形修需许选学循讯序旋",
            "y移已以意因引应用优有右于与语元原远约越运一压延验样要也业页依眼演阳养摇药音银印营影硬永由游友又余鱼雨预域遇员圆源院月云允",
            "z杂在暂早增找这真整正之支知直值只指制质中种重周主注转装状准着子自字总组最左作坐做再造则怎展站张照者阵证置终助住追资走嘴昨尊",
        };

        static readonly Dictionary<char, char> Table = BuildTable();

        static Dictionary<char, char> BuildTable()
        {
            var map = new Dictionary<char, char>();

            for (int i = 0; i < Groups.Length; i++)
            {
                string g = Groups[i];
                if (string.IsNullOrEmpty(g)) continue;

                char initial = g[0];
                for (int k = 1; k < g.Length; k++) map[g[k]] = initial;
            }

            return map;
        }

        public static char Initial(char c)
        {
            char v;
            return Table.TryGetValue(c, out v) ? v : Fallback;
        }
    }

    /// <summary>
    /// Хранилище наборов. Лежит в папке с точкой, поэтому Unity её не
    /// импортирует, а собственный .gitignore держит наборы вне истории —
    /// ровно как точки сохранения.
    /// </summary>
    public static class Nsg_Presets
    {
        public const int MaxBlocks = 80;

        public static string RootDir
        {
            get { return Nsg_Paths.Root + "/.presets"; }
        }

        static string FilePath
        {
            get { return RootDir + "/presets.json"; }
        }

        static Nsg_PresetFile _cache;

        public static void EnsureGitIgnore()
        {
            Nsg_Paths.EnsureDirectory(RootDir);
            string gi = RootDir + "/.gitignore";
            if (!File.Exists(gi))
            {
                File.WriteAllText(gi, "# Наборы блоков NekoScriptGraph приватны для плагина.\n*\n!.gitignore\n");
            }
        }

        public static Nsg_PresetFile Load()
        {
            if (_cache != null) return _cache;

            _cache = new Nsg_PresetFile();
            try
            {
                if (File.Exists(FilePath))
                {
                    var parsed = JsonUtility.FromJson<Nsg_PresetFile>(File.ReadAllText(FilePath));
                    if (parsed != null && parsed.presets != null) _cache = parsed;
                }
            }
            catch
            {
                _cache = new Nsg_PresetFile();
            }

            Reindex(_cache);
            return _cache;
        }

        public static void Save()
        {
            if (_cache == null) return;
            EnsureGitIgnore();
            File.WriteAllText(FilePath, JsonUtility.ToJson(_cache, true));
        }

        public static void Reload()
        {
            _cache = null;
            Load();
        }

        static void Reindex(Nsg_PresetFile file)
        {
            for (int i = 0; i < file.presets.Count; i++)
            {
                file.presets[i].sortKey = Nsg_IndexKey.Build(file.presets[i].name);
            }
        }

        /// <summary>
        /// Наборы в порядке 符号 → 数字 → 英文首字母 → 拼音首字母.
        /// </summary>
        public static List<Nsg_Preset> Sorted()
        {
            var file = Load();
            var list = new List<Nsg_Preset>(file.presets);

            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrEmpty(list[i].sortKey)) list[i].sortKey = Nsg_IndexKey.Build(list[i].name);
            }

            list.Sort((a, b) =>
            {
                int ca = Nsg_IndexKey.ClassOf(a.sortKey);
                int cb = Nsg_IndexKey.ClassOf(b.sortKey);
                if (ca != cb) return ca.CompareTo(cb);

                int k = string.CompareOrdinal(a.sortKey, b.sortKey);
                if (k != 0) return k;

                return string.CompareOrdinal(a.name, b.name);
            });

            return list;
        }

        public static Nsg_Preset Find(string name)
        {
            var file = Load();
            for (int i = 0; i < file.presets.Count; i++)
            {
                if (file.presets[i].name == name) return file.presets[i];
            }
            return null;
        }

        public static bool Add(Nsg_Preset preset, out string error)
        {
            error = null;
            if (preset == null || string.IsNullOrEmpty(preset.name))
            {
                error = Nsg_L10n.T("preset.needName");
                return false;
            }

            if (preset.nodes == null || preset.nodes.Count == 0)
            {
                error = Nsg_L10n.T("preset.empty");
                return false;
            }

            if (preset.nodes.Count > MaxBlocks)
            {
                error = Nsg_L10n.T("preset.tooBig", preset.nodes.Count, MaxBlocks);
                return false;
            }

            var file = Load();

            // Одно имя — один набор: перезаписываем.
            for (int i = 0; i < file.presets.Count; i++)
            {
                if (file.presets[i].name == preset.name)
                {
                    file.presets[i] = preset;
                    preset.sortKey = Nsg_IndexKey.Build(preset.name);
                    Save();
                    return true;
                }
            }

            preset.sortKey = Nsg_IndexKey.Build(preset.name);
            file.presets.Add(preset);
            Save();
            return true;
        }

        public static bool Remove(string name)
        {
            var file = Load();
            for (int i = 0; i < file.presets.Count; i++)
            {
                if (file.presets[i].name == name)
                {
                    file.presets.RemoveAt(i);
                    Save();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Проверяет, что набор — одна связная группа, и возвращает число блоков.
        /// </summary>
        public static bool IsConnectedGroup(List<NsgGraphNode> nodes, string rootId)
        {
            if (nodes == null || nodes.Count == 0 || string.IsNullOrEmpty(rootId)) return false;

            var byId = new Dictionary<string, NsgGraphNode>();
            for (int i = 0; i < nodes.Count; i++) byId[nodes[i].id] = nodes[i];
            if (!byId.ContainsKey(rootId)) return false;

            var seen = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(rootId);

            while (stack.Count > 0)
            {
                string id = stack.Pop();
                if (!seen.Add(id)) continue;

                NsgGraphNode n;
                if (!byId.TryGetValue(id, out n)) continue;

                Push(stack, n.next);
                Push(stack, n.body);
                Push(stack, n.els);

                for (int i = 0; i < n.args.Count; i++)
                {
                    if (n.args[i] != null) Push(stack, n.args[i].link);
                }
                for (int i = 0; i < n.extra.Count; i++)
                {
                    if (n.extra[i] != null) Push(stack, n.extra[i].link);
                }
            }

            return seen.Count == nodes.Count;
        }

        static void Push(Stack<string> stack, string id)
        {
            if (!string.IsNullOrEmpty(id)) stack.Push(id);
        }
    }
}
