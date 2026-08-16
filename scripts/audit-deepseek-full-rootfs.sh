#!/usr/bin/env bash
set -euo pipefail

payload="${1:?usage: audit-deepseek-full-rootfs.sh <payload.wsl>}"
if [[ ! -f "$payload" ]]; then
  printf 'Payload not found: %s\n' "$payload" >&2
  exit 66
fi

failed=0
archive_entries="$(tar -tzf "$payload")"
audit_root="$(mktemp -d)"
trap 'rm -rf -- "$audit_root"' EXIT
tar -xzf "$payload" \
  -C "$audit_root" \
  --no-same-owner \
  --no-same-permissions \
  ./etc/passwd \
  ./etc/shadow \
  ./etc/hostname \
  ./etc/hosts \
  ./etc/machine-id

printf '%s\n' '--- forbidden paths ---'
forbidden_paths="$(
  printf '%s\n' "$archive_entries" |
    grep -E '^\./(etc/resolv\.conf|(root|home/codexmicro)/(\.bash_history|\.zsh_history|\.npmrc|\.ssh|\.aws|id_rsa|id_ed25519)(/|$)|boot/(vmlinuz|initrd))' || true
)"
if [[ -n "$forbidden_paths" ]]; then
  printf '%s\n' "$forbidden_paths"
  failed=1
else
  printf '%s\n' none
fi

printf '%s\n' '--- passwd uid0/1000 ---'
passwd_entries="$(
  awk -F: '$3 == 0 || $3 == 1000 { print $1 ":" $3 ":" $6 ":" $7 }' \
    "$audit_root/etc/passwd"
)"
printf '%s\n' "$passwd_entries"
if ! grep -qx 'root:0:/root:/bin/bash' <<<"$passwd_entries" ||
   ! grep -qx 'codexmicro:1000:/home/codexmicro:/bin/bash' \
     <<<"$passwd_entries"; then
  failed=1
fi

printf '%s\n' '--- shadow hashes ---'
if awk -F: '$2 ~ /^\$/ { found=1 } END { exit found ? 0 : 1 }' \
    "$audit_root/etc/shadow"; then
  printf '%s\n' found
  failed=1
else
  printf '%s\n' none
fi

printf '%s\n' '--- host identity files ---'
for path in ./etc/hostname ./etc/hosts ./etc/machine-id; do
  printf '%s=' "$path"
  tr '\n' ' ' < "$audit_root/${path#./}" || true
  printf '\n'
done

hostname_value="$(tr -d '[:space:]' < "$audit_root/etc/hostname")"
hosts_value="$(tr -d '[:space:]' < "$audit_root/etc/hosts")"
machine_id_value="$(tr -d '[:space:]' < "$audit_root/etc/machine-id")"
if [[ -n "$hostname_value" || -n "$hosts_value" ||
      -n "$machine_id_value" ]]; then
  failed=1
fi

if [[ "$failed" != "0" ]]; then
  printf 'rootfs-audit=failed\n' >&2
  exit 65
fi
printf 'rootfs-audit=ready\n'
