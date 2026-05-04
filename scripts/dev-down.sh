#!/usr/bin/env bash
# RUMI Backend — stop the local dev stack.
#
# Usage:
#   bash scripts/dev-down.sh           # stop containers, keep data volume
#   bash scripts/dev-down.sh --reset   # stop containers AND delete data volume

set -euo pipefail

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
ok()   { echo -e "${GREEN}✓${NC}  $1"; }
warn() { echo -e "${YELLOW}⚠${NC}  $1"; }

cd "$(dirname "$0")/.."

if [[ "${1:-}" == "--reset" ]]; then
  warn "Stopping containers AND removing data volume…"
  docker compose -f docker-compose-dev-all.yml down -v
  ok "Containers stopped, postgres-data-dev volume removed."
else
  docker compose -f docker-compose-dev-all.yml down
  ok "Containers stopped (data volume preserved)."
fi
