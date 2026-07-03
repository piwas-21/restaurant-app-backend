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
  *Handler.cs)                    lim=200; kind="Handler" ;;
  *Validator.cs)                  lim=60;  kind="Validator" ;;
  *Settings.cs|*Configuration.cs) lim=50;  kind="Config" ;;
  *Dto.cs|*/Dtos/*)               lim=60;  kind="DTO/record" ;;
  *Service.cs)                    lim=800; kind="Service" ;;
  */Domain/Entities/*)            lim=100; kind="Entity" ;;
esac
[[ $lim -gt 0 && $loc -gt $lim ]] && warn "file-length: $kind ~${loc} LOC (limit ${lim}) — split per CLAUDE.md §4"

# Convention checks (grep -n for a line hint)
case "$f" in *Handler.cs)
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
