using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Способ рисования метода.
    ///
    ///   Stack     — стопка блоков в стиле Scratch: блоки вложены друг в друга,
    ///               тело управления нарисовано внутри блока.
    ///   Blueprint — свободные узлы в стиле UE Blueprints: узлы стоят в своих
    ///               координатах, связи нарисованы проводами.
    ///
    /// Обе модели читают и пишут ОДИН и тот же NsgMethodGraph, поэтому
    /// переключение режима не меняет файл и не требует конвертации.
    /// </summary>
    public enum NsgViewMode
    {
        Stack = 0,
        Blueprint = 1
    }

    /// <summary>
    /// Настройки плагина. Поддержка спрайтов необязательна: без спрайтов
    /// редактор рисует плоские цветные блоки, а если положить в Sprites/
    /// серый 9-slice набор, внешний вид улучшится без правки кода (цвет
    /// категории накладывается как tint, поэтому одного серого набора
    /// хватает на все категории).
    /// </summary>
    [Serializable]
    public class Nsg_Settings
    {
        /// <summary>Режим рисования. 0 — стопка (по умолчанию), 1 — чертёж.</summary>
        public int viewMode = (int)NsgViewMode.Stack;

        /// <summary>
        /// Прятать файлы .nsg.json в окне Project. По умолчанию прячем: это
        /// служебные файлы, рядом со скриптами они только мешают.
        /// </summary>
        public bool hideBlockFiles = true;

        /// <summary>Режим рисования, безопасно приведённый к известному значению.</summary>
        public NsgViewMode Mode
        {
            get { return viewMode == (int)NsgViewMode.Blueprint ? NsgViewMode.Blueprint : NsgViewMode.Stack; }
            set { viewMode = (int)value; }
        }

        public bool useSprites;
        public string headerSprite = "block_header";
        public string bodySprite = "block_body";
        public string footerSprite = "block_footer";
        public string exprSprite = "block_expr";

        public int bodyIndent = 14;
        public int cornerRadius = 6;
        public int footerHeight = 10;

        /// <summary>Ширина правой колонки (блоки + наборы), запоминается.</summary>
        public float rightPaneWidth = 240f;

        /// <summary>Высота области наборов в правой колонке, запоминается.</summary>
        public float presetPaneHeight = 200f;

        /// <summary>Выбранная графическая管线: "auto" | "builtin" | "urp" | "hdrp".</summary>
        public string shaderPipeline = "auto";

        public string apiOutputFolder = "Assets/NekoScriptGraph/Blocks/API";

        static Nsg_Settings _instance;

        public static Nsg_Settings Instance
        {
            get
            {
                if (_instance == null) Load();
                return _instance;
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(Nsg_Paths.SettingsPath))
                {
                    string json = File.ReadAllText(Nsg_Paths.SettingsPath);
                    _instance = JsonUtility.FromJson<Nsg_Settings>(json);
                }
            }
            catch
            {
                _instance = null;
            }
            if (_instance == null) _instance = new Nsg_Settings();
        }

        public void Save()
        {
            Nsg_Paths.EnsureDirectory(Nsg_Paths.Root);
            File.WriteAllText(Nsg_Paths.SettingsPath, JsonUtility.ToJson(this, true));
        }

        public Sprite SpriteFor(string kind)
        {
            if (!useSprites) return null;

            string file;
            switch (kind)
            {
                case "header": file = headerSprite; break;
                case "body": file = bodySprite; break;
                case "footer": file = footerSprite; break;
                case "expr": file = exprSprite; break;
                default: return null;
            }
            if (string.IsNullOrEmpty(file)) return null;

            string path = Nsg_Paths.Root + "/Sprites/" + file + ".png";
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
