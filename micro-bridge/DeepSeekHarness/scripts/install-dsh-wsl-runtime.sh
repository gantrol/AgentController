#!/usr/bin/env bash
set -euo pipefail

# Program-managed DeepSeek runtime. The Windows setup coordinator runs the
# root phase in its dedicated WSL distribution; this script then switches to
# the unprivileged app user and installs every mutable file below that user's
# home. It never requires an AgentController or DeepSeek Harness checkout.

managed_user="${CODEX_MICRO_DSH_USER:-codexmicro}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
runtime_versions="$script_dir/runtime-versions.env"
if [[ ! -f "$runtime_versions" ]]; then
  printf 'The managed runtime version manifest is missing: %s\n' \
    "$runtime_versions" >&2
  exit 66
fi
# shellcheck source=runtime-versions.env
source "$runtime_versions"
managed_node_version="$CODEX_MICRO_NODE_VERSION"
managed_node_archive="node-${managed_node_version}-linux-x64.tar.xz"
managed_node_sha256="$CODEX_MICRO_NODE_LINUX_X64_SHA256"
managed_dsh_package="$CODEX_MICRO_DSH_NPM_PACKAGE"
managed_dsh_version="$CODEX_MICRO_DSH_VERSION"
managed_pnpm_version="$CODEX_MICRO_PNPM_VERSION"
bundled_runtime_marker="/etc/codex-micro/deepseek-runtime-v1"
plugin_source="$(cd -- "$script_dir/.." && pwd -P)"

if [[ ! "$managed_node_version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ||
      ! "$managed_node_sha256" =~ ^[0-9a-f]{64}$ ||
      ! "$managed_pnpm_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ||
      ! "$managed_dsh_package" =~ ^(@[a-z0-9._-]+/)?[a-z0-9._-]+$ ||
      ! "$managed_dsh_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  printf 'The managed runtime version manifest contains an invalid value.\n' >&2
  exit 65
fi

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
  if [[ ! -f "$bundled_runtime_marker" && \
        "${CODEX_MICRO_DSH_OFFLINE:-0}" != "1" ]]; then
    command -v make >/dev/null 2>&1 || missing_packages+=(make)
    command -v g++ >/dev/null 2>&1 || missing_packages+=(g++)
    command -v python3 >/dev/null 2>&1 || missing_packages+=(python3)
  fi
  if [[ "${#missing_packages[@]}" -ne 0 ]]; then
    if [[ -f "$bundled_runtime_marker" || "${CODEX_MICRO_DSH_OFFLINE:-0}" == "1" ]]; then
      printf 'The bundled DeepSeek runtime is missing required system tools: %s\n' \
        "${missing_packages[*]}" >&2
      exit 69
    fi
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

  offline="${CODEX_MICRO_DSH_OFFLINE:-0}"
  if [[ -f "$bundled_runtime_marker" ]]; then
    offline=1
  fi
  exec runuser -u "$managed_user" -- \
    env HOME="$managed_home" USER="$managed_user" LOGNAME="$managed_user" \
    CODEX_MICRO_DSH_OFFLINE="$offline" \
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

node_install_version="$("$node_install/bin/node" --version 2>/dev/null || true)"
if [[ "$node_install_version" != "$managed_node_version" ]]; then
  if [[ "${CODEX_MICRO_DSH_OFFLINE:-0}" == "1" ]]; then
    printf 'The bundled Node runtime is missing or incompatible (expected %s, actual %s).\n' \
      "$managed_node_version" "${node_install_version:-missing}" >&2
    exit 69
  fi
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
  case "$node_install" in
    "$node_versions_root"/v*) rm -rf -- "$node_install" ;;
    *) exit 78 ;;
  esac
  mv -- "$install_tmp" "$node_install"
fi
ln -sfn "$node_install" "$runtime_root/node"

export PATH="$runtime_root/node/bin:$tools_root/bin:$PATH"
export npm_config_update_notifier=false
export npm_config_fund=false
export npm_config_audit=false

installed_node_version="$("$runtime_root/node/bin/node" --version 2>/dev/null || true)"
installed_pnpm_version="$(
  "$runtime_root/node/bin/node" -p \
    "require('$tools_root/lib/node_modules/pnpm/package.json').version" \
    2>/dev/null || true
)"
installed_dsh_version="$(
  "$runtime_root/node/bin/node" -p \
    "require('$tools_root/lib/node_modules/$managed_dsh_package/package.json').version" \
    2>/dev/null || true
)"
runtime_is_ready=0
if [[ "$installed_node_version" == "$managed_node_version" && \
      "$installed_pnpm_version" == "$managed_pnpm_version" && \
      "$installed_dsh_version" == "$managed_dsh_version" && \
      -x "$tools_root/bin/pnpm" && -x "$tools_root/bin/dsh" ]]; then
  runtime_is_ready=1
fi

if [[ "$runtime_is_ready" == "1" ]]; then
  printf 'runtime-source=bundled-or-cached\n'
elif [[ "${CODEX_MICRO_DSH_OFFLINE:-0}" == "1" ]]; then
  printf 'The bundled DeepSeek runtime is incomplete or has incompatible versions.\n' >&2
  printf 'expected node=%s pnpm=%s dsh=%s\n' \
    "$managed_node_version" "$managed_pnpm_version" "$managed_dsh_version" >&2
  printf 'actual node=%s pnpm=%s dsh=%s\n' \
    "${installed_node_version:-missing}" \
    "${installed_pnpm_version:-missing}" \
    "${installed_dsh_version:-missing}" >&2
  exit 69
else
  "$runtime_root/node/bin/npm" install \
    --global \
    --prefix "$tools_root" \
    "pnpm@$managed_pnpm_version" \
    "$managed_dsh_package@$managed_dsh_version"
  printf 'runtime-source=network\n'
fi

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
printf 'dsh-package=%s\n' "$managed_dsh_package"
printf 'dsh=%s\n' "$managed_dsh_version"
printf 'runtime=%s\n' "$runtime_root"
printf 'managed-ready=1\n'
