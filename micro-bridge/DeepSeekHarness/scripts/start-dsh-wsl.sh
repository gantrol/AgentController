#!/usr/bin/env bash
set -euo pipefail

# Stable WSL entry point used by the Windows Codex Micro launcher.  The
# installer owns the runtime below ~/.local/share; keeping this script in the
# AgentController checkout lets development builds update the launch contract
# without editing DeepSeek Harness itself.
runtime_root="${AGENTCONTROLLER_DSH_RUNTIME_ROOT:-$HOME/.local/share/agentcontroller-dsh}"
node_root="${AGENTCONTROLLER_DSH_NODE_ROOT:-$runtime_root/node}"
source_root="${AGENTCONTROLLER_DSH_SOURCE_ROOT:-$runtime_root/deepseek-harness}"

node="$node_root/bin/node"
if [[ ! -x "$node" ]]; then
  printf 'DeepSeek Harness WSL runtime is incomplete: Linux Node is missing at %s\n' "$node" >&2
  exit 72
fi
if [[ ! -f "$source_root/apps/cli/src/bin.ts" ]]; then
  printf 'DeepSeek Harness WSL runtime is incomplete: source is missing at %s\n' "$source_root" >&2
  exit 72
fi

export PATH="$node_root/bin:$runtime_root/tools/bin:$PATH"
cd "$source_root"
exec "$node" --import tsx/esm apps/cli/src/bin.ts web "$@"
