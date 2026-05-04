#!/usr/bin/env bash
# RUMI Backend — bootstrap local secrets file for development.
#
# Creates RestaurantSystem.Api/app-secrets.json from the example template if missing.
# This file is gitignored. NEVER commit it.
#
# Usage:
#   bash scripts/dev-secrets.sh

set -euo pipefail

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
ok()   { echo -e "${GREEN}✓${NC}  $1"; }
warn() { echo -e "${YELLOW}⚠${NC}  $1"; }

cd "$(dirname "$0")/.."

SECRETS_PATH="RestaurantSystem.Api/app-secrets.json"
EXAMPLE_PATH="RestaurantSystem.Api/app-secrets.example.json"

if [[ -f "$SECRETS_PATH" ]]; then
  ok "$SECRETS_PATH already exists. Not overwriting."
  echo "    To re-bootstrap: delete the file first, then re-run this script."
  exit 0
fi

if [[ -f "$EXAMPLE_PATH" ]]; then
  cp "$EXAMPLE_PATH" "$SECRETS_PATH"
  ok "Created $SECRETS_PATH from example template."
  warn "Edit it to add real local credentials BEFORE first run. The file is gitignored."
else
  warn "$EXAMPLE_PATH not found. Creating a minimal stub at $SECRETS_PATH."
  cat > "$SECRETS_PATH" <<'EOF'
{
  "_comment": "Local development secrets. NEVER commit. Add to .gitignore.",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=restaurantdb;Username=postgres;Password=postgres123"
  },
  "JwtSettings": {
    "Secret": "REPLACE_WITH_AT_LEAST_32_CHAR_SECRET",
    "Issuer": "RestaurantSystemDev",
    "Audience": "RestaurantSystemDevUsers",
    "ExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 7
  },
  "EmailSettings": {
    "SmtpHost": "",
    "SmtpPort": 587,
    "Username": "",
    "Password": "",
    "FromEmail": "noreply@example.com",
    "FromName": "RUMI Dev",
    "AdminEmail": "admin@example.com",
    "FrontendBaseUrl": "http://localhost:3000",
    "BackendBaseUrl": "http://localhost:5221",
    "EmailsEnabled": false,
    "LogEmailsOnly": true
  }
}
EOF
  ok "Stub written. Edit $SECRETS_PATH to add real credentials before first run."
fi
