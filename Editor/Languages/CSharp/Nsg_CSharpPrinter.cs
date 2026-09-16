using System.Collections.Generic;
using System.Text;

namespace NekoScriptGraph
{
    /// <summary>
    /// Канонический печататель для поддерживаемого подмножества C#.
    ///
    /// Именно каноническая форма делает круговое преобразование устойчивым:
    ///   * ровно одна поверхностная форма на конструкцию
    ///   * минимум скобок, выведенный из таблицы приоритетов
    ///   * тела управления всегда в фигурных скобках (нет неоднозначности dangling-else)
    ///   * отступ 4 пробела, скобки в стиле Allman
    ///
    /// Поскольку печататель никогда не выводит лишнюю скобку, а разборщик
    /// её никогда не сохраняет, P(R(P(b))) == P(b) выполняется по построению.
    /// </summary>
    public class NsgPrinter
    {
        public string IndentUnit = "    ";

        /// <summary>
        /// Профиль языка. Нужен для немногих синтаксических отличий:
        /// условия без скобок (Rust) и цикл "for x in y" вместо foreach.
        /// </summary>
        public Nsg_LanguageProfile Profile;

        bool Parenless
        {
            get { return Profile != null && Profile.ParenlessConditions; }
        }

        /// <summary>
        /// Завершитель оператора. У языка с автоматической ';' (Go) его нет:
        /// разделителем служит перевод строки, и лишняя ';' в выводе — это
        /// уже не тот код, что был на входе.
        /// </summary>
        string Term
        {
            get { return Profile != null && Profile.AutoSemicolon ? string.Empty : ";"; }
        }

        /// <summary>
        /// Открывающая скобка тела стоит на строке заголовка (Go, Swift).
        ///
        /// Это не косметика. Там, где конец строки сам закрывает оператор,
        /// "if x" и "{", разнесённые по строкам, дают неразборный код, а "}"
        /// и "else" на разных строках — тоже.
        /// </summary>
        bool SameLineBrace
        {
            get { return Profile != null && Profile.SameLineBrace; }
        }

        /// <summary>Разделитель перед телом: пробел или перевод строки.</summary>
        string BeforeBody
        {
            get { return SameLineBrace ? " " : "\n"; }
        }

        /// <summary>
        /// Закрывает тело. Когда скобка остаётся на строке заголовка,
        /// завершающий перевод строки добавляет вызывающий: за '}' может
        /// последовать 'else', и тогда перевод строки всё сломает.
        /// </summary>
        void EndBody(StringBuilder sb)
        {
            if (SameLineBrace) sb.Append('\n');
        }

        bool ForIn
        {
            get { return Profile != null && Profile.ForIn; }
        }

        // ------------------------------------------------------------------
        // Точки входа
        // ------------------------------------------------------------------

