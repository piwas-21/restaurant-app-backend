#!/usr/bin/env bash
# Dry-run the stored-image resize backfill and build a side-by-side comparison page.
#
# Resize-on-upload only ever applied to NEW uploads; anything stored before it shipped is still
# whatever the camera produced. This runs the same pipeline over what's already on disk, writes
# each resized candidate to the preview folder, and renders original-vs-preview pairs so the
# result can be judged BEFORE any original is overwritten.
#
# Nothing here writes over a stored image. Applying is a separate, explicit call — see the
# "next step" line this prints at the end.
#
# Usage:
#   API_BASE=https://www.rumirestaurant.ch ADMIN_TOKEN=eyJ… ./scripts/image-backfill-preview.sh
#   API_BASE=… ADMIN_TOKEN=… MAX_FILES=50 OUT_DIR=/tmp/backfill ./scripts/image-backfill-preview.sh
set -euo pipefail

API_BASE="${API_BASE:?set API_BASE, e.g. https://www.rumirestaurant.ch}"
ADMIN_TOKEN="${ADMIN_TOKEN:?set ADMIN_TOKEN to an Admin JWT}"
MAX_FILES="${MAX_FILES:-500}"
OUT_DIR="${OUT_DIR:-./image-backfill-preview}"

command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }

mkdir -p "$OUT_DIR"
REPORT="$OUT_DIR/report.json"
PAGE="$OUT_DIR/comparison.html"

echo "Dry-running the backfill against $API_BASE (max $MAX_FILES files)…"
curl -fsS -X POST \
  "$API_BASE/api/maintenance/images/backfill?apply=false&maxFiles=$MAX_FILES" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Accept: application/json' \
  -o "$REPORT"

jq -e '.success' "$REPORT" >/dev/null || { echo "Backfill call failed:" >&2; jq . "$REPORT" >&2; exit 1; }

# The page loads both images straight from their public URLs, so it renders the real bytes rather
# than anything this script re-encodes.
{
  cat <<'HTML'
<!doctype html>
<meta charset="utf-8">
<title>Image backfill — before / after</title>
<style>
  body { font-family: system-ui, sans-serif; margin: 2rem; background: #fafafa; color: #222; }
  h1 { margin: 0 0 .25rem; }
  .summary { margin-bottom: 2rem; color: #555; }
  .pair { background: #fff; border: 1px solid #e3e3e3; border-radius: 10px; padding: 1rem; margin-bottom: 1.5rem; }
  .pair h2 { font-size: 1rem; margin: 0 0 .75rem; font-family: ui-monospace, monospace; }
  .cols { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
  .cols figure { margin: 0; }
  .cols img { width: 100%; height: auto; border-radius: 6px; background: #eee; }
  figcaption { font-size: .8rem; color: #666; margin-top: .4rem; }
  .saved { color: #1a7f37; font-weight: 600; }
  @media (max-width: 700px) { .cols { grid-template-columns: 1fr; } }
</style>
HTML
  jq -r '
    .data as $d
    | "<h1>Image backfill — before / after</h1>",
      "<p class=\"summary\">Scanned \($d.filesScanned) · would change \($d.filesChanged) · skipped \($d.filesSkipped) · failed \($d.filesFailed)<br>" +
      "Bound: longest edge \($d.maxImageEdgePixels)px, quality \($d.imageQuality) — the same settings new uploads use.<br>" +
      "Total <span class=\"saved\">\(($d.totalBytesSaved / 1048576 * 10 | floor) / 10) MB</span> saved of " +
      "\((($d.totalOriginalBytes / 1048576) * 10 | floor) / 10) MB.</p>",
      ( $d.entries[] | select(.previewUrl != null) |
        "<div class=\"pair\"><h2>\(.relativePath) — \(.outcome)</h2><div class=\"cols\">" +
        "<figure><img loading=\"lazy\" src=\"\(.originalUrl)\" alt=\"original\">" +
        "<figcaption>Before — \(.originalWidth)×\(.originalHeight), \((.originalBytes / 1024 | floor)) KB</figcaption></figure>" +
        "<figure><img loading=\"lazy\" src=\"\(.previewUrl)\" alt=\"resized\">" +
        "<figcaption>After — \(.newWidth)×\(.newHeight), \((.newBytes / 1024 | floor)) KB " +
        "(<span class=\"saved\">−\((.bytesSaved * 100 / (if .originalBytes == 0 then 1 else .originalBytes end)) | floor)%</span>)</figcaption></figure>" +
        "</div></div>" )
  ' "$REPORT"
} > "$PAGE"

echo
jq -r '.message' "$REPORT"
echo "Report:     $REPORT"
echo "Comparison: $PAGE   (open it in a browser)"
echo
echo "Happy with them? Apply for real:"
echo "  curl -X POST '$API_BASE/api/maintenance/images/backfill?apply=true&maxFiles=$MAX_FILES' -H 'Authorization: Bearer \$ADMIN_TOKEN'"
echo "Then clear the previews (they are full-size copies sitting on the uploads volume):"
echo "  curl -X DELETE '$API_BASE/api/maintenance/images/backfill/previews' -H 'Authorization: Bearer \$ADMIN_TOKEN'"
