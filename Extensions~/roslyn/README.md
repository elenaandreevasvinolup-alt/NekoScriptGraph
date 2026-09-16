# NSG Roslyn Completion

Optional completion engine for [NekoScriptGraph](../..).

This package is **not** part of the plugin. The plugin contains none of its code,
so installing or removing it can never break the plugin — and the plugin keeps
working with no completion engine at all.

## Requirements

* NekoScriptGraph (the plugin)
* The **ProgramNeko assistant**. Every advanced feature is gated on her: a hint
  that nobody can explain is worse than no hint.
* Roslyn. It is **not** bundled here. The engine looks for it in this order:

  1. `Assets/NSG.Roslyn/` (or any first-level folder inside it) — put
     `Microsoft.CodeAnalysis.dll` and `Microsoft.CodeAnalysis.CSharp.dll` there
     if your editor does not ship them;
  2. the Unity editor itself (`DotNetSdkRoslyn`, `Managed`, `Tools/Roslyn`).

  Nothing is copied into the plugin, and no NuGet or package dependency is
  declared. If Roslyn cannot be found the engine reports `failed` with the
  reason instead of failing to compile.

## Install / remove

From the plugin: `NekoScriptGraph ▸ Settings ▸ Extensions`, or
`NekoScriptGraph ▸ Extensions`.

Both actions go through the Unity Package Manager. The plugin remembers the
request in `EditorPrefs`, because installing triggers a domain reload and the
request object does not survive it; the result is confirmed by looking at the
actual package state afterwards.

Offline or on a private network, point `Extensions~/catalog.json` at a git URL
or at any local folder instead of the bundled template.

## What it adds

* symbols visible at the caret (locals, parameters, fields, properties, methods,
  types) via `SemanticModel.LookupSymbols`
* a real compilation built from the project's own references, so Unity and
  package types resolve

Member-chain completion (`transform.Tra`) is the next step; it needs the
expression under the caret to be typed, which in a block editor means mapping a
graph slot back to a source position.

## Contract

Implements `NekoScriptGraph.INsg_CompletionProvider` and is discovered by
reflection. It never throws into the plugin: a failure is reported as
`Nsg_ProviderState.Failed` with a message.
