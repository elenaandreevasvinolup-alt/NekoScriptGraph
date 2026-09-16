using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Ограниченная отмена и повтор для модели блоков.
    ///
    /// Оптимизация: правка внутри одного метода снимает снимок только графа
    /// этого метода. Снимок всей модели делается лишь при структурных правках
    /// (переименование типа, правка «сырого» члена, добавление или удаление
    /// метода). Так обычный случай остаётся дешёвым, а потолок памяти низким —
    /// именно поэтому глубина может оставаться небольшой (24) и не мешать.
    /// </summary>
    public class Nsg_UndoStack
    {
        public int MaxDepth = 24;

        public class Entry
        {
            public string Label;
            public NsgFileModel Full;      // structural snapshot
            public string MethodKey;       // method-scoped snapshot
            public NsgMethodGraph Method;
        }

        readonly List<Entry> _undo = new List<Entry>();
        readonly List<Entry> _redo = new List<Entry>();

        public bool CanUndo
        {
            get { return _undo.Count > 0; }
        }

        public bool CanRedo
        {
            get { return _redo.Count > 0; }
        }

        public int Depth
        {
            get { return _undo.Count; }
        }

        public string NextUndoLabel
        {
            get { return _undo.Count > 0 ? _undo[_undo.Count - 1].Label : null; }
        }

        public string NextRedoLabel
        {
            get { return _redo.Count > 0 ? _redo[_redo.Count - 1].Label : null; }
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        /// <summary>Снимок перед структурной правкой.</summary>
        public void PushFull(NsgFileModel model, string label)
        {
            if (model == null) return;
            _undo.Add(new Entry { Label = label, Full = Nsg_Cloner.CloneModel(model) });
            _redo.Clear();
            Trim();
        }

        /// <summary>Снимок перед правкой внутри одного метода.</summary>
        public void PushMethod(NsgStructNode method, string label)
        {
            if (method == null || method.graph == null) return;
            _undo.Add(new Entry
            {
                Label = label,
                MethodKey = method.header,
                Method = Nsg_Cloner.CloneGraph(method.graph)
            });
            _redo.Clear();
            Trim();
        }

        public NsgFileModel Undo(NsgFileModel current)
        {
            if (_undo.Count == 0 || current == null) return current;

            var e = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            _redo.Add(Capture(current, e));
            TrimRedo();

            return Apply(current, e);
        }

        public NsgFileModel Redo(NsgFileModel current)
        {
            if (_redo.Count == 0 || current == null) return current;

            var e = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);

            _undo.Add(Capture(current, e));
            Trim();

            return Apply(current, e);
        }

        static Entry Capture(NsgFileModel current, Entry like)
        {
            if (like.Full != null)
            {
                return new Entry { Label = like.Label, Full = Nsg_Cloner.CloneModel(current) };
            }

            var m = FindMethod(current, like.MethodKey);
            return new Entry
            {
                Label = like.Label,
                MethodKey = like.MethodKey,
                Method = (m != null && m.graph != null) ? Nsg_Cloner.CloneGraph(m.graph) : new NsgMethodGraph()
            };
        }

        static NsgFileModel Apply(NsgFileModel current, Entry e)
        {
            if (e.Full != null) return Nsg_Cloner.CloneModel(e.Full);

            var m = FindMethod(current, e.MethodKey);
            if (m != null) m.graph = Nsg_Cloner.CloneGraph(e.Method);
            return current;
        }

        static NsgStructNode FindMethod(NsgFileModel model, string key)
        {
            if (model == null || string.IsNullOrEmpty(key)) return null;
            var methods = model.AllMethods();
            for (int i = 0; i < methods.Count; i++)
            {
                if (methods[i].header == key) return methods[i];
            }
            return null;
        }

        void Trim()
        {
            while (_undo.Count > MaxDepth) _undo.RemoveAt(0);
        }

        void TrimRedo()
        {
            while (_redo.Count > MaxDepth) _redo.RemoveAt(0);
        }
    }

    /// <summary>Глубокое копирование вручную. Намного дешевле, чем круг через JSON.</summary>
    public static class Nsg_Cloner
    {
        public static NsgMethodGraph CloneGraph(NsgMethodGraph g)
        {
            if (g == null) return null;
            var c = new NsgMethodGraph { entry = g.entry };
            for (int i = 0; i < g.nodes.Count; i++) c.nodes.Add(CloneNode(g.nodes[i]));
            return c;
        }

        public static NsgGraphNode CloneNode(NsgGraphNode n)
        {
            if (n == null) return null;
            var c = new NsgGraphNode();
            c.id = n.id;
            c.block = n.block;
            c.x = n.x;
            c.y = n.y;
            c.next = n.next;
            c.body = n.body;
            c.els = n.els;
            c.comments = n.comments;
            c.blankBefore = n.blankBefore;
            for (int i = 0; i < n.args.Count; i++) c.args.Add(CloneSlot(n.args[i]));
            for (int i = 0; i < n.extra.Count; i++) c.extra.Add(CloneSlot(n.extra[i]));
            return c;
        }

        public static NsgSlot CloneSlot(NsgSlot s)
        {
            return s == null ? null : new NsgSlot(s.link, s.text);
        }

        public static NsgFileModel CloneModel(NsgFileModel m)
        {
            if (m == null) return null;
            var c = new NsgFileModel();
            c.schemaVersion = m.schemaVersion;
            c.target = m.target;
            c.engineMin = m.engineMin;
            c.sourceFile = m.sourceFile;
            c.sourceGuid = m.sourceGuid;
            c.epilogue = m.epilogue;
            for (int i = 0; i < m.roots.Count; i++) c.roots.Add(CloneStruct(m.roots[i]));
            return c;
        }

        public static NsgStructNode CloneStruct(NsgStructNode n)
        {
            if (n == null) return null;
            var c = new NsgStructNode();
            c.kind = n.kind;
            c.leading = n.leading;
            c.header = n.header;
            c.headerPrefix = n.headerPrefix;
            c.name = n.name;
            c.headerBasePrefix = n.headerBasePrefix;
            c.baseList = n.baseList;
            c.headerSuffix = n.headerSuffix;
            c.typeKeyword = n.typeKeyword;
            c.tail = n.tail;
            c.text = n.text;
            c.printed = n.printed;
            c.line = n.line;
            c.graph = n.graph != null ? CloneGraph(n.graph) : null;
            for (int i = 0; i < n.children.Count; i++) c.children.Add(CloneStruct(n.children[i]));
            return c;
        }
    }
}
