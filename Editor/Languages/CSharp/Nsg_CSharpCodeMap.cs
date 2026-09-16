using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Переводит между разобранным AST и редактируемым графом блоков.
    ///
    /// Идентификаторы узлов — это структурные пути (s0_, s0_t_1_, ...),
    /// поэтому повторный импорт того же кода даёт те же идентификаторы и
    /// прежнюю раскладку узлов можно переиспользовать. В файле графа лежат
    /// только позиции.
    /// </summary>
    public class NsgCodeMap
    {
        readonly Nsg_BlockLibrary _lib;
        readonly NsgDiagnostics _diag;

        NsgMethodGraph _g;
        NsgMethodGraph _prev;

        public NsgCodeMap(Nsg_BlockLibrary library, NsgDiagnostics diagnostics)
        {
            _lib = library;
            _diag = diagnostics;
        }

        // ==================================================================
        // AST -> граф
        // ==================================================================

        public NsgMethodGraph ToGraph(List<NsgStmt> statements, NsgMethodGraph previous)
        {
            _g = new NsgMethodGraph();
            _prev = previous;
            _g.entry = EmitChain(statements, "s");
            return _g;
        }

        string EmitChain(List<NsgStmt> statements, string path)
        {
            var ids = new List<string>();
            Collect(statements, path, ids);

            for (int i = 0; i < ids.Count - 1; i++)
            {
                var n = _g.Find(ids[i]);
                if (n != null) n.next = ids[i + 1];
            }

            return ids.Count > 0 ? ids[0] : null;
        }

        void Collect(List<NsgStmt> statements, string path, List<string> ids)
        {
            if (statements == null) return;

            for (int i = 0; i < statements.Count; i++)
            {
                var s = statements[i];
                if (s == null) continue;

                string p = path + i + "_";

                // Голый блок — это только группировка: разворачиваем его в родительский список.
                if (s.Kind == NsgStmtKind.Block)
                {
                    var blk = (NsgBlockStmt)s;
                    if (!string.IsNullOrEmpty(s.Comments) &&
                        blk.Statements.Count > 0 &&
                        string.IsNullOrEmpty(blk.Statements[0].Comments))
                    {
                        blk.Statements[0].Comments = s.Comments;
                    }
                    Collect(blk.Statements, p, ids);
                    continue;
                }

                string id = EmitStmt(s, p);
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
        }

        NsgGraphNode NewNode(string block, string id)
        {
            var n = new NsgGraphNode();
            n.id = id;
            n.block = block;

            // Переиспользуем прежнюю раскладку, когда структура не изменилась.
            var p = _prev != null ? _prev.Find(id) : null;
            if (p != null)
            {
                n.x = p.x;
                n.y = p.y;
            }

            _g.nodes.Add(n);
            return n;
        }

        string EmitStmt(NsgStmt s, string path)
        {
            switch (s.Kind)
            {
                case NsgStmtKind.LocalDecl:
                {
                    var d = (NsgLocalDeclStmt)s;
                    var n = NewNode("stmt.localDecl", path);
                    n.EnsureArg(0).text = d.TypeText;
                    n.EnsureArg(1).text = d.Name;
                    n.EnsureArg(2);
                    n.args[2] = EmitExpr(d.Init, path + "v_");
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.Expr:
                {
                    var es = (NsgExprStmt)s;

                    if (es.Expr != null && es.Expr.Kind == NsgExprKind.Assign)
                    {
                        var a = (NsgAssignExpr)es.Expr;
                        string block = AssignBlockFor(a.Op) ?? "stmt.assign";
                        var n = NewNode(block, path);
                        n.EnsureArg(0);
                        n.EnsureArg(1);
                        n.args[0] = EmitExpr(a.Target, path + "t_");
                        n.args[1] = EmitExpr(a.Value, path + "v_");
                        n.comments = s.Comments;
                        n.blankBefore = s.BlankBefore;
                        return n.id;
                    }

                    var en = NewNode("stmt.expr", path);
                    en.EnsureArg(0);
                    en.args[0] = EmitExpr(es.Expr, path + "v_");
                    en.comments = s.Comments;
                    en.blankBefore = s.BlankBefore;
                    return en.id;
                }

                case NsgStmtKind.If:
                {
                    var f = (NsgIfStmt)s;
                    var n = NewNode("stmt.if", path);
                    n.EnsureArg(0);
                    n.args[0] = EmitExpr(f.Cond, path + "c_");
                    n.body = EmitBody(f.Then, path + "t");
                    n.els = EmitBody(f.Else, path + "e");
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.While:
                {
                    var w = (NsgWhileStmt)s;
                    var n = NewNode("stmt.while", path);
                    n.EnsureArg(0);
                    n.args[0] = EmitExpr(w.Cond, path + "c_");
                    n.body = EmitBody(w.Body, path + "w");
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.For:
                {
                    var f = (NsgForStmt)s;
                    var n = NewNode("stmt.for", path);
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.args[0] = EmitInlineStmt(f.Init, path + "i_");
                    n.args[1] = EmitExpr(f.Cond, path + "c_");
                    n.extra = new List<NsgSlot>();
                    for (int k = 0; k < f.Incr.Count; k++)
                    {
                        n.extra.Add(EmitExpr(f.Incr[k], path + "n" + k + "_"));
                    }
                    n.body = EmitBody(f.Body, path + "f");
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.ForEach:
                {
                    var f = (NsgForEachStmt)s;
                    var n = NewNode("stmt.foreach", path);
                    n.EnsureArg(0).text = f.TypeText;
                    n.EnsureArg(1).text = f.Name;
                    n.EnsureArg(2);
                    n.args[2] = EmitExpr(f.Source, path + "s_");
                    n.body = EmitBody(f.Body, path + "b");
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.Return:
                {
                    var r = (NsgReturnStmt)s;
                    var n = NewNode("stmt.return", path);
                    n.EnsureArg(0);
                    n.args[0] = EmitExpr(r.Value, path + "v_");
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.Break:
                {
                    var n = NewNode("stmt.break", path);
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.Continue:
                {
                    var n = NewNode("stmt.continue", path);
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }

                case NsgStmtKind.Raw:
                {
                    var r = (NsgRawStmt)s;
                    var n = NewNode("stmt.raw", path);
                    n.EnsureArg(0).text = r.Text;
                    n.comments = s.Comments;
                    n.blankBefore = s.BlankBefore;
                    return n.id;
                }
            }

            return null;
        }

        string EmitBody(NsgStmt body, string path)
        {
            if (body == null) return null;
            if (body.Kind == NsgStmtKind.Block)
            {
                return EmitChain(((NsgBlockStmt)body).Statements, path + "_");
            }
            return EmitStmt(body, path + "_");
        }

        NsgSlot EmitInlineStmt(NsgStmt s, string path)
        {
            if (s == null) return new NsgSlot();
            if (s.Kind == NsgStmtKind.Expr)
            {
                return EmitExpr(((NsgExprStmt)s).Expr, path);
            }
            if (s.Kind == NsgStmtKind.LocalDecl)
            {
                var d = (NsgLocalDeclStmt)s;
                var n = NewNode("stmt.localDecl", path);
                n.EnsureArg(0).text = d.TypeText;
                n.EnsureArg(1).text = d.Name;
                n.EnsureArg(2);
                n.args[2] = EmitExpr(d.Init, path + "v_");
                return new NsgSlot(n.id, null);
            }
            if (s.Kind == NsgStmtKind.Raw)
            {
                return new NsgSlot(null, ((NsgRawStmt)s).Text);
            }
            return new NsgSlot();
        }

        NsgSlot EmitExpr(NsgExpr e, string path)
        {
            if (e == null) return new NsgSlot();

            // Листья остаются встроенным текстом, чтобы граф не зарастал
            // узлами с единственным значением.
            if (e.Kind == NsgExprKind.Literal)
                return new NsgSlot(null, ((NsgLiteralExpr)e).Text);
            if (e.Kind == NsgExprKind.Ident)
                return new NsgSlot(null, ((NsgIdentExpr)e).Name);

            string block = BlockForExpr(e);
            if (block == null) return new NsgSlot();

            // API-блоки: статический вызов вроде "Debug.Log" отображается
            // обратно в собственный блок, а не в общий expr.call, поэтому
            // API-блоки переживают круговое преобразование.
            if (e.Kind == NsgExprKind.Call)
            {
                var c0 = (NsgCallExpr)e;
                string tgt = StaticTargetText(c0.Target);
                var api = tgt != null ? _lib.FindByStaticCall(tgt, c0.Args.Count) : null;
                if (api != null) block = api.id;
            }

            var n = NewNode(block, path);

            switch (e.Kind)
            {
                case NsgExprKind.Member:
                {
                    var m = (NsgMemberExpr)e;
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.args[0] = EmitExpr(m.Target, path + "x0_");
                    n.args[1].text = m.Name;
                    break;
                }
                case NsgExprKind.Call:
                {
                    var c = (NsgCallExpr)e;
                    var callDef = _lib.Get(n.block);
                    bool hasTargetSocket = callDef != null && callDef.SocketCount > 0 &&
                                           callDef.sockets[0].name == "target";
                    if (hasTargetSocket)
                    {
                        n.EnsureArg(0);
                        n.args[0] = EmitExpr(c.Target, path + "x0_");
                    }
                    n.extra = new List<NsgSlot>();
                    for (int i = 0; i < c.Args.Count; i++)
                    {
                        n.extra.Add(EmitExpr(c.Args[i], path + "a" + i + "_"));
                    }
                    break;
                }
                case NsgExprKind.Index:
                {
                    var c = (NsgIndexExpr)e;
                    n.EnsureArg(0);
                    n.args[0] = EmitExpr(c.Target, path + "x0_");
                    n.extra = new List<NsgSlot>();
                    for (int i = 0; i < c.Args.Count; i++)
                    {
                        n.extra.Add(EmitExpr(c.Args[i], path + "a" + i + "_"));
                    }
                    break;
                }
                case NsgExprKind.Binary:
                {
                    var b = (NsgBinaryExpr)e;
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.EnsureArg(2);
                    n.args[0] = EmitExpr(b.Left, path + "x0_");
                    n.args[1].text = b.Op;
                    n.args[2] = EmitExpr(b.Right, path + "x1_");
                    break;
                }
                case NsgExprKind.Unary:
                {
                    var u = (NsgUnaryExpr)e;
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.args[0].text = u.Op;
                    n.args[1] = EmitExpr(u.Operand, path + "x0_");
                    break;
                }
                case NsgExprKind.Postfix:
                {
                    var u = (NsgPostfixExpr)e;
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.args[0] = EmitExpr(u.Operand, path + "x0_");
                    n.args[1].text = u.Op;
                    break;
                }
                case NsgExprKind.New:
                {
                    var nn = (NsgNewExpr)e;
                    n.EnsureArg(0);
                    n.args[0].text = nn.TypeText;
                    n.extra = new List<NsgSlot>();
                    for (int i = 0; i < nn.Args.Count; i++)
                    {
                        n.extra.Add(EmitExpr(nn.Args[i], path + "a" + i + "_"));
                    }
                    break;
                }
                case NsgExprKind.Conditional:
                {
                    var q = (NsgConditionalExpr)e;
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.EnsureArg(2);
                    n.args[0] = EmitExpr(q.Cond, path + "x0_");
                    n.args[1] = EmitExpr(q.Then, path + "x1_");
                    n.args[2] = EmitExpr(q.Else, path + "x2_");
                    break;
                }
                case NsgExprKind.Cast:
                {
                    var cd = (NsgCastExpr)e;
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.args[0].text = cd.TypeText;
                    n.args[1] = EmitExpr(cd.Operand, path + "x0_");
                    break;
                }
                case NsgExprKind.Assign:
                {
                    var a = (NsgAssignExpr)e;
                    n.EnsureArg(0);
                    n.EnsureArg(1);
                    n.args[0] = EmitExpr(a.Target, path + "t_");
                    n.args[1] = EmitExpr(a.Value, path + "v_");
                    break;
                }
                case NsgExprKind.Raw:
                {
                    n.EnsureArg(0).text = ((NsgRawExpr)e).Text;
                    break;
                }
            }

            return new NsgSlot(n.id, null);
        }

        static string BlockForExpr(NsgExpr e)
        {
            switch (e.Kind)
            {
                case NsgExprKind.Member: return "expr.member";
                case NsgExprKind.Call: return "expr.call";
                case NsgExprKind.Index: return "expr.index";
                case NsgExprKind.Binary: return "expr.binary";
                case NsgExprKind.Unary: return "expr.unary";
                case NsgExprKind.Postfix: return "expr.postfix";
                case NsgExprKind.New: return "expr.new";
                case NsgExprKind.Conditional: return "expr.conditional";
                case NsgExprKind.Cast: return "expr.cast";
                case NsgExprKind.Raw: return "expr.raw";
                case NsgExprKind.Assign: return AssignBlockFor(((NsgAssignExpr)e).Op);
            }
            return null;
        }

        public static string AssignBlockFor(string op)
        {
            switch (op)
            {
                case "=": return "stmt.assign";
                case "+=": return "stmt.add";
                case "-=": return "stmt.sub";
                case "*=": return "stmt.mul";
                case "/=": return "stmt.div";
                case "%=": return "stmt.mod";
                case "&=": return "stmt.andAssign";
                case "|=": return "stmt.orAssign";
                case "^=": return "stmt.xorAssign";
                case "<<=": return "stmt.shlAssign";
                case ">>=": return "stmt.shrAssign";
                case "??=": return "stmt.coalesceAssign";
            }
            return null;
        }

        // ==================================================================
        // Граф -> AST
        // ==================================================================

        public List<NsgStmt> ToAst(NsgMethodGraph g)
        {
            if (g == null) return new List<NsgStmt>();
            return ToStmtList(g, g.entry);
        }

        List<NsgStmt> ToStmtList(NsgMethodGraph g, string head)
        {
            var list = new List<NsgStmt>();
            var seen = new HashSet<string>();
            string cur = head;

            while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
            {
                var n = g.Find(cur);
                if (n == null) break;

                var s = ToStmt(g, n);
                if (s != null)
                {
                    s.Comments = n.comments;
                    s.BlankBefore = n.blankBefore;
                    list.Add(s);
                }
                cur = n.next;
            }

            return list;
        }

        NsgStmt ToStmt(NsgMethodGraph g, NsgGraphNode n)
        {
            string key = NodeKey(n);

            // Блоки, созданные агентом: нет сопоставителя, только шаблон. Работают в одну сторону.
            if (key == "template")
            {
                return new NsgRawStmt { Text = RenderTemplate(g, n) };
            }

            switch (key)
            {
                case "localDecl":
                {
                    return new NsgLocalDeclStmt
                    {
                        TypeText = Text(n, 0),
                        Name = Text(n, 1),
                        Init = ToExpr(g, n.Arg(2)),
                        IsVar = Text(n, 0) == "var"
                    };
                }
                case "assign":
                {
                    var def = _lib.Get(n.block);
                    string op = (def != null && !string.IsNullOrEmpty(def.op)) ? def.op : "=";
                    return new NsgExprStmt
                    {
                        Expr = new NsgAssignExpr
                        {
                            Op = op,
                            Target = ToExpr(g, n.Arg(0)),
                            Value = ToExpr(g, n.Arg(1))
                        }
                    };
                }
                case "expr":
                    return new NsgExprStmt { Expr = ToExpr(g, n.Arg(0)) };

                case "if":
                    return new NsgIfStmt
                    {
                        Cond = ToExpr(g, n.Arg(0)),
                        Then = new NsgBlockStmt { Statements = ToStmtList(g, n.body) },
                        Else = string.IsNullOrEmpty(n.els)
                            ? null
                            : new NsgBlockStmt { Statements = ToStmtList(g, n.els) }
                    };

                case "while":
                    return new NsgWhileStmt
                    {
                        Cond = ToExpr(g, n.Arg(0)),
                        Body = new NsgBlockStmt { Statements = ToStmtList(g, n.body) }
                    };

                case "for":
                {
                    var f = new NsgForStmt();
                    f.Init = ToInlineStmt(g, n.Arg(0));
                    f.Cond = ToExpr(g, n.Arg(1));
                    f.Incr = ToExprs(g, n.extra);
                    f.Body = new NsgBlockStmt { Statements = ToStmtList(g, n.body) };
                    return f;
                }

                case "foreach":
                    return new NsgForEachStmt
                    {
                        TypeText = Text(n, 0),
                        Name = Text(n, 1),
                        Source = ToExpr(g, n.Arg(2)),
                        Body = new NsgBlockStmt { Statements = ToStmtList(g, n.body) }
                    };

                case "return":
                    return new NsgReturnStmt { Value = ToExpr(g, n.Arg(0)) };

                case "break":
                    return new NsgBreakStmt();

                case "continue":
                    return new NsgContinueStmt();

                case "rawStmt":
                    return new NsgRawStmt { Text = Text(n, 0) };
            }

            _diag.Error(NsgCodes.UnknownBlock,
                Nsg_L10n.T("msg.unknownStmt", n.block));
            return null;
        }

        NsgStmt ToInlineStmt(NsgMethodGraph g, NsgSlot slot)
        {
            if (slot == null) return null;
            if (!string.IsNullOrEmpty(slot.link))
            {
                var n = g.Find(slot.link);
                if (n == null) return null;
                return ToStmt(g, n);
            }
            if (string.IsNullOrEmpty(slot.text)) return null;
            return new NsgRawStmt { Text = slot.text };
        }

        NsgExpr ToExpr(NsgMethodGraph g, NsgSlot slot)
        {
            if (slot == null) return null;
            if (!string.IsNullOrEmpty(slot.link))
            {
                var n = g.Find(slot.link);
                if (n == null) return null;
                return ToExprNode(g, n);
            }
            if (string.IsNullOrEmpty(slot.text)) return null;
            return LeafFromText(slot.text);
        }

        List<NsgExpr> ToExprs(NsgMethodGraph g, List<NsgSlot> slots)
        {
            var list = new List<NsgExpr>();
            if (slots == null) return list;
            for (int i = 0; i < slots.Count; i++)
            {
                var e = ToExpr(g, slots[i]);
                if (e != null) list.Add(e);
            }
            return list;
        }

        NsgExpr ToExprNode(NsgMethodGraph g, NsgGraphNode n)
        {
            string key = NodeKey(n);

            if (key == "template")
            {
                return new NsgRawExpr { Text = RenderTemplate(g, n) };
            }

            // API-блок: восстанавливаем вызов из matchCall (или из явной цели).
            var apiDef = _lib.Get(n.block);
            if (apiDef != null && apiDef.node == "call" && !string.IsNullOrEmpty(apiDef.matchCall))
            {
                NsgExpr apiTarget = null;
                if (apiDef.SocketCount > 0 && apiDef.sockets[0].name == "target")
                {
                    apiTarget = ToExpr(g, n.Arg(0));
                }
                if (apiTarget == null) apiTarget = DottedFromText(apiDef.matchCall);
                return new NsgCallExpr { Target = apiTarget, Args = ToExprs(g, n.extra) };
            }

            switch (key)
            {
                case "literal":
                    return new NsgLiteralExpr { Text = Text(n, 0) };
                case "ident":
                    return new NsgIdentExpr { Name = Text(n, 0) };
                case "member":
                    return new NsgMemberExpr { Target = ToExpr(g, n.Arg(0)), Name = Text(n, 1) };
                case "call":
                    return new NsgCallExpr { Target = ToExpr(g, n.Arg(0)), Args = ToExprs(g, n.extra) };
                case "index":
                    return new NsgIndexExpr { Target = ToExpr(g, n.Arg(0)), Args = ToExprs(g, n.extra) };
                case "binary":
                    return new NsgBinaryExpr
                    {
                        Left = ToExpr(g, n.Arg(0)),
                        Op = Text(n, 1),
                        Right = ToExpr(g, n.Arg(2))
                    };
                case "unary":
                    return new NsgUnaryExpr { Op = Text(n, 0), Operand = ToExpr(g, n.Arg(1)) };
                case "postfix":
                    return new NsgPostfixExpr { Operand = ToExpr(g, n.Arg(0)), Op = Text(n, 1) };
                case "new":
                    return new NsgNewExpr { TypeText = Text(n, 0), Args = ToExprs(g, n.extra) };
                case "conditional":
                    return new NsgConditionalExpr
                    {
                        Cond = ToExpr(g, n.Arg(0)),
                        Then = ToExpr(g, n.Arg(1)),
                        Else = ToExpr(g, n.Arg(2))
                    };
                case "cast":
                    return new NsgCastExpr { TypeText = Text(n, 0), Operand = ToExpr(g, n.Arg(1)) };
                case "rawExpr":
                    return new NsgRawExpr { Text = Text(n, 0) };
                case "assign":
                {
                    var def = _lib.Get(n.block);
                    string op = (def != null && !string.IsNullOrEmpty(def.op)) ? def.op : "=";
                    return new NsgAssignExpr
                    {
                        Op = op,
                        Target = ToExpr(g, n.Arg(0)),
                        Value = ToExpr(g, n.Arg(1))
                    };
                }
            }

            _diag.Error(NsgCodes.UnknownBlock, Nsg_L10n.T("msg.unknownExpr", n.block));
            return null;
        }

        string NodeKey(NsgGraphNode n)
        {
            var def = _lib.Get(n.block);
            if (def == null)
            {
                _diag.Error(NsgCodes.UnknownBlock, Nsg_L10n.T("msg.unknownBlock", n.block));
                return string.Empty;
            }
            if (!string.IsNullOrEmpty(def.node)) return def.node;
            if (!string.IsNullOrEmpty(def.emit)) return "template";
            return def.id;
        }

        /// <summary>
        /// Рендерит блок, состоящий только из шаблона. Именно это позволяет
        /// агенту добавить новый простой блок правкой одного Blocks/*.json:
        /// код движка не нужен, ценой односторонности (при импорте он не
        /// распознаётся).
        /// </summary>
        string RenderTemplate(NsgMethodGraph g, NsgGraphNode n)
        {
            var def = _lib.Get(n.block);
            if (def == null) return string.Empty;

            var printer = new NsgPrinter();
            var values = new string[def.SocketCount];

            for (int i = 0; i < def.SocketCount; i++)
            {
                var sock = def.sockets[i];
                var slot = n.Arg(i);

                if (sock.kind == "text" || sock.kind == "var")
                {
                    values[i] = slot != null ? (slot.text ?? string.Empty) : string.Empty;
                    continue;
                }

                if (sock.variadic)
                {
                    var parts = new List<string>();
                    for (int k = 0; k < n.extra.Count; k++)
                    {
                        var e = ToExpr(g, n.extra[k]);
                        if (e != null) parts.Add(printer.Expr(e));
                    }
                    values[i] = string.Join(", ", parts.ToArray());
                    continue;
                }

                var ex = ToExpr(g, slot);
                values[i] = ex == null ? string.Empty : printer.Expr(ex);
            }

            return def.RenderEmit(values);
        }

        static string Text(NsgGraphNode n, int index)
        {
            var s = n.Arg(index);
            if (s == null) return string.Empty;
            return s.text ?? string.Empty;
        }

        public static bool IsIdentifierLike(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            char c0 = t[0];
            if (!(char.IsLetter(c0) || c0 == '_' || c0 == '@')) return false;
            for (int i = 1; i < t.Length; i++)
            {
                char c = t[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            }
            return true;
        }

        /// <summary>
        /// Точечный текст чисто статической цели ("Debug.Log") или null,
        /// когда цель — что-то другое (экземпляр, индексатор, результат вызова...).
        /// </summary>
        public static string StaticTargetText(NsgExpr e)
        {
            if (e == null) return null;
            if (e.Kind == NsgExprKind.Ident) return ((NsgIdentExpr)e).Name;
            if (e.Kind == NsgExprKind.Member)
            {
                string left = StaticTargetText(((NsgMemberExpr)e).Target);
                if (left == null) return null;
                return left + "." + ((NsgMemberExpr)e).Name;
            }
            return null;
        }

        /// <summary>Превращает "UnityEngine.Debug.Log" в выражение доступа к члену.</summary>
        public static NsgExpr DottedFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = text.Split('.');
            NsgExpr cur = new NsgIdentExpr { Name = parts[0] };
            for (int i = 1; i < parts.Length; i++)
            {
                cur = new NsgMemberExpr { Target = cur, Name = parts[i] };
            }
            return cur;
        }

        static NsgExpr LeafFromText(string t)
        {
            if (IsIdentifierLike(t)) return new NsgIdentExpr { Name = t };
            return new NsgLiteralExpr { Text = t };
        }
    }
}
