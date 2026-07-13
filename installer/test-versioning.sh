#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$root/installer/versioning.sh"

assert_case() {
  local input="$1" product="$2" info="$3"
  resolve_codex_version "$input"
  [[ "$CODEX_VERSION" == "$input" ]]
  [[ "$CODEX_PRODUCT_VERSION" == "$product" ]]
  [[ "$CODEX_INFORMATIONAL_VERSION" == "$info" ]]
}

assert_case 1.2.3 1.2.3.0 1.2.3
assert_case 1.2.3.4 1.2.3.4 1.2.3.4
assert_case 1.2.3-abcd 1.2.3.0 1.2.3-abcd
assert_case 1.2.3.4-abcd 1.2.3.4 1.2.3.4-abcd
assert_case 1.2.3-rc.1 1.2.3.0 1.2.3-rc.1
resolve_codex_version 1.2.3-abcd 1.2.3-abcd-abcdef1
[[ "$CODEX_INFORMATIONAL_VERSION" == 1.2.3-abcd-abcdef1 ]]

for invalid in 1.2 1.2.3.4.5 1.2.3- 1.2.3+meta 1.2.3/evil 01.2.3 1.2.65535 1.2.99999999999999999999; do
  if resolve_codex_version "$invalid" >/dev/null 2>&1; then
    echo "非法版本未被拒绝：$invalid" >&2
    exit 1
  fi
done

echo 'Bash versioning tests passed.'
