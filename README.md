![banner](https://raw.githubusercontent.com/elenaandreevasvinolup-alt/NekoScriptGraph/main/banner.png)
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
- **8 languages**, **15 locales**, including full right-to-left for Arabic and Hebrew
- **Agent-ready** — a headless CLI *and* an in-editor **MCP server**; see [MCP.md](MCP.md)
- **One-click release** — delete every block config in a folder, or in the whole
  project, for a clean uninstall

## Requirements

- Unity 2022.3 or newer
- Editor-only: the whole package is scoped by an Editor-only assembly definition,
  so nothing is added to a player build.

## What's new in 1.0.3

Core **1.0.2** (unchanged engine), plugin **1.0.3**.

### Block explanation reads the branch

Explaining a block used to be a count: "there are 3 blocks inside". It now walks the
block's actual children — the branch body, the `else` branch, and the blocks plugged
into value slots — and describes them one by one. This is the data the optional
assistant renders; the explanation itself is built by NSG, so any assistant
implementation can use it.

- **Bounded on purpose.** Eight blocks per answer, six children per node, three levels
  deep. A large method would otherwise be a wall of text and a noticeable cost on every
  click.
- **It says when it stopped.** The remainder is not lost: the explanation reports how
  many blocks are left and asks you to select them, instead of stopping silently.
- **Data-driven.** The new strings live in a separate `tree.*` slot group, so a locale
  that has not translated them still gets the block sentence, and adding a tree
  translation is a data edit, not a code change. English, Simplified and Traditional
  Chinese ship translated.

### Auto-optimisation, with a gate that can say no

`Nsg_Optimizer` and two passes, run in order.

- **`optimizer.constantcondition`** — `if (true) { … }` is exactly `{ … }`, and
  `if (false) { … }` is nothing. A `true`/`false` literal has no side effects, so
  substituting the taken branch cannot change behaviour — unlike a condition that
  computes something. Code that followed the `if` is spliced onto the branch tail, and
  the orphaned literal is left for the next pass.
- **`optimizer.deadcode`** — removes statements after `return` / `break` / `continue` in
  the same list, and nodes nothing references any more.
- **Why that is safe:** reachability is walked from the entry point and stops at a
  terminator. Anything not marked reachable cannot run under any input.
- **It is not trusted on its own.** The model is deep-cloned first, then the result must
  resolve every reference, contain no cycles, render with no more errors than before,
  and the printed text must parse back. Any failure rolls the whole thing back and
  reports why. A file that was already imperfect is not rejected for staying imperfect.
- **Where:** `NekoWorks ▸ NSG ▸ Blocks ▸ Optimise Blocks` on the selected script, or Health window ▸ *Optimise blocks*. Both run the same passes through the same gate.

### It reads the logic, not just the shape

Explaining a block now ends with what it *does to the data*, not only what it contains:

> *"So while `health > 0` holds, it changes `health`, `timer`."*

- **Condition** — the socket named `cond`, rendered with its inputs filled in (`foreach`
  uses its `source` instead, since it has no condition).
- **Targets** — the variables the block changes: its own `target` / `name` socket, plus
  the same from every statement inside the branch or loop body, nested branches
  included.
- **Actions** — how many statements the body runs.

**All eight languages, no per-language code.** The reader never looks at syntax. It reads
two things each language's blocks already fill in: the `node` field (`while` / `for` /
`foreach` / `if` / `assign` / `localDecl` / `call` / `return` / `expr`) and the socket
names (`cond`, `target`, `name`, `source`). A new language gets the logic reading for
free as long as its blocks are labelled the same way.

It deliberately does **not** try to explain *why* a block exists — that needs a language
model. It reports only what is visible in the graph.

### Smarter next-block suggestions

`Nsg_Autocomplete` gained two signals on top of the category prior:

- **Transition scoring** — the graph is read for "block A followed by block B"
  (continuation, branch body, `else`), and a candidate that already followed the
  current block in this file scores higher. No dictionary, no training: it is the same
  graph read differently, so it adapts per script.
