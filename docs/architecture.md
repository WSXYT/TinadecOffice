# TinadecCode Architecture

TinadecCode is split into four product responsibilities:

- `src/TinadecCore`: portable C# Core framework and runtime. It owns agents, runs, task graphs, context packs, supervision, approvals, model routes, events, secrets, permissions, capability discovery, and SQLite persistence.
- `native/glue/*`: Codex Rust glue. Core treats Codex as the mature kernel/tool capability source and calls it through stable adapters instead of reimplementing file search, patch, sandbox, and related primitives.
- `apps/gateway`: TinadecCode Elysia BFF/API layer. It exposes `/api/v1/*`, OpenAPI docs at `/docs`, and proxies to the Core runtime.
- `apps/desktop`: TinadecCode Desktop, built with Electron + Vue. The renderer receives only the `window.tinadec.*` preload API and talks to TinadecCode over HTTP/SSE.

Core is the only state authority. Gateway and Desktop must not keep session state, approval decisions, model routing state, tool policy state, or provider lifecycle state.

## Default Ports

- TinadecCode Elysia API: `http://127.0.0.1:48730`
- TinadecCore runtime: `http://127.0.0.1:48731`
- Vite renderer: `http://127.0.0.1:5173`

## Event Envelope

All runtime events use:

```json
{
  "v": "1.0",
  "type": "message.created",
  "request_id": "req_xxx",
  "session_id": "sess_xxx",
  "trace_id": "trace_xxx",
  "seq": 1,
  "ts": "2026-05-18T10:15:30Z",
  "capabilities": ["agent.message"],
  "payload": {},
  "error": null
}
```

## Run Locally

```powershell
npm install
npm run restore:dotnet
npm run dev
```

The local environment currently contains `Version=V7.24.42SP3`, which breaks MSBuild version parsing. Root npm scripts remove that variable only for the child .NET process.
