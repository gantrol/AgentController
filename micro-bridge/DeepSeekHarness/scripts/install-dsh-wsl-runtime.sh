#!/usr/bin/env bash
set -euo pipefail

# Program-managed DeepSeek runtime. The Windows setup coordinator runs the
# root phase in its dedicated WSL distribution; this script then switches to
# the unprivileged app user and installs every mutable file below that user's
# home. It never requires an AgentController or DeepSeek Harness checkout.

managed_user="${CODEX_MICRO_DSH_USER:-codexmicro}"
managed_node_version="v24.19.0"
managed_node_archive="node-${managed_node_version}-linux-x64.tar.xz"
managed_node_sha256="14b342e71204f811bde6153be8e04b62aef63c236fef92b55f9c83154b409647"
managed_dsh_version="0.1.0-rc.6"
managed_pnpm_version="11.7.0"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
plugin_source="$(cd -- "$script_dir/.." && pwd -P)"

if [[ ! -f "$plugin_source/package.json" || ! -f "$plugin_source/lib/index.js" || ! -f "$plugin_source/lib/client.js" ]]; then
  printf 'The packaged DeepSeek Micro bridge is incomplete at %s\n' "$plugin_source" >&2
  exit 66
fi

if [[ "${1:-}" != "--user-phase" ]]; then
  if [[ "$(id -u)" -ne 0 ]]; then
    printf 'The managed installer root phase must run as root.\n' >&2
    exit 77
  fi

  missing_packages=()
  command -v curl >/dev/null 2>&1 || missing_packages+=(curl)
  command -v sha256sum >/dev/null 2>&1 || missing_packages+=(coreutils)
  command -v tar >/dev/null 2>&1 || missing_packages+=(tar)
  command -v xz >/dev/null 2>&1 || missing_packages+=(xz-utils)
  command -v rsync >/dev/null 2>&1 || missing_packages+=(rsync)
  command -v runuser >/dev/null 2>&1 || missing_packages+=(util-linux)
  if [[ "${#missing_packages[@]}" -ne 0 ]]; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y --no-install-recommends \
      ca-certificates \
      "${missing_packages[@]}"
  fi

  if ! id "$managed_user" >/dev/null 2>&1; then
    useradd --create-home --shell /bin/bash "$managed_user"
  fi
  managed_home="$(getent passwd "$managed_user" | cut -d: -f6)"
  if [[ -z "$managed_home" || "$managed_home" != /* ]]; then
    printf 'Could not resolve a safe home directory for %s.\n' "$managed_user" >&2
    exit 78
  fi
  install -d -m 0750 -o "$managed_user" -g "$managed_user" \
    "$managed_home/.local/share/codex-micro"

  exec runuser -u "$managed_user" -- \
    env HOME="$managed_home" USER="$managed_user" LOGNAME="$managed_user" \
    bash "$script_dir/install-dsh-wsl-runtime.sh" --user-phase
fi

runtime_root="${CODEX_MICRO_DSH_RUNTIME_ROOT:-$HOME/.local/share/codex-micro/deepseek}"
case "$runtime_root" in
  "$HOME"/.local/share/codex-micro/deepseek) ;;
  *)
    printf 'Refusing an unmanaged runtime root: %s\n' "$runtime_root" >&2
    exit 78
    ;;
esac

node_versions_root="$runtime_root/versions/node"
node_install="$node_versions_root/$managed_node_version"
tools_root="$runtime_root/tools"
bridge_root="$runtime_root/bridge"
cache_root="$runtime_root/cache"
bin_root="$runtime_root/bin"
dsh_home="$runtime_root/dsh-home"
mkdir -p \
  "$node_versions_root" \
  "$tools_root" \
  "$cache_root" \
  "$bin_root" \
  "$dsh_home"
chmod 700 "$dsh_home"

if [[ ! -x "$node_install/bin/node" ]]; then
  archive_path="$cache_root/$managed_node_archive"
  if [[ ! -f "$archive_path" ]]; then
    curl --fail --show-error --location \
      --output "$archive_path.part" \
      "https://nodejs.org/dist/$managed_node_version/$managed_node_archive"
    mv -- "$archive_path.part" "$archive_path"
  fi
  printf '%s  %s\n' "$managed_node_sha256" "$archive_path" | sha256sum --check -
  install_tmp="$node_versions_root/${managed_node_version}.installing"
  case "$install_tmp" in
    "$node_versions_root"/*.installing) rm -rf -- "$install_tmp" ;;
    *) exit 78 ;;
  esac
  mkdir -p "$install_tmp"
  tar -xJf "$archive_path" -C "$install_tmp" --strip-components=1
  mv -- "$install_tmp" "$node_install"
fi
ln -sfn "$node_install" "$runtime_root/node"

export PATH="$runtime_root/node/bin:$tools_root/bin:$PATH"
export npm_config_update_notifier=false
export npm_config_fund=false
export npm_config_audit=false
"$runtime_root/node/bin/npm" install \
  --global \
  --prefix "$tools_root" \
  "pnpm@$managed_pnpm_version" \
  "@deepseek-ai/dsh@$managed_dsh_version"

bridge_next="$runtime_root/bridge.installing"
case "$bridge_next" in
  "$runtime_root"/bridge.installing) rm -rf -- "$bridge_next" ;;
  *) exit 78 ;;
esac
mkdir -p "$bridge_next"
rsync -a \
  --exclude '.git/' \
  --exclude 'node_modules/' \
  --exclude '*.tsbuildinfo' \
  "$plugin_source/" "$bridge_next/"
"$runtime_root/node/bin/node" \
  "$bridge_next/scripts/prepare-managed-bridge.mjs" \
  "$bridge_next"
case "$bridge_root" in
  "$runtime_root"/bridge) rm -rf -- "$bridge_root" ;;
  *) exit 78 ;;
esac
mv -- "$bridge_next" "$bridge_root"

export DSH_HOME="$dsh_home"
"$tools_root/bin/dsh" plugin --profile web add "file:$bridge_root"
install -m 0755 "$script_dir/start-dsh-wsl.sh" \
  "$bin_root/start-dsh-wsl.sh"

printf 'node=%s\n' "$(node --version)"
printf 'pnpm=%s\n' "$(pnpm --version)"
printf 'dsh=%s\n' "$managed_dsh_version"
printf 'runtime=%s\n' "$runtime_root"
printf 'managed-ready=1\n'
