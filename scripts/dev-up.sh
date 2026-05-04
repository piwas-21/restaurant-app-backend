#!/usr/bin/env bash
# RUMI Backend — one-shot local stack + run the API.
#
# What it does:
#   1. Brings up postgres + redis containers (from docker-compose-dev-all.yml).
#   2. Waits for postgres to be ready.
#   3. Applies pending EF Core migrations.
#   4. Runs the API locally via `dotnet run` (fast iteration, native debugger).
#
# Usage:
#   bash scripts/dev-up.sh                # full flow
#   bash scripts/dev-up.sh --no-run       # set up DB only; don't start the API
#   bash scripts/dev-up.sh --reset        # nuke postgres volume + reapply everything
#   bash scripts/dev-up.sh --help         # show this header

set -euo pipefail

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; CYAN='\033[0;36m'; NC='\033[0m'
info() { echo -e "${CYAN}ℹ${NC}  $1"; }
ok()   { echo -e "${GREEN}✓${NC}  $1"; }
warn() { echo -e "${YELLOW}⚠${NC}  $1"; }
fail() { echo -e "${RED}✗${NC}  $1"; exit 1; }

cd "$(dirname "$0")/.."

# ── Args ─────────────────────────────────────────────────────────────
NO_RUN=false
RESET=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-run) NO_RUN=true; shift ;;
    --reset)  RESET=true; shift ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) fail "Unknown arg: $1" ;;
  esac
done

# ── Preflight ────────────────────────────────────────────────────────
command -v docker >/dev/null || fail "docker not installed."
command -v dotnet >/dev/null || fail "dotnet not installed."

if ! docker info >/dev/null 2>&1; then
  fail "Docker daemon not running. Start Docker Desktop / OrbStack / etc., then re-run."
fi

# ── Optional reset ───────────────────────────────────────────────────
if $RESET; then
  warn "Resetting: stopping containers and removing postgres volume…"
  docker compose -f docker-compose-dev-all.yml down -v 2>/dev/null || true
  ok "Volumes nuked."
fi

# ── 1. Bring up postgres + redis ─────────────────────────────────────
info "[1/4] Starting postgres + redis…"
docker compose -f docker-compose-dev-all.yml up -d postgres redis
ok "Containers started."

# ── 2. Wait for postgres ─────────────────────────────────────────────
info "[2/4] Waiting for postgres readiness (timeout 30s)…"
for i in {1..30}; do
  if docker compose -f docker-compose-dev-all.yml exec -T postgres pg_isready -U postgres >/dev/null 2>&1; then
    ok "Postgres is ready."
    break
  fi
  if [[ "$i" -eq 30 ]]; then
    fail "Postgres did not become ready in 30s. Check 'docker compose logs postgres'."
  fi
  sleep 1
done

# ── 3. Apply migrations ──────────────────────────────────────────────
info "[3/4] Applying EF Core migrations…"
if ! dotnet ef --version >/dev/null 2>&1; then
  warn "dotnet-ef not installed. Installing as a local tool…"
  if [[ ! -f .config/dotnet-tools.json ]]; then
    dotnet new tool-manifest
  fi
  dotnet tool install --local dotnet-ef
fi

dotnet ef database update \
  --project RestaurantSystem.Infrastructure \
  --startup-project RestaurantSystem.Api \
  || fail "EF migration failed. Check connection string / migration history."
ok "Migrations applied."

# ── 4. Run the API ───────────────────────────────────────────────────
echo ""
ok "Local stack is up:"
echo "    Postgres:   localhost:5432  (user: postgres / pass: postgres123 / db: restaurantdb)"
echo "    Redis:      localhost:6379"
echo "    API (when started): http://localhost:5221  (Swagger: http://localhost:5221/swagger)"
echo ""

if $NO_RUN; then
  echo "  Skipping API run (--no-run). Start manually:  dotnet run --project RestaurantSystem.Api"
  echo "  Tear down:  bash scripts/dev-down.sh"
  exit 0
fi

info "[4/4] Starting API (Ctrl-C to stop; then run 'bash scripts/dev-down.sh' to stop containers)…"
echo ""
exec dotnet run --project RestaurantSystem.Api