- **Symbols in value slots** — `Nsg_SymbolIndex` collects every name that was filled
  into a `var`-kind socket (declarations, loop variables) generically, so it works for
  any language and any block. The completion provider now returns `symbol` items for
  locals and for API members matched by their `matchCall` prefix.

### Localisation completed

Every locale now carries the full key set — 349 keys × 14 languages, no gaps and no duplicates. Thirteen packs were filled in this pass (Arabic, Hebrew, Polish, Turkish, German, Spanish, French, Italian, Japanese, Korean, Portuguese, Russian, Traditional Chinese); Simplified Chinese was already complete. Translations are unreviewed by native speakers — Arabic and Hebrew especially are worth a second pass.

### Fixes

- **The Problems window was empty when both filters were on.** The two toggles were two
  independent `continue`s, that is a logical AND: errors failed "not a warning" and
  warnings failed "not an error". They are now an OR-set — enable either, or both, and
  you see exactly that.
- **The Problems window stole focus on every action.** `Push` called `Open`, and `Push`
  runs on every file open, so the window jumped to the front even when it had been
  closed. It now updates in place and only surfaces for an error — which is the whole
  reason the window exists.
- **Any recompile turned the MCP bridge off permanently.** `Stop` wrote the "enabled"
  preference to `false`, and `Stop` was bound to `beforeAssemblyReload`. Lifecycle
  shutdown now keeps the preference; only an explicit stop clears it.
- **A corrupt `.nsg.json` threw while opening the file.** It is data, not code: the
  failure is now a diagnostic (`msg.modelLoadFailed`) and the file opens empty.
- **`empty.method` reported the opposite.** The guard returned when the entry was
  *empty*, so the rule fired on methods that had a body. Inverted back.
- **One unknown block produced unbounded duplicate errors.** The notice is emitted
  during rendering, which repeats on every rebuild, while diagnostics are cleared on
  import. Duplicates are now suppressed by code+message.

## What's new in 1.0.2

### Two more languages

- **Go** — `func` with the return type *after* the parameters, `:=`, `var`,
  conditions without parentheses, the three-clause `for`, `for {}` / `for cond {}`
  and `type … struct`. Go omits semicolons, so the lexer inserts them the way the Go
  spec does, and the printer keeps the opening brace on the header line so generated
  Go stays valid. 49 Go idiom blocks.
- **Swift** — `func … -> T`, argument labels (`clamp(value: 12, low: 0)`), `let`/`var`,
  `for … in`, opening brace on the header line. 25 Swift idiom blocks.

That makes **eight** drop-in languages, all of them bidirectional. Anything outside
the block subset is still preserved verbatim as a raw snippet and reported.

### Fixes

- **Opening a Python file crashed the block editor.** `py.pass` declared a `null`
  socket array, and five renderers read `def.sockets.Length` without a guard. Socket
  arrays are now normalised to empty when a block is created and again in the block
  library, so a block with no inputs is simply a block with no inputs.
- **Rust's `println!` produced wrong code.** The template wrapped the text socket in
  quotes, so arguments landed inside the string literal — `println!("sum = {}, x")`.
  It now takes the whole argument list, exactly as C's `printf` always did.
- **Go generated invalid code.** The printer put the opening brace on the next line,
  and Go's automatic semicolon insertion closed the statement before the block
  opened. Braces now stay on the header line, including `} else {`.
- **Go's `:=` silently lost code.** There was no block for it, and an expression
  whose block is unknown was dropped without a word: the line vanished from the
  graph *and* from the generated file. `:=` is now a real assignment block, and an
  unknown block is reported as a diagnostic instead of being discarded.
- **Palettes no longer offer blocks the language cannot express.** Rust and Swift
  were showing a C-style cast, a ternary, `++`/`--` and a three-clause `for`. A
  per-language exclusion list removes them.
- **The raw-snippet blocks were labelled "raw C#"** in every language, Rust and Go
  included. They now read "raw text", localised in all 15 locales.
- **Any language can be taken under management.** The menu required a `.cs` file, so
  `.py`, `.rs`, `.go` and `.swift` were refused outright. The check now asks the
  language registry instead of testing for one extension.
- **"Generate API Blocks for Selected Folder" only ever walked C#** — it looked for
  `MonoScript` assets, which no other language produces. It now asks every loaded
  engine for its own files.
