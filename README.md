# NekoScriptGraph (NSG)

A Scratch-like visual programming plugin for Unity that **never puts anything into
your code**. NSG writes a block configuration file next to a script, which is what
makes that script editable as blocks — and it translates in both directions, so the
same file can be worked on as blocks or as ordinary code.

The generated `.cs` contains no trace of the plugin. Delete the plugin folder and
your scripts still compile.

## Highlights

- Scratch-inspired visual programming, inside the Unity Editor
- **Non-invasive** — no generated code, no attributes, no runtime dependency
- **Bidirectional** — blocks ⇄ code, both ways, for every supported language
- **Block vocabulary is data** — a block is a `Blocks/*.json` entry, so adding one
  needs no recompile
- **7 languages**, **15 locales**, including full right-to-left for Arabic and Hebrew
- **Agent-ready** — a headless CLI *and* an in-editor **MCP server**; see [MCP.md](MCP.md)
- **One-click release** — delete every block config in a folder, or in the whole
  project, for a clean uninstall

## Requirements

- Unity 2022.3 or newer
- Editor-only: the whole package is scoped by an Editor-only assembly definition,
  so nothing is added to a player build.

## Supported languages

`C` · `C++` · `C#` · `Go` · `HLSL` · `Java` · `Rust` · `Python`

Every one of them translates both ways. C# is built in; the others are drop-in
folders, and deleting a folder removes that language from the plugin without
breaking anything else.

## Localization

15 locales:

Simplified Chinese · Traditional Chinese · English · French · German · **Italian** ·
Russian · Spanish · Portuguese · Japanese · Korean · Polish · Turkish ·
Arabic · Hebrew

Arabic and Hebrew mirror the whole editor — the block palette moves to the left and
blocks grow leftward. Unity's own menu bar stays English by design.

## How it works

A script under management gets a block configuration file beside it:

```
Assets/Scripts/CompassManager.cs
Assets/Scripts/CompassManager.nsg.json    ← the block model
```

Only **method bodies** become blocks. `using`, the type declaration, fields,
attributes and comments outside method bodies are kept verbatim and survive both
directions. Anything the block model cannot express is preserved as a raw snippet
and reported as a diagnostic, so nothing is ever silently lost.

The editor tracks which side changed and reports it as `Synced`, `Code changed`,
`Blocks changed` or `Conflict`, so you always know whether you are about to
overwrite your own work.

## Agents and MCP

NSG ships an **MCP server that runs inside the Unity Editor** and speaks JSON-RPC
2.0 over the MCP Streamable HTTP transport. There is no side-car process and no
extra runtime — no Node, no Python. The Editor *is* the server.

Start it with `NekoScriptGraph ▸ MCP Bridge: Start`, then point your client at
`http://127.0.0.1:8765/`. The bridge listens on loopback only, refuses requests
that carry an `Origin` header, and remembers to restart itself after a domain
reload.

Seven tools are exposed: `nsg_writing_spec`, `nsg_verify`, `nsg_plan`, `nsg_canon`,
`nsg_apply`, `nsg_list_managed`, `nsg_release`.

The direction the agent drives is one-way, **code → blocks**: write ordinary code,
then let the plugin rebuild the block file. `nsg_plan` validates candidate text
without writing anything and without needing it to compile, and `nsg_canon`
returns the canonical form, so the agent can iterate to a fixed point before it
touches the project.

Full setup, client config and the tool reference: **[MCP.md](MCP.md)**

For CI or a closed Editor there is also a headless entry point:

```
Unity -batchmode -quit -projectPath <project> \
      -executeMethod NekoScriptGraph.Nsg_AgentCli.Main \
      -nsgRequest req.json -nsgResponse resp.json
```

## Non-invasive uninstall

Two buttons — or two menu items — delete every block configuration file under the
selected folder, or under the whole project. Source files are never touched, so
afterwards the only thing left is the plugin folder itself.

## Package size

Roughly **2.2 MB** as shipped:

| Part | Size |
|---|---|
| `Editor/` — core, UI, C# engine | ~0.9 MB |
| `Dependencies/Editor/Sprite/` — optional 9-slice sprites | ~0.86 MB |
| `Blocks/` — built-in block library (regenerated on demand) | ~0.23 MB |
| `LanguageSupport/` — the seven drop-in languages | ~0.21 MB |

The sprite pack is optional: delete `Dependencies/` and the plugin is about 1.4 MB,
falling back to plain rounded corners.

## Contact

Author: NekoAndreeva

- Email: elenaandreevasvinolup@gmail.com
- WhatsApp: +852 5247 4163
