#!/usr/bin/env bash
set -euo pipefail

runtime_root="${CODEX_MICRO_DSH_RUNTIME_ROOT:-$HOME/.local/share/codex-micro/deepseek}"
node_root="$runtime_root/node"
tools_root="$runtime_root/tools"
dsh_home="$runtime_root/dsh-home"
port="3080"

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --port)
      if [[ "$#" -lt 2 ]]; then
        printf '%s\n' '--port requires a value.' >&2
        exit 64
      fi
      port="$2"
      shift 2
      ;;
    *)
      printf 'Unknown managed DeepSeek launch argument: %s\n' "$1" >&2
      exit 64
      ;;
  esac
done

if [[ ! "$port" =~ ^[0-9]+$ ]] || (( port < 1 || port > 65535 )); then
  printf 'Invalid DeepSeek Harness port: %s\n' "$port" >&2
  exit 64
fi
if [[ ! -x "$node_root/bin/node" || ! -x "$tools_root/bin/dsh" ]]; then
  printf 'The program-managed DeepSeek runtime is incomplete below %s.\n' "$runtime_root" >&2
  exit 72
fi

export PATH="$node_root/bin:$tools_root/bin:$PATH"
export DSH_HOME="$dsh_home"
workspace="${CODEX_MICRO_DSH_WORKSPACE:-$HOME}"
cd "$workspace"
exec "$tools_root/bin/dsh" web --port "$port"
