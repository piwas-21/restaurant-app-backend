#!/usr/bin/env bash
# RUMI Backend — file-length gate.
# Enforces the per-file LOC limits from CLAUDE.md §4.
#
# Modes:
#   bash scripts/check-file-length.sh --all            # whole-tree (CI mode)
#   bash scripts/check-file-length.sh <file> [<file>]  # specific files (pre-commit mode)
#   bash scripts/check-file-length.sh --regen-baseline # rewrite scripts/file-length-baseline.txt
#
# Behaviour:
#   New violations  → exit 1, print path + limit + actual.
#   Baselined files → exit 0 (grandfathered; ratchet down via refactor track).
#   Per-file exempt → file may opt out via top-of-file marker:
#                       // FILE_LENGTH_EXEMPT: <reason>
#                     within the first 5 lines.
#
# CLAUDE.md §4 limits:
#   Controller (Features/**/*Controller.cs)      150
#   Command/Query handler (Commands|Queries .cs) 200
#   Service class (*Service.cs)                  300
#   Entity (Domain/**/*.cs)                      100
#   DTO (Features/**/Dtos/*.cs)                   60
#   Validator (*Validator.cs)                     60
#   Configuration class (*Settings.cs)            50

set -euo pipefail

cd "$(dirname "$0")/.."

BASELINE_PATH="scripts/file-length-baseline.txt"

# Returns the LOC limit for a given path, or 0 if no rule matches.
limit_for_path() {
  local p="$1"
  case "$p" in
    # Validators are also Commands/Queries — match validators first.
    *Validator.cs)                                                   echo 60 ;;
    # DTOs (path contains /Dtos/)
    */Dtos/*.cs)                                                     echo 60 ;;
    # Configuration / settings classes
    *Settings.cs)                                                    echo 50 ;;
    # Controllers
    RestaurantSystem.Api/Features/*Controller.cs)                    echo 150 ;;
    RestaurantSystem.Api/Features/**/*Controller.cs)                 echo 150 ;;
    # Domain entities
    RestaurantSystem.Domain/*.cs|RestaurantSystem.Domain/**/*.cs)    echo 100 ;;
    # Command / Query handlers
    *Command.cs|*CommandHandler.cs)                                  echo 200 ;;
    *Query.cs|*QueryHandler.cs)                                      echo 200 ;;
    # Services (catch-all *Service.cs in the API project)
    RestaurantSystem.Api/*Service.cs)                                echo 300 ;;
    RestaurantSystem.Api/**/*Service.cs)                             echo 300 ;;
    *)                                                               echo 0 ;;
  esac
}

# True if the file opts out via FILE_LENGTH_EXEMPT marker in its first 5 lines.
is_exempt() {
  head -n 5 "$1" 2>/dev/null | grep -q "FILE_LENGTH_EXEMPT"
}

# True if the file is listed in the baseline (existing oversized files).
is_baselined() {
  [[ -f "$BASELINE_PATH" ]] || return 1
  grep -Fxq "$1" "$BASELINE_PATH"
}

# Walk the API + Domain trees.
list_all_files() {
  find RestaurantSystem.Api RestaurantSystem.Domain -name "*.cs" -type f \
    -not -path "*/bin/*" -not -path "*/obj/*" -not -path "*/Migrations/*" 2>/dev/null
}

check_one() {
  local path="$1"
  [[ -f "$path" ]] || return 0
  local limit
  limit=$(limit_for_path "$path")
  [[ "$limit" -gt 0 ]] || return 0

  local actual
  actual=$(wc -l < "$path" | tr -d ' ')
  [[ "$actual" -le "$limit" ]] && return 0

  # Over limit. Check exemptions.
  is_exempt "$path"  && return 0
  is_baselined "$path" && return 0

  echo "FAIL  $path  ($actual LOC > $limit)"
  return 1
}

if [[ "${1:-}" == "--regen-baseline" ]]; then
  cat > "$BASELINE_PATH" <<'HEADER'
# Backend file-length baseline.
# Files listed here exceed the CLAUDE.md §4 limit and are tracked
# for refactor (see docs/SPRINT-PLAN.md). The file-length checker
# (scripts/check-file-length.sh) skips these so existing debt
# doesn't block new MRs; new violations still block.
#
# Regenerate after a refactor lands:
#   bash scripts/check-file-length.sh --regen-baseline
# Inspect for ratchet opportunities:
#   bash scripts/check-file-length.sh --all
HEADER
  while IFS= read -r f; do
    limit=$(limit_for_path "$f")
    [[ "$limit" -gt 0 ]] || continue
    actual=$(wc -l < "$f" | tr -d ' ')
    [[ "$actual" -le "$limit" ]] && continue
    is_exempt "$f" && continue
    echo "$f" >> "$BASELINE_PATH"
  done < <(list_all_files)
  echo "Wrote $BASELINE_PATH ($(grep -cv '^#\|^$' "$BASELINE_PATH" || true) entries)."
  exit 0
fi

failed=0
if [[ "${1:-}" == "--all" || $# -eq 0 ]]; then
  while IFS= read -r f; do
    check_one "$f" || failed=1
  done < <(list_all_files)
else
  for f in "$@"; do
    check_one "$f" || failed=1
  done
fi

if [[ "$failed" -ne 0 ]]; then
  echo ""
  echo "File-length gate failed."
  echo "  Either refactor the file below the CLAUDE.md §4 limit, or — if a"
  echo "  refactor is not in scope for this MR — add the path to"
  echo "  $BASELINE_PATH and link a follow-up issue in the MR description."
  echo "  Per-file opt-out (rare; needs explicit reviewer sign-off):"
  echo "    // FILE_LENGTH_EXEMPT: <reason>     (first 5 lines)"
  exit 1
fi
