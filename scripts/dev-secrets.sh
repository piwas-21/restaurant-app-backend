#!/usr/bin/env bash
# RUMI Frontend — bootstrap local .env.local from .env.example.
#
# .env.local is gitignored. NEVER commit it.
#
# Usage:
#   bash scripts/dev-secrets.sh

set -euo pipefail

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
ok()   { echo -e "${GREEN}✓${NC}  $1"; }
warn() { echo -e "${YELLOW}⚠${NC}  $1"; }

cd "$(dirname "$0")/.."

ENV_PATH=".env.local"
EXAMPLE_PATH=".env.example"

if [[ -f "$ENV_PATH" ]]; then
  ok "$ENV_PATH already exists. Not overwriting."
  echo "    To re-bootstrap: delete the file first, then re-run this script."
  exit 0
fi

if [[ ! -f "$EXAMPLE_PATH" ]]; then
  warn "$EXAMPLE_PATH not found. Cannot bootstrap $ENV_PATH."
  exit 1
fi

cp "$EXAMPLE_PATH" "$ENV_PATH"
ok "Created $ENV_PATH from $EXAMPLE_PATH."
warn "Review $ENV_PATH and adjust values for your local setup before first run."
echo "    Common adjustments:"
echo "      NEXT_PUBLIC_API_URL          — set to your local backend (default: http://localhost:5221)"
echo "      NEXT_PUBLIC_GOOGLE_CLIENT_ID — your dev OAuth client ID (if testing Google login)"
echo "      ADMIN / CASHIER / CUSTOMER   — your dev/test sign-in credentials (Playwright + manual login)"
echo ""
echo "    The file is gitignored. NEVER commit it."
