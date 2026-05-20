use codex_apply_patch::{AppliedPatchFileChange, apply_patch};
use codex_exec_server::LOCAL_FS;
use codex_file_search::{FileSearchOptions, run};
use codex_utils_absolute_path::AbsolutePathBuf;
use serde::Deserialize;
use serde_json::{Map, Value, json};
use std::io::{self, Read};
use std::num::NonZero;
use std::path::PathBuf;

#[derive(Debug, Deserialize)]
struct ExecuteRequest {
    tool_id: String,
    session_id: Option<String>,
    run_id: Option<String>,
    task_node_id: Option<String>,
    approval_id: Option<String>,
    cwd: Option<String>,
    #[serde(default)]
    arguments: Value,
}

fn main() {
    let mut args = std::env::args().skip(1);
    match args.next().as_deref() {
        Some("execute") => execute(),
        Some("version") | None => print_json(json!({
            "name": "tinadec-code-native",
            "version": "0.1.0",
            "upstream": "codex-rust",
            "upstream_commit": "14953023471159aaed89f360c0f3da2346cb4bc0"
        })),
        Some(_) => {
            eprintln!("unknown command");
            std::process::exit(2);
        }
    }
}

fn execute() {
    let mut input = String::new();
    if io::stdin().read_to_string(&mut input).is_err() {
        eprintln!("failed to read stdin");
        std::process::exit(1);
    }

    let request = match serde_json::from_str::<ExecuteRequest>(&input) {
        Ok(request) => request,
        Err(error) => {
            print_json(failed_result(
                "unknown",
                format!("invalid native tool request: {error}"),
                false,
                None,
                json!({}),
            ));
            return;
        }
    };

    let result = match request.tool_id.as_str() {
        "search_files" => execute_search_files(&request),
        "apply_patch" => execute_apply_patch(&request),
        "sandbox_exec" => execute_sandbox_exec(&request),
        "review_format" => native_result(
            &request,
            "Review formatter native bridge is active; Codex review formatting will be wired here next.",
            common_data(
                &request,
                json!({
                    "cwd": request.cwd,
                    "argument_keys": argument_keys(&request.arguments)
                }),
            ),
        ),
        _ => failed_result(
            &request.tool_id,
            format!("unknown native tool '{}'", request.tool_id),
            false,
            None,
            common_data(
                &request,
                json!({ "argument_keys": argument_keys(&request.arguments) }),
            ),
        ),
    };

    print_json(result);
}