        /// <summary>Печатает всё тело метода вместе со скобками, с заданным базовым отступом.</summary>
        public string PrintMethodBody(List<NsgStmt> statements, string signatureIndent)
        {
            if (signatureIndent == null) signatureIndent = string.Empty;
            var sb = new StringBuilder();
            sb.Append(signatureIndent).Append("{\n");
            sb.Append(PrintStatements(statements, signatureIndent + IndentUnit));
            sb.Append(signatureIndent).Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Печатает тело, которое нужно приписать сразу после сохранённой
        /// сигнатуры. Сигнатура уже несёт отступ перед скобкой, поэтому
        /// скобку нельзя отступать второй раз.
        /// </summary>
        public string PrintMethodBodyRaw(List<NsgStmt> statements, string indent)
        {
            if (indent == null) indent = string.Empty;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append(PrintStatements(statements, indent + IndentUnit));
            // Без завершающего перевода строки: он принадлежит следующему
            // фрагменту файла. С ним сравнение с записанным телом не совпадало
            // бы, и файл сразу помечался бы как «изменённый».
            sb.Append(indent).Append('}');
            return sb.ToString();
        }

        public string PrintStatements(List<NsgStmt> statements, string indent)
        {
            var sb = new StringBuilder();
            if (statements == null) return string.Empty;

            for (int i = 0; i < statements.Count; i++)
            {
                var s = statements[i];
                if (s == null) continue;

                if (i > 0)
                {
                    int blanks = s.BlankBefore;
                    for (int b = 0; b < blanks; b++) sb.Append('\n');
                }
                EmitStatement(sb, s, indent);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Операторы
        // ------------------------------------------------------------------

        void EmitComments(StringBuilder sb, NsgStmt s, string indent)
        {
            if (string.IsNullOrEmpty(s.Comments)) return;
            var lines = s.Comments.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(indent).Append(lines[i]).Append('\n');
            }
        }

        void EmitStatement(StringBuilder sb, NsgStmt s, string indent)
        {
            EmitComments(sb, s, indent);

            switch (s.Kind)
            {
                case NsgStmtKind.LocalDecl:
                {
                    var d = (NsgLocalDeclStmt)s;
                    sb.Append(indent).Append(InlineStatement(d)).Append(Term).Append('\n');
                    break;
                }
                case NsgStmtKind.Expr:
                {
                    sb.Append(indent).Append(Expr(((NsgExprStmt)s).Expr)).Append(Term).Append('\n');
                    break;
                }
                case NsgStmtKind.If:
                {
                    EmitIfChain(sb, (NsgIfStmt)s, indent, false);
                    break;
                }
                case NsgStmtKind.While:
                {
                    var w = (NsgWhileStmt)s;
                    sb.Append(indent).Append("while");
                    sb.Append(Parenless ? " " : " (");
                    sb.Append(Expr(w.Cond));
                    if (!Parenless) sb.Append(')');
                    sb.Append(BeforeBody);
                    EmitBody(sb, w.Body, indent);
                    EndBody(sb);
                    break;
                }
                case NsgStmtKind.For:
                {
                    EmitFor(sb, (NsgForStmt)s, indent);
                    break;
                }
                case NsgStmtKind.ForEach:
                {
                    var fe = (NsgForEachStmt)s;

                    if (ForIn)
                    {
                        sb.Append(indent)
                          .Append("for ").Append(fe.Name)
                          .Append(" in ").Append(Expr(fe.Source));
                    }
                    else
                    {
                        sb.Append(indent)
                          .Append("foreach (").Append(fe.TypeText).Append(' ').Append(fe.Name)
                          .Append(" in ").Append(Expr(fe.Source)).Append(')');
                    }

                    sb.Append(BeforeBody);
                    EmitBody(sb, fe.Body, indent);
                    EndBody(sb);
                    break;
                }
                case NsgStmtKind.Return:
                {
                    var r = (NsgReturnStmt)s;
                    sb.Append(indent).Append("return");
                    if (r.Value != null) sb.Append(' ').Append(Expr(r.Value));
                    sb.Append(Term).Append('\n');
                    break;
                }
                case NsgStmtKind.Break:
                    sb.Append(indent).Append("break").Append(Term).Append('\n');
                    break;
                case NsgStmtKind.Continue:
                    sb.Append(indent).Append("continue").Append(Term).Append('\n');
                    break;
                case NsgStmtKind.Raw:
                    sb.Append(indent).Append(((NsgRawStmt)s).Text).Append('\n');
                    break;
                case NsgStmtKind.Block:
                    sb.Append(indent).Append("{\n");
                    sb.Append(PrintStatements(((NsgBlockStmt)s).Statements, indent + IndentUnit));
                    sb.Append(indent).Append("}\n");
                    break;
            }
        }

        void EmitIfChain(StringBuilder sb, NsgIfStmt s, string indent, bool isElseIf)
        {
            // "} else if" продолжает строку закрывающей скобки, поэтому отступ
            // здесь уже не начало строки.
            if (!isElseIf || !SameLineBrace) sb.Append(indent);

            sb.Append(isElseIf ? "else if" : "if");
            sb.Append(Parenless ? " " : " (");
            sb.Append(Expr(s.Cond));
            if (!Parenless) sb.Append(')');
            sb.Append(BeforeBody);
            EmitBody(sb, s.Then, indent);

            if (s.Else == null)
            {
                EndBody(sb);
                return;
            }

            if (s.Else.Kind == NsgStmtKind.If && string.IsNullOrEmpty(s.Else.Comments))
            {
                EmitIfChain(sb, (NsgIfStmt)s.Else, indent, true);
                return;
            }

            sb.Append(SameLineBrace ? "else " : indent + "else\n");
            EmitBody(sb, s.Else, indent);
            EndBody(sb);
        }

        void EmitFor(StringBuilder sb, NsgForStmt s, string indent)
        {
            // Go пишет цикл без круглых скобок. Одночастная форма — это
            // "for cond {", а не "for ; cond; ".
            if (Parenless)
            {
                bool threePart = s.Init != null || s.Incr.Count > 0;
                if (!threePart)
                {
                    sb.Append(indent).Append("for");
                    if (s.Cond != null) sb.Append(' ').Append(Expr(s.Cond));
                    sb.Append(BeforeBody);
                    EmitBody(sb, s.Body, indent);
                    EndBody(sb);
                    return;
                }
            }

            // Трёхчастный заголовок: ';' здесь часть цикла, а не завершитель
            // оператора, поэтому остаются всегда.
            sb.Append(indent).Append(Parenless ? "for " : "for (");
            sb.Append(InlineStatement(s.Init));
            sb.Append("; ");
            if (s.Cond != null) sb.Append(Expr(s.Cond));
            sb.Append("; ");
            for (int i = 0; i < s.Incr.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Expr(s.Incr[i]));
            }
            sb.Append(Parenless ? BeforeBody : ")\n");
            EmitBody(sb, s.Body, indent);
            EndBody(sb);
        }

        /// <summary>Рендерит оператор без отступа и без завершающей точки с запятой.</summary>
        public string InlineStatement(NsgStmt s)
        {
            if (s == null) return string.Empty;
            switch (s.Kind)
            {
                case NsgStmtKind.LocalDecl:
                {
                    var d = (NsgLocalDeclStmt)s;
                    string t = d.TypeText + " " + d.Name;
                    if (d.Init != null) t += " = " + Expr(d.Init);
                    return t;
                }
                case NsgStmtKind.Expr:
                    return Expr(((NsgExprStmt)s).Expr);
                case NsgStmtKind.Raw:
                    return ((NsgRawStmt)s).Text;
            }
            return string.Empty;
        }

        void EmitBody(StringBuilder sb, NsgStmt body, string indent)
        {
            // Go и Swift: '{' стоит на строке заголовка, поэтому отступ перед
            // ней не нужен, а комментарии тела переезжают ВНУТРЬ скобок —
            // между заголовком и '{' их поставить нельзя.
            if (SameLineBrace)
            {
                sb.Append("{\n");
                if (body != null && !string.IsNullOrEmpty(body.Comments))
                {
                    EmitComments(sb, body, indent + IndentUnit);
                }
            }
            else
            {
                if (body != null && !string.IsNullOrEmpty(body.Comments))
                {
                    EmitComments(sb, body, indent);
                }
                sb.Append(indent).Append("{\n");
            }

            if (body != null)
            {
                if (body.Kind == NsgStmtKind.Block)
                {
                    sb.Append(PrintStatements(((NsgBlockStmt)body).Statements, indent + IndentUnit));
                }
                else
                {
                    EmitStatement(sb, body, indent + IndentUnit);
                }
            }

            // Завершающий перевод строки добавляет вызывающий: за '}' может
            // последовать 'else'.
            sb.Append(indent).Append('}');
            if (!SameLineBrace) sb.Append('\n');
        }

        // ------------------------------------------------------------------
        // Выражения
        // ------------------------------------------------------------------

        public string Expr(NsgExpr e)
        {
            if (e == null) return string.Empty;
            return Expr(e, 0, false);
        }

        string Expr(NsgExpr e, int parentPrec, bool isRightChild)
        {
            int p = Prec(e);
            bool needParen = p < parentPrec ||
                             (p == parentPrec && isRightChild && !RightAssoc(e));
            string body = ExprCore(e);
            return needParen ? "(" + body + ")" : body;
        }

        string ExprCore(NsgExpr e)
        {
            switch (e.Kind)
            {
                case NsgExprKind.Literal:
                    return ((NsgLiteralExpr)e).Text;
                case NsgExprKind.Ident:
                    return ((NsgIdentExpr)e).Name;
                case NsgExprKind.Raw:
                    return ((NsgRawExpr)e).Text;

                case NsgExprKind.Member:
                {
                    var m = (NsgMemberExpr)e;
                    return Expr(m.Target, 16, false) + "." + m.Name;
                }
                case NsgExprKind.Call:
                {
                    var c = (NsgCallExpr)e;
                    return Expr(c.Target, 16, false) + "(" + JoinArgs(c.Args) + ")";
                }
                case NsgExprKind.Index:
                {
                    var ix = (NsgIndexExpr)e;
                    return Expr(ix.Target, 16, false) + "[" + JoinArgs(ix.Args) + "]";
                }
                case NsgExprKind.Postfix:
                {
                    var pf = (NsgPostfixExpr)e;
                    return Expr(pf.Operand, 16, false) + pf.Op;
                }
                case NsgExprKind.New:
                {
                    var n = (NsgNewExpr)e;
                    string s = "new " + n.TypeText;
                    if (n.Initializers.Count == 0)
                    {
                        s += "(" + JoinArgs(n.Args) + ")";
                    }
                    else
                    {
                        if (n.Args.Count > 0) s += "(" + JoinArgs(n.Args) + ")";
                        s += " { ";
                        for (int i = 0; i < n.Initializers.Count; i++)
                        {
                            if (i > 0) s += ", ";
                            string name = n.InitializerNames.Count > i ? n.InitializerNames[i] : null;
                            if (!string.IsNullOrEmpty(name)) s += name + " = ";
                            s += Expr(n.Initializers[i]);
                        }
                        s += " }";
                    }
                    return s;
                }
                case NsgExprKind.Cast:
                {
                    var cd = (NsgCastExpr)e;
                    return "(" + cd.TypeText + ")" + Expr(cd.Operand, 15, true);
                }
                case NsgExprKind.Unary:
                {
                    var u = (NsgUnaryExpr)e;
                    return u.Op + Expr(u.Operand, 15, true);
                }
                case NsgExprKind.Binary:
                {
                    var b = (NsgBinaryExpr)e;
                    int bp = BinPrec(b.Op);
                    return Expr(b.Left, bp, false) + " " + b.Op + " " + Expr(b.Right, bp, true);
                }
                case NsgExprKind.Conditional:
                {
                    var q = (NsgConditionalExpr)e;
                    return Expr(q.Cond, 4, false) + " ? " + Expr(q.Then, 3, false) +
                           " : " + Expr(q.Else, 3, true);
                }
                case NsgExprKind.Assign:
                {
                    var a = (NsgAssignExpr)e;
                    return Expr(a.Target, 2, false) + " " + a.Op + " " + Expr(a.Value, 2, true);
                }
            }
            return string.Empty;
        }

        string JoinArgs(List<NsgExpr> args)
        {
            if (args == null || args.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Expr(args[i]));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Приоритеты
        // ------------------------------------------------------------------

        public static int BinPrec(string op)
        {
            switch (op)
            {
                case "??": return 1;
                case "||": return 2;
                case "or": return 2;
                case "&&": return 3;
                case "and": return 3;
                case "|": return 4;
                case "^": return 5;
                case "&": return 6;
                case "==":
                case "!=": return 7;
                case "<":
                case ">":
                case "<=":
                case ">=":
                case "is":
                case "as": return 8;
                case "<<":
                case ">>": return 9;
                case "+":
                case "-": return 10;
                case "*":
                case "/":
                case "%": return 11;
            }
            return 0;
        }

        public static int Prec(NsgExpr e)
        {
            switch (e.Kind)
            {
                case NsgExprKind.Literal:
                case NsgExprKind.Ident:
                case NsgExprKind.Member:
                case NsgExprKind.Call:
                case NsgExprKind.Index:
                case NsgExprKind.New:
                case NsgExprKind.Postfix:
                case NsgExprKind.Raw:
                    return 16;
                case NsgExprKind.Unary:
                case NsgExprKind.Cast:
                    return 15;
                case NsgExprKind.Binary:
                    return BinPrec(((NsgBinaryExpr)e).Op);
                case NsgExprKind.Conditional:
                    return 3;
                case NsgExprKind.Assign:
                    return 2;
            }
            return 0;
        }

        public static bool RightAssoc(NsgExpr e)
        {
            switch (e.Kind)
            {
                case NsgExprKind.Assign:
                case NsgExprKind.Conditional:
                    return true;
                case NsgExprKind.Binary:
                    return ((NsgBinaryExpr)e).Op == "??";
            }
            return false;
        }
    }
}
