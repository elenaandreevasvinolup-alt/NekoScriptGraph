using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Слой MCP: JSON-RPC 2.0 поверх модели NsgJson.
    ///
    /// Здесь нет ни сокетов, ни UnityEditor — только «строка запроса на вход,
    /// строка ответа на выход». Транспорт (loopback HTTP) живёт отдельно, в
    /// Nsg_McpBridge. Такое разделение даёт две вещи: слой можно прогнать
    /// тестом без редактора, и тот же код годится для любого другого
    /// транспорта (stdio-обёртка, свой клиент, curl).
    ///
    /// Реализована серверная часть MCP: initialize, tools/list, tools/call,
    /// ping. Остальные методы отвечают «не найдено» — это корректно, клиент
    /// обязан смотреть в capabilities.
    /// </summary>
    public static class Nsg_Mcp
    {
        public const string ServerName = "nekoscriptgraph";

        /// <summary>
        /// Версия сервера — это версия ЯДРА, а не плагина: сервер живёт в
        /// ядре и переживает выпуски интерфейса. См. Nsg_Core.Version.
        /// </summary>
        public const string ServerVersion = Nsg_Core.Version;

        /// <summary>
        /// Версии протокола, которые понимаем. Отвечаем той же, что просил
        /// клиент, если она есть в списке, иначе самой свежей своей.
        /// </summary>
        public static readonly string[] ProtocolVersions =
        {
            "2025-06-18",
            "2025-03-26",
            "2024-11-05"
        };

        /// <summary>
        /// Исполнитель операции. Вынесен в поле, чтобы тест мог подставить
        /// заглушку и проверить весь слой протокола без Unity.
        /// </summary>
        public static System.Func<Nsg_AgentRequest, Nsg_AgentResponse> Execute = Nsg_AgentApi.Run;

        // ------------------------------------------------------------------
        // Точка входа
        // ------------------------------------------------------------------

        /// <summary>
        /// Обрабатывает одну строку JSON-RPC. Возвращает строку ответа либо
        /// null, если это уведомление (на него по стандарту отвечать нельзя).
        /// </summary>
        public static string Handle(string requestJson)
        {
            NsgJson req;
            string parseError;
            if (!NsgJson.TryParse(requestJson, out req, out parseError))
                return Error(null, -32700, "Parse error: " + parseError).ToJson();

            if (req.Kind != NsgJsonKind.Object)
                return Error(null, -32600, "Invalid Request: expected a JSON object").ToJson();

            var id = req.Get("id");
            bool isNotification = id == null || id.IsNull;

            string method = req.Get("method") != null ? req.Get("method").AsString(null) : null;
            if (string.IsNullOrEmpty(method))
            {
                if (isNotification) return null;
                return Error(id, -32600, "Invalid Request: method is missing").ToJson();
            }

            // Уведомления не получают ответа ни при каком исходе.
            if (isNotification && method.StartsWith("notifications/")) return null;

            var result = Dispatch(method, req.Get("params"), out int errCode, out string errMessage);

            if (errCode != 0)
            {
                if (isNotification) return null;
                return Error(id, errCode, errMessage).ToJson();
            }

            if (isNotification) return null;
            return Success(id, result).ToJson();
        }

        // ------------------------------------------------------------------
        // Маршрутизация методов
        // ------------------------------------------------------------------

        static NsgJson Dispatch(string method, NsgJson prm, out int errCode, out string errMessage)
        {
            errCode = 0;
            errMessage = null;

            switch (method)
            {
                case "initialize":
                    return Initialize(prm);

                case "ping":
                    return NsgJson.NewObject();

                case "tools/list":
                    return ToolsList();

                case "tools/call":
                    return ToolsCall(prm, out errCode, out errMessage);

                case "resources/list":
                    return NsgJson.NewObject().Set("resources", NsgJson.NewArray());

                case "prompts/list":
                    return NsgJson.NewObject().Set("prompts", NsgJson.NewArray());

                default:
                    errCode = -32601;
                    errMessage = "Method not found: " + method;
                    return null;
            }
        }

        static NsgJson Initialize(NsgJson prm)
        {
            string asked = prm != null && prm.Get("protocolVersion") != null
                ? prm.Get("protocolVersion").AsString(null)
                : null;

            string agreed = ProtocolVersions[0];
            for (int i = 0; i < ProtocolVersions.Length; i++)
            {
                if (ProtocolVersions[i] == asked) { agreed = asked; break; }
            }

            var caps = NsgJson.NewObject();
            caps.Set("tools", NsgJson.NewObject().Set("listChanged", false));

            var info = NsgJson.NewObject();
            info.Set("name", ServerName);
            info.Set("title", "NekoScriptGraph");
            info.Set("version", ServerVersion);

            var r = NsgJson.NewObject();
            r.Set("protocolVersion", agreed);
            r.Set("capabilities", caps);
            r.Set("serverInfo", info);
            r.Set("instructions",
                "NekoScriptGraph turns ordinary source files into visual blocks and back. " +
                "The direction you drive is code → blocks: write normal code, then call " +
                "nsg_apply to rebuild the .nsg.json next to it. Call nsg_writing_spec first " +
                "to learn the canonical writing subset for the language, and iterate with " +
                "nsg_canon until canon(t) == t so nothing gets reformatted later. " +
                "nsg_plan never writes anything.");
            return r;
        }

        // ------------------------------------------------------------------
        // Инструменты
        // ------------------------------------------------------------------

        public static readonly string[] ToolNames =
        {
            "nsg_writing_spec",
            "nsg_verify",
            "nsg_plan",
            "nsg_canon",
            "nsg_apply",
            "nsg_list_managed",
            "nsg_release"
        };

        static NsgJson ToolsList()
        {
            var arr = NsgJson.NewArray();
            for (int i = 0; i < ToolNames.Length; i++) arr.Add(ToolSchema(ToolNames[i]));

            return NsgJson.NewObject().Set("tools", arr);
        }

        static NsgJson Prop(string type, string description)
        {
            return NsgJson.NewObject().Set("type", type).Set("description", description);
        }

        static NsgJson Schema(string[] required, NsgJson properties)
        {
            var s = NsgJson.NewObject();
            s.Set("type", "object");
            s.Set("properties", properties);

            var req = NsgJson.NewArray();
            if (required != null)
            {
                for (int i = 0; i < required.Length; i++) req.Add(NsgJson.NewString(required[i]));
            }
            s.Set("required", req);
            s.Set("additionalProperties", false);
            return s;
        }

        static NsgJson ToolSchema(string name)
        {
            var t = NsgJson.NewObject();
            t.Set("name", name);

            switch (name)
            {
                case "nsg_writing_spec":
                    t.Set("title", "NekoScriptGraph writing spec");
                    t.Set("description",
                        "Return the canonical writing subset for a language, generated from the " +
                        "block library and the printer. Read this before editing any file: it lists " +
                        "which constructs stay blocks and which collapse into a raw snippet (NSG0002).");
                    t.Set("inputSchema", Schema(null, NsgJson.NewObject()
                        .Set("language", Prop("string", "Language id, for example csharp or rust. Defaults to csharp."))));
                    return t;

                case "nsg_verify":
                    t.Set("title", "Verify a file");
                    t.Set("description",
                        "Read a source file and its block config from disk and report the state " +
                        "(Synced, CsDirty, BlocksDirty, Conflict, Unmanaged), block counts and " +
                        "escape ratio. Changes nothing.");
                    t.Set("inputSchema", Schema(new[] { "file" }, NsgJson.NewObject()
                        .Set("file", Prop("string", "Project-relative path, for example Assets/Scripts/Foo.cs."))));
                    return t;

                case "nsg_plan":
                    t.Set("title", "Plan an edit");
                    t.Set("description",
                        "Parse candidate code and report diagnostics, block counts, escape ratio and " +
                        "whether the text is already canonical. Writes nothing, and needs no Unity " +
                        "compile — safe to call with code that does not compile yet.");
                    t.Set("inputSchema", Schema(new[] { "file" }, NsgJson.NewObject()
                        .Set("file", Prop("string", "Project-relative path of the target source file."))
                        .Set("source", Prop("string", "Candidate text. Omit to read the file from disk."))
                        .Set("maxEscapeRatio", Prop("number", "Fail the plan when the raw-snippet share exceeds this, 0..1."))));
                    return t;

                case "nsg_canon":
                    t.Set("title", "Canonicalize");
                    t.Set("description",
                        "Return the canonical text of the file. This is the fixed-point oracle: iterate " +
                        "until canon(text) == text and the document will stay in sync with no later " +
                        "reformatting. Writes nothing.");
                    t.Set("inputSchema", Schema(new[] { "file" }, NsgJson.NewObject()
                        .Set("file", Prop("string", "Project-relative path of the target source file."))
                        .Set("source", Prop("string", "Candidate text. Omit to read the file from disk."))));
                    return t;

                case "nsg_apply":
                    t.Set("title", "Apply an edit");
                    t.Set("description",
                        "Rebuild the .nsg.json for a file and write the canonical source. Only writes " +
                        "when there are no errors and the escape gate passes; a rejected apply leaves " +
                        "the project untouched. Use nsg_plan first.");
                    t.Set("inputSchema", Schema(new[] { "file" }, NsgJson.NewObject()
                        .Set("file", Prop("string", "Project-relative path of the target source file."))
                        .Set("source", Prop("string", "Candidate text. Omit to use the file on disk as-is."))
                        .Set("maxEscapeRatio", Prop("number", "Refuse to write when the raw-snippet share exceeds this, 0..1. 0 means full translation only."))
                        .Set("requireClean", Prop("boolean", "Refuse when the document is in the Conflict state. Defaults to true."))));
                    return t;

                case "nsg_list_managed":
                    t.Set("title", "List managed files");
                    t.Set("description",
                        "List every .nsg.json under a folder, project-wide by default. Includes configs " +
                        "whose source was deleted, and configs of languages that are not loaded.");
                    t.Set("inputSchema", Schema(null, NsgJson.NewObject()
                        .Set("folder", Prop("string", "Folder to scan, project-relative. Omit for the whole Assets folder."))));
                    return t;

                case "nsg_release":
                    t.Set("title", "Release block configs");
                    t.Set("description",
                        "Delete the .nsg.json files under a folder, returning those scripts to ordinary " +
                        "files. Source files are never touched. This is the clean-uninstall path.");
                    t.Set("inputSchema", Schema(null, NsgJson.NewObject()
                        .Set("folder", Prop("string", "Folder to release, project-relative. Omit for the whole Assets folder."))));
                    return t;
            }

            t.Set("title", name);
            t.Set("description", "(unknown tool)");
            t.Set("inputSchema", Schema(null, NsgJson.NewObject()));
            return t;
        }

        static NsgJson ToolsCall(NsgJson prm, out int errCode, out string errMessage)
        {
            errCode = 0;
            errMessage = null;

            if (prm == null || prm.Kind != NsgJsonKind.Object)
            {
                errCode = -32602;
                errMessage = "Invalid params: expected an object with name and arguments";
                return null;
            }

            string name = prm.Get("name") != null ? prm.Get("name").AsString(null) : null;
            if (string.IsNullOrEmpty(name))
            {
                errCode = -32602;
                errMessage = "Invalid params: name is missing";
                return null;
            }

            bool known = false;
            for (int i = 0; i < ToolNames.Length; i++)
            {
                if (ToolNames[i] == name) { known = true; break; }
            }
            if (!known)
            {
                errCode = -32602;
                errMessage = "Unknown tool: " + name;
                return null;
            }

            var args = prm.Get("arguments");
            if (args != null && args.Kind != NsgJsonKind.Object && args.Kind != NsgJsonKind.Null)
            {
                errCode = -32602;
                errMessage = "Invalid params: arguments must be an object";
                return null;
            }

            string buildError;
            var req = BuildRequest(name, args, out buildError);
            if (req == null)
            {
                // Ошибка аргументов — это протокольная ошибка, а не сбой инструмента.
                errCode = -32602;
                errMessage = "Invalid params: " + buildError;
                return null;
            }

            Nsg_AgentResponse res;
            try
            {
                res = Execute != null ? Execute(req) : null;
            }
            catch (System.Exception e)
            {
                res = new Nsg_AgentResponse();
                res.ok = false;
                res.op = req.op;
                res.error = e.GetType().Name + ": " + e.Message;
            }

            if (res == null)
            {
                res = new Nsg_AgentResponse();
                res.ok = false;
                res.op = req.op;
                res.error = "no result";
            }

            return ToolResult(name, res);
        }

        /// <summary>
        /// Переводит аргументы инструмента в запрос агентского интерфейса.
        /// Возвращает null и текст ошибки, если аргументов не хватает.
        /// </summary>
        static Nsg_AgentRequest BuildRequest(string name, NsgJson args, out string error)
        {
            error = null;
            var req = new Nsg_AgentRequest();

            string file = args != null && args.Get("file") != null ? args.Get("file").AsString(null) : null;
            string folder = args != null && args.Get("folder") != null ? args.Get("folder").AsString(null) : null;

            switch (name)
            {
                case "nsg_writing_spec":
                    req.op = Nsg_AgentOps.Spec;
                    req.specLanguage = args != null && args.Get("language") != null
                        ? args.Get("language").AsString("csharp")
                        : "csharp";
                    return req;

                case "nsg_verify":
                    req.op = Nsg_AgentOps.Verify;
                    req.file = file;
                    if (string.IsNullOrEmpty(file)) { error = "file is required"; return null; }
                    return req;

                case "nsg_plan":
                    req.op = Nsg_AgentOps.Plan;
                    req.file = file;
                    if (string.IsNullOrEmpty(file)) { error = "file is required"; return null; }
                    req.source = args.Get("source") != null ? args.Get("source").AsString(null) : null;
                    req.maxEscapeRatio = args.Get("maxEscapeRatio") != null
                        ? (float)args.Get("maxEscapeRatio").AsNumber(-1.0)
                        : -1f;
                    return req;

                case "nsg_canon":
                    req.op = Nsg_AgentOps.Canon;
                    req.file = file;
                    if (string.IsNullOrEmpty(file)) { error = "file is required"; return null; }
                    req.source = args.Get("source") != null ? args.Get("source").AsString(null) : null;
                    return req;

                case "nsg_apply":
                    req.op = Nsg_AgentOps.Apply;
                    req.file = file;
                    if (string.IsNullOrEmpty(file)) { error = "file is required"; return null; }
                    req.source = args.Get("source") != null ? args.Get("source").AsString(null) : null;
                    req.maxEscapeRatio = args.Get("maxEscapeRatio") != null
                        ? (float)args.Get("maxEscapeRatio").AsNumber(-1.0)
                        : -1f;
                    req.requireClean = args.Get("requireClean") != null
                        ? args.Get("requireClean").AsBool(true)
                        : true;
                    return req;

                case "nsg_list_managed":
                    req.op = Nsg_AgentOps.List;
                    req.folder = folder;
                    return req;

                case "nsg_release":
                    req.op = Nsg_AgentOps.Release;
                    req.folder = folder;
                    return req;
            }

            error = "unknown tool " + name;
            return null;
        }

        static NsgJson ToolResult(string toolName, Nsg_AgentResponse res)
        {
            var structured = ResponseToJson(res);
            // Массив отдаём всегда, даже пустым: клиенту проще, когда поле есть
            // и его тип не меняется от вызова к вызову.
            var files = NsgJson.NewArray();
            if (res.files != null)
            {
                for (int i = 0; i < res.files.Length; i++) files.Add(NsgJson.NewString(res.files[i]));
            }
            structured.Set("files", files);

            var text = NsgJson.NewObject();
            text.Set("type", "text");
            text.Set("text", res.ok ? Summary(toolName, res) : "Failed: " + (res.error ?? "unknown error"));

            var content = NsgJson.NewArray();
            content.Add(text);

            var r = NsgJson.NewObject();
            r.Set("content", content);
            r.Set("structuredContent", structured);
            r.Set("isError", !res.ok);
            return r;
        }

        static string Summary(string toolName, Nsg_AgentResponse res)
        {
            switch (res.op)
            {
                case Nsg_AgentOps.Spec:
                    return "Writing spec for '" + res.language + "', " + (res.text != null ? res.text.Length : 0) +
                           " characters. See structuredContent.text.";

                case Nsg_AgentOps.Canon:
                    return "Canonical text for " + res.file + ", " + (res.text != null ? res.text.Length : 0) +
                           " characters. See structuredContent.text.";

                case Nsg_AgentOps.List:
                    return (res.files != null ? res.files.Length : 0) + " block config file(s) found.";

                case Nsg_AgentOps.Release:
                    return "Released " + res.released + " file(s), " + res.failed + " failed.";

                case Nsg_AgentOps.Verify:
                    return res.file + ": " + res.state + ", " + res.blocks + " block(s), " +
                           res.rawBlocks + " raw, escape " + res.escapeRatio.ToString("F4") + ".";

                case Nsg_AgentOps.Plan:
                    return res.file + ": " + (res.canonical ? "canonical" : "not canonical") + ", " +
                           res.blocks + " block(s), " + res.rawBlocks + " raw, escape " +
                           res.escapeRatio.ToString("F4") + ".";

                case Nsg_AgentOps.Apply:
                    return res.file + ": written. State is now " + res.state + ".";
            }

            return "ok";
        }

        static NsgJson ResponseToJson(Nsg_AgentResponse res)
        {
            var o = NsgJson.NewObject();
            o.Set("ok", res.ok);
            o.Set("op", res.op ?? string.Empty);
            if (res.file != null) o.Set("file", res.file);
            if (res.language != null) o.Set("language", res.language);
            if (res.state != null) o.Set("state", res.state);

            o.Set("methods", res.methods);
            o.Set("blocks", res.blocks);
            o.Set("rawBlocks", res.rawBlocks);
            o.Set("escapeRatio", (double)res.escapeRatio);
            o.Set("canonical", res.canonical);
            o.Set("wrote", res.wrote);

            if (res.wroteFiles != null)
            {
                var arr = NsgJson.NewArray();
                for (int i = 0; i < res.wroteFiles.Length; i++) arr.Add(NsgJson.NewString(res.wroteFiles[i]));
                o.Set("wroteFiles", arr);
            }

            if (res.diagnostics != null)
            {
                var arr = NsgJson.NewArray();
                for (int i = 0; i < res.diagnostics.Length; i++) arr.Add(NsgJson.NewString(res.diagnostics[i]));
                o.Set("diagnostics", arr);
            }
            else
            {
                o.Set("diagnostics", NsgJson.NewArray());
            }

            if (res.op == Nsg_AgentOps.List || res.op == Nsg_AgentOps.Release)
            {
                o.Set("released", res.released);
                o.Set("failed", res.failed);
            }

            if (res.text != null) o.Set("text", res.text);
            if (res.error != null) o.Set("error", res.error);

            return o;
        }

        // ------------------------------------------------------------------
        // Каркас JSON-RPC
        // ------------------------------------------------------------------

        static NsgJson Success(NsgJson id, NsgJson result)
        {
            var o = NsgJson.NewObject();
            o.Set("jsonrpc", "2.0");
            o.Set("id", id != null ? id : NsgJson.Null());
            o.Set("result", result != null ? result : NsgJson.NewObject());
            return o;
        }

        static NsgJson Error(NsgJson id, int code, string message)
        {
            var err = NsgJson.NewObject();
            err.Set("code", code);
            err.Set("message", message);

            var o = NsgJson.NewObject();
            o.Set("jsonrpc", "2.0");
            o.Set("id", id != null ? id : NsgJson.Null());
            o.Set("error", err);
            return o;
        }
    }
}
