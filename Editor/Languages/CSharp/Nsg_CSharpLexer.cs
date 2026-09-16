using System.Collections.Generic;
using System.Text;

namespace NekoScriptGraph
{
    public enum NsgTokenKind
    {
        Ident,
        Keyword,
        Number,
        String,
        Char,
        Punct,
        EOF
    }

    /// <summary>
    /// Лексема вместе с окружающим её тривиальным текстом, чтобы текст вне
    /// моделируемой области никогда не уничтожался молча. Комментарии и
    /// пустые строки восстанавливаются из <see cref="Leading"/>.
    /// </summary>
    public struct NsgToken
    {
        public NsgTokenKind Kind;
        public string Text;
        public int Line;
        public int Col;
        public int Start;      // inclusive char offset in the source
        public int End;        // exclusive char offset in the source
        public string Leading;  // whitespace + comments before this token
        public string Trailing; // whitespace after this token on the same line

        public bool Is(string text)
        {
            return Kind != NsgTokenKind.EOF && Text == text;
        }

        public bool IsIdent
        {
            get { return Kind == NsgTokenKind.Ident; }
        }

        public override string ToString()
        {
            return Kind + ":" + Text;
        }
    }

    public static class NsgKeywords
    {
        public static readonly HashSet<string> Set = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while", "var", "async", "await", "yield"
        };

