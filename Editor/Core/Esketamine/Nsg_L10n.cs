using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Строки интерфейса.
    ///
    /// Встроен ТОЛЬКО английский: это язык-основа, к которому откатывается
    /// любой незаполненный ключ. Все остальные языки лежат данными в
    /// Locale/&lt;code&gt;/strings.json — по папке на язык. Поэтому перевод
    /// добавляется без правки кода: достаточно положить новую папку, и она
    /// сама появится в списке языков.
    ///
    /// Ядро перевода намеренно не ссылается на UnityEditor: путь берётся из
    /// Nsg_Paths, чтение — обычным File, поэтому таблица работает и в
    /// автономных тестах.
    ///
    /// Языки интерфейса НЕ нумеруются жёстко: индекс 0 — английский, дальше
    /// идут обнаруженные папки в порядке поля order. Добавление языка больше
    /// не сдвигает индексы уже существующих.
    /// </summary>
    public static class Nsg_L10n
    {
        /// <summary>Индекс английского: он же язык по умолчанию и запасной.</summary>
        public const int EnIndex = 0;

        /// <summary>Встроенные английские строки. Ключи те же, что в Locale.</summary>
        static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "health.title"              , "Architecture health" },
            { "health.btn"                , "Health check" },
            { "health.score"              , "Score {0}/100" },
            { "health.rerun"              , "Re-run" },
            { "health.none"               , "No issues found." },
            { "health.summary"            , "errors {0} / warnings {1} / hints {2}" },
            { "health.methods"            , "Method" },
            { "health.metric.methods"     , "methods {0}" },
            { "health.metric.statements"  , "statement blocks {0}" },
            { "health.metric.escapes"     , "raw snippets {0} ({1:F1}%)" },
            { "health.optimizerReady"     , "Auto-optimisation hook ready: {0} optimizer pass(es) registered." },
            { "health.optimizerNone"      , "Auto-optimisation is not enabled yet (it ships with the low-level blocks). The hook is ready: register an INsg_Pass(Optimizer)." },
            { "health.passes"             , "registered passes: {0}" },
            { "health.emptyMethod"        , "the method body is empty" },
            { "health.methodLength"       , "method is long: {0} statements (threshold {1})" },
            { "health.nesting"            , "nesting is deep: {0} levels (threshold {1})" },
            { "health.locals"             , "too many locals: {0} (threshold {1})" },
            { "health.afterReturn"        , "statements after return can never run" },
            { "health.emptyBody"          , "the control block has no body and does nothing" },
            { "health.constantCondition"  , "the condition is always {0}; the branch is dead code" },
            { "health.shortName"          , "name is too short: '{0}'" },
            { "health.placeholderName"    , "placeholder name: '{0}'" },
            { "health.unusedLocal"        , "local '{0}' is never read after being declared" },
            { "health.memberChain"        , "member access chain is long: {0} (threshold {1})" },
            { "health.expressionSize"     , "expression is large: {0} nodes (threshold {1})" },
            { "health.duplicate"          , "duplicated fragment: this structure appears {0} times" },
            { "health.magicNumber"        , "magic number {0} appears {1} times" },
            { "health.escapeRatio"        , "raw snippets are {0}% of the code (threshold {1}%), which means the subset is too narrow" },
            { "health.danglingInput"      , "required input '{0}' is empty; the generated code will be missing an argument" },
            { "health.cycle"              , "circular dependency: the expression reaches itself through its inputs, so it cannot be generated" },
            { "health.leak"               , "possible leak: {0} used {2} time(s), {1} only {3}" },
            { "health.fix"                , "Suggested fix" },
            { "health.fixApply"           , "Apply" },
            { "health.fixNone"            , "No automatically applicable fix" },
            { "health.fixBreakLink"       , "Break the link that closes the cycle" },
            { "health.fixFillZero"        , "Fill with a 0 placeholder" },
            { "health.fixAddRelease"      , "Append the matching release call" },
            { "msg.lexUnknown"            , "unrecognized character '{0}'" },
            { "msg.expect"                , "expected '{0}', found '{1}'" },
            { "msg.expectIdent"           , "expected an identifier, found '{0}'" },
            { "msg.outOfSubset"           , "this statement is outside the subset; kept as a raw snippet (cannot be turned back into blocks)" },
            { "msg.foreachName"           , "foreach is missing the iteration variable name" },
            { "msg.noSource"              , "source file not found: {0}" },
            { "msg.importFailed"          , "failed to import {0}: {1}" },
            { "msg.canonicalized"         , "{0} methods are not in canonical form (missing braces, inconsistent indentation, or one of several equivalent spellings). The first \"Blocks → Code\" will normalise them; semantics are unchanged but formatting will change." },
            { "msg.noModelRender"         , "there is no block model to render." },
            { "msg.noModelGenerate"       , "there is no block model to generate." },
            { "msg.methodCount"           , "the number of methods on disk ({0}) does not match the block file ({1}). Re-import the code first, or align them by hand, then generate." },
            { "msg.methodName"            , "method #{0} does not match: disk has '{1}', the block file has '{2}'." },
            { "msg.unknownBlock"          , "block '{0}' is not in the library." },
            { "msg.unknownBlockHint"      , "block '{0}' is not in the library; add the matching JSON under Blocks/." },
            { "msg.unknownStmt"           , "unknown block '{0}', cannot translate back to code. Install the matching block definition or use a raw snippet block." },
            { "msg.unknownExpr"           , "unknown expression block '{0}', cannot translate back to code." },
            { "diag.summary"              , "errors {0} / warnings {1} / info {2}" },
            { "cat.var"                   , "Variables" },
            { "cat.ctrl"                  , "Control" },
            { "cat.expr"                  , "Expressions" },
            { "cat.raw"                   , "Escape hatch" },
            { "cat.api"                   , "API" },
            { "cat.frame"                 , "Structure" },
            { "cat.apiAll"                , "All" },
            { "cat.macro"                 , "My blocks" },
            { "macro.create"              , "Create custom block" },
            { "macro.saved"               , "custom block saved: {0}" },
            { "macro.cannot"              , "this node cannot become a block" },
            { "btn.apiAll"                , "Build API library for whole project" },
            { "btn.manageFolder"          , "Manage selected folder" },
            { "btn.manageAll"             , "Manage whole project" },
            { "btn.unmanageFolder"        , "Release selected folder" },
            { "btn.unmanageAll"           , "Release whole project" },
            { "menu.apiAll"               , "Build API Library for Whole Project" },
            { "menu.manageFolder"         , "Take Selected Folder Under Management" },
            { "menu.manageAll"            , "Take Whole Project Under Management" },
            { "api.projectPrompt"         , "Scanned {0} source files. API blocks will be generated for every public method into:\n{1}\n\nOn a large project this can create thousands of blocks and take a while. Continue?" },
            { "api.noSources"             , "No source files were found." },
            { "manage.title"              , "Bulk management" },
            { "manage.prompt"             , "{0} source files will be taken under management; each gets its own .nsg.json and is imported into blocks. On a large project this takes a while. Continue?" },
            { "manage.nothing"            , "There are no new files to manage." },
            { "manage.done"               , "Managed {0} files, {1} failed." },
            { "manage.unmanagePrompt"     , "{0} managed files will be released: the .nsg.json next to each one is deleted and every script becomes an ordinary file again. The .cs itself is never touched. Continue?" },
            { "manage.unmanageNothing"    , "There are no managed files to release." },
            { "manage.unmanageDone"       , "Released {0} files, {1} failed." },
            { "win.pipeline"              , "Pipeline" },
            { "win.pipelineAuto"          , "Auto ({0})" },
            { "btn.shell"                 , "Generate pipeline shell" },
            { "menu.shell"                , "Generate ShaderLab Shell" },
            { "shader.shellTitle"         , "Generate ShaderLab shell" },
            { "shader.shellPrompt"        , "The current file will be overwritten with a complete ShaderLab shell for the {0} pipeline. Everything currently in the file is discarded. Continue?" },
            { "shader.needShader"         , "Select a .shader file first." },
            { "shader.shellDone"          , "Generated {0} pipeline shell: {1}" },
            { "drag.blocks"               , "{0} block(s)" },
            { "preset.title"              , "Presets" },
            { "preset.save"               , "Save selection as preset" },
            { "preset.namePrompt"         , "Preset name" },
            { "preset.none"               , "No presets yet. Select a clump of blocks and press the button above." },
            { "preset.confirmDelete"      , "Delete preset \"{0}\"? This cannot be undone." },
            { "preset.dragHint"           , "Drag onto the canvas to insert, or click to append" },
            { "preset.noSelection"        , "Select some blocks first." },
            { "preset.notConnected"       , "The selection is not one connected clump ({0} separate pieces). Select a single connected group." },
            { "preset.multiGraph"         , "The selection spans several methods. Select within one method." },
            { "preset.tooBig"             , "Too big: {0} blocks, limit is {1}." },
            { "preset.empty"              , "The preset has no blocks." },
            { "preset.needName"           , "Enter a preset name." },
            { "scratch.hint"              , "Detached blocks (not compiled)" },
            { "menu.open"                 , "Open NekoScriptGraph" },
            { "menu.problems"             , "Problems" },
            { "menu.health"               , "Architecture Health" },
            { "menu.manage"               , "Take Selected Script Under Management" },
            { "menu.unmanage"             , "Release Selected Script" },
            { "menu.api"                  , "Generate API Blocks for Selected Folder" },
            { "menu.languages"            , "Languages: Show Loaded" },
            { "menu.reload"               , "Reload Block Library" },
            { "menu.export"               , "Export Default Block Library" },
            { "menu.selftest"             , "Self Test: Round Trip" },
            { "palette.empty"             , "（no blocks available）" },
            { "palette.search"            , "Search blocks…" },
            { "palette.other"             , "Other" },
            { "palette.noMatch"           , "No matching blocks" },
            { "palette.found"             , "{0} blocks found" },
            { "btn.fit"                   , "Fit to view" },
            { "btn.undo"                  , "Undo" },
            { "btn.redo"                  , "Redo" },
            { "btn.checkpoint"            , "Checkpoints" },
            { "btn.git"                   , "Git checkpoint" },
            { "btn.sprites"               , "Use sprites" },
            { "btn.api"                   , "Generate API blocks for selected folder" },
            { "cp.save"                   , "Save to checkpoint {0}" },
            { "cp.overwrite"              , "Save to checkpoint {0} (overwrite: {1})" },
            { "cp.load"                   , "Load checkpoint {0} ({1})" },
            { "cp.empty"                  , "Checkpoint {0} (empty)" },
            { "cp.delete"                 , "Delete checkpoint {0}" },
            { "cp.saved"                  , "Saved to checkpoint {0}" },
            { "cp.loaded"                 , "Loaded checkpoint {0}" },
            { "cp.confirmLoad"            , "Loading a checkpoint overwrites the current code and blocks. Continue?" },
            { "git.notRepo"               , "This project is not a git repository." },
            { "git.noChanges"             , "No changes for this script." },
            { "git.done"                  , "Git checkpoint created." },
            { "git.failed"                , "Git commit failed: " },
            { "git.note"                  , "Built-in checkpoints live in .checkpoints (git-ignored) and can never clash with the repository history." },
            { "win.title"                 , "NekoScriptGraph" },
            { "win.lang"                  , "Language" },
            { "view.mode"                 , "View" },
            { "view.stack"                , "Stack (Scratch)" },
            { "view.blueprint"            , "Blueprint (UE)" },
            { "bp.tidy"                   , "Auto arrange" },
            { "ribbon.tab.home"           , "Home" },
            { "ribbon.tab.manage"         , "Manage" },
            { "ribbon.tab.tools"          , "Tools" },
            { "ribbon.tab.view"           , "View" },
            { "ribbon.group.script"       , "Script" },
            { "ribbon.group.edit"         , "Edit" },
            { "ribbon.group.zoom"         , "Zoom" },
            { "ribbon.group.file"         , "This file" },
            { "ribbon.group.folder"       , "Folder" },
            { "ribbon.group.project"      , "Whole project" },
            { "ribbon.group.api"          , "API" },
            { "ribbon.group.inspect"      , "Inspect" },
            { "ribbon.group.version"      , "Version" },
            { "ribbon.group.library"      , "Library" },
            { "ribbon.group.lang"         , "Language" },
            { "ribbon.group.mode"         , "Mode" },
            { "ribbon.group.files"        , "Config files" },
            { "files.btnHide"             , "Hide config files" },
            { "files.btnShow"             , "Show config files" },
            { "files.hidden"              , "{0} block config files hidden" },
            { "files.shown"               , "{0} block config files shown" },
            { "files.hotkeyHint"          , "Shortcut: Ctrl/Cmd+Shift+H" },
            { "win.noDoc"                 , "No file open" },
            { "win.none"                  , "—" },
            { "btn.open"                  , "Open selected script" },
            { "btn.manage"                , "Take under management" },
            { "btn.unmanage"              , "Release management" },
            { "btn.import"                , "Code → Blocks" },
            { "btn.generate"              , "Blocks → Code" },
            { "btn.preview"               , "Preview" },
            { "btn.errors"                , "Problems" },
            { "btn.selftest"              , "Self test" },
            { "btn.library"               , "Reload block library" },
            { "btn.expandAll"             , "Expand all" },
            { "btn.collapseAll"           , "Collapse all" },
            { "state.unmanaged"           , "Free script (no block file)" },
            { "state.synced"              , "In sync" },
            { "state.csdirty"             , "Code changed" },
            { "state.blocksdirty"         , "Blocks changed" },
            { "state.conflict"            , "Conflict: both sides changed" },
            { "confirm.alreadyManaged"    , "is already managed." },
            { "confirm.notManaged"        , "has no block file; it is already a free script." },
            { "confirm.title"             , "NekoScriptGraph" },
            { "confirm.manage"            , "Create the block file and import now?" },
            { "confirm.manageYes"         , "Create and import" },
            { "confirm.unmanage"          , "Delete the block file? The script becomes free again." },
            { "confirm.unmanageYes"       , "Delete block file" },
            { "confirm.overwriteConflict" , "Both sides changed. Overwriting discards manual edits inside method bodies (fields, attributes and comments are kept). Continue?" },
            { "confirm.reimportConflict"  , "The blocks have changes that were never written back. Re-importing discards them. Continue?" },
            { "confirm.continue"          , "Continue" },
            { "confirm.cancel"            , "Cancel" },
            { "confirm.ok"                , "OK" },
            { "confirm.selectCs"          , "Select a .cs file in the Project window first." },
            { "confirm.selectSource"      , "Select a source file of a supported language in the Project window first." },
            { "confirm.selectFolder"      , "Select a folder in the Project window first." },
            { "pick.statement"            , "Insert a statement block" },
            { "pick.expression"           , "Insert an expression block" },
            { "pick.search"               , "Search" },
            { "pick.empty"                , "(empty)" },
            { "pick.clickToAdd"           , "Click to add a block" },
            { "pick.noMatch"              , "No matching block" },
            { "blk.menu"                  , "Block actions" },
            { "blk.up"                    , "Move up" },
            { "blk.down"                  , "Move down" },
            { "blk.dup"                   , "Duplicate" },
            { "blk.del"                   , "Delete" },
            { "blk.replace"               , "Replace" },
            { "blk.insertAbove"           , "Insert above" },
            { "blk.insertBelow"           , "Insert below" },
            { "blk.editRaw"               , "Edit raw snippet" },
            { "blk.rename"                , "Rename" },
            { "blk.body"                  , "body" },
            { "blk.else"                  , "else" },
            { "tree.namespace"            , "namespace" },
            { "tree.class"                , "class" },
            { "tree.struct"               , "struct" },
            { "tree.interface"            , "interface" },
            { "tree.enum"                 , "enum" },
            { "tree.method"               , "method" },
            { "tree.raw"                  , "member (kept verbatim)" },
            { "tree.using"                , "using" },
            { "tree.base"                 , "extends" },
            { "tree.noBase"               , "(none)" },
            { "tree.customBase"           , "Custom…" },
            { "tree.bodyEmpty"            , "(empty)" },
            { "err.title"                 , "NekoScriptGraph Problems" },
            { "err.none"                  , "No diagnostics." },
            { "err.clear"                 , "Clear" },
            { "err.goto"                  , "Go to" },
            { "err.error"                 , "Error" },
            { "err.warning"               , "Warning" },
            { "err.info"                  , "Info" },
            { "err.count"                 , "errors {0} / warnings {1} / info {2}" },
            { "err.manageFailed"          , "Import failed" },
            { "err.generateFailed"        , "Overwrite failed" },
            { "err.reimportFailed"        , "Re-import failed" },
            { "err.details"               , "See the Problems window for details." },
            { "api.title"                 , "Generate API block library" },
            { "api.prompt"                , "Generate API blocks for the selected folder?" },
            { "api.done"                  , "Generated {0} API blocks ({1} skipped)." },
            { "api.doneAll"               , "Generated {0} API blocks across all languages ({1} language(s) reported errors)." },
            { "api.noFolder"              , "The selection is not a folder." },
            { "api.folder"                , "API block source folder" },
            { "selftest.title"            , "Round-trip self test" },
            { "selftest.pass"             , "PASS" },
            { "selftest.fail"             , "FAIL" },
            { "selftest.summary"          , "{0} checks, {1} passed, {2} failed" },
            { "info.managed"              , "Now managed: {0}" },
            { "info.unmanaged"            , "Released: {0}" },
            { "info.libraryReloaded"      , "Block library reloaded: {0} blocks." },
            { "info.exported"             , "Default block library written to {0}" },
            { "info.canonicalized"        , "{0} methods are not in canonical form; the first \"Blocks → Code\" will reformat them (semantics unchanged)." },

            // Диагностика самих языковых реализаций. Живёт здесь, а не строкой
            // в коде языка, иначе перевод пришлось бы править в C#.
            { "lang.python.apiNotImplemented", "API block generation is not implemented for Python yet" },
            { "lang.python.unparsed"      , "not parsed: {0}" },

            // Необязательная кошка-ассистент. Без папки ProgramNeko эти строки
            // просто не показываются.
            { "err.explain"               , "Explain" },
            { "neko.noArt"                , "(no portrait yet)" },
            { "blk.explain"               , "Explain this block" },

            // ------------------------------------------------------------------
            // Ворота продвинутых возможностей.
            // Базовое (разбор, печать, диагностика, окно блоков) работает всегда.
            // Продвинутое (подсказки, авто-правки, оптимизаторы) требует кошки.
            // ------------------------------------------------------------------
            { "adv.enabled"               , "Advanced features are on: ProgramNeko is installed and can explain every change." },
            { "adv.disabled"              , "Advanced features are off: the core (parse, print, diagnostics, blocks) works, but hints, auto-fixes and optimisers need ProgramNeko." },
            { "adv.needAssistant"         , "This needs ProgramNeko. Install or re-enable her under Extensions." },

            // ------------------------------------------------------------------
            // Окно настроек
            // ------------------------------------------------------------------
            { "menu.settings"             , "Settings" },
            { "menu.extensions"           , "Extensions" },
            { "set.title"                 , "NekoScriptGraph Settings" },
            { "set.versions"              , "plugin {0} · core {1} ({2})" },
            { "set.tab.general"           , "General" },
            { "set.tab.assistant"         , "ProgramNeko" },
            { "set.tab.extensions"        , "Extensions" },
            { "set.tab.advanced"          , "Advanced" },

            { "set.general.view"          , "Editor" },
            { "set.general.api"           , "API blocks" },
            { "set.general.langNote"      , "Interface language. English is built in and cannot be removed: it is the fallback for every unfinished translation." },

            { "set.assistant.missing"     , "ProgramNeko is not installed" },
            { "set.assistant.missingNote" , "Without her the plugin still parses, prints and reports diagnostics. Her character settings appear here once she is installed." },
            { "set.assistant.noSchema"    , "No character settings are described." },
            { "set.assistant.presets"     , "Presets" },
            { "set.assistant.persona"     , "Character" },

            { "set.persona.speakRate"     , "How often she speaks" },
            { "set.persona.speakRateNote" , "Scales the pause between remarks. Higher means she speaks up sooner." },
            { "set.persona.chatter"       , "Small talk" },
            { "set.persona.chatterNote"   , "Remarks with no reason at all. Set to zero and she only reacts to what happens." },
            { "set.persona.serious"       , "Seriousness" },
            { "set.persona.seriousNote"   , "Biases wording from playful to dry. It reweights existing lines, it does not add new ones." },
            { "set.persona.alert"         , "Interrupt for new errors" },
            { "set.persona.alertNote"     , "When on, a new error makes her speak immediately, past the cooldown." },
            { "set.persona.stuckMoves"    , "Fidget threshold (pointer moves)" },
            { "set.persona.stuckSeconds"  , "Fidget threshold (seconds)" },
            { "set.persona.idleSleep"     , "Falls asleep after" },
            { "set.persona.workRemind"    , "Reminds you to rest after" },
            { "set.persona.explainBudget" , "How many sub-blocks she explains" },
            { "set.persona.explainBudgetNote", "She stops and asks you to select the rest when a block has too many children." },

            { "set.preset.quiet"          , "Quiet" },
            { "set.preset.normal"         , "Normal" },
            { "set.preset.chatty"         , "Chatty" },
            { "set.preset.errorsOnly"     , "Errors only" },

            { "set.ext.locales"           , "Language packs" },
            { "set.ext.localesNote"       , "English is built into the code and cannot be removed: every missing translation falls back to it. Other packs are ordinary folders and can be removed or put back at any time." },
            { "set.ext.none"              , "Nothing found." },
            { "set.ext.enable"            , "Enable" },
            { "set.ext.disable"           , "Remove" },
            { "set.ext.assistant"         , "ProgramNeko" },
            { "set.ext.assistantOn"       , "ProgramNeko is installed" },
            { "set.ext.assistantOff"      , "ProgramNeko is removed (files kept)" },
            { "set.ext.assistantAbsent"   , "ProgramNeko is not present" },
            { "set.ext.assistantNote"     , "Removing ProgramNeko keeps every file and turns off the advanced features. The core keeps working. Put her back with one click." },
            { "set.ext.engines"           , "External engines" },
            { "set.ext.enginesNote"       , "These live outside the plugin package and are installed through the Package Manager. The plugin contains none of their code, so removing one can never break it." },
            { "set.ext.installed"         , "installed" },
            { "set.ext.notInstalled"      , "not installed" },
            { "set.ext.install"           , "Install" },
            { "set.ext.uninstall"         , "Uninstall" },
            { "set.ext.catalog"           , "Show catalog" },
            { "set.ext.refresh"           , "Refresh" },
            { "ext.noSource"              , "No install source is configured for this extension. Add a git URL or a local path to Extensions~/catalog.json." },
            { "ext.reloadFailed"          , "The package state did not match the request after the reload. Check the Package Manager." },
            { "ext.completion.data"       , "Built-in block hints" },
            { "ext.roslyn.name"           , "Roslyn completion engine" },
            { "ext.roslyn.desc"           , "Optional. Real member and overload completion from the C# compiler. Installed as a separate package, requires ProgramNeko." },
            { "ext.roslyn.notFound"       , "Roslyn assemblies were not found. Put Microsoft.CodeAnalysis.dll and Microsoft.CodeAnalysis.CSharp.dll into {0}/, or use an editor that ships them." },

            { "set.adv.gate"              , "Gate" },
            { "set.adv.providers"         , "Hint sources" },
            { "set.adv.priority"          , "priority" },
            { "set.adv.noProviders"       , "No sources registered." },
            { "set.adv.test"              , "Try the external engines" },
            { "set.adv.testNoFile"        , "Select a .cs file in the Project window first." },
            { "set.adv.testRunning"       , "Running…" },
            { "set.adv.testDone"          , "Returned {0} item(s)" },
            { "set.adv.optimizers"        , "Optimisers" },
            { "set.adv.optimizerCount"    , "{0} optimiser pass(es) registered. Optimisers never run without ProgramNeko." },
            { "set.adv.state.ready"       , "ready" },
            { "set.adv.state.warming"     , "warming up" },
            { "set.adv.state.failed"      , "failed" },
            { "set.adv.state.missing"     , "not available" },
        };

        // ------------------------------------------------------------------
        // Обнаруженные языки
        // ------------------------------------------------------------------

        /// <summary>Один язык из Locale/&lt;code&gt;/strings.json.</summary>
        class Locale
        {
            public string Code;
            public string Name;
            public bool Rtl;
            public int Order;

            public readonly Dictionary<string, string> Ui = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Blocks = new Dictionary<string, string>();
        }

        [System.Serializable]
        class LocaleEntry
        {
            public string key;
            public string value;
        }

        [System.Serializable]
        class LocaleFile
        {
            public int schemaVersion = 1;
            public string code;
            public string name;
            public bool rtl;
            public int order;
            public List<LocaleEntry> ui = new List<LocaleEntry>();
            public List<LocaleEntry> blocks = new List<LocaleEntry>();
        }

        static readonly List<Locale> _locales = new List<Locale>();
        static bool _scanned;
        static int _lang = EnIndex;

        /// <summary>Хук восстановления. Ставится слоем редактора при запуске.</summary>
        public static System.Func<int> Restore;

        /// <summary>Хук сохранения. Ставится слоем редактора при запуске.</summary>
        public static System.Action<int> Persist;

        /// <summary>Английский плюс все найденные папки Locale.</summary>
        public static int LanguageCount
        {
            get { Scan(); return _locales.Count + 1; }
        }

        public static string LanguageName(int index)
        {
            Scan();
            if (index <= EnIndex || index > _locales.Count) return "English";
            return _locales[index - 1].Name;
        }

        public static int Lang
        {
            get { return _lang; }
            set
            {
                Scan();
                if (value < 0 || value > _locales.Count) value = EnIndex;
                _lang = value;
                if (Persist != null) Persist(_lang);
            }
        }

        public static bool IsEnglish
        {
            get { return _lang == EnIndex; }
        }

        /// <summary>
        /// Код текущего языка («zh», «zh-Hant», «en»). Нужен тем, у кого свои
        /// языковые пакеты: кошка-ассистент ищет папку по этому коду, а не по
        /// индексу, — индекс зависит от состава папок, код стабилен.
        /// </summary>
        public static string LangCode
        {
            get
            {
                Scan();
                int i = _lang - 1;
                if (i >= 0 && i < _locales.Count) return _locales[i].Code;
                return "en";
            }
        }

        /// <summary>Язык пишется справа налево: интерфейс зеркалится.</summary>
        public static bool IsRtl
        {
            get
            {
                Scan();
                int i = _lang - 1;
                return i >= 0 && i < _locales.Count && _locales[i].Rtl;
            }
        }

        /// <summary>Читает сохранённый язык. Вызывается слоем редактора.</summary>
        public static void LoadFromHook()
        {
            if (Restore == null) return;
            Lang = Restore();
        }

        // ------------------------------------------------------------------
        // Перевод
        // ------------------------------------------------------------------

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            if (_lang != EnIndex)
            {
                Scan();
                int i = _lang - 1;
                if (i >= 0 && i < _locales.Count)
                {
                    string value;
                    if (_locales[i].Ui.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;
                }
            }

            // Незаполненный ключ откатывается к английскому, а не к первому
            // попавшемуся языку: иначе новый язык внезапно говорил бы по-китайски.
            string en;
            return English.TryGetValue(key, out en) ? en : key;
        }

        public static string T(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        /// <summary>
        /// Подпись блока из файла языка. null — перевода нет, и вызывающий
        /// откатывается к английской подписи самого блока.
        /// </summary>
        public static string BlockLabel(string blockId)
        {
            if (string.IsNullOrEmpty(blockId) || _lang == EnIndex) return null;

            Scan();
            int i = _lang - 1;
            if (i < 0 || i >= _locales.Count) return null;

            string value;
            return _locales[i].Blocks.TryGetValue(blockId, out value) && !string.IsNullOrEmpty(value)
                ? value
                : null;
        }

        // ------------------------------------------------------------------
        // Обнаружение папок Locale/<code>/
        // ------------------------------------------------------------------

        static void Scan()
        {
            if (_scanned) return;
            _scanned = true;

            string root = Nsg_Paths.LocaleDir;
            if (!Directory.Exists(root)) return;

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return; }

            for (int i = 0; i < dirs.Length; i++)
            {
                string file = Path.Combine(dirs[i], "strings.json");
                if (!File.Exists(file)) continue;

                LocaleFile data = null;
                try { data = JsonUtility.FromJson<LocaleFile>(File.ReadAllText(file)); }
                catch { data = null; }
                if (data == null) continue;

                var loc = new Locale
                {
                    Code = string.IsNullOrEmpty(data.code) ? Path.GetFileName(dirs[i]) : data.code,
                    Name = data.name,
                    Rtl = data.rtl,
                    Order = data.order
                };
                if (string.IsNullOrEmpty(loc.Name)) loc.Name = loc.Code;

                Fill(loc.Ui, data.ui);
                Fill(loc.Blocks, data.blocks);
                _locales.Add(loc);
            }

            // Порядок задаётся полем order: файловая система сортирует папки по
            // имени, и без этого 简体中文 уезжал бы в самый конец списка.
            _locales.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Code, b.Code);
            });
        }

        static void Fill(Dictionary<string, string> into, List<LocaleEntry> entries)
        {
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || string.IsNullOrEmpty(e.key)) continue;
                into[e.key] = e.value;
            }
        }

        /// <summary>Сбрасывает кэш: вызывается после правки папки Locale.</summary>
        public static void Reload()
        {
            _scanned = false;
            _locales.Clear();
            Scan();
        }
    }
}
