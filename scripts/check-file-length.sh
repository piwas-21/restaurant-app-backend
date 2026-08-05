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
    # Validators are also Commands/Queries — match validators first. *ValidatorBase.cs is matched
    # explicitly for the same reason *ControllerBase.cs is below: a shared validator base holds
    # validation rules by every measure the limit cares about, but `*Validator.cs` does not match
    # it, so it could grow without bound while the validators deriving from it stayed compliant.
    *Validator.cs|*ValidatorBase.cs)                                 echo 60 ;;
    # DTOs (path contains /Dtos/)
    */Dtos/*.cs)                                                     echo 60 ;;
    # Configuration / settings classes
    *Settings.cs)                                                    echo 50 ;;
    # Controllers. ControllerBase.cs is matched explicitly: a shared controller base is a controller
    # by every measure the limit cares about, but `*Controller.cs` does not match it, so one could
    # grow without bound while the classes deriving from it stayed compliant.
    RestaurantSystem.Api/Features/*Controller.cs)                    echo 150 ;;
    RestaurantSystem.Api/Features/**/*Controller.cs)                 echo 150 ;;
    RestaurantSystem.Api/Features/*ControllerBase.cs)                echo 150 ;;
    RestaurantSystem.Api/Features/**/*ControllerBase.cs)             echo 150 ;;
    # Domain entities
    RestaurantSystem.Domain/*.cs|RestaurantSystem.Domain/**/*.cs)    echo 100 ;;
    # Command / Query handlers
    *Command.cs|*CommandHandler.cs)                                  echo 200 ;;
    *Query.cs|*QueryHandler.cs)                                      echo 200 ;;
    # Services (catch-all *Service.cs in the API project)
    RestaurantSystem.Api/*Service.cs)                                echo 300 ;;
    RestaurantSystem.Api/**/*Service.cs)                             echo 300 ;;
    # Anything else living in a Services/ directory. Matching services only by the `*Service.cs`
    # suffix left a hole: a class in Services/ named anything else fell through to `echo 0` and was
    # skipped in silence. That is not hypothetical — AnonymousBasketMerger.cs reached 279 committed
    # LOC (at 0a8fe28) with NO limit applied at any size, because its name does not end in `Service`;
    # it went past 300 in the working tree during #313 and this gate still printed nothing. A file's
    # directory says what it is at least as reliably as its suffix, so gate on the directory too.
    #
    # `*Services/` and not `*/Services/`: in a case pattern `*` matches `/`, so `*/Services/` needs
    # at least one intervening segment and would MISS the two Services dirs that sit directly under
    # the project root — `RestaurantSystem.Api/Services/` and `RestaurantSystem.Api/BackgroundServices/`
    # (the latter is the data-loss class of §9). With `*Services/` the `*` may also match the empty
    # string, so depth 0 and any deeper nesting are both covered.
    RestaurantSystem.Api/*Services/*.cs)                             echo 300 ;;
    # Shared validation rule classes. These are what a validator at its 60-line limit extracts INTO
    # (ProductContentRule for #306, NestedContentRule for #321), so leaving them ungated made the
    # validator limit trivially escapable — move the rule out and it is unbounded again. 60 is the
    # wrong number here: it would fail both existing rule classes on sight and undo the very pattern
    # those extractions established. §4 has no row for this kind, so they are gated as the service
    # classes they most resemble, pending a §4 decision (#315).
    RestaurantSystem.Api/Common/Validation/*.cs)                     echo 300 ;;
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

# Tallies, so a passing run can say what it actually looked at. A gate that prints nothing on
# success is indistinguishable from a gate that examined nothing, and this one has a live example
# of exactly that: a 279-LOC file passed in silence because no rule matched its name.
n_walked=0; n_gated=0; n_ungated=0; n_exempt=0; n_baselined=0; n_failed=0

# Every rule in limit_for_path is anchored on a literal `RestaurantSystem.Api/` or
# `RestaurantSystem.Domain/`, so a path given as absolute or `./`-prefixed matches NOTHING and is
# waved through at limit 0 — the same file by relative path exits 1. Normalise to repo-relative
# first. The script has already cd'd to the repo root, so $PWD is that root.
normalise_path() {
  local p="${1#./}"
  p="${p#"$PWD"/}"
  echo "${p#./}"
}

# Hard-fail if the whole-tree walk found nothing. Shared by --all and --regen-baseline: the latter
# TRUNCATES the baseline before writing, so a run finding no corpus does not merely report a false
# clean, it un-grandfathers all existing entries and commits that.
# NOTE: this cannot be a wrong-cwd guard — line 29 cd's to the repo root unconditionally. It fires
# only when the project directories are genuinely absent: renamed, deleted, or a sparse checkout.
assert_corpus() {
  [[ "$(list_all_files | head -n 1)" != "" ]] && return 0
  echo "File-length gate ERROR: found 0 .cs files under RestaurantSystem.Api / RestaurantSystem.Domain." >&2
  echo "  Those project directories must exist under $PWD." >&2
  exit 2
}

check_one() {
  local path
  path="$(normalise_path "$1")"
  [[ -f "$path" ]] || return 0
  case "$path" in *.cs) ;; *) return 0 ;; esac   # count only what the message claims to count
  n_walked=$((n_walked + 1))
  local limit
  limit=$(limit_for_path "$path")
  if [[ "$limit" -le 0 ]]; then
    n_ungated=$((n_ungated + 1))
    return 0
  fi
  n_gated=$((n_gated + 1))

  local actual
  actual=$(wc -l < "$path" | tr -d ' ')
  [[ "$actual" -le "$limit" ]] && return 0

  # Over limit. Check exemptions.
  if is_exempt "$path"; then n_exempt=$((n_exempt + 1)); return 0; fi
  if is_baselined "$path"; then n_baselined=$((n_baselined + 1)); return 0; fi

  n_failed=$((n_failed + 1))
  echo "FAIL  $path  ($actual LOC > $limit)"
  return 1
}

if [[ "${1:-}" == "--regen-baseline" ]]; then
  assert_corpus
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
  echo "Wrote $BASELINE_PATH from $(list_all_files | wc -l | tr -d ' ') walked .cs file(s) — $(grep -cv '^#\|^$' "$BASELINE_PATH" || true) entries."
  exit 0
fi

failed=0
mode=""
if [[ "${1:-}" == "--all" || $# -eq 0 ]]; then
  mode="whole-tree"
  # Assert the corpus BEFORE walking. A whole-tree run that found no files is a broken gate, not a
  # clean tree — a renamed project directory would otherwise report success having examined nothing.
  assert_corpus
  while IFS= read -r f; do
    check_one "$f" || failed=1
  done < <(list_all_files)
else
  mode="$# path(s)"
  for f in "$@"; do
    check_one "$f" || failed=1
  done
  # Same principle for pre-commit's path mode, which is where the gate actually blocks commits:
  # being handed paths and examining none of them is a broken invocation, not a pass. Unreachable
  # via .pre-commit-config.yaml, whose `files: \.cs$` filter only ever passes existing .cs files
  # (and which skips the hook entirely when that list is empty), so this cannot false-block a commit.
  if [[ "$n_walked" -eq 0 ]]; then
    echo "File-length gate ERROR: given $# path(s), examined 0 — no argument was an existing .cs file." >&2
    printf '  given: %s\n' "$*" >&2
    exit 2
  fi
fi

# Say what was examined, pass or fail. Counts are the evidence that the gate ran over a real corpus.
# gated + no-rule = walked. The second group is a breakdown of the over-limit files only: baselined
# and exempt files ARE over their limit, they are excused; $n_failed is what actually blocks.
echo "File-length gate ($mode): walked $n_walked .cs file(s) — $n_gated gated, $n_ungated with no matching rule. Over limit: $n_baselined baselined, $n_exempt exempt, $n_failed blocking."

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
