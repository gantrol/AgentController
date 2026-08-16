#!/usr/bin/env bash
set -euo pipefail

runtime_root="${AGENTCONTROLLER_DSH_RUNTIME_ROOT:-$HOME/.local/share/agentcontroller-dsh}"
source_checkout="${DSH_SOURCE_CHECKOUT:-/mnt/d/project/ai/deepseek/deepseek-harness}"
agentcontroller_root="${AGENTCONTROLLER_ROOT:-/mnt/d/AgentController}"
windows_user="${WINDOWS_USER_NAME:-gantrol}"
windows_dsh_home="/mnt/c/Users/$windows_user/.dsh"
wsl_dsh_home="${DSH_HOME:-$HOME/.dsh}"

if [[ ! -f "$source_checkout/package.json" ]]; then
  printf 'DeepSeek Harness checkout was not found at %s\n' "$source_checkout" >&2
  exit 66
fi
if [[ ! -f "$agentcontroller_root/micro-bridge/DeepSeekHarness/package.json" ]]; then
  printf 'AgentController checkout was not found at %s\n' "$agentcontroller_root" >&2
  exit 66
fi

mkdir -p "$runtime_root" "$HOME/.cache"
temp_root="$(mktemp -d /tmp/agentcontroller-dsh-node.XXXXXX)"
cleanup() {
  case "$temp_root" in
    /tmp/agentcontroller-dsh-node.*) rm -rf -- "$temp_root" ;;
  esac
}
trap cleanup EXIT

cd "$temp_root"
curl -fsSLo SHASUMS256.txt \
  https://nodejs.org/dist/latest-v24.x/SHASUMS256.txt
archive_line="$(grep 'linux-x64.tar.xz$' SHASUMS256.txt | head -n 1)"
archive="${archive_line##* }"
if [[ -z "$archive" ]]; then
  printf 'Could not resolve the current Node 24 Linux archive.\n' >&2
  exit 69
fi
curl -fsSLo "$archive" "https://nodejs.org/dist/latest-v24.x/$archive"
grep "  $archive$" SHASUMS256.txt | sha256sum -c -

version="${archive%-linux-x64.tar.xz}"
node_install="$runtime_root/$version"
if [[ ! -x "$node_install/bin/node" ]]; then
  mkdir -p "$node_install"
  tar -xJf "$archive" -C "$node_install" --strip-components=1
fi
ln -sfn "$version" "$runtime_root/node"
export PATH="$runtime_root/node/bin:$runtime_root/tools/bin:$PATH"
"$runtime_root/node/bin/npm" install \
  --global \
  --prefix "$runtime_root/tools" \
  pnpm@11.7.0

source_runtime="$runtime_root/deepseek-harness"
mkdir -p "$source_runtime"
rsync -a \
  --exclude '.git/' \
  --exclude 'node_modules/' \
  --exclude '*.tsbuildinfo' \
  "$source_checkout/" "$source_runtime/"

if [[ -d "$windows_dsh_home" ]]; then
  mkdir -p "$wsl_dsh_home"
  chmod 700 "$wsl_dsh_home"
  rsync -a \
    --exclude 'node_modules/' \
    "$windows_dsh_home/" "$wsl_dsh_home/"
  profile_lock="$wsl_dsh_home/profiles/web/pnpm-lock.yaml"
  if [[ -f "$profile_lock" ]]; then
    rm -f -- "$profile_lock"
  fi
  "$runtime_root/node/bin/node" \
    "$agentcontroller_root/micro-bridge/DeepSeekHarness/scripts/migrate-dsh-home-wsl.mjs" \
    "$wsl_dsh_home"
fi

cd "$source_runtime"
pnpm install --frozen-lockfile

# Initialize the WSL-owned web profile on a clean machine and reconcile the
# external Micro bundle on upgrades.  This never edits the DeepSeek Harness
# checkout and keeps native dependencies (notably node-pty) on the Linux side.
"$runtime_root/node/bin/node" \
  --import tsx/esm \
  apps/cli/src/bin.ts \
  plugin --profile web add \
  "$agentcontroller_root/micro-bridge/DeepSeekHarness"

printf 'node=%s\n' "$(node --version)"
printf 'pnpm=%s\n' "$(pnpm --version)"
printf 'platform=%s\n' "$(node -p 'process.platform')"
printf 'runtime=%s\n' "$source_runtime"
