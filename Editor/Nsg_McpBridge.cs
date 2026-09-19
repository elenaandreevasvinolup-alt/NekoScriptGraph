using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Транспорт MCP: HTTP-сервер на петлевом интерфейсе внутри редактора Unity.
    ///
    /// Почему HTTP, а не stdio. MCP знает два транспорта: stdio (клиент
    /// запускает процесс сервера) и Streamable HTTP (клиент ходит по URL).
    /// stdio потребовал бы отдельного рантайма — Node или Python, — то есть
    /// установки ещё одной вещи ради плагина к Unity. HTTP не требует ничего:
    /// сервером становится сам редактор, а клиент прописывается одной строкой
    /// с URL. Поэтому реализован именно он.
    ///
    /// Логика протокола лежит в Nsg_Mcp (ядро, без UnityEditor). Здесь только
    /// транспорт: разбор HTTP, поток запросов и очередь на главный поток.
    ///
    /// Главный поток обязателен: операции трогают AssetDatabase и графы
    /// редактора. Запрос приходит на фоновом потоке, ждёт в очереди, а
    /// исполняется в EditorApplication.update.
    ///
    /// Безопасность: слушаем только 127.0.0.1 и отклоняем запросы с заголовком
    /// Origin. Это не паранойя: мост умеет писать файлы в проект, а браузер
    /// шлёт Origin всегда — значит, ни одна открытая в браузере страница не
    /// сможет достучаться до моста. Локальные клиенты MCP Origin не шлют.
    /// </summary>
    [InitializeOnLoad]
    public static class Nsg_McpBridge
    {
        const string KeyEnabled = "NekoScriptGraph.Mcp.Enabled";
        const string KeyPort = "NekoScriptGraph.Mcp.Port";
        const string KeyAllowOrigin = "NekoScriptGraph.Mcp.AllowOrigin";

        public const int DefaultPort = 8765;

        /// <summary>Сколько ждать главный поток, прежде чем отдать 503.</summary>
        const int MainThreadTimeoutMs = 60000;

        /// <summary>Предел размера тела запроса. Ответы бывают большими, запросы — нет.</summary>
        const int MaxBodyBytes = 16 * 1024 * 1024;

        class Work
        {
            public string Request;
            public string Response;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        static readonly ConcurrentQueue<Work> Queue = new ConcurrentQueue<Work>();

        static HttpListener _listener;
        static Thread _thread;
        static volatile bool _running;

        // ------------------------------------------------------------------
        // Состояние
        // ------------------------------------------------------------------

        public static bool IsRunning
        {
            get { return _running; }
        }

        public static int Port
        {
            get { return EditorPrefs.GetInt(KeyPort, DefaultPort); }
        }

        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(KeyEnabled, false); }
        }

        /// <summary>URL, который надо вписать в конфиг MCP-клиента.</summary>
        public static string Url
        {
            get { return "http://127.0.0.1:" + Port + "/"; }
        }

        static bool AllowOrigin
        {
            get { return EditorPrefs.GetBool(KeyAllowOrigin, false); }
        }

        // ------------------------------------------------------------------
        // Жизненный цикл
        // ------------------------------------------------------------------

        static Nsg_McpBridge()
        {
            // Перезагрузка домена уничтожает управляемое состояние, но сокет —
            // ресурс операционной системы. Закрываем его явно, иначе порт
            // останется занятым, и после перезагрузки Start() упадёт.
            //
            // Именно StopQuiet, а не Stop: перезагрузка домена случается при
            // любой перекомпиляции, и «явная остановка» здесь означала бы, что
            // первый же скрипт-релоад забывает пользовательский выбор и мост
            // больше не поднимается. Настройку снимает только сам пользователь.
            AssemblyReloadEvents.beforeAssemblyReload += StopQuiet;
            EditorApplication.quitting += StopQuiet;
            EditorApplication.update += DrainQueue;

            if (Enabled) EditorApplication.delayCall += Restore;
        }

        static void Restore()
        {
            if (!Enabled) return;
            Start(Port, false);
        }

        public static bool Start()
        {
            return Start(Port, true);
        }

        public static bool Start(int port, bool remember)
        {
            if (_running) return true;

            if (port <= 0 || port > 65535)
            {
                Debug.LogError("[NekoScriptGraph] MCP: недопустимый порт " + port);
                return false;
            }

            try
            {
                var l = new HttpListener();
                l.Prefixes.Add("http://127.0.0.1:" + port + "/");
                l.Start();

                _listener = l;
                _running = true;

                _thread = new Thread(AcceptLoop);
                _thread.IsBackground = true;
                _thread.Name = "NsgMcpBridge";
                _thread.Start();

                if (remember)
                {
                    EditorPrefs.SetBool(KeyEnabled, true);
                    EditorPrefs.SetInt(KeyPort, port);
                }

                Debug.Log("[NekoScriptGraph] MCP bridge listening on " + Url +
                          " — put this URL into your MCP client config.");
                return true;
            }
            catch (Exception e)
            {
                _listener = null;
                _running = false;
                Debug.LogError("[NekoScriptGraph] MCP: не удалось занять порт " + port + ": " + e.Message +
                               ". Порт занят другим процессом — выберите другой.");
                return false;
            }
        }

        /// <summary>Явная остановка: закрыть сокет И забыть пользовательский
        /// выбор, чтобы следующий запуск редактора мост не поднимал.</summary>
        public static void Stop()
        {
            Stop(true);
        }

        /// <summary>Остановка по жизненному циклу: закрыть сокет, но сохранить
        /// настройку — иначе перекомпиляция выключала бы мост навсегда.</summary>
        static void StopQuiet()
        {
            Stop(false);
        }

        static void Stop(bool forget)
        {
            _running = false;

            try
            {
                if (_listener != null) _listener.Close();
            }
            catch
            {
                // уже закрыт
            }

            _listener = null;

            // Поток фоновый и завершится сам, когда Close() уронит Accept.
            _thread = null;

            if (forget) EditorPrefs.SetBool(KeyEnabled, false);
        }

        public static void SetPort(int port)
        {
            EditorPrefs.SetInt(KeyPort, Mathf.Clamp(port, 1, 65535));
        }

        public static void SetAllowOrigin(bool on)
        {
            EditorPrefs.SetBool(KeyAllowOrigin, on);
        }

        // ------------------------------------------------------------------
        // Приём соединений
        // ------------------------------------------------------------------

        static void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    // Close() роняет GetContext — это штатное завершение.
                    return;
                }

                try
                {
                    Handle(ctx);
                }
                catch (Exception e)
                {
                    TryFail(ctx, 500, "Internal error: " + e.Message);
                }
            }
        }

        static void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;

            // Браузер всегда присылает Origin. Локальные клиенты — нет.
            if (!AllowOrigin && !string.IsNullOrEmpty(req.Headers["Origin"]))
            {
                TryFail(ctx, 403, "Requests with an Origin header are refused. " +
                                  "Enable it in NekoScriptGraph if you really need a browser client.");
                return;
            }

            if (req.HttpMethod == "GET")
            {
                Write(ctx, 200, "application/json", StatusPage().ToJson(true));
                return;
            }

            if (req.HttpMethod != "POST")
            {
                TryFail(ctx, 405, "Only POST is supported");
                return;
            }

            if (req.ContentLength64 > MaxBodyBytes)
            {
                TryFail(ctx, 413, "Request body is too large");
                return;
            }

            string body;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            string response = Invoke(body);
            Write(ctx, 200, "application/json", response);
        }

        /// <summary>
        /// Отдаёт запрос главному потоку и ждёт ответа. Блокирует только
        /// фоновый поток этого соединения.
        /// </summary>
        static string Invoke(string body)
        {
            if (!_running)
                return "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32603,\"message\":\"bridge is stopping\"}}";

            var work = new Work { Request = body };
            Queue.Enqueue(work);

            if (!work.Done.Wait(MainThreadTimeoutMs))
            {
                return "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32603," +
                       "\"message\":\"the editor did not answer in time; it is probably compiling\"}}";
            }

            return work.Response;
        }

        /// <summary>
        /// Главный поток: разбирает накопившиеся запросы. Всё, что трогает
        /// редактор, обязано происходить здесь.
        /// </summary>
        static void DrainQueue()
        {
            if (Queue.IsEmpty) return;

            Work work;
            while (Queue.TryDequeue(out work))
            {
                try
                {
                    string response = Nsg_Mcp.Handle(work.Request);

                    // null означает уведомление: по стандарту ответа нет, но
                    // HTTP всё равно должен что-то вернуть.
                    work.Response = response != null
                        ? response
                        : "{\"jsonrpc\":\"2.0\",\"id\":null,\"result\":null}";
                }
                catch (Exception e)
                {
                    work.Response = "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32603," +
                                    "\"message\":\"" + Escape(e.GetType().Name + ": " + e.Message) + "\"}}";
                }

                work.Done.Set();
            }
        }

        // ------------------------------------------------------------------
        // HTTP-ответы
        // ------------------------------------------------------------------

        static NsgJson StatusPage()
        {
            var o = NsgJson.NewObject();
            o.Set("server", Nsg_Mcp.ServerName);

            // Две версии намеренно раздельно: ядро и плагин выпускаются
            // независимо. version оставлен для совместимости клиентов и равен
            // версии ядра — сервер живёт в нём.
            o.Set("version", Nsg_Mcp.ServerVersion);
            o.Set("coreVersion", Nsg_Core.Version);
            o.Set("pluginVersion", Nsg_Version.Plugin);

            o.Set("transport", "streamable-http");
            o.Set("url", Url);
            o.Set("protocolVersions", Strings(Nsg_Mcp.ProtocolVersions));
            o.Set("tools", Strings(Nsg_Mcp.ToolNames));
            o.Set("note", "POST JSON-RPC 2.0 to this URL to talk to the server.");
            return o;
        }

        static NsgJson Strings(string[] src)
        {
            var a = NsgJson.NewArray();
            if (src != null)
            {
                for (int i = 0; i < src.Length; i++) a.Add(NsgJson.NewString(src[i]));
            }
            return a;
        }

        static void Write(HttpListenerContext ctx, int status, string contentType, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);

            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.AddHeader("Cache-Control", "no-store");

            using (var s = ctx.Response.OutputStream) s.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        static void TryFail(HttpListenerContext ctx, int status, string message)
        {
            try
            {
                Write(ctx, status, "text/plain; charset=utf-8", message);
            }
            catch
            {
                // соединение уже разорвано — отвечать некому
            }
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
