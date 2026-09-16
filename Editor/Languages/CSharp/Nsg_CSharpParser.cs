using System.Collections.Generic;
using System.Text;

namespace NekoScriptGraph
{
    /// <summary>
    /// Разборщик рекурсивного спуска для поддерживаемого подмножества C#.
    ///
    /// Правила проектирования:
    ///   * Никогда не терять исходник. Всё, что подмножество не умеет
    ///     моделировать, становится узлом Raw (аварийный выход) плюс
    ///     диагностика, вместо отказа от всего файла.
    ///   * Каноничность по построению. Лишние скобки отбрасываются: дерево
    ///     и есть группировка, а печататель возвращает минимум скобок.
    ///   * Комментарии и пустые строки запоминаются для каждого оператора
    ///     и выводятся обратно.
    /// </summary>
    public class NsgParser
    {
        static readonly Dictionary<string, int> BinPrec = new Dictionary<string, int>
        {
            { "??", 1 },
            { "||", 2 },
            { "or", 2 },
            { "&&", 3 },
            { "and", 3 },
            { "|", 4 },
            { "^", 5 },
            { "&", 6 },
            { "==", 7 }, { "!=", 7 },
            { "<", 8 }, { ">", 8 }, { "<=", 8 }, { ">=", 8 }, { "is", 8 }, { "as", 8 },
            { "<<", 9 }, { ">>", 9 },
            { "+", 10 }, { "-", 10 },
            { "*", 11 }, { "/", 11 }, { "%", 11 }
        };

        readonly List<NsgToken> _t;
        readonly string _src;
        readonly NsgDiagnostics _diag;
        readonly int _end;
        readonly NsgToken _eof;
        readonly Nsg_LanguageProfile _profile;
        int _p;

        public NsgParser(List<NsgToken> tokens, string source, NsgDiagnostics diagnostics, int start, int end)
            : this(tokens, source, diagnostics, start, end, null)
        {
        }

        /// <summary>
        /// Профиль языка задаёт наборы ключевых слов. null — прежнее поведение (C#).
        /// </summary>
        public NsgParser(List<NsgToken> tokens, string source, NsgDiagnostics diagnostics,
                         int start, int end, Nsg_LanguageProfile profile)
        {
            _t = tokens;
            _src = source ?? string.Empty;
            _diag = diagnostics;
            _p = start;
            _end = end < 0 || end > tokens.Count ? tokens.Count : end;
            _profile = profile;
            _eof = new NsgToken
            {
                Kind = NsgTokenKind.EOF,
                Text = "<eof>",
                Line = tokens.Count > 0 ? tokens[tokens.Count - 1].Line : 1,
                Col = 1,
                Start = _src.Length,
                End = _src.Length,
                Leading = string.Empty,
                Trailing = string.Empty
            };
        }

        // ------------------------------------------------------------------
        // Курсор
        // ------------------------------------------------------------------

        NsgToken Cur
        {
            get { return _p < _end ? _t[_p] : _eof; }
        }

        NsgToken Peek(int n)
        {
            int j = _p + n;
            return (j >= 0 && j < _end) ? _t[j] : _eof;
        }

        bool At(string text)
        {
            return _p < _end && _t[_p].Kind != NsgTokenKind.EOF && _t[_p].Text == text;
        }

        bool AtAny(params string[] texts)
        {
            for (int i = 0; i < texts.Length; i++)
            {
                if (At(texts[i])) return true;
            }
            return false;
        }

        void Next()
        {
            if (_p < _end) _p++;
        }

        bool Expect(string text)
        {
            if (At(text))
            {
                Next();
                return true;
            }
            _diag.Error(NsgCodes.OutOfSubset,
                Nsg_L10n.T("msg.expect", text, Cur.Text), Cur.Line, Cur.Col);
            return false;
        }

        bool ExpectGreater()
        {
            if (At(">"))
            {
                Next();
                return true;
            }
            if (At(">>"))
            {
                SplitToken(2, ">");
                return true;
            }
            if (At(">="))
            {
                SplitToken(2, "=");
                return true;
            }
            if (At(">>="))
            {
                SplitToken(3, ">=");
                return true;
            }
            _diag.Error(NsgCodes.OutOfSubset, Nsg_L10n.T("msg.expect", ">", Cur.Text), Cur.Line, Cur.Col);
            return false;
        }

