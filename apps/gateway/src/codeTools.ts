import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export interface CodeToolExecuteRequest {
  session_id?: string | null;
  run_id?: string | null;
  task_node_id?: string | null;
  approval_id?: string | null;
  cwd?: string | null;
  arguments?: Record<string, unknown> | null;
}

export interface CodeToolExecuteResult {
  tool_id: string;
  status: 'native' | 'stubbed' | 'blocked' | 'failed';
  summary: string;
  evidence: string[];
  data: Record<string, unknown>;
  requires_approval: boolean;
  approval_summary?: string | null;
}

interface ToolSpec {
  id: string;
  summary: string;
  requiresApproval: boolean;
  approvalSummary?: string;
}

const TOOL_SPECS: Record<string, ToolSpec> = {
  search_files: {
    id: 'search_files',
    summary: 'Gateway proxy: search_files is routed to the Code native runtime. When the native binary is unavailable, this stub is returned.',
    requiresApproval: false
  },
  sandbox_exec: {
    id: 'sandbox_exec',
    summary: 'Code-layer sandbox exec stub is wired. Execution is blocked until Core approval is supplied.',
    requiresApproval: true,
    approvalSummary: 'Run a sandboxed command in the workspace.'
  },
  apply_patch: {
    id: 'apply_patch',
    summary: 'Code-layer apply patch stub is wired. Workspace writes are blocked until Core approval is supplied.',
    requiresApproval: true,
    approvalSummary: 'Apply a patch that may modify workspace files.'
  },
  review_format: {
    id: 'review_format',
    summary: 'Gateway proxy: review_format is routed to the Code native runtime. When the native binary is unavailable, this stub is returned.',
    requiresApproval: false
  }
};

export function listCodeToolIds(): string[] {
  return Object.keys(TOOL_SPECS);
}

export async function executeCodeTool(toolId: string, request: CodeToolExecuteRequest = {}): Promise<CodeToolExecuteResult | null> {
  const spec = TOOL_SPECS[toolId];
  if (!spec) {
    return null;
  }

  const nativeResult = await tryExecuteNativeTool(spec, request);
  if (nativeResult) {
    return nativeResult;
  }

  const args = request.arguments ?? {};
  return {
    tool_id: spec.id,
    status: spec.requiresApproval ? 'blocked' : 'stubbed',
    summary: spec.summary,
    evidence: [
      'domain: programming',
      'state_owner: core',
      'native_runtime: pending'
    ],
    data: {
      cwd: request.cwd ?? null,
      argument_keys: Object.keys(args).sort()
    },
    requires_approval: spec.requiresApproval,
    approval_summary: spec.approvalSummary ?? null
  };
}

async function tryExecuteNativeTool(spec: ToolSpec, request: CodeToolExecuteRequest): Promise<CodeToolExecuteResult | null> {
  const binary = resolveNativeBinary();
  if (!binary) {
    return null;
  }

  const payload = JSON.stringify({
    tool_id: spec.id,
    session_id: request.session_id ?? null,
    run_id: request.run_id ?? null,
    task_node_id: request.task_node_id ?? null,
    approval_id: request.approval_id ?? null,
    cwd: request.cwd ?? null,
    arguments: request.arguments ?? {}
  });

  return new Promise((resolve) => {
    const child = spawn(binary, ['execute'], {
      cwd: request.cwd ?? process.cwd(),
      env: {
        ...process.env,
        PATH: nativeRuntimePath()
      },
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true
    });

    let stdout = '';
    let stderr = '';
    const timeout = setTimeout(() => {
      child.kill();
      resolve(null);
    }, 5000);

    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    child.on('error', () => {
      clearTimeout(timeout);
      resolve(null);
    });
    child.on('close', (code) => {
      clearTimeout(timeout);
      if (code !== 0 || stdout.trim().length === 0) {
        if (stderr.trim().length > 0) {
          console.warn(`tinadec-code-native failed: ${stderr.trim()}`);
        }
        resolve(null);
        return;
      }

      try {
        resolve(JSON.parse(stdout) as CodeToolExecuteResult);
      } catch {
        resolve(null);
      }
    });
    child.stdin.end(payload);
  });
}

function nativeRuntimePath(): string {
  const separator = process.platform === 'win32' ? ';' : ':';
  const here = path.dirname(fileURLToPath(import.meta.url));
  const repoRoot = path.resolve(here, '..', '..', '..');
  const runtimeDirs: string[] = [];

  const cargoHome = process.env.CARGO_HOME || process.env.RUSTUP_HOME;
  if (cargoHome) {
    runtimeDirs.push(path.join(cargoHome, 'bin'));
  }

  runtimeDirs.push(
    path.join(repoRoot, 'native', 'target', 'debug'),
    path.join(repoRoot, 'native', 'target', 'release')
  );

  return [...runtimeDirs, process.env.PATH ?? ''].join(separator);
}

function resolveNativeBinary(): string | null {
  const explicit = process.env.TINADEC_CODE_NATIVE_BIN;
  if (explicit && existsSync(explicit)) {
    return explicit;
  }

  const here = path.dirname(fileURLToPath(import.meta.url));
  const repoRoot = path.resolve(here, '..', '..', '..');
  const exe = process.platform === 'win32' ? 'tinadec-code-native.exe' : 'tinadec-code-native';
  const candidates = [
    path.join(repoRoot, 'native', 'target', 'debug', exe),
    path.join(repoRoot, 'native', 'target', 'release', exe)
  ];

  return candidates.find((candidate) => existsSync(candidate)) ?? null;
}
