#!/usr/bin/env bash
# PostToolUse single-file checker (backend, *.cs). Gives an agent instant, in-loop
# feedback on file-length + a few CLAUDE.md conventions right after an edit — long
# before pre-commit/CI. Contract: NON-BLOCKING (always exit 0), fast (<200ms, no
# build/network), quiet on success, warnings to stderr as `path: <rule>: <details>`.
# Path comes from $1, else from the PostToolUse hook JSON on stdin (.tool_input.file_path).
set -uo pipefail

f="${1:-}"
if [[ -z "$f" && ! -t 0 ]]; then
  f="$(python3 -c "import sys,json;print(json.load(sys.stdin).get('tool_input',{}).get('file_path',''))" 2>/dev/null || true)"
fi
[[ -n "$f" && -f "$f" ]] || exit 0
case "$f" in *.cs) ;; *) exit 0 ;; esac                       # .cs only
case "$f" in *.g.cs|*.Designer.cs|*/Migrations/*|*/obj/*|*/bin/*) exit 0 ;; esac

warn() { echo "$f: $1" >&2; }
loc=$(wc -l < "$f" | tr -d ' ')

# File-length limits (backend/CLAUDE.md §4)
lim=0; kind=""
case "$f" in
  *Controller.cs)                 lim=150; kind="Controller" ;;
  *Validator.cs|*ValidatorBase.cs) lim=60; kind="Validator" ;;
  *Handler.cs|*Command.cs|*Query.cs) lim=200; kind="handler/command/query" ;;
  *Settings.cs|*Configuration.cs) lim=50;  kind="Config" ;;
  *Dto.cs|*/Dtos/*)               lim=60;  kind="DTO/record" ;;
  # 300, per §4 and the blocking gate. This read 800 until #315, so the in-loop checker reported a
  # clean bill of health ~500 LOC past the limit the blocking gate actually applies.
  *Service.cs)                    lim=300; kind="Service" ;;
  # Directory rules, mirroring scripts/check-file-length.sh — a class in Services/ or
  # Common/Validation/ named anything but *Service.cs had no limit here either.
  *Services/*.cs)                 lim=300; kind="Service" ;;
  */Common/Validation/*.cs)       lim=300; kind="shared validation rule" ;;
  */Domain/Entities/*)            lim=100; kind="Entity" ;;
esac

# Respect BOTH of the blocking gate's escape hatches — the baseline and the FILE_LENGTH_EXEMPT
# marker. Honouring one but not the other is how this checker ends up warning about files that
# pre-commit accepts: five baselined files (EmailService, BasketService, OrderMappingService,
# FidelityPointsService, CustomerDiscountService) commit cleanly, and so does any file carrying the
# documented opt-out. A warning no pre-commit run will ever reproduce is not early feedback, it is
# a standing false positive — and this checker's whole contract is "quiet on success".
is_excused() {
  local root rel
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  rel="${1#./}"; rel="${rel#"$root"/}"; rel="${rel#./}"     # -> repo-relative, whatever form came in
  head -n 5 "$1" 2>/dev/null | grep -q "FILE_LENGTH_EXEMPT" && return 0
  [[ -f "$root/scripts/file-length-baseline.txt" ]] || return 1
  grep -Fxq "$rel" "$root/scripts/file-length-baseline.txt"
}

if [[ $lim -gt 0 && $loc -gt $lim ]] && ! is_excused "$f"; then
  warn "file-length: $kind ~${loc} LOC (limit ${lim}) — split per CLAUDE.md §4"
fi

# Convention checks (grep -n for a line hint)
case "$f" in *Handler.cs|*Command.cs|*Query.cs)
  grep -nq 'throw new InvalidOperationException' "$f" \
    && warn "use NotFoundException/BadRequestException/ForbiddenException, not InvalidOperationException" ;;
esac
case "$f" in *Controller.cs)
  grep -nqE '\bDbContext\b' "$f" && warn "no DbContext in a controller — dispatch via a CQRS handler" ;;
esac
case "$f" in *Dto.cs|*/Dtos/*)
  grep -nq 'null!' "$f" && warn "no null! in a DTO — use 'required' or = string.Empty" ;;
esac
grep -nqE '"https?://' "$f" \
  && warn "hardcoded URL literal — source URLs belong in IOptions (EmailSettings.Frontend/BackendBaseUrl)"

exit 0