- **Python API-block generation was a stub** that returned zero. It now emits a call
  block for every top-level `def`.
- **A language that fails now says so.** An exception while splitting, parsing or
  building the graph used to look like "the button does nothing". It is caught and
  reported with the method name and the exception, and errors also reach the Console.

### Settings and extensions

- `NekoScriptGraph ▸ Settings` — editor mode, hiding block files, the API output
  folder and the interface language.
- `NekoScriptGraph ▸ Extensions` — enable or remove any language pack except English
  (English is built into the code and is the fallback for every unfinished
  translation), and install or remove optional external engines in one click through
  the Unity Package Manager. The request is persisted, so an install that triggers a
  domain reload reports its real outcome instead of hanging.
- The settings window only shows what is actually present. With no language packs
  and no external engines it is a single General tab.
- **Optional external engines are a separate package.** The plugin contains none of
  their code, so removing one cannot break it — and it keeps working when none is
  installed.

### Not in this release

Advanced features — block hints, one-click optimisers and multi-step fix plans —
belong to the optional **ProgramNeko** pack, which is not part of this release. Without it
the core is unaffected: parsing, printing, diagnostics, the block editor, the block
library and the agent interface all work as before.

## Supported languages

`C` · `C++` · `C#` · `Go` · `HLSL` · `Java` · `Rust` · `Python` · `Swift`

Every one of them translates both ways. C# is built in; the others are drop-in
folders, and deleting a folder removes that language from the plugin without
breaking anything else.

Each language is honest about its own limits, and the limits live next to the code
rather than here — a language whose syntax the block model cannot express yet (Go's
`for … range`, Swift's `guard`, Rust's macros) keeps those lines as raw snippets,
which survive both directions unchanged. The header comment of each
`LanguageSupport/<id>/Nsg_<Id>Language.cs` lists exactly what stays raw.

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

Roughly **4.2 MB** as shipped:

| Part | Size |
|---|---|
| `Editor/` — core, UI, C# engine, settings | ~1.4 MB |
| `Dependencies/Editor/Sprite/` — optional 9-slice sprites | ~0.86 MB |
| `Documents/` — the guide in 15 languages | ~0.7 MB |
| `Locale/` — 15 interface languages | ~0.7 MB |
| `LanguageSupport/` — the eight drop-in languages | ~0.24 MB |
| `Blocks/` — default block library (regenerated on demand) | ~0.23 MB |
| `Extensions~/` — installable external-engine template | ~0.04 MB |

Figures include the `.meta` files and are approximate; generated API blocks under
`Blocks/API/` are project data and are not counted.

Every optional part can go, and each one is independent:

- delete `Dependencies/` — about **3.3 MB** left, and the UI falls back to plain
  rounded corners;
- delete a `LanguageSupport/<id>/` folder — that language disappears, the rest keep
  working;
- remove a `Locale/<code>/` folder (or use `NekoScriptGraph ▸ Extensions`) — that
  interface language disappears. English is built into the code and cannot be
  removed: it is the fallback for every unfinished translation.

The optional **ProgramNeko** pack is **not part of this release** and is
not included in the figures above.

## Contact

Author: NekoAndreeva

- Email: elenaandreevasvinolup@gmail.com
- WhatsApp: +852 5247 4163
<img width="1436" height="752" alt="Screenshot 2026-09-16 at 19 51 33" src="https://github.com/user-attachments/assets/5eea5914-19c6-4324-aee4-a3fb118d5f30" />
<img width="1436" height="752" alt="Screenshot 2026-09-16 at 19 51 19" src="https://github.com/user-attachments/assets/8d648043-dd58-4a2d-86ea-eed99f1a36ca" />
<img width="1436" height="752" alt="Screenshot 2026-09-16 at 19 51 03" src="https://github.com/user-attachments/assets/47ba12ec-1bdd-4499-8288-2c946916088b" />
<img width="1436" height="752" alt="Screenshot 2026-09-16 at 19 50 50" src="https://github.com/user-attachments/assets/02b373bf-99b0-4273-b763-1a76da883733" />
