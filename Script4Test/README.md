# Script4Test

One short file per language, written to the **NSG writing subset** on purpose. They exist
to answer one question: *can this language express itself through blocks without falling
back to raw snippets?*

| Language | File | Shows off |
|---|---|---|
| Java | `java/Script4Test.java` | `List<Integer>` / `Map<String,Integer>`, `new ArrayList<Integer>()`, ternary, bit ops, early `++` |
| C# | `cs/Script4Test.cs` | `List<int>` / `Dictionary<string,int>`, indexer read **and** write, `Count` property, ternary, bit ops |
| C | `c/Script4Test.c` | fixed array, nested `while`, ternary, casts |
| C++ | `cpp/Script4Test.cpp` | `std::vector<int>` / `std::map<std::string,int>`, `push_back`, `(int)` cast |
| Rust | `rust/script4test.rs` | `let mut` with types, nested `while`, `%`, bit ops |
| Go | `go/script4test.go` | `:=` style declarations, `for cond {}`, `%` |
| Swift | `swift/Script4Test.swift` | `var` / `let` with types, `? :`, bit ops |
| Python | `python/script4test.py` | no declarations, `while`, `%` |
| HLSL | `hlsl/Script4Test.hlsl` | `max` / `dot` intrinsics, `while`, ternary |

Every file is short, has **no dependency outside its own language**, and uses no Unity
API.

## What the subset actually is

Only **method bodies** become blocks. The shell around them — `import`, `package`, `#include`,
the class or function signature — is carried through verbatim and never has to be
expressible. That is why generics in a *signature* are always safe.

Inside a body, the vocabulary is 28 blocks:

| Kind | Blocks |
|---|---|
| Statements | `stmt.localDecl`, `stmt.assign`, `stmt.add` / `sub` / `mul` / `div` / `mod`, `stmt.expr`, `stmt.return`, `stmt.break`, `stmt.continue` |
| Control | `stmt.if` (+ else), `stmt.while`, `stmt.for`, `stmt.foreach` |
| Expressions | `expr.ident`, `expr.literal`, `expr.binary`, `expr.unary`, `expr.postfix`, `expr.cast`, `expr.conditional`, `expr.member`, `expr.index`, `expr.call`, `expr.new` |
| Escape | `expr.raw`, `stmt.raw` |

**Generics are expressible, but only in a text socket.** `stmt.localDecl`, `stmt.foreach`,
`expr.new` and `expr.cast` all take a free-text `type`. `List<Integer>` there is a normal
block value. A generic in a position the grammar has to *parse* — `foo.<Integer>bar()`,
`x is List<int>` — is not, and becomes a raw snippet.

Operators are a closed set, taken from the block definitions:

- `expr.binary`: `+ - * / % == != < > <= >= && || ?? & | ^ << >>`
- `expr.unary`: `! - + ~ ++ --`
- `expr.postfix`: `++ --`

## Deliberately not used

Anything outside the subset is preserved verbatim as a raw snippet and reported as
`NSG0002`, which would defeat the point of the pack. So these are avoided:

- lambdas, closures, anonymous functions
- `try` / `catch` / `throw`
- `switch` / `case`
- generics in *parsed* expression positions (see above)
- array or collection literals (`{1,2,3}`, `[1,2,3]`, `[]int{...}`)
- string interpolation and formatted strings
- attributes, decorators, annotations
- pointer syntax (`->`, `*` dereference)

## Verifying the pack

The pack is written to the subset, but "written to the subset" is a claim — check it:

1. **Editor**: put a file under management (`Take Selected Script Under Management`), open
   the block editor, and read the escape ratio in **Architecture Health**. It must be `0`.
2. **Agent / CLI**: `nsg_plan` on a file reports diagnostics, block counts and
   `escapeRatio`. Gate on `maxEscapeRatio: 0` to make any escape a hard failure.
3. **Bulk**: `Self Test: Round Trip` runs the built-in round-trip cases, which exercise
   the same graph → code → graph path the pack depends on.

If a file reports an escape, the construct is outside the subset — adjust the file rather
than widening the subset, unless the construct is genuinely worth a new block.
