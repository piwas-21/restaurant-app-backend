#!/usr/bin/env bash
# RUMI Backend — install pre-commit hooks.
# Run this once after cloning the repo, and again after pulling hook updates.
#
# Usage: bash scripts/setup_hooks.sh

set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'
ok()   { echo -e "${GREEN}✓${NC}  $1"; }
warn() { echo -e "${YELLOW}⚠${NC}  $1"; }
fail() { echo -e "${RED}✗${NC}  $1"; exit 1; }

cd "$(dirname "$0")/.."

# ── pre-commit framework ──────────────────────────────────────────────
if ! command -v pre-commit >/dev/null 2>&1; then
  warn "pre-commit not installed."
  echo "    Install: brew install pre-commit       (macOS)"
  echo "             pip install pre-commit         (any platform)"
  echo "             pipx install pre-commit        (recommended for global)"
  fail "Re-run this script after installing pre-commit."
fi
ok "pre-commit found: $(pre-commit --version)"

# ── detect-secrets (used by the secret-scan hook) ─────────────────────
if ! command -v detect-secrets >/dev/null 2>&1; then
  warn "detect-secrets not installed (used by the secret-scan hook)."
  echo "    Install: pip install detect-secrets"
  echo "             pipx install detect-secrets   (recommended for global)"
  fail "Re-run this script after installing detect-secrets."
fi
ok "detect-secrets found: $(detect-secrets --version)"

# ── Install git hooks ────────────────────────────────────────────────
pre-commit install
pre-commit install --hook-type pre-push 2>/dev/null || true
ok "Git hooks installed (.git/hooks/pre-commit, .git/hooks/pre-push)."

# ── Verify .secrets.baseline exists ──────────────────────────────────
if [[ ! -f .secrets.baseline ]]; then
  warn ".secrets.baseline missing — generating now..."
  detect-secrets scan --exclude-files '\.secrets\.baseline$|/bin/|/obj/|/node_modules/|\.dll$|\.pdb$' > .secrets.baseline
  ok "Generated .secrets.baseline (review and commit it)."
fi

# ── Optional: warm hook cache ────────────────────────────────────────
echo ""
echo "Warming pre-commit hook cache..."
pre-commit install-hooks
ok "Hooks ready."

echo ""
echo "Done. Hooks installed for this clone."
echo "  Pre-commit gates: trailing-whitespace, EOF, large files, secret scan, no-commit-to-protected"
echo "  Bypass attempts:  use 'pre-commit run --all-files' to dry-run; never 'git commit --no-verify'."