fn execute_search_files(request: &ExecuteRequest) -> Value {
    let query = request
        .arguments
        .get("query")
        .and_then(Value::as_str)
        .or_else(|| request.arguments.get("pattern").and_then(Value::as_str))
        .unwrap_or_default();

    if query.trim().is_empty() {
        return failed_result(
            &request.tool_id,
            "search_files requires a non-empty query argument.",
            false,
            None,
            common_data(
                request,
                json!({
                    "cwd": request.cwd,
                    "argument_keys": argument_keys(&request.arguments)
                }),
            ),
        );
    }

    let cwd = match request.cwd.as_deref() {
        Some(value) => PathBuf::from(value),
        None => match std::env::current_dir() {
            Ok(value) => value,
            Err(error) => {
                return failed_result(
                    &request.tool_id,
                    format!("failed to resolve current directory: {error}"),
                    false,
                    None,
                    common_data(request, json!({})),
                );
            }
        },
    };

    let limit = request
        .arguments
        .get("limit")
        .and_then(Value::as_u64)
        .and_then(|value| usize::try_from(value).ok())
        .and_then(NonZero::new)
        .unwrap_or_else(|| NonZero::new(20).expect("20 is non-zero"));

    let exclude = request
        .arguments
        .get("exclude")
        .and_then(Value::as_array)
        .map(|items| {
            items
                .iter()
                .filter_map(Value::as_str)
                .map(ToString::to_string)
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();

    match run(
        query,
        vec![cwd.clone()],
        FileSearchOptions {
            limit,
            exclude,
            threads: NonZero::new(2).expect("2 is non-zero"),
            compute_indices: false,
            respect_gitignore: true,
        },
        None,
    ) {
        Ok(results) => {
            let matches = results
                .matches
                .iter()
                .map(|item| {
                    json!({
                        "score": item.score,
                        "path": item.path.to_string_lossy(),
                        "full_path": item.full_path().to_string_lossy(),
                        "match_type": format!("{:?}", item.match_type).to_lowercase()
                    })
                })
                .collect::<Vec<_>>();

            native_result(
                request,
                format!(
                    "Codex Rust file search returned {} of {} matches.",
                    matches.len(),
                    results.total_match_count
                ),
                common_data(
                    request,
                    json!({
                        "cwd": cwd.to_string_lossy(),
                        "query": query,
                        "total_match_count": results.total_match_count,
                        "matches": matches
                    }),
                ),
            )
        }
        Err(error) => failed_result(
            &request.tool_id,
            format!("Codex Rust file search failed: {error}"),
            false,
            None,
            common_data(
                request,
                json!({
                    "cwd": cwd.to_string_lossy(),
                    "query": query
                }),
            ),
        ),
    }
}

fn execute_apply_patch(request: &ExecuteRequest) -> Value {
    if request.approval_id.as_deref().unwrap_or_default().trim().is_empty() {
        return blocked_result(
            request,
            "Apply a patch that may modify workspace files.",
        );
    }

    let patch = request
        .arguments
        .get("patch")
        .and_then(Value::as_str)
        .unwrap_or_default();
    if patch.trim().is_empty() {
        return failed_result(
            &request.tool_id,
            "apply_patch requires a non-empty patch argument.",
            false,
            None,
            common_data(
                request,
                json!({ "argument_keys": argument_keys(&request.arguments) }),
            ),
        );
    }

    let cwd = match resolve_absolute_cwd(request) {
        Ok(value) => value,
        Err(error) => {
            return failed_result(
                &request.tool_id,
                error,
                false,
                None,
                common_data(request, json!({})),
            );
        }
    };

    let runtime = match tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
    {
        Ok(runtime) => runtime,
        Err(error) => {
            return failed_result(
                &request.tool_id,
                format!("failed to start apply_patch runtime: {error}"),
                false,
                None,
                common_data(request, json!({ "cwd": cwd.to_string_lossy() })),
            );
        }
    };

    let mut stdout = Vec::new();
    let mut stderr = Vec::new();
    let result = runtime.block_on(apply_patch(
        patch,
        &cwd,
        &mut stdout,
        &mut stderr,
        LOCAL_FS.as_ref(),
        None,
    ));

    let stdout_text = String::from_utf8_lossy(&stdout).to_string();
    let stderr_text = String::from_utf8_lossy(&stderr).to_string();
    match result {
        Ok(delta) => {
            let changes = delta
                .changes()
                .iter()
                .map(|change| {
                    let kind = match &change.change {
                        AppliedPatchFileChange::Add { .. } => "add",
                        AppliedPatchFileChange::Delete { .. } => "delete",
                        AppliedPatchFileChange::Update { .. } => "update",
                    };
                    json!({
                        "path": change.path.to_string_lossy(),
                        "kind": kind
                    })
                })
                .collect::<Vec<_>>();

            native_result(
                request,
                format!("Codex Rust apply_patch applied {} file changes.", changes.len()),
                common_data(
                    request,
                    json!({
                        "cwd": cwd.to_string_lossy(),
                        "stdout": stdout_text,
                        "stderr": stderr_text,
                        "delta_exact": delta.is_exact(),
                        "affected_files": changes
                    }),
                ),
            )
        }
        Err(error) => {
            let (apply_error, delta) = error.into_parts();
            let partial_changes = delta
                .changes()
                .iter()
                .map(|change| change.path.to_string_lossy().to_string())
                .collect::<Vec<_>>();
            failed_result(
                &request.tool_id,
                format!("Codex Rust apply_patch failed: {apply_error}"),
                false,
                None,
                common_data(
                    request,
                    json!({
                        "cwd": cwd.to_string_lossy(),
                        "stdout": stdout_text,
                        "stderr": stderr_text,
                        "delta_exact": delta.is_exact(),
                        "partial_files": partial_changes
                    }),
                ),
            )
        }
    }
}

fn execute_sandbox_exec(request: &ExecuteRequest) -> Value {
    if request.approval_id.as_deref().unwrap_or_default().trim().is_empty() {
        return blocked_result(
            request,
            "Run a sandboxed command in the workspace.",
        );
    }

    let command = request
        .arguments
        .get("command")
        .and_then(Value::as_array)
        .map(|items| items.iter().filter_map(Value::as_str).collect::<Vec<_>>())
        .unwrap_or_default();

    let sandbox_status = if cfg!(target_os = "windows") {
        "unsupported:windows-execution-disabled-in-v1"
    } else {
        "unsupported:execution-disabled-in-v1"
    };

    failed_result(
        &request.tool_id,
        "sandbox_exec is approval-cleared, but execution is not enabled in this Windows-first slice. Codex sandbox capability discovery is wired and returned as structured unsupported.",
        false,
        None,
        common_data(
            request,
            json!({
                "cwd": request.cwd,
                "command": command,
                "exit_code": null,
                "stdout": "",
                "stderr": "",
                "sandbox_status": sandbox_status,
                "sandbox_supported": false
            }),
        ),
    )
}

fn native_result(request: &ExecuteRequest, summary: impl Into<String>, data: Value) -> Value {
    json!({
        "tool_id": request.tool_id,
        "status": "native",
        "summary": summary.into(),
        "evidence": [
            "domain: programming",
            "state_owner: core",
            "native_runtime: tinadec-code-native",
            "upstream: codex-rust"
        ],
        "data": data,
        "requires_approval": false,
        "approval_summary": null
    })
}

fn blocked_result(request: &ExecuteRequest, approval_summary: &str) -> Value {
    json!({
        "tool_id": request.tool_id,
        "status": "blocked",
        "summary": "Native programming tool is registered, but Core approval is required before execution.",
        "evidence": [
            "domain: programming",
            "state_owner: core",
            "native_runtime: tinadec-code-native",
            "policy_owner: core"
        ],
        "data": common_data(
            request,
            json!({
                "cwd": request.cwd,
                "argument_keys": argument_keys(&request.arguments)
            })
        ),
        "requires_approval": true,
        "approval_summary": approval_summary
    })
}

fn failed_result(
    tool_id: &str,
    summary: impl Into<String>,
    requires_approval: bool,
    approval_summary: Option<&str>,
    data: Value,
) -> Value {
    json!({
        "tool_id": tool_id,
        "status": "failed",
        "summary": summary.into(),
        "evidence": [
            "domain: programming",
            "state_owner: core",
            "native_runtime: tinadec-code-native"
        ],
        "data": data,
        "requires_approval": requires_approval,
        "approval_summary": approval_summary
    })
}

fn resolve_absolute_cwd(request: &ExecuteRequest) -> Result<AbsolutePathBuf, String> {
    let path = match request.cwd.as_deref() {
        Some(value) => PathBuf::from(value),
        None => std::env::current_dir()
            .map_err(|error| format!("failed to resolve current directory: {error}"))?,
    };
    let absolute = if path.is_absolute() {
        path
    } else {
        std::env::current_dir()
            .map_err(|error| format!("failed to resolve current directory: {error}"))?
            .join(path)
    };
    AbsolutePathBuf::from_absolute_path(&absolute)
        .map_err(|error| format!("cwd must resolve to an absolute path: {error}"))
}

fn argument_keys(arguments: &Value) -> Vec<String> {
    let mut keys = arguments
        .as_object()
        .map(|object| object.keys().cloned().collect::<Vec<_>>())
        .unwrap_or_default();
    keys.sort();
    keys
}

fn common_data(request: &ExecuteRequest, data: Value) -> Value {
    let mut object = data.as_object().cloned().unwrap_or_else(Map::new);
    object.insert("session_id".to_string(), json!(request.session_id));
    object.insert("run_id".to_string(), json!(request.run_id));
    object.insert("task_node_id".to_string(), json!(request.task_node_id));
    object.insert("approval_id".to_string(), json!(request.approval_id));
    Value::Object(object)
}

fn print_json(value: Value) {
    match serde_json::to_string(&value) {
        Ok(output) => println!("{output}"),
        Err(error) => {
            eprintln!("failed to serialize native tool response: {error}");
            std::process::exit(1);
        }
    }
}
