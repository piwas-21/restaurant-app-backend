# ADR-006 — Runtime tenant attribution

Status: accepted 2026-10-10.

Tenant branding retains the public `/api/tenant/partner` route and adds nullable `email`.
`Partner:RuntimeUrl` and `Partner:TenantSlug` enable a cached outbound HTTPS read from
Sofra's public branding API. The API carries only approved public attribution and no
credentials, legal identity or partner contact data. The platform default alone supplies
company email; restaurant identity and contact fields are unchanged.

Fresh snapshots live for `Partner:RefreshSeconds` (60 by default). Failed requests retry
on the same cadence and cached data expires after `Partner:MaxStaleSeconds` (300). An
explicit null brand or 404 clears a prior credit immediately on the next refresh. ETag
304 renews a valid snapshot. Invalid bodies are outages; they never clear good data or
invent the Sofra default. No dynamic failure restores stale environment-based attribution.
Redirects are disabled and requests have a configured three-second timeout. This service
is independent of ordering and holds no DB credentials for the control plane.

An empty runtime URL retains the original environment-based attribution for rollback.
Deploy the Sofra migration and API before enabling runtime configuration on tenant stacks.
Use the deploy repo's template-owned narrow migration mode and recreate only the backend.