        public static bool Is(string s)
        {
            return Set.Contains(s);
        }
    }

    /// <summary>
    /// Лексер, написанный вручную для поддерживаемого подмножества C#. Даёт
    /// список лексем, где каждая знает предшествующий ей тривиальный текст,
    /// поэтому комментарии и пустые строки переживают круговое преобразование.
    /// </summary>
    public class NsgLexer
    {
        static readonly string[] Puncts =
        {
            ">>=", "<<=", "??=", "=>", "==", "!=", "<=", ">=", "&&", "||", "++", "--",
            "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "??", "->", "::", ":=",
            "+", "-", "*", "/", "%", "=", "<", ">", "!", "&", "|", "^", "~", "?", ":",
            ";", ",", ".", "(", ")", "[", "]", "{", "}", "@"
        };

        readonly string _s;
        readonly NsgDiagnostics _diag;
        readonly HashSet<string> _keywords;
        readonly bool _preprocessor;
        readonly bool _autoSemicolon;
        readonly bool _shortDecl;
        int _i;
        int _line = 1;
        int _col = 1;
        bool _lineStart = true;

        public NsgLexer(string source, NsgDiagnostics diagnostics)
            : this(source, diagnostics, null)
        {
        }

        /// <summary>
        /// Профиль языка задаёт набор ключевых слов и наличие препроцессора.
        /// null — прежнее поведение (C#).
        /// </summary>
        public NsgLexer(string source, NsgDiagnostics diagnostics, Nsg_LanguageProfile profile)
        {
            _s = source ?? string.Empty;
            _diag = diagnostics;
            _keywords = profile != null ? profile.Keywords : null;
            _preprocessor = profile == null || profile.Preprocessor;

            // Обе возможности выключены по умолчанию: включённые, они меняют
            // поток лексем, а значит могли бы сдвинуть разбор у C-семейства.
            _autoSemicolon = profile != null && profile.AutoSemicolon;
            _shortDecl = profile != null && profile.ShortDecl;
        }

        bool IsKeyword(string s)
        {
            return _keywords != null ? _keywords.Contains(s) : NsgKeywords.Is(s);
        }

        char Cur
        {
            get { return _i < _s.Length ? _s[_i] : '\0'; }
        }

        char At(int n)
        {
            int j = _i + n;
            return j < _s.Length ? _s[j] : '\0';
        }

        void Adv()
        {
            if (_i >= _s.Length) return;
            char c = _s[_i];
            if (c == '\n')
            {
                _line++;
                _col = 1;
                _lineStart = true;
            }
            else
            {
                _col++;
                if (c != ' ' && c != '\t' && c != '\r') _lineStart = false;
            }
            _i++;
        }

        public List<NsgToken> Lex()
        {
            var outp = new List<NsgToken>();
            string pending = string.Empty;

            while (true)
            {
                string trivia = ReadTrivia();

                // Go: конец строки завершает оператор. Тривиальный текст
                // читается ДО проверки, поэтому перевод строки уже известен.
                bool newline = _autoSemicolon && trivia.IndexOf('\n') >= 0;
                pending += trivia;

                if (_i >= _s.Length)
                {
                    // Файл, кончающийся переводом строки, тоже закрывает
                    // последний оператор.
                    if (newline) InsertVirtualSemicolon(outp);

                    outp.Add(new NsgToken
                    {
                        Kind = NsgTokenKind.EOF,
                        Text = "<eof>",
                        Line = _line,
                        Col = _col,
                        Start = _i,
                        End = _i,
                        Leading = pending,
                        Trailing = string.Empty
                    });
                    break;
                }

                if (newline) InsertVirtualSemicolon(outp);

                var t = ReadToken();
                t.Leading = pending;
                pending = string.Empty;
                outp.Add(t);
            }

            return outp;
        }

        /// <summary>
        /// Невидимая ';' в конце строки (правило Go).
        ///
        /// Токен нулевой ширины и ставится СРАЗУ за предыдущим: перевод строки
        /// и отступ остаются в тривиальном тексте следующего токена. Поэтому
        /// вырезка исходника по границам токенов не сдвигается ни на символ —
        /// сырой текст и комментарии переживают разбор без потерь.
        /// </summary>
        void InsertVirtualSemicolon(List<NsgToken> outp)
        {
            if (outp.Count == 0) return;

            var prev = outp[outp.Count - 1];
            if (!EndsStatement(prev)) return;

            outp.Add(new NsgToken
            {
                Kind = NsgTokenKind.Punct,
                Text = ";",
                Line = prev.Line,
                Col = prev.Col,
                Start = prev.End,
                End = prev.End,
                Leading = string.Empty,
                Trailing = string.Empty
            });
        }

        /// <summary>
        /// Может ли лексема заканчивать оператор. Набор взят из спецификации
        /// Go дословно: идентификатор, литерал, return/break/continue/
        /// fallthrough, ++/--, закрывающие скобки.
        /// </summary>
        static bool EndsStatement(NsgToken t)
        {
            if (t.Kind == NsgTokenKind.Ident) return true;
            if (t.Kind == NsgTokenKind.Number) return true;
            if (t.Kind == NsgTokenKind.String) return true;
            if (t.Kind == NsgTokenKind.Char) return true;

            switch (t.Text)
            {
                case "break":
                case "continue":
                case "fallthrough":
                case "return":
                case "++":
                case "--":
                case ")":
                case "]":
                case "}":
                    return true;
            }
            return false;
        }

        string ReadTrivia()
        {
            int start = _i;
            while (_i < _s.Length)
            {
                char c = Cur;
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    Adv();
                    continue;
                }
                if (c == '/' && At(1) == '/')
                {
                    while (_i < _s.Length && Cur != '\n') Adv();
                    continue;
                }
                if (c == '/' && At(1) == '*')
                {
                    Adv();
                    Adv();
                    while (_i < _s.Length && !(Cur == '*' && At(1) == '/')) Adv();
                    if (_i < _s.Length)
                    {
                        Adv();
                        Adv();
                    }
                    continue;
                }

                // Строка препроцессора (#include, #define, #pragma, #region...)
                // целиком уходит в тривиальный текст. Так директива сохраняется
                // дословно и не попадает в поток лексем, где сломала бы разбор.
                if (_preprocessor && c == '#' && _lineStart)
                {
                    while (_i < _s.Length && Cur != '\n') Adv();
                    continue;
                }

                break;
            }
            return _s.Substring(start, _i - start);
        }

        NsgToken ReadToken()
        {
            int start = _i;
            int line = _line;
            int col = _col;
            char c = Cur;

            // ---- идентификаторы, ключевые слова, verbatim-идентификаторы ----
            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                bool verbatimIdent = c == '@';
                if (verbatimIdent) Adv();
                while (_i < _s.Length && (char.IsLetterOrDigit(Cur) || Cur == '_')) Adv();
                string txt = _s.Substring(start, _i - start);
                NsgTokenKind kind = (!verbatimIdent && IsKeyword(txt))
                    ? NsgTokenKind.Keyword
                    : NsgTokenKind.Ident;
                return Make(kind, txt, start, line, col);
            }

            // ---- числа ----
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(At(1))))
            {
                bool lastWasExp = false;
                while (_i < _s.Length)
                {
                    char n = Cur;
                    if (char.IsLetterOrDigit(n) || n == '.' || n == '_')
                    {
                        lastWasExp = (n == 'e' || n == 'E');
                        Adv();
                        continue;
                    }
                    if (lastWasExp && (n == '+' || n == '-'))
                    {
                        lastWasExp = false;
                        Adv();
                        continue;
                    }
                    break;
                }
                return Make(NsgTokenKind.Number, _s.Substring(start, _i - start), start, line, col);
            }

            // ---- строки (обычные, verbatim, интерполированные) ----
            if (c == '"')
                return ReadQuoted(start, line, col, NsgTokenKind.String, false);

            if (c == '$' && At(1) == '"')
            {
                Adv();
                return ReadQuoted(start, line, col, NsgTokenKind.String, false);
            }

            if (c == '@' && At(1) == '"')
            {
                Adv();
                return ReadQuoted(start, line, col, NsgTokenKind.String, true);
            }

            if (c == '$' && At(1) == '@' && At(2) == '"')
            {
                Adv();
                Adv();
                return ReadQuoted(start, line, col, NsgTokenKind.String, true);
            }

            if (c == '@' && At(1) == '$' && At(2) == '"')
            {
                Adv();
                Adv();
                return ReadQuoted(start, line, col, NsgTokenKind.String, true);
            }

            // ---- символы ----
            if (c == '\'')
                return ReadQuoted(start, line, col, NsgTokenKind.Char, false);

            // ---- пунктуация ----
            for (int k = 0; k < Puncts.Length; k++)
            {
                string p = Puncts[k];

                // ":=" есть только у языков с коротким объявлением (Go).
                // Для остальных двоеточие и равно остаются разными лексемами.
                if (p == ":=" && !_shortDecl) continue;

                if (Matches(p))
                {
                    for (int n = 0; n < p.Length; n++) Adv();
                    return Make(NsgTokenKind.Punct, p, start, line, col);
                }
            }

            // ---- неизвестный символ ----
            _diag.Error(NsgCodes.OutOfSubset, Nsg_L10n.T("msg.lexUnknown", c), line, col);
            Adv();
            return Make(NsgTokenKind.Punct, c.ToString(), start, line, col);
        }

        bool Matches(string p)
        {
            for (int n = 0; n < p.Length; n++)
            {
                if (At(n) != p[n]) return false;
            }
            return true;
        }

        NsgToken ReadQuoted(int start, int line, int col, NsgTokenKind kind, bool verbatim)
        {
            // Текущий символ — открывающая кавычка.
            Adv();
            while (_i < _s.Length)
            {
                char ch = Cur;
                if (verbatim)
                {
                    if (ch == '"')
                    {
                        if (At(1) == '"')
                        {
                            Adv();
                            Adv();
                            continue;
                        }
                        Adv();
                        break;
                    }
                    Adv();
                    continue;
                }

                if (ch == '\\')
                {
                    Adv();
                    if (_i < _s.Length) Adv();
                    continue;
                }
                if (ch == '"' || ch == '\'')
                {
                    Adv();
                    break;
                }
                if (ch == '\n')
                {
                    break; // unterminated; keep going, the parser will complain
                }
                Adv();
            }
            return Make(kind, _s.Substring(start, _i - start), start, line, col);
        }

        NsgToken Make(NsgTokenKind kind, string text, int start, int line, int col)
        {
            return new NsgToken
            {
                Kind = kind,
                Text = text,
                Line = line,
                Col = col,
                Start = start,
                End = _i,
                Trailing = string.Empty
            };
        }
    }

    public static class NsgTrivia
    {
        /// <summary>Вытаскивает текст комментариев из блока тривиального текста, по строке на запись.</summary>
        public static List<string> ExtractComments(string trivia)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(trivia)) return list;

            int i = 0;
            while (i < trivia.Length)
            {
                char c = trivia[i];
                if (c == '/' && i + 1 < trivia.Length && trivia[i + 1] == '/')
                {
                    int e = trivia.IndexOf('\n', i);
                    if (e < 0) e = trivia.Length;
                    list.Add(trivia.Substring(i, e - i).TrimEnd('\r'));
                    i = e;
                    continue;
                }
                if (c == '/' && i + 1 < trivia.Length && trivia[i + 1] == '*')
                {
                    int e = trivia.IndexOf("*/", i, System.StringComparison.Ordinal);
                    if (e < 0) e = trivia.Length;
                    else e += 2;
                    list.Add(trivia.Substring(i, e - i));
                    i = e;
                    continue;
                }
                i++;
            }
            return list;
        }

        /// <summary>
        /// Считает пустые строки непосредственно перед лексемой. Первая и
        /// последняя строки блока тривиального текста — это хвост предыдущей
        /// строки и отступ самой лексемы, поэтому ни та, ни другая не считаются.
        /// </summary>
        public static int CountBlankLines(string trivia)
        {
            if (string.IsNullOrEmpty(trivia)) return 0;

            string[] lines = trivia.Split('\n');
            if (lines.Length <= 2) return 0;

            int blanks = 0;
            for (int i = 1; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim().Length == 0) blanks++;
                else break;
            }

            if (blanks > 2) blanks = 2;
            return blanks;
        }
    }
}
