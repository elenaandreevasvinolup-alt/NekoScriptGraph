using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Один входной слот экземпляра блока. Либо он соединён с другим узлом
    /// (<see cref="link"/>), либо хранит текст, введённый пользователем
    /// (<see cref="text"/>). Листовые значения (числа, строки, идентификаторы)
    /// используют текстовую форму, чтобы графы оставались компактными.
    /// </summary>
    [System.Serializable]
    public class NsgSlot
    {
        public string link;
        public string text;

        public NsgSlot() { }

        public NsgSlot(string link, string text)
        {
            this.link = link;
            this.text = text;
        }

        public bool IsLinked
        {
            get { return !string.IsNullOrEmpty(link); }
        }

        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(link) && string.IsNullOrEmpty(text); }
        }
    }

    /// <summary>
    /// Экземпляр блока. <see cref="next"/> — продолжение выполнения внутри
    /// объемлющего списка операторов; <see cref="body"/> и <see cref="els"/> —
    /// вложенные списки операторов у блоков управления.
    /// </summary>
    [System.Serializable]
    public class NsgGraphNode
    {
        public string id;
        public string block;
        public float x;
        public float y;
        public string next;
        public string body;
        public string els;
        public List<NsgSlot> args = new List<NsgSlot>();
        public List<NsgSlot> extra = new List<NsgSlot>();
        public string comments;
        public int blankBefore;

        public NsgSlot Arg(int index)
        {
            if (index < 0 || index >= args.Count) return null;
            return args[index];
        }

        public NsgSlot EnsureArg(int index)
        {
            while (args.Count <= index) args.Add(new NsgSlot());
            return args[index];
        }
    }

    [System.Serializable]
    public class NsgMethodGraph
    {
        public string entry;
        public List<NsgGraphNode> nodes = new List<NsgGraphNode>();

        public NsgGraphNode Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].id == id) return nodes[i];
            }
            return null;
        }

        public bool Remove(string id)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].id == id)
                {
                    nodes.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }
}
