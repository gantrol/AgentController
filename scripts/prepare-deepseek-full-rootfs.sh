#!/usr/bin/env bash
set -euo pipefail

# Finalize the clean, temporary WSL distribution used to build the Full
# one-click payload. This script must only run inside that throw-away distro.

managed_user="${CODEX_MICRO_DSH_USER:-codexmicro}"
release_version="${CODEX_MICRO_RELEASE_VERSION:-0.2.7}"
runtime_root="/home/$managed_user/.local/share/codex-micro/deepseek"
marker_root="/etc/codex-micro"

if [[ "$(id -u)" -ne 0 ]]; then
  printf 'Rootfs preparation must run as root.\n' >&2
  exit 77
fi
if ! id "$managed_user" >/dev/null 2>&1; then
  printf 'Managed user does not exist: %s\n' "$managed_user" >&2
  exit 78
fi
if [[ "$(id -u "$managed_user")" != "1000" ]]; then
  printf 'Managed user must have uid 1000; actual uid is %s.\n' \
    "$(id -u "$managed_user")" >&2
  exit 78
fi
if [[ ! -x "$runtime_root/node/bin/node" || \
      ! -x "$runtime_root/tools/bin/pnpm" || \
      ! -x "$runtime_root/tools/bin/dsh" || \
      ! -x "$runtime_root/bin/start-dsh-wsl.sh" ]]; then
  printf 'Managed DeepSeek runtime is incomplete below %s.\n' "$runtime_root" >&2
  exit 69
fi

install -d -m 0755 "$marker_root"
printf 'format=1\nrelease=%s\n' "$release_version" \
  > "$marker_root/deepseek-runtime-v1"
chmod 0644 "$marker_root/deepseek-runtime-v1"

printf '%s\n' \
  '[boot]' \
  'systemd=false' \
  '' \
  '[network]' \
  'generateResolvConf=true' \
  'generateHosts=true' \
  '' \
  '[user]' \
  "default=$managed_user" \
  > /etc/wsl.conf
chmod 0644 /etc/wsl.conf
chown root:root /etc/wsl.conf

printf '%s\n' \
  '[oobe]' \
  'defaultUid=1000' \
  'defaultName=CodexMicro-DeepSeek' \
  '' \
  '[shortcut]' \
  'enabled=false' \
  '' \
  '[windowsterminal]' \
  'enabled=false' \
  > /etc/wsl-distribution.conf
chmod 0644 /etc/wsl-distribution.conf
chown root:root /etc/wsl-distribution.conf

# Native npm modules are already compiled at this point. Remove the temporary
# compiler toolchain from the appliance, while retaining runtime libraries.
apt-get purge -y g++ make
apt-get autoremove --purge -y

# A WSL rootfs must not ship the host-specific resolver file. WSL recreates it
# on first launch because generateResolvConf remains enabled above.
rm -f -- /etc/resolv.conf
# WSL also recreates these host identity files on first launch. Keep the
# exported appliance independent from the workstation that built it.
truncate -s 0 /etc/hostname /etc/hosts

# The imported appliance has no password-login surface. WSL still launches the
# pre-created uid directly, while both local password fields remain locked.
usermod --password '!' root
usermod --password '!' "$managed_user"
if awk -F: '$2 ~ /^\$/ { found=1 } END { exit found ? 0 : 1 }' \
    /etc/shadow; then
  printf 'Password hashes remain in /etc/shadow.\n' >&2
  exit 65
fi

apt-get clean
rm -rf -- /var/lib/apt/lists/* /var/cache/apt/archives/*.deb
rm -rf -- "$runtime_root/cache"/*
rm -rf -- "/home/$managed_user/.npm" "/home/$managed_user/.cache/node-gyp"
rm -f -- /root/.bash_history "/home/$managed_user/.bash_history"
rm -rf -- /root/.ssh "/home/$managed_user/.ssh"
find /var/log -type f -exec truncate -s 0 -- {} +
rm -rf -- /tmp/* /var/tmp/*
truncate -s 0 /etc/machine-id
rm -f -- /var/lib/dbus/machine-id

export PATH="$runtime_root/node/bin:$runtime_root/tools/bin:$PATH"
printf 'rootfs-ready=1\n'
printf 'release=%s\n' "$release_version"
printf 'node=%s\n' "$($runtime_root/node/bin/node --version)"
printf 'pnpm=%s\n' "$($runtime_root/tools/bin/pnpm --version)"
