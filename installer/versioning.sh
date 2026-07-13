#!/usr/bin/env bash

resolve_codex_version() {
  local input="$1" informational_override="${2:-}" part value
  input="${input#"${input%%[![:space:]]*}"}"
  input="${input%"${input##*[![:space:]]}"}"
  [[ "$input" == [vV]* ]] && input="${input:1}"

  local pattern='^((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.((0|[1-9][0-9]*)))?)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$'
  [[ "$input" =~ $pattern ]] || { echo "版本号必须为 x.y.z[-suffix] 或 x.y.z.w[-suffix]：$1" >&2; return 2; }

  CODEX_VERSION="$input"
  CODEX_NUMERIC_VERSION="${BASH_REMATCH[1]}"
  IFS='.' read -r -a parts <<< "$CODEX_NUMERIC_VERSION"
  for part in "${parts[@]}"; do
    (( ${#part} <= 5 )) || { echo "数字版本的每一段必须位于 0..65534：$CODEX_NUMERIC_VERSION" >&2; return 2; }
    value=$((10#$part))
    (( value <= 65534 )) || { echo "数字版本的每一段必须位于 0..65534：$CODEX_NUMERIC_VERSION" >&2; return 2; }
  done
  if (( ${#parts[@]} == 3 )); then
    CODEX_PRODUCT_VERSION="$CODEX_NUMERIC_VERSION.0"
  else
    CODEX_PRODUCT_VERSION="$CODEX_NUMERIC_VERSION"
  fi
  CODEX_MACOS_VERSION="${parts[0]}.${parts[1]}.${parts[2]}"

  CODEX_INFORMATIONAL_VERSION="${informational_override:-$CODEX_VERSION}"
  [[ "$CODEX_INFORMATIONAL_VERSION" == [vV]* ]] && CODEX_INFORMATIONAL_VERSION="${CODEX_INFORMATIONAL_VERSION:1}"
  [[ "$CODEX_INFORMATIONAL_VERSION" =~ $pattern ]] || { echo "InformationalVersion 格式无效：$CODEX_INFORMATIONAL_VERSION" >&2; return 2; }
  [[ "${BASH_REMATCH[1]}" == "$CODEX_NUMERIC_VERSION" ]] || { echo "InformationalVersion 的数字部分必须与版本号一致。" >&2; return 2; }
}