        void SplitToken(int consumed, string remainder)
        {
            var t = _t[_p];
            var nt = t;
            nt.Text = remainder;
            nt.Start = t.Start + (consumed - remainder.Length);
            _t[_p] = nt;
        }

        string TextSpan(int startTok, int endTok)
        {
            if (startTok >= endTok) return string.Empty;
            int a = _t[startTok].Start;
            int b = _t[endTok - 1].End;
            if (a < 0) a = 0;
            if (b > _src.Length) b = _src.Length;
            if (b <= a) return string.Empty;
            return _src.Substring(a, b - a);
        }

        // ------------------------------------------------------------------
        // Точка входа
        // ------------------------------------------------------------------

        public NsgMethodBody ParseMethodBody()
        {
            var body = new NsgMethodBody();
            while (_p < _end)
            {
                int before = _p;
                var s = ParseStatement();
                if (s != null) body.Statements.Add(s);
                if (_p == before) Next(); // hard guard against a stall
            }
            return body;
        }

        // ------------------------------------------------------------------
        // Операторы
        // ------------------------------------------------------------------

        public NsgStmt ParseStatement()
        {
            // Язык без обязательных ';' (Go): невидимая точка с запятой в конце
            // строки — разделитель, а не пустой оператор. Пропускаем молча,
            // иначе после каждого блока появлялся бы лишний ";" в выводе.
            if (AutoSemicolon && At(";"))
            {
                Next();
                return null;
            }

            int line = Cur.Line;
            int col = Cur.Col;
            string leading = Cur.Leading;
            int saveDiag = _diag.Count;
            int savePos = _p;

            NsgStmt stmt = ParseStatementCore();

            if (stmt == null)
            {
                // Откатываемся, чтобы аварийный выход захватил оператор целиком,
                // а не только ту часть, которую успела съесть неудачная попытка.
                _p = savePos;
                _diag.Truncate(saveDiag);
                stmt = CaptureRawStatement();
                if (stmt == null) return null;
                _diag.Warn(NsgCodes.OutOfSubset, Nsg_L10n.T("msg.outOfSubset"), line, col);
            }

            stmt.Line = line;
            stmt.Col = col;
            var comments = NsgTrivia.ExtractComments(leading);
            stmt.Comments = comments.Count == 0 ? null : string.Join("\n", comments);
            stmt.BlankBefore = NsgTrivia.CountBlankLines(leading);
            return stmt;
        }

        NsgStmt ParseStatementCore()
        {
            if (At("{")) return ParseBlock();

            if (At("if")) return ParseIf();
            if (At("while")) return ParseWhile();
            if (At("for")) return ParseFor();
            if (At("foreach")) return ParseForEach();
            if (At("return")) return ParseReturn();

            if (At("break"))
            {
                Next();
                Expect(";");
                return new NsgBreakStmt();
            }
            if (At("continue"))
            {
                Next();
                Expect(";");
                return new NsgContinueStmt();
            }

            if (At(";"))
            {
                Next();
                return new NsgRawStmt { Text = ";" };
            }

            if (At("[") || IsUnsupportedStatementStart(Cur.Text))
                return null;

            NsgStmt decl;
            if (TryParseLocalDecl(out decl)) return decl;

            return ParseExprStatement();
        }

