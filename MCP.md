# NekoScriptGraph — MCP server

NSG ships an **MCP server that runs inside the Unity Editor**. It speaks
JSON-RPC 2.0 over the MCP **Streamable HTTP** transport, so there is nothing to
install: no Node, no Python, no side-car process. The Editor *is* the server.

```
MCP client  ──HTTP POST JSON-RPC──▶  127.0.0.1:8765  ──▶  Unity Editor
```

## 1. Start it

In Unity: `NekoScriptGraph ▸ MCP Bridge: Start`.

```
[NekoScriptGraph] MCP bridge listening on http://127.0.0.1:8765/
```

The bridge remembers that it was on and comes back automatically after a
domain reload or an Editor restart. Stop it with
`NekoScriptGraph ▸ MCP Bridge: Stop`.

`NekoScriptGraph ▸ MCP Bridge: Copy Client URL` puts the URL on the clipboard.

Check it by hand — this needs no client at all:

```bash
curl -s http://127.0.0.1:8765/ -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

A plain `GET` returns a small status page listing the server version, the
supported protocol revisions and the available tools.

The page reports **two** versions on purpose, because the core and the plugin
are released independently:

| Field | Meaning |
|---|---|
| `version` | The server version — the same as `coreVersion`. Kept for clients that already read it. |
| `coreVersion` | `Editor/Core/Esketamine` — the engine (model, parse, print, diagnostics, this protocol layer). |
| `pluginVersion` | The package around the core (UI, extensions, assistant, locales). Matches `package.json`. |

## 2. Point your client at it

Any MCP client that supports Streamable HTTP works. The shape of the config
varies slightly between clients — the key is `type` in some, `transport` in
others, and a few accept a bare `url`:

```json
{
  "mcpServers": {
    "nekoscriptgraph": {
      "type": "http",
      "url": "http://127.0.0.1:8765/"
    }
  }
}
```

VS Code uses `servers` instead of `mcpServers`; the entry is the same.

If your client only speaks **stdio**, put an HTTP↔stdio proxy in front of the
bridge (for example `npx mcp-remote http://127.0.0.1:8765/`). That proxy is the
only thing that needs a runtime, and it is the client's business, not the
plugin's.

> The legacy HTTP+SSE transport (`GET /sse`) is **not** implemented. The bridge
> serves the Streamable HTTP transport, protocol revision `2025-03-26` and
> newer, and also accepts `2024-11-05` clients that POST to the same URL.

## 3. Tools

| Tool | Writes? | What it does |
|---|---|---|
| `nsg_writing_spec` | no | The canonical writing subset for a language, generated from the block library and the printer |
| `nsg_verify` | no | State of a file on disk: `Synced`, `CsDirty`, `BlocksDirty`, `Conflict`, `Unmanaged` |
| `nsg_plan` | no | Parse candidate code, report diagnostics, block counts, escape ratio, canonical? |
| `nsg_canon` | no | Canonical text — the fixed-point oracle |
| `nsg_apply` | **yes** | Rebuild the `.nsg.json` and write the canonical source |
| `nsg_list_managed` | no | Every `.nsg.json` under a folder |
| `nsg_release` | **yes** | Delete those `.nsg.json` files (clean uninstall) |

## 4. The intended loop

The direction is one-way, **code → blocks**. The agent writes ordinary source
code; the plugin re-parses it and rebuilds the block file.

1. `nsg_writing_spec` once, to learn the subset for the language.
2. Edit the `.cs` (or just hold the text in the conversation).
3. `nsg_plan` — diagnostics, block counts, escape ratio. Writes nothing, and
   needs no compile, so it is safe to call with code that does not build yet.
4. If `canonical` is false, call `nsg_canon` and iterate until
   `canon(text) == text`. That is the fixed point: once the text is a fixed
   point, the document stays `Synced` and nothing gets reformatted later.
5. `nsg_apply` — writes the canonical `.cs` and the rebuilt `.nsg.json`.

`escapeRatio` is the share of raw-snippet blocks. Gate on it with
`maxEscapeRatio: 0` to demand a full translation into blocks; anything the
parser cannot model is preserved verbatim and reported as `NSG0002`.

### Example

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{
  "name":"nsg_plan",
  "arguments":{"file":"Assets/Scripts/CompassManager.cs","maxEscapeRatio":0.0}}}
```

`nsg_plan`, `nsg_canon` and `nsg_apply` also accept `source`, so the agent can
validate text before it ever reaches disk:

```json
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{
  "name":"nsg_canon",
  "arguments":{"file":"Assets/Scripts/Foo.cs","source":"class Foo { void A(){ } }"}}}
```

## 5. Security

The bridge writes files into your project, so it is deliberately narrow:

* It binds to `127.0.0.1` only — never to a routable interface.
* Requests carrying an `Origin` header are refused with `403`. Browsers always
  send `Origin`; native MCP clients never do. That means no page open in your
  browser can reach the bridge. If you really need a browser client, the check
  can be relaxed (`Nsg_McpBridge.SetAllowOrigin(true)`).
* The server is off until you start it, and it stops on quit.

## 6. Troubleshooting

**The Editor did not answer in time.** The bridge marshals every call onto the
main thread, and Unity does not run `EditorApplication.update` while it
compiles or reloads the domain. Requests sent during a recompile wait, then
fail after 60 seconds. Just retry.

**Port already in use.** Another process holds `8765`. Change it with
`Nsg_McpBridge.SetPort(n)`, or close the other listener.

**The tools are missing.** Check that the plugin compiled — `Nsg_Json`,
`Nsg_Mcp` and `Nsg_McpBridge` are ordinary Editor scripts and need no setup.
`GET /` on the bridge lists the tools the server is currently offering.

## 7. Using it without MCP

The bridge is a plain JSON-RPC endpoint, and the same operations are available
without any protocol at all:

* **Headless / CI** —
  `Unity -batchmode -quit -executeMethod NekoScriptGraph.Nsg_AgentCli.Main -nsgRequest req.json -nsgResponse resp.json`
* **In code** — `Nsg_AgentApi.Run(request)`, plus `Nsg_Mcp.Handle(jsonString)`
  for the protocol layer alone.
