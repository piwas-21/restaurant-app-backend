# Customer mobile bootstrap

GET /api/tenant/mobile/bootstrap?contractVersion=1 is an additive anonymous, no-store public
contract. Existing web/printer contracts and tenant behavior are unchanged. It is unavailable by
default. CustomerMobile settings use IOptions, validated at startup when enabled.

| Configuration key | Requirement |
|---|---|
| CustomerMobile:Enabled | Explicit true only after tenant/native acceptance |
| CustomerMobile:TenantId | Immutable non-empty GUID matching the reviewed shared directory |
| CustomerMobile:Lifecycle | active, read-only or disabled |
| CustomerMobile:EnabledCapabilities | Explicit native capability allow-list; absent means none |

The response uses the existing RestaurantInfo.currency column, never the device locale or legacy
Localization currency fallback. Null means unknown and disables financial capabilities. Locale and
actual effective timezone come from tenant localization and ITenantClock. Modules are effective
backend modules with ModulesEnforced as a separate diagnostic; native rollout does not replace
entitlements or server endpoint authorization. Provider-dependent payment capabilities stay unknown
until an authenticated/authorized availability/reconciliation contract proves them usable.

The initial customer-mobile source consumes this response for public browsing only. Account/order/
reservation/table/push integration, durable idempotency, device/signing acceptance and shared directory
service deployment remain separate gates. Do not enable native checkout from this endpoint alone.

Verification: CustomerMobileCapabilityTests and CustomerMobileBootstrapEndpointTests cover inert
configuration, capability/module/flag separation, incompatible versions, anonymous HTTP/envelope/
no-store behavior and a controlled EUR database fixture. No production config is changed here.