        static bool IsUnsupportedStatementStart(string s)
        {
            switch (s)
            {
                case "do":
                case "switch":
                case "try":
                case "using":
                case "lock":
                case "throw":
                case "goto":
                case "yield":
                case "await":
                case "unsafe":
                case "fixed":
                case "checked":
                case "unchecked":
                case "const":
                case "else":
                case "case":
                case "default":
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Слово, с которого начинается оператор. Набор берётся из профиля языка,
        /// поэтому C, C++, Java и HLSL обслуживает один и тот же разборщик.
        /// </summary>
        bool IsStatementKeyword(string s)
        {
            if (_profile != null) return _profile.IsStatementKeyword(s);
            return DefaultStatementKeyword(s);
        }

        static bool DefaultStatementKeyword(string s)
        {
            switch (s)
            {
                case "if":
                case "else":
                case "for":
                case "foreach":
                case "while":
                case "do":
                case "switch":
                case "case":
                case "default":
                case "break":
                case "continue":
                case "return":
                case "throw":
                case "try":
                case "catch":
                case "finally":
                case "using":
                case "lock":
                case "goto":
                case "yield":
                case "await":
                case "new":
                case "sizeof":
                case "stackalloc":
                case "delegate":
                case "in":
                case "is":
                case "as":
                case "out":
                case "ref":
                case "params":
                    return true;
            }
            return false;
        }

        NsgBlockStmt ParseBlock()
        {
            var blk = new NsgBlockStmt { Line = Cur.Line, Col = Cur.Col };
            Expect("{");
            while (!At("}") && _p < _end)
            {
                int before = _p;
                var s = ParseStatement();
                if (s != null) blk.Statements.Add(s);
                if (_p == before) Next(); // hard guard against a stall
            }
            Expect("}");
            return blk;
        }

        bool ParenlessConditions
        {
            get { return _profile != null && _profile.ParenlessConditions; }
        }

        /// <summary>
        /// Язык сам вставляет ';' в конце строки (Go). Разборщику это нужно,
        /// чтобы отличать настоящий пустой оператор от разделителя.
        /// </summary>
        bool AutoSemicolon
        {
            get { return _profile != null && _profile.AutoSemicolon; }
        }

        /// <summary>Условие оператора: "if (x)" или "if x" (Rust, Python).</summary>
        bool ParseCondition(out NsgExpr cond)
        {
            cond = null;

            if (ParenlessConditions)
            {
                if (At("("))
                {
                    Next();
                    cond = ParseExpression();
                    return Expect(")");
                }

                cond = ParseExpression();
                return cond != null;
            }

            if (!Expect("(")) return false;
            cond = ParseExpression();
            return Expect(")");
        }

        NsgStmt ParseIf()
        {
            int line = Cur.Line, col = Cur.Col;
            Next();

            NsgExpr cond;
            if (!ParseCondition(out cond)) return null;

            var then = ParseStatement();
            NsgStmt els = null;
            if (At("else"))
            {
                Next();
                els = ParseStatement();
            }
            return new NsgIfStmt { Cond = cond, Then = then, Else = els, Line = line, Col = col };
        }

        NsgStmt ParseWhile()
        {
            int line = Cur.Line, col = Cur.Col;
            Next();

            NsgExpr cond;
            if (!ParseCondition(out cond)) return null;

            var body = ParseStatement();
            return new NsgWhileStmt { Cond = cond, Body = body, Line = line, Col = col };
        }

        NsgStmt ParseFor()
        {
            int line = Cur.Line, col = Cur.Col;
            Next();

            // Go пишет цикл без круглых скобок и в трёх формах. Выбор формы
            // делается по тому, что стоит до '{', а не по скобкам.
            if (Parenless) return ParseForParenless(line, col);

            if (!Expect("(")) return null;

            NsgStmt init = null;
            if (!At(";"))
            {
                if (!TryParseForInit(out init))
                {
                    int s0 = _p;
                    while (!At(";") && _p < _end) Next();
                    init = new NsgRawStmt { Text = TextSpan(s0, _p) };
                }
            }
            Expect(";");

            NsgExpr cond = null;
            if (!At(";")) cond = ParseExpression();
            Expect(";");

            var incr = new List<NsgExpr>();
            if (!At(")"))
            {
                while (true)
                {
                    var e = ParseExpression();
                    if (e == null) break;
                    incr.Add(e);
                    if (At(","))
                    {
                        Next();
                        continue;
                    }
                    break;
                }
            }
            Expect(")");

            var body = ParseStatement();
            return new NsgForStmt
            {
                Init = init,
                Cond = cond,
                Incr = incr,
                Body = body,
                Line = line,
                Col = col
            };
        }

        /// <summary>
        /// Цикл без круглых скобок: "for {", "for cond {",
        /// "for init; cond; incr {".
        ///
        /// Первая часть разбирается как объявление, а если не вышло — как
        /// выражение. Именно так короткое объявление Go ("i := 0") попадает
        /// в заголовок цикла: ":=" — оператор присваивания, а не отдельная
        /// конструкция.
        /// </summary>
        NsgStmt ParseForParenless(int line, int col)
        {
            // "for {" — бесконечный цикл.
            if (At("{"))
            {
                return new NsgForStmt { Body = ParseStatement(), Line = line, Col = col };
            }

            NsgStmt init = null;
            NsgExpr cond = null;
            var incr = new List<NsgExpr>();

            if (!At(";"))
            {
                if (!TryParseForInit(out init))
                {
                    var e = ParseExpression();
                    if (e != null) init = new NsgExprStmt { Expr = e };
                }
            }

            if (At(";"))
            {
                Next();
                if (!At(";")) cond = ParseExpression();
                Expect(";");

                if (!At("{"))
                {
                    while (true)
                    {
                        var e = ParseExpression();
                        if (e == null) break;
                        incr.Add(e);
                        if (At(","))
                        {
                            Next();
                            continue;
                        }
                        break;
                    }
                }
            }
            else if (init is NsgExprStmt)
            {
                // Одночастная форма "for cond {": первая часть оказалась
                // условием, а не инициализацией.
                cond = ((NsgExprStmt)init).Expr;
                init = null;
            }

            // Тело обязано начинаться сразу. Если осталось что-то ещё, форма
            // нам неизвестна ("for k, v := range m", второй счётчик) — уходим
            // в сырой текст, чтобы не выдать неверную модель.
            if (!At("{")) return null;

            return new NsgForStmt
            {
                Init = init,
                Cond = cond,
                Incr = incr,
                Body = ParseStatement(),
                Line = line,
                Col = col
            };
        }

        bool TryParseForInit(out NsgStmt init)
        {
            init = null;
            int savePos = _p;
            int saveDiag = _diag.Count;

            if (!IsStatementKeyword(Cur.Text) &&
                (Cur.Kind == NsgTokenKind.Ident || Cur.Kind == NsgTokenKind.Keyword))
            {
                string type = ParseTypeText();
                if (type != null && Cur.Kind == NsgTokenKind.Ident)
                {
                    string name = Cur.Text;
                    Next();
                    if (At("="))
                    {
                        Next();
                        var v = ParseExpression();
                        if (v != null && At(";"))
                        {
                            init = new NsgLocalDeclStmt
                            {
                                TypeText = type,
                                Name = name,
                                Init = v,
                                IsVar = type == "var"
                            };
                            return true;
                        }
                    }
                    else if (At(";"))
                    {
                        init = new NsgLocalDeclStmt
                        {
                            TypeText = type,
                            Name = name,
                            Init = null,
                            IsVar = type == "var"
                        };
                        return true;
                    }
                }
            }

            _p = savePos;
            _diag.Truncate(saveDiag);

            var e = ParseExpression();
            if (e == null)
            {
                _p = savePos;
                _diag.Truncate(saveDiag);
                return false;
            }
            init = new NsgExprStmt { Expr = e };
            return true;
        }

        NsgStmt ParseForEach()
        {
            int line = Cur.Line, col = Cur.Col;
            Next();

            // Rust: for name in expr { } — тип не пишется.
            if (_profile != null && _profile.ForIn)
            {
                string name = ExpectIdentLike();
                if (name == null) return null;
                if (!Expect("in")) return null;

                var source = ParseExpression();
                if (source == null) return null;

                var forBody = ParseStatement();
                return new NsgForEachStmt
                {
                    TypeText = string.Empty,
                    Name = name,
                    Source = source,
                    Body = forBody,
                    Line = line,
                    Col = col
                };
            }

            if (!Expect("(")) return null;
            string type = ParseTypeText();
            if (type == null) return null;
            if (Cur.Kind != NsgTokenKind.Ident)
            {
                _diag.Error(NsgCodes.OutOfSubset, Nsg_L10n.T("msg.foreachName"), Cur.Line, Cur.Col);
                return null;
            }
            string itName = Cur.Text;
            Next();
            if (!Expect("in")) return null;
            var src = ParseExpression();
            if (!Expect(")")) return null;
            var body = ParseStatement();
            return new NsgForEachStmt
            {
                TypeText = type,
                Name = itName,
                Source = src,
                Body = body,
                Line = line,
                Col = col
            };
        }

        NsgStmt ParseReturn()
        {
            int line = Cur.Line, col = Cur.Col;
            Next();
            NsgExpr v = null;
            if (!At(";")) v = ParseExpression();
            Expect(";");
            return new NsgReturnStmt { Value = v, Line = line, Col = col };
        }

        bool TryParseLocalDecl(out NsgStmt result)
        {
            result = null;
            if (IsStatementKeyword(Cur.Text)) return false;
            if (Cur.Kind != NsgTokenKind.Ident && Cur.Kind != NsgTokenKind.Keyword) return false;

            int savePos = _p;
            int saveDiag = _diag.Count;

            string type = ParseTypeText();
            if (type == null || Cur.Kind != NsgTokenKind.Ident)
            {
                _p = savePos;
                _diag.Truncate(saveDiag);
                return false;
            }

            // Rust: "let mut x = ..." — mut это часть объявления, а не имя.
            if (_profile != null && _profile.LetBinding && type == "let" && Cur.Text == "mut")
            {
                type = "let mut";
                Next();

                if (Cur.Kind != NsgTokenKind.Ident)
                {
                    _p = savePos;
                    _diag.Truncate(saveDiag);
                    return false;
                }
            }

            string name = Cur.Text;
            Next();

            if (At("="))
            {
                Next();
                var init = ParseExpression();
                if (init == null || !At(";"))
                {
                    _p = savePos;
                    _diag.Truncate(saveDiag);
                    return false;
                }
                Next(); // ;
                result = new NsgLocalDeclStmt
                {
                    TypeText = type,
                    Name = name,
                    Init = init,
                    IsVar = type == "var"
                };
                return true;
            }

            if (At(";"))
            {
                Next();
                result = new NsgLocalDeclStmt
                {
                    TypeText = type,
                    Name = name,
                    Init = null,
                    IsVar = type == "var"
                };
                return true;
            }

            _p = savePos;
            _diag.Truncate(saveDiag);
            return false;
        }

        NsgStmt ParseExprStatement()
        {
            var e = ParseExpression();
            if (e == null) return null;
            if (!Expect(";")) return null;
            return new NsgExprStmt { Expr = e };
        }

        NsgStmt CaptureRawStatement()
        {
            int startTok = _p;
            int depth = 0;

            while (_p < _end)
            {
                var t = Cur;
                if (t.Kind == NsgTokenKind.Punct)
                {
                    string s = t.Text;
                    if (s == "(" || s == "[")
                    {
                        depth++;
                    }
                    else if (s == ")" || s == "]")
                    {
                        if (depth > 0) depth--;
                    }
                    else if (s == "{")
                    {
                        if (depth == 0)
                        {
                            SkipBalanced("{", "}");
                            break;
                        }
                        depth++;
                    }
                    else if (s == "}")
                    {
                        if (depth == 0) break;
                        depth--;
                    }
                    else if (s == ";" && depth == 0)
                    {
                        Next();
                        break;
                    }
                }
                Next();
            }

            string text = TextSpan(startTok, _p);
            if (string.IsNullOrEmpty(text)) return null;
            return new NsgRawStmt { Text = text.Trim() };
        }

        void SkipBalanced(string open, string close)
        {
            if (!Expect(open)) return;
            int depth = 1;
            while (_p < _end && depth > 0)
            {
                if (At(open)) depth++;
                else if (At(close)) depth--;
                Next();
            }
        }

        // ------------------------------------------------------------------
        // Выражения
        // ------------------------------------------------------------------

        public NsgExpr ParseExpression()
        {
            return ParseAssignment();
        }

        bool IsAssignOp(string s)
        {
            // ":=" — короткое объявление Go. У остальных языков двоеточие и
            // равно никогда не стоят рядом, поэтому флаг выключен по умолчанию.
            if (s == ":=") return _profile != null && _profile.ShortDecl;

            switch (s)
            {
                case "=":
                case "+=":
                case "-=":
                case "*=":
                case "/=":
                case "%=":
                case "&=":
                case "|=":
                case "^=":
                case "<<=":
                case ">>=":
                case "??=":
                    return true;
            }
            return false;
        }

        NsgExpr ParseAssignment()
        {
            var left = ParseConditional();
            if (left == null) return null;

            if (Cur.Kind == NsgTokenKind.Punct && IsAssignOp(Cur.Text))
            {
                string op = Cur.Text;
                int line = Cur.Line, col = Cur.Col;
                Next();
                var right = ParseAssignment();
                if (right == null) return left;
                return new NsgAssignExpr
                {
                    Op = op,
                    Target = left,
                    Value = right,
                    Line = line,
                    Col = col
                };
            }
            return left;
        }

        NsgExpr ParseConditional()
        {
            var cond = ParseBinary(0);
            if (cond == null) return null;

            if (At("?"))
            {
                int line = Cur.Line, col = Cur.Col;
                Next();
                var then = ParseAssignment();
                Expect(":");
                var els = ParseAssignment();
                if (then == null || els == null) return cond;
                return new NsgConditionalExpr
                {
                    Cond = cond,
                    Then = then,
                    Else = els,
                    Line = line,
                    Col = col
                };
            }
            return cond;
        }

        NsgExpr ParseBinary(int minPrec)
        {
            var left = ParseUnary();
            if (left == null) return null;

            while (_p < _end)
            {
                var t = Cur;
                if (t.Kind != NsgTokenKind.Punct && t.Kind != NsgTokenKind.Keyword) break;

                int prec;
                if (!BinPrec.TryGetValue(t.Text, out prec)) break;
                if (prec < minPrec) break;

                string op = t.Text;
                int line = t.Line, col = t.Col;
                Next();

                bool rightAssoc = op == "??";
                var right = ParseBinary(rightAssoc ? prec : prec + 1);
                if (right == null) break;

                left = new NsgBinaryExpr
                {
                    Op = op,
                    Left = left,
                    Right = right,
                    Line = line,
                    Col = col
                };
            }
            return left;
        }

        NsgExpr ParseUnary()
        {
            var t = Cur;

            // Python: "not x" — унарный оператор, записанный словом.
            if (t.Kind == NsgTokenKind.Keyword && t.Text == "not")
            {
                Next();
                var notOperand = ParseUnary();
                if (notOperand == null) return null;
                return new NsgUnaryExpr { Op = "not ", Operand = notOperand, Line = t.Line, Col = t.Col };
            }

            if (t.Kind == NsgTokenKind.Punct)
            {
                string op = t.Text;
                if (op == "!" || op == "-" || op == "+" || op == "~" || op == "++" || op == "--")
                {
                    Next();
                    var operand = ParseUnary();
                    if (operand == null) return null;
                    return new NsgUnaryExpr { Op = op, Operand = operand, Line = t.Line, Col = t.Col };
                }
            }

            if (At("(") && LooksLikeCast())
            {
                int line = t.Line, col = t.Col;
                Next();
                string type = ParseTypeText();
                Expect(")");
                var operand = ParseUnary();
                if (type == null) return operand;
                if (operand == null) return null;
                return new NsgCastExpr { TypeText = type, Operand = operand, Line = line, Col = col };
            }

            return ParsePostfix();
        }

        bool LooksLikeCast()
        {
            int depth = 0;
            int i = _p;
            for (; i < _end; i++)
            {
                var tk = _t[i];
                if (tk.Kind != NsgTokenKind.Punct) continue;
                string s = tk.Text;
                if (s == "(") depth++;
                else if (s == ")")
                {
                    depth--;
                    if (depth == 0) break;
                }
                else if (s == ";" || s == "{" || s == "}" || s == "=") return false;
            }
            if (i >= _end) return false;
            if (i == _p + 1) return false; // "()" is not a cast

            bool primitiveType = false;
            for (int k = _p + 1; k < i; k++)
            {
                var tk = _t[k];
                if (tk.Kind == NsgTokenKind.Ident)
                {
                    if (k != _p + 1) continue;
                    continue;
                }
                if (tk.Kind == NsgTokenKind.Keyword)
                {
                    if (IsPrimitiveTypeKeyword(tk.Text) && k == _p + 1) primitiveType = true;
                    continue;
                }
                if (tk.Kind == NsgTokenKind.Punct)
                {
                    string s = tk.Text;
                    if (s == "." || s == "<" || s == ">" || s == ">>" || s == "[" || s == "]" ||
                        s == "," || s == "?" || s == "*")
                        continue;
                    return false;
                }
                return false;
            }

            var next = _t[i + 1];
            if (next.Kind == NsgTokenKind.Ident ||
                next.Kind == NsgTokenKind.Number ||
                next.Kind == NsgTokenKind.String ||
                next.Kind == NsgTokenKind.Char)
                return true;

            if (next.Kind == NsgTokenKind.Keyword)
            {
                if (IsPrimitiveTypeKeyword(next.Text)) return true;
                switch (next.Text)
                {
                    case "this":
                    case "base":
                    case "new":
                    case "typeof":
                    case "default":
                    case "true":
                    case "false":
                    case "null":
                        return true;
                }
                return false;
            }

            if (next.Kind == NsgTokenKind.Punct)
            {
                string s = next.Text;
                if (s == "(" || s == "!" || s == "~") return true;
                if (primitiveType && (s == "+" || s == "-" || s == "++" || s == "--")) return true;
            }
            return false;
        }

        /// <summary>Имя встроенного типа. Набор берётся из профиля языка.</summary>
        bool IsPrimitiveTypeKeyword(string s)
        {
            if (_profile != null) return _profile.IsTypeKeyword(s);
            return DefaultPrimitiveTypeKeyword(s);
        }

        static bool DefaultPrimitiveTypeKeyword(string s)
        {
            switch (s)
            {
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "short":
                case "ushort":
                case "byte":
                case "sbyte":
                case "float":
                case "double":
                case "decimal":
                case "bool":
                case "char":
                case "string":
                case "object":
                case "void":
                case "var":
                    return true;
            }
            return false;
        }

        NsgExpr ParsePostfix()
        {
            var e = ParsePrimary();
            if (e == null) return null;

            while (_p < _end)
            {
                if (At("."))
                {
                    Next();
                    string name = ExpectIdentLike();
                    if (name == null) return e;
                    e = new NsgMemberExpr { Target = e, Name = name };
                    continue;
                }
                if (At("("))
                {
                    var args = ParseDelimitedList("(", ")");
                    if (args == null) return e;
                    e = new NsgCallExpr { Target = e, Args = args };
                    continue;
                }
                if (At("["))
                {
                    var args = ParseDelimitedList("[", "]");
                    if (args == null) return e;
                    e = new NsgIndexExpr { Target = e, Args = args };
                    continue;
                }
                if (At("++") || At("--"))
                {
                    string op = Cur.Text;
                    Next();
                    e = new NsgPostfixExpr { Operand = e, Op = op };
                    continue;
                }
                if (At("->"))
                {
                    Next();
                    string name = ExpectIdentLike();
                    if (name == null) return e;
                    e = new NsgMemberExpr { Target = e, Name = name };
                    continue;
                }
                break;
            }
            return e;
        }

        NsgExpr ParsePrimary()
        {
            var t = Cur;

            if (t.Kind == NsgTokenKind.Number || t.Kind == NsgTokenKind.String || t.Kind == NsgTokenKind.Char)
            {
                Next();
                return new NsgLiteralExpr { Text = t.Text, Line = t.Line, Col = t.Col };
            }

            if (t.Kind == NsgTokenKind.Keyword)
            {
                if (t.Text == "true" || t.Text == "false" || t.Text == "null")
                {
                    Next();
                    return new NsgLiteralExpr { Text = t.Text, Line = t.Line, Col = t.Col };
                }
                if (t.Text == "this" || t.Text == "base")
                {
                    Next();
                    return new NsgIdentExpr { Name = t.Text, Line = t.Line, Col = t.Col };
                }
                if (t.Text == "new") return ParseNew();
                if (t.Text == "sizeof") return ParseSizeof();
                return null;
            }

            if (t.Kind == NsgTokenKind.Ident)
            {
                Next();
                return new NsgIdentExpr { Name = t.Text, Line = t.Line, Col = t.Col };
            }

            if (At("("))
            {
                Next();
                var inner = ParseExpression();
                Expect(")");
                return inner;
            }

            return null;
        }

        /// <summary>
        /// sizeof(x) и sizeof(int).
        ///
        /// Аргумент-тип нельзя разобрать как выражение, поэтому он сохраняется
        /// сырым фрагментом ВНУТРИ вызова. Так sizeof остаётся полноценным
        /// блоком вызова и переживает круговое преобразование.
        /// </summary>
        NsgExpr ParseSizeof()
        {
            var kw = Cur;
            Next();

            var call = new NsgCallExpr
            {
                Target = new NsgIdentExpr { Name = "sizeof", Line = kw.Line, Col = kw.Col },
                Line = kw.Line,
                Col = kw.Col
            };

            if (!At("(")) return call;
            Next();

            int save = _p;
            var inner = ParseExpression();

            if (inner == null)
            {
                _p = save;
                int start = _p;
                while (_p < _end && !At(")")) Next();
                inner = new NsgRawExpr { Text = TextSpan(start, _p) };
            }

            Expect(")");
            if (inner != null) call.Args.Add(inner);
            return call;
        }

        NsgExpr ParseNew()
        {
            int line = Cur.Line, col = Cur.Col;
            Next(); // new

            string type = ParseTypeText();
            if (type == null) return null;

            var e = new NsgNewExpr { TypeText = type, Line = line, Col = col };

            if (At("[")) return null; // array creation is not modelled yet

            if (At("("))
            {
                var args = ParseDelimitedList("(", ")");
                if (args == null) return null;
                e.Args = args;
            }

            if (At("{"))
            {
                Next();
                while (!At("}") && _p < _end)
                {
                    if (Cur.Kind == NsgTokenKind.Ident && Peek(1).Text == "=")
                    {
                        string name = Cur.Text;
                        Next();
                        Next();
                        var v = ParseExpression();
                        e.InitializerNames.Add(name);
                        e.Initializers.Add(v);
                    }
                    else
                    {
                        var v = ParseExpression();
                        e.InitializerNames.Add(null);
                        e.Initializers.Add(v);
                    }
                    if (At(","))
                    {
                        Next();
                        continue;
                    }
                    break;
                }
                Expect("}");
            }

            return e;
        }

        List<NsgExpr> ParseDelimitedList(string open, string close)
        {
            var list = new List<NsgExpr>();
            if (!Expect(open)) return null;

            if (At(close))
            {
                Next();
                return list;
            }

            while (_p < _end)
            {
                var a = ParseExpression();
                if (a == null) return null;
                list.Add(a);

                if (At(","))
                {
                    Next();
                    continue;
                }
                break;
            }

            if (!Expect(close)) return null;
            return list;
        }

        string ExpectIdentLike()
        {
            if (Cur.Kind == NsgTokenKind.Ident || Cur.Kind == NsgTokenKind.Keyword)
            {
                string s = Cur.Text;
                Next();
                return s;
            }
            _diag.Error(NsgCodes.OutOfSubset, Nsg_L10n.T("msg.expectIdent", Cur.Text), Cur.Line, Cur.Col);
            return null;
        }

        // ------------------------------------------------------------------
        // Типы
        // ------------------------------------------------------------------

        public string ParseTypeText()
        {
            var sb = new StringBuilder();
            if (!ParseTypeAtom(sb)) return null;

            while (_p < _end)
            {
                if (At("."))
                {
                    Next();
                    sb.Append('.');
                    if (!ParseTypeAtom(sb)) return null;
                    continue;
                }
                if (At("["))
                {
                    Next();
                    sb.Append('[');
                    if (At(","))
                    {
                        Next();
                        sb.Append(',');
                    }
                    if (!Expect("]")) return null;
                    sb.Append(']');
                    continue;
                }
                if (At("?"))
                {
                    Next();
                    sb.Append('?');
                    continue;
                }
                break;
            }

            return sb.ToString();
        }

        bool ParseTypeAtom(StringBuilder sb)
        {
            if (Cur.Kind != NsgTokenKind.Ident && Cur.Kind != NsgTokenKind.Keyword) return false;
            if (IsStatementKeyword(Cur.Text)) return false;

            sb.Append(Cur.Text);
            Next();

            if (At("<"))
            {
                Next();
                sb.Append('<');
                while (_p < _end)
                {
                    if (!ParseTypeAtom(sb)) return false;
                    if (At(","))
                    {
                        Next();
                        sb.Append(", ");
                        continue;
                    }
                    break;
                }
                if (!ExpectGreater()) return false;
                sb.Append('>');
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Удобство для тестов: разобрать голый фрагмент тела метода.
        // ------------------------------------------------------------------

        public static NsgMethodBody ParseBodyFragment(string bodyText, NsgDiagnostics diagnostics)
        {
            return ParseBodyFragment(bodyText, diagnostics, null);
        }

        public static NsgMethodBody ParseBodyFragment(string bodyText, NsgDiagnostics diagnostics,
                                                      Nsg_LanguageProfile profile)
        {
            string wrapped = "void __nsg__()\n{\n" + (bodyText ?? string.Empty) + "\n}\n";
            var diag = diagnostics ?? new NsgDiagnostics();
            var tokens = new NsgLexer(wrapped, diag, profile).Lex();

            // Находим фигурные скобки тела.
            int open = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Is("{"))
                {
                    open = i;
                    break;
                }
            }
            if (open < 0) return new NsgMethodBody();

            int close = -1;
            int depth = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Is("{")) depth++;
                else if (tokens[i].Is("}"))
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }
            if (close < 0) close = tokens.Count - 1;

            var parser = new NsgParser(tokens, wrapped, diag, open + 1, close, profile);
            return parser.ParseMethodBody();
        }
    }
}
