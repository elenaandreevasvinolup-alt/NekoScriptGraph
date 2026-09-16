using System.Collections.Generic;

namespace NekoScriptGraph
{
    // ---------------------------------------------------------------------
    // Выражения
    // ---------------------------------------------------------------------

    public enum NsgExprKind
    {
        Literal,
        Ident,
        Member,
        Call,
        Index,
        Binary,
        Unary,
        Postfix,
        New,
        Assign,
        Conditional,
        Cast,
        Raw
    }

    public abstract class NsgExpr
    {
        public int Line;
        public int Col;
        public abstract NsgExprKind Kind { get; }
    }

    public class NsgLiteralExpr : NsgExpr
    {
        public string Text; // raw literal text, kept verbatim (numbers, strings, true/false/null)
        public override NsgExprKind Kind { get { return NsgExprKind.Literal; } }
    }

    public class NsgIdentExpr : NsgExpr
    {
        public string Name;
        public override NsgExprKind Kind { get { return NsgExprKind.Ident; } }
    }

    public class NsgMemberExpr : NsgExpr
    {
        public NsgExpr Target;
        public string Name;
        public override NsgExprKind Kind { get { return NsgExprKind.Member; } }
    }

    public class NsgCallExpr : NsgExpr
    {
        public NsgExpr Target;
        public List<NsgExpr> Args = new List<NsgExpr>();
        public override NsgExprKind Kind { get { return NsgExprKind.Call; } }
    }

    public class NsgIndexExpr : NsgExpr
    {
        public NsgExpr Target;
        public List<NsgExpr> Args = new List<NsgExpr>();
        public override NsgExprKind Kind { get { return NsgExprKind.Index; } }
    }

    public class NsgBinaryExpr : NsgExpr
    {
        public string Op;
        public NsgExpr Left;
        public NsgExpr Right;
        public override NsgExprKind Kind { get { return NsgExprKind.Binary; } }
    }

    public class NsgUnaryExpr : NsgExpr
    {
        public string Op;
        public NsgExpr Operand;
        public override NsgExprKind Kind { get { return NsgExprKind.Unary; } }
    }

    public class NsgPostfixExpr : NsgExpr
    {
        public NsgExpr Operand;
        public string Op; // ++ or --
        public override NsgExprKind Kind { get { return NsgExprKind.Postfix; } }
    }

    public class NsgNewExpr : NsgExpr
    {
        public string TypeText;
        public List<NsgExpr> Args = new List<NsgExpr>();
        public List<NsgExpr> Initializers = new List<NsgExpr>(); // new Foo { A = 1 }
        public List<string> InitializerNames = new List<string>();
        public override NsgExprKind Kind { get { return NsgExprKind.New; } }
    }

    public class NsgAssignExpr : NsgExpr
    {
        public string Op; // = += -= *= /= %= &= |= ^= <<= >>= ??=
        public NsgExpr Target;
        public NsgExpr Value;
        public override NsgExprKind Kind { get { return NsgExprKind.Assign; } }
    }

    public class NsgConditionalExpr : NsgExpr
    {
        public NsgExpr Cond;
        public NsgExpr Then;
        public NsgExpr Else;
        public override NsgExprKind Kind { get { return NsgExprKind.Conditional; } }
    }

    public class NsgCastExpr : NsgExpr
    {
        public string TypeText;
        public NsgExpr Operand;
        public override NsgExprKind Kind { get { return NsgExprKind.Cast; } }
    }

    /// <summary>Аварийный выход: сырой C#, который модель блоков не понимает.</summary>
    public class NsgRawExpr : NsgExpr
    {
        public string Text;
        public override NsgExprKind Kind { get { return NsgExprKind.Raw; } }
    }

    // ---------------------------------------------------------------------
    // Операторы
    // ---------------------------------------------------------------------

    public enum NsgStmtKind
    {
        Block,
        LocalDecl,
        Expr,
        If,
        While,
        For,
        ForEach,
        Return,
        Break,
        Continue,
        Raw
    }

    public abstract class NsgStmt
    {
        public int Line;
        public int Col;
        public string Comments;   // preserved leading comments, newline separated
        public int BlankBefore;   // preserved blank lines before the statement, 0..2
        public abstract NsgStmtKind Kind { get; }
    }

    public class NsgBlockStmt : NsgStmt
    {
        public List<NsgStmt> Statements = new List<NsgStmt>();
        public override NsgStmtKind Kind { get { return NsgStmtKind.Block; } }
    }

    public class NsgLocalDeclStmt : NsgStmt
    {
        public string TypeText;
        public string Name;
        public NsgExpr Init; // may be null
        public bool IsVar;
        public override NsgStmtKind Kind { get { return NsgStmtKind.LocalDecl; } }
    }

    public class NsgExprStmt : NsgStmt
    {
        public NsgExpr Expr;
        public override NsgStmtKind Kind { get { return NsgStmtKind.Expr; } }
    }

    public class NsgIfStmt : NsgStmt
    {
        public NsgExpr Cond;
        public NsgStmt Then;
        public NsgStmt Else; // may be null
        public override NsgStmtKind Kind { get { return NsgStmtKind.If; } }
    }

    public class NsgWhileStmt : NsgStmt
    {
        public NsgExpr Cond;
        public NsgStmt Body;
        public override NsgStmtKind Kind { get { return NsgStmtKind.While; } }
    }

    public class NsgForStmt : NsgStmt
    {
        public NsgStmt Init;          // may be null; LocalDecl or Expr (no semicolon printed here)
        public NsgExpr Cond;          // may be null
        public List<NsgExpr> Incr = new List<NsgExpr>();
        public NsgStmt Body;
        public override NsgStmtKind Kind { get { return NsgStmtKind.For; } }
    }

    public class NsgForEachStmt : NsgStmt
    {
        public string TypeText;
        public string Name;
        public NsgExpr Source;
        public NsgStmt Body;
        public override NsgStmtKind Kind { get { return NsgStmtKind.ForEach; } }
    }

    public class NsgReturnStmt : NsgStmt
    {
        public NsgExpr Value; // may be null
        public override NsgStmtKind Kind { get { return NsgStmtKind.Return; } }
    }

    public class NsgBreakStmt : NsgStmt
    {
        public override NsgStmtKind Kind { get { return NsgStmtKind.Break; } }
    }

    public class NsgContinueStmt : NsgStmt
    {
        public override NsgStmtKind Kind { get { return NsgStmtKind.Continue; } }
    }

    /// <summary>Аварийный выход: целый оператор, который модель блоков не понимает.</summary>
    public class NsgRawStmt : NsgStmt
    {
        public string Text;
        public override NsgStmtKind Kind { get { return NsgStmtKind.Raw; } }
    }

    public class NsgMethodBody
    {
        public List<NsgStmt> Statements = new List<NsgStmt>();
    }
}
