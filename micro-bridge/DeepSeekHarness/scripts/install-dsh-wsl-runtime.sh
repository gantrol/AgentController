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

if [[ "${1:-}" != "--user-phase" ]]; then
  if [[ "$(id -u)" -ne 0 ]]; then
    printf 'The managed installer root phase must run as root.\n' >&2
    exit 77
  fi

  if [[ ! "$managed_user" =~ ^[a-z_][a-z0-9_-]*$ ]]; then
    printf 'Invalid managed Linux user: %s\n' "$managed_user" >&2
    exit 78
  fi
  if ! id "$managed_user" >/dev/null 2>&1; then
    useradd --create-home --shell /bin/bash "$managed_user"
  fi
  managed_home="$(getent passwd "$managed_user" | cut -d: -f6)"
  if [[ -z "$managed_home" || "$managed_home" != /* ]]; then
    printf 'Could not resolve a safe home directory for %s.\n' "$managed_user" >&2
    exit 78
  fi

  runtime_parent="$managed_home/.local/share/codex-micro"
  runtime_root="$runtime_parent/deepseek"
  upgrade_state="$runtime_parent/.deepseek-upgrade-state-v1"
  install -d -m 0750 -o "$managed_user" -g "$managed_user" \
    "$runtime_parent"

  installed_dsh_version_at() {
    local root="$1"
    local node="$root/node/bin/node"
    local manifest="$root/tools/lib/node_modules/$managed_dsh_package/package.json"
    if [[ ! -x "$node" || ! -f "$manifest" ]]; then
      return 0
    fi
    "$node" -p \
      "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).version" \
      "$manifest" 2>/dev/null || true
  }

  read_upgrade_state() {
    pending_backup_name=""
    pending_expected_version=""
    if [[ ! -f "$upgrade_state" ]]; then
      return 1
    fi
    pending_backup_name="$(
      sed -n 's/^backup=//p' "$upgrade_state" | head -n 1
    )"
    pending_expected_version="$(
      sed -n 's/^expected=//p' "$upgrade_state" | head -n 1
    )"
    if [[ ! "$pending_backup_name" =~ ^deepseek\.backup-[A-Za-z0-9._-]+$ ||
          ! "$pending_expected_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
      printf 'The managed DeepSeek upgrade state is invalid: %s\n' \
        "$upgrade_state" >&2
      exit 78
    fi
    return 0
  }

  rollback_pending_upgrade() {
    if ! read_upgrade_state; then
      printf 'upgrade-rollback=none\n'
      return 0
    fi
    local backup_root="$runtime_parent/$pending_backup_name"
    case "$backup_root" in
      "$runtime_parent"/deepseek.backup-*) ;;
      *)
        printf 'Refusing an unsafe DeepSeek rollback path: %s\n' \
          "$backup_root" >&2
        exit 78
        ;;
    esac
    if [[ ! -d "$backup_root" ]]; then
      if [[ -d "$runtime_root" ]]; then
        rm -f -- "$upgrade_state"
        printf 'upgrade-rollback=not-needed\n'
        return 0
      fi
      printf 'The DeepSeek rollback backup is missing: %s\n' \
        "$backup_root" >&2
      exit 69
    fi
    if [[ -e "$runtime_root" ]]; then
      case "$runtime_root" in
        "$runtime_parent"/deepseek) rm -rf -- "$runtime_root" ;;
        *) exit 78 ;;
      esac
    fi
    mv -- "$backup_root" "$runtime_root"
    chown -R "$managed_user:$managed_user" "$runtime_root"
    rm -f -- "$upgrade_state"
    printf 'upgrade-rollback=restored\n'
  }

  commit_pending_upgrade() {
    if ! read_upgrade_state; then
      printf 'upgrade-commit=none\n'
      return 0
    fi
    local backup_root="$runtime_parent/$pending_backup_name"
    rm -f -- "$upgrade_state"
    printf 'upgrade-commit=ready\n'
    printf 'upgrade-backup=%s\n' "$backup_root"
  }

  case "${1:-}" in
    --runtime-status)
      actual_version="$(installed_dsh_version_at "$runtime_root")"
      printf 'expected-dsh=%s\n' "$managed_dsh_version"
      printf 'actual-dsh=%s\n' "${actual_version:-missing}"
      if read_upgrade_state; then
        printf 'upgrade-pending=1\n'
        printf 'upgrade-pending-target=%s\n' "$pending_expected_version"
        printf 'upgrade-backup=%s/%s\n' \
          "$runtime_parent" "$pending_backup_name"
      else
        printf 'upgrade-pending=0\n'
      fi
      exit 0
      ;;
    --rollback-upgrade)
      rollback_pending_upgrade
      exit 0
      ;;
    --commit-upgrade)
      commit_pending_upgrade
      exit 0
      ;;
    "") ;;
    *)
      printf 'Unknown managed installer action: %s\n' "$1" >&2
      exit 64
      ;;
  esac

  # A previous app process may have stopped after the runtime swap but before
  # recording the health-check result. Keep a healthy target and otherwise
  # restore the old runtime before beginning another attempt.
  if read_upgrade_state; then
    pending_actual_version="$(installed_dsh_version_at "$runtime_root")"
    if [[ "$pending_actual_version" == "$pending_expected_version" ]]; then
      commit_pending_upgrade
      printf 'upgrade-recovery=committed\n'
    else
      rollback_pending_upgrade
      printf 'upgrade-recovery=rolled-back\n'
    fi
  fi

  if [[ ! -f "$plugin_source/package.json" || \
        ! -f "$plugin_source/lib/index.js" || \
        ! -f "$plugin_source/lib/client.js" ]]; then
    printf 'The packaged DeepSeek Micro bridge is incomplete at %s\n' \
      "$plugin_source" >&2
    exit 66
  fi

  if [[ -n "${CODEX_MICRO_DSH_OFFLINE+x}" ]]; then
    offline="$CODEX_MICRO_DSH_OFFLINE"
  elif [[ -f "$bundled_runtime_marker" ]]; then
    offline=1
  else
    offline=0
  fi
  if [[ "$offline" != "0" && "$offline" != "1" ]]; then
    printf 'CODEX_MICRO_DSH_OFFLINE must be 0 or 1.\n' >&2
    exit 64
  fi

  missing_packages=()
  command -v curl >/dev/null 2>&1 || missing_packages+=(curl)
  command -v sha256sum >/dev/null 2>&1 || missing_packages+=(coreutils)
  command -v tar >/dev/null 2>&1 || missing_packages+=(tar)
  command -v xz >/dev/null 2>&1 || missing_packages+=(xz-utils)
  command -v rsync >/dev/null 2>&1 || missing_packages+=(rsync)
  command -v runuser >/dev/null 2>&1 || missing_packages+=(util-linux)
  if [[ "$offline" != "1" ]]; then
    command -v make >/dev/null 2>&1 || missing_packages+=(make)
    command -v g++ >/dev/null 2>&1 || missing_packages+=(g++)
    command -v python3 >/dev/null 2>&1 || missing_packages+=(python3)
  fi
  if [[ "${#missing_packages[@]}" -ne 0 ]]; then
    if [[ "$offline" == "1" ]]; then
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

  current_dsh_version="$(installed_dsh_version_at "$runtime_root")"
  payload="${CODEX_MICRO_DSH_PAYLOAD:-}"
  if [[ -n "$payload" && ( "$payload" != /* || ! -f "$payload" ) ]]; then
    printf 'The bundled DeepSeek upgrade payload is unavailable: %s\n' \
      "$payload" >&2
    exit 66
  fi

  if [[ -d "$runtime_root" && \
        "$current_dsh_version" != "$managed_dsh_version" ]]; then
    timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
    old_label="${current_dsh_version:-unknown}"
    old_label="${old_label//[^A-Za-z0-9._-]/_}"
    backup_name="deepseek.backup-$old_label-$timestamp-$$"
    backup_root="$runtime_parent/$backup_name"
    staging_root="$runtime_parent/deepseek.installing-$timestamp-$$"
    state_next="$upgrade_state.next-$$"
    case "$backup_root" in
      "$runtime_parent"/deepseek.backup-*) ;;
      *) printf 'Refusing an unsafe DeepSeek backup path.\n' >&2; exit 78 ;;
    esac
    case "$staging_root" in
      "$runtime_parent"/deepseek.installing-*) ;;
      *) printf 'Refusing an unsafe DeepSeek staging path.\n' >&2; exit 78 ;;
    esac
    case "$state_next" in
      "$runtime_parent"/.deepseek-upgrade-state-v1.next-*) ;;
      *) printf 'Refusing an unsafe DeepSeek state path.\n' >&2; exit 78 ;;
    esac
    if [[ -e "$backup_root" || -e "$staging_root" || -e "$state_next" ]]; then
      printf 'A DeepSeek upgrade staging path already exists.\n' >&2
      exit 73
    fi

    mkdir -p "$staging_root/dsh-home"
    if [[ -n "$payload" ]]; then
      payload_prefix="./home/$managed_user/.local/share/codex-micro/deepseek"
      payload_manifest="$payload_prefix/tools/lib/node_modules/$managed_dsh_package/package.json"
      if ! tar -tzf "$payload" "$payload_manifest" >/dev/null 2>&1; then
        printf 'The bundled payload does not contain the managed DSH package.\n' >&2
        exit 69
      fi
      rm -rf -- "$staging_root"
      mkdir -p "$staging_root"
      # WSL's exported rootfs entries start with ./home/...; GNU tar counts
      # that leading dot as a component, so seven components remove the
      # appliance path and leave the contents of the managed runtime itself.
      tar -xzf "$payload" \
        -C "$staging_root" \
        --strip-components=7 \
        "$payload_prefix/"
      staged_node="$staging_root/versions/node/$managed_node_version/bin/node"
      staged_manifest="$staging_root/tools/lib/node_modules/$managed_dsh_package/package.json"
      staged_version=""
      if [[ -x "$staged_node" && -f "$staged_manifest" ]]; then
        staged_version="$(
          "$staged_node" -p \
            "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).version" \
            "$staged_manifest" 2>/dev/null || true
        )"
      fi
      if [[ "$staged_version" != "$managed_dsh_version" || \
            ! -x "$staging_root/tools/bin/dsh" ]]; then
        case "$staging_root" in
          "$runtime_parent"/deepseek.installing-*) rm -rf -- "$staging_root" ;;
          *) exit 78 ;;
        esac
        printf 'The bundled payload has an incompatible DSH runtime (expected %s, actual %s).\n' \
          "$managed_dsh_version" "${staged_version:-missing}" >&2
        exit 69
      fi
    fi

    printf 'backup=%s\nexpected=%s\n' \
      "$backup_name" "$managed_dsh_version" > "$state_next"
    chmod 0600 "$state_next"
    mv -- "$state_next" "$upgrade_state"
    mv -- "$runtime_root" "$backup_root"
    mv -- "$staging_root" "$runtime_root"
    install -d -m 0700 -o "$managed_user" -g "$managed_user" \
      "$runtime_root/dsh-home"
    for safe_name in .credentials.yaml settings.yaml .anonymous-user-id; do
      safe_source="$backup_root/dsh-home/$safe_name"
      if [[ -f "$safe_source" && ! -L "$safe_source" ]]; then
        install -m 0600 -o "$managed_user" -g "$managed_user" \
          "$safe_source" "$runtime_root/dsh-home/$safe_name"
      fi
    done
    chown -R "$managed_user:$managed_user" "$runtime_root"
    printf 'upgrade-prepared=1\n'
    printf 'upgrade-from=%s\n' "${current_dsh_version:-unknown}"
    printf 'upgrade-to=%s\n' "$managed_dsh_version"
    printf 'upgrade-backup=%s\n' "$backup_root"
  fi

  set +e
  runuser -u "$managed_user" -- \
    env HOME="$managed_home" USER="$managed_user" LOGNAME="$managed_user" \
    CODEX_MICRO_DSH_OFFLINE="$offline" \
    bash "$script_dir/install-dsh-wsl-runtime.sh" --user-phase
  user_phase_status=$?
  set -e
  if [[ "$user_phase_status" -ne 0 ]]; then
    if [[ -f "$upgrade_state" ]]; then
      rollback_pending_upgrade
    fi
    exit "$user_phase_status"
  fi
  exit 0
fi

if [[ ! -f "$plugin_source/package.json" || \
      ! -f "$plugin_source/lib/index.js" || \
      ! -f "$plugin_source/lib/client.js" ]]; then
  printf 'The packaged DeepSeek Micro bridge is incomplete at %s\n' \
    "$plugin_source" >&2
  exit 66
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
    --allow-scripts=@deepseek-ai/dsh-subprocess-local,koffi,node-pty,@google/genai,protobufjs \
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
"$runtime_root/node/bin/node" \
  "$bridge_root/scripts/configure-managed-settings.mjs" \
  "$dsh_home/settings.yaml" \
  "$tools_root/lib/node_modules/$managed_dsh_package/package.json"
install -m 0755 "$script_dir/start-dsh-wsl.sh" \
  "$bin_root/start-dsh-wsl.sh"

printf 'node=%s\n' "$(node --version)"
printf 'pnpm=%s\n' "$(pnpm --version)"
printf 'dsh-package=%s\n' "$managed_dsh_package"
printf 'dsh=%s\n' "$managed_dsh_version"
printf 'runtime=%s\n' "$runtime_root"
printf 'managed-ready=1\n'
