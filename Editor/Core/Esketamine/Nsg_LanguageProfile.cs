using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Описание языка: наборы ключевых слов, по которым лексер, разборщик и
    /// разделитель файла отличают конструкции.
    ///
    /// Тип живёт в ядре, потому что им пользуется и встроенный C#: он тоже
    /// параметризован профилем (по умолчанию — своим). Конкретные профили
    /// (C, C++, Java, HLSL, Rust, Python) собирают сами языки в своих папках
    /// внутри LanguageSupport.
    /// </summary>
    public class Nsg_LanguageProfile
    {
        public string Id;
        public string DisplayName;
        public string IconName;
        public string[] Extensions;

        /// <summary>Ключевые слова для лексера.</summary>
        public HashSet<string> Keywords = new HashSet<string>();

        /// <summary>Имена типов: по ним распознаётся объявление переменной.</summary>
        public HashSet<string> TypeKeywords = new HashSet<string>();

        /// <summary>Слова, с которых начинается оператор, а не выражение.</summary>
        public HashSet<string> StatementKeywords = new HashSet<string>();

        /// <summary>Слова, открывающие объявление типа (class / struct / ...).</summary>
        public HashSet<string> TypeDeclKeywords = new HashSet<string>();

        /// <summary>
        /// Идентификаторы, открывающие структурный блок ShaderLab:
        /// Shader, Properties, SubShader, Pass, Tags, cbuffer.
        /// Это не ключевые слова языка, поэтому проверяются по тексту.
        /// </summary>
        public HashSet<string> ContainerKeywords = new HashSet<string>();

        /// <summary>Обрабатывать строки препроцессора (#include, #define, ...).</summary>
        public bool Preprocessor = true;

        /// <summary>Есть ли цикл foreach (C, Rust и HLSL — нет).</summary>
        public bool HasForeach;

        /// <summary>Есть ли оператор new (C, Rust и HLSL — нет).</summary>
        public bool HasNew;

        /// <summary>
        /// Условия без круглых скобок: Rust и Python пишут "if x {", а не
        /// "if (x)". Влияет на разбор и печать.
        /// </summary>
        public bool ParenlessConditions;

        /// <summary>Тело блока задаётся отступами, а не фигурными скобками (Python).</summary>
        public bool IndentBlocks;

        /// <summary>Объявление переменной начинается со слова let (Rust).</summary>
        public bool LetBinding;

        /// <summary>Цикл по коллекции пишется "for x in y" (Rust), а не "foreach (T x in y)".</summary>
        public bool ForIn;

        /// <summary>
        /// Точка с запятой ставится автоматически в конце строки (Go).
        ///
        /// Лексер вставляет НЕВИДИМУЮ ';' там, где перевод строки завершает
        /// оператор, а печататель её обратно не выводит. Без этого язык без
        /// обязательных ';' разбирался бы почти целиком как сырой текст.
        /// </summary>
        public bool AutoSemicolon;

        /// <summary>Короткое объявление ":=" (Go).</summary>
        public bool ShortDecl;

        /// <summary>
        /// Тип возврата пишется ПОСЛЕ списка параметров: "func f() error {".
        /// Разделителю это нужно, чтобы найти '{' тела функции: иначе строка
        /// не опознаётся как метод и остаётся сырым текстом.
        /// </summary>
        public bool TrailingReturnType;

        public bool IsKeyword(string s)
        {
            return s != null && Keywords.Contains(s);
        }

        public bool IsTypeKeyword(string s)
        {
            return s != null && TypeKeywords.Contains(s);
        }

        public bool IsStatementKeyword(string s)
        {
            return s != null && StatementKeywords.Contains(s);
        }

        public bool IsTypeDeclKeyword(string s)
        {
            return s != null && TypeDeclKeywords.Contains(s);
        }

        public bool IsContainerKeyword(string s)
        {
            return s != null && ContainerKeywords.Contains(s);
        }

        /// <summary>Добавить слова в набор. Помощник для языков-плагинов.</summary>
        public static void Fill(HashSet<string> set, params string[] words)
        {
            for (int i = 0; i < words.Length; i++)
            {
                if (!string.IsNullOrEmpty(words[i])) set.Add(words[i]);
            }
        }
    }
}
