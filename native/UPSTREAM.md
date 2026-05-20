# Codex Rust Upstream

TinadecCode uses Codex Rust as a mature upstream capability source while keeping TinadecCore as the product-level agent runtime and state owner.

## Layering Rule

- Core owns general agent runtime capabilities that TinadecDoc, TinadecData, and TinadecCode all need.
- Code owns programming-domain capabilities such as search, patch, sandbox, shell, watcher, tests, and review formatting.
- Rust is an implementation and ecosystem source, not a layering rule.

## Upstream Source

- Repository: `https://github.com/openai/codex`
- Local checkout used by this workspace: `D:\github\CodeX Rust`
- Codex Rust workspace path: `D:\github\CodeX Rust\codex-rs`
- Current local upstream commit: `14953023471159aaed89f360c0f3da2346cb4bc0`
- Optional vendored subtree path: `native/codex-src`
- Pinning rule: record the exact upstream commit before replacing any stub implementation.

The first native Code-layer integration uses a local path dependency on
`codex-file-search`, keeping the Tinadec-facing JSON contract stable while the
implementation moves to Codex Rust. If the upstream checkout is relocated, update
the path dependency in `native/glue/code-native/Cargo.toml` or vendor the exact
commit with:

```powershell
git subtree add --prefix native/codex-src https://github.com/openai/codex.git <commit-sha> --squash
```

Then replace the implementations under `native/glue/*` incrementally, keeping the public Core/Code contracts stable.
