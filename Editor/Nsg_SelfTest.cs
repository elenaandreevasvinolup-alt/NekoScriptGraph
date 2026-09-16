using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Приёмочные тесты ядра перевода. Именно из-за них круговому
    /// преобразованию можно доверять, и именно они позволяют отклонить блок,
    /// написанный агентом, ещё до того, как он коснётся файла проекта.
    ///
    ///   T1  печать идемпотентна:  P(R(P(b))) == P(b)
    ///   T2  круговое преобразование графа: AST -> граф -> AST печатается одинаково
    ///   T3  побайтовое совпадение: P(R(t)) == t для канонического исходника
    ///   T4  аварийный выход: неподдерживаемый синтаксис сохраняется дословно И сообщается
    /// </summary>
    public static class Nsg_SelfTest
    {
        class Case
        {
            public string Name;
            public bool Ok;
            public string Detail;
        }

        // Канонические тела, с отступом как внутри метода.
        static readonly string[] Samples =
        {
            "    if (playerCamera == null)\n" +
            "    {\n" +
            "        return;\n" +
            "    }\n" +
            "\n" +
            "    // поворот камеры по оси Y\n" +
            "    float rawYaw = playerCamera.eulerAngles.y;\n" +
            "    currentCameraYaw = Mathf.Round(rawYaw * 10f) / 10f;\n",

            "    for (int i = 0; i < 10; i++)\n" +
            "    {\n" +
            "        Debug.Log(i * 2 + 1);\n" +
            "    }\n" +
            "\n" +
            "    foreach (var item in list)\n" +
            "    {\n" +
            "        if (item == null)\n" +
            "        {\n" +
            "            continue;\n" +
            "        }\n" +
            "        total += item.value;\n" +
            "    }\n",

            "    while (running && count > 0)\n" +
            "    {\n" +
            "        count -= 1;\n" +
            "        if (count == 0)\n" +
            "        {\n" +
            "            break;\n" +
            "        }\n" +
            "    }\n",

            "    var a = (x + y) * (x - y);\n" +
            "    var b = x + y * z;\n" +
            "    var c = flag ? 1 : 2;\n",

            "    var v = new Vector3(1f, 2f, 3f);\n" +
            "    int n = (int)value;\n" +
            "    var first = list[0].name;\n" +
            "    var s = a ?? b ?? c;\n",
        };

        public static bool Run()
        {
            var cases = new List<Case>();

            CorpusTests(cases);
            GraphRoundTripTests(cases);
            EscapeHatchTests(cases);
            UnknownBlockTests(cases);
            FileSplitTests(cases);

            int pass = 0;
            var sb = new StringBuilder();
            sb.Append("[NekoScriptGraph] Результат самопроверки\n");

            for (int i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                if (c.Ok) pass++;
                sb.Append(c.Ok ? "  PASS  " : "  FAIL  ").Append(c.Name).Append('\n');
                if (!c.Ok && !string.IsNullOrEmpty(c.Detail))
                {
                    sb.Append("        ").Append(c.Detail.Replace("\n", "\n        ")).Append('\n');
                }
            }

            sb.Append("проверок ").Append(cases.Count).Append(", успешно ").Append(pass)
              .Append(", ошибок ").Append(cases.Count - pass);

            if (cases.Count - pass > 0) Debug.LogError(sb.ToString());
            else Debug.Log(sb.ToString());

            return pass == cases.Count;
        }

        static string Print(List<NsgStmt> statements)
        {
            return new NsgPrinter().PrintMethodBody(statements, string.Empty);
        }

        static void Add(List<Case> cases, string name, bool ok, string detail)
        {
            cases.Add(new Case { Name = name, Ok = ok, Detail = ok ? null : detail });
        }

        // ------------------------------------------------------------------
        // T3 + T1
        // ------------------------------------------------------------------

        static void CorpusTests(List<Case> cases)
        {
            for (int i = 0; i < Samples.Length; i++)
            {
                string inner = Samples[i];
                string expected = "{\n" + inner + "}\n";

                var d1 = new NsgDiagnostics();
                var body1 = NsgParser.ParseBodyFragment(inner, d1);
                string text1 = Print(body1.Statements);

                Add(cases, "T3 корпус #" + i + " побайтовое совпадение", text1 == expected,
                    "ожидалось:\n" + expected + "\nполучено:\n" + text1);

                var d2 = new NsgDiagnostics();
                var body2 = NsgParser.ParseBodyFragment(NsgFileSplitter.InnerBody(text1), d2);
                string text2 = Print(body2.Statements);

                Add(cases, "T1 корпус #" + i + " идемпотентность печати", text2 == text1,
                    "первый раз:\n" + text1 + "\nвторой раз:\n" + text2);
            }
        }

        // ------------------------------------------------------------------
        // T2
        // ------------------------------------------------------------------

        static void GraphRoundTripTests(List<Case> cases)
        {
            var lib = Nsg_Manager.Instance.Library;

            for (int i = 0; i < Samples.Length; i++)
            {
                string inner = Samples[i];
                string expected = "{\n" + inner + "}\n";

                var d1 = new NsgDiagnostics();
                var body = NsgParser.ParseBodyFragment(inner, d1);

                var toGraph = new NsgCodeMap(lib, new NsgDiagnostics());
                var graph = toGraph.ToGraph(body.Statements, null);

                var toAst = new NsgCodeMap(lib, new NsgDiagnostics());
                var back = toAst.ToAst(graph);
                string text = Print(back);

                Add(cases, "T2 корпус #" + i + " круговое преобразование", text == expected,
                    "ожидалось:\n" + expected + "\nполучено:\n" + text);
            }
        }

        // ------------------------------------------------------------------
        // T4
        // ------------------------------------------------------------------

        static void EscapeHatchTests(List<Case> cases)
        {
            const string inner = "    Foo?.Bar();\n";
            string expected = "{\n" + inner + "}\n";

            var diag = new NsgDiagnostics();
            var body = NsgParser.ParseBodyFragment(inner, diag);
            string text = Print(body.Statements);

            Add(cases, "T4 аварийный выход сохраняется дословно", text == expected,
                "ожидалось:\n" + expected + "\nполучено:\n" + text);

            Add(cases, "T4 аварийный выход обязан сообщать диагностику", diag.Items.Count > 0,
                "синтаксис вне подмножества не дал диагностики");
        }

        // ------------------------------------------------------------------
        // Неизвестный блок обязан быть жёсткой ошибкой, а не тихой потерей
        // ------------------------------------------------------------------

        static void UnknownBlockTests(List<Case> cases)
        {
            var lib = Nsg_Manager.Instance.Library;

            var g = new NsgMethodGraph();
            var n = new NsgGraphNode();
            n.id = "x1";
            n.block = "totally.unknown.block";
            g.nodes.Add(n);
            g.entry = "x1";

            var diag = new NsgDiagnostics();
            var map = new NsgCodeMap(lib, diag);
            map.ToAst(g);

            Add(cases, "неизвестный блок обязан быть жёсткой ошибкой", diag.HasErrors, "неизвестный блок не дал ошибки");
        }

        // ------------------------------------------------------------------
        // Уровень файла: нетронутый каркас должен воспроизводиться побайтово
        // ------------------------------------------------------------------

        static void FileSplitTests(List<Case> cases)
        {
            string[] candidates =
            {
                "Assets/Scripts/ChrControl/CompassManager.cs",
                "Assets/Scripts/ChrControl/CompassMarker.cs",
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                if (!File.Exists(path)) continue;

                string src = File.ReadAllText(path);
                var diag = new NsgDiagnostics();
                var model = NsgFileSplitter.Split(src, Path.GetFileName(path), diag);
                string rebuilt = NsgFileSplitter.Rebuild(model);

                Add(cases, "побайтовое восстановление каркаса " + Path.GetFileName(path), rebuilt == src,
                    FirstDifference(src, rebuilt));
            }
        }

        static string FirstDifference(string a, string b)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;

            int n = Mathf.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] != b[i])
                {
                    int from = Mathf.Max(0, i - 40);
                    int lenA = Mathf.Max(0, Mathf.Min(80, a.Length - from));
                    int lenB = Mathf.Max(0, Mathf.Min(80, b.Length - from));
                    return "первое расхождение на смещении " + i + "\nисходник: " +
                           a.Substring(from, lenA).Replace("\n", "\\n") + "\nпересборка: " +
                           b.Substring(from, lenB).Replace("\n", "\\n");
                }
            }

            if (a.Length != b.Length)
            {
                return "разная длина: исходник " + a.Length + " / пересборка " + b.Length;
            }
            return "совпадает";
        }
    }
}
