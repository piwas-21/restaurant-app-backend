# Shared customer mobile service

Independent .NET 10 host for the shared Sofra native application. It does not load tenant
application services, connection strings, customer accounts or the private provisioning registry.
The current slice serves the public directory, health and a disabled-by-default payment-return
landing plus opt-in OS-association documents. Transactional push persistence, tenant-authenticated registration/dispatch and other link
destinations remain in the mobile plan.

Run with `dotnet run --project Sofra.MobileService`. Configuration is required before startup;
production deployment is a separate reviewed deploy change. Build its container from repository
root using `docker build -f Sofra.MobileService/Dockerfile .`.

## Reviewed directory configuration

Set `MobileDirectory` through a dedicated configuration file or environment variables. The host
uses standard .NET configuration, rejects unknown keys in this section and validates on startup.
Use only public fields: `Environment` (staging/production), `Revision`, `LifetimeSeconds` (60–86400,
default 3600) and `Tenants`. Each tenant contains immutable `Id`, `Slug`, public `Name`, canonical
HTTPS `ApiOrigin`, `WebOrigin`, `ImageOrigin`, `Status` (active/read-only/disabled), and positive
`ContractVersions`. ID, slug and API origin must each be unique; the directory is limited to 1000
tenants. Origin credentials, paths, queries, fragments and noncanonical spelling are refused.

Never mount the internal provisioning registry into this process. Its settings contain only the
public DTO and serialization uses explicit response types; unrelated host configuration cannot
appear in the directory. Tenant endpoint changes require configuration review and a host restart.
Do not change an existing immutable tenant ID to represent a different restaurant.

`GET /v1/directory` returns a raw version-1 directory matching the mobile runtime schema, with UTC
Z timestamps, expiry and the reviewed revision. Every read gets a fresh bounded lease over the
same immutable configuration snapshot. `Cache-Control: no-store` prevents intermediary caches from
extending an active tenant lease. The client withdraws expired entries during directory outages.
An empty directory is valid for initial setup/offboarding; missing environment/revision is not.
The response is public navigation configuration and conveys no account or table authorization.

## Verification and rollout

Run `dotnet test RestaurantSystem.IntegrationTests --filter FullyQualifiedName~MobileDirectory`.
Tests cover canonical origins, ambiguity, compatibility/lifecycle refusal, real HTTP wire shape,
expiry, mutation isolation and non-disclosure of unrelated private settings. The solution build,
format, file-length, existing integration CI and Sonar gates include the new host.

This PR does not publish a domain, associate native app IDs, activate a tenant, configure a database,
send push notifications or create a signed application. Review directory activation, trusted app
origins, deployment health, configuration rollback and installed-app readback separately.

`GET /health` is intentionally anonymous infrastructure liveness and returns only the fixed
`{"status":"healthy"}` payload after validated host startup. It exposes no configuration, tenant
identity, build/environment details, secrets, dependency diagnostics or customer data. It does not
claim notification/database readiness; those dependencies need their own later readiness probes.

## Central payment-return fallback

`MobileLinks` is a separate strict public configuration section with `Enabled` (default false) and
`Origin` (canonical HTTPS origin on the standard port). No production origin or signing identity is
embedded. Unknown configuration keys or invalid nonempty origins prevent startup. Enabling this
landing alone does not enable native payments or establish an OS domain association.

`GET /r/{slug}/payment-return/{orderReference}?operation={operationId}` requires the configured Host,
an active/read-only, schema-1-compatible directory restaurant, and exact nonempty canonical UUID
references. Missing, duplicate or extra query data is refused. The anonymous route does not read
orders, accept credentials, call a provider, select an account or assert settlement. It returns fixed,
input-independent recovery instructions with no scripts, forms, external destinations or embedded
references. No-store, no-referrer, nosniff and restrictive content/frame policies limit browser residue.

The installed app uses this path as navigation only and rechecks its original private proof. A delayed
return cannot silently replace a newer restaurant selection. Actual approved domain publication,
Apple/Android association files, app entitlements/signing, device return/provider switching and
per-tenant payment activation remain separate gates in CUSTOMER-MOBILE-APPS-PLAN. Configuration and
landing deployment need their own reviewed deploy change; this source does not publish them.

## OS association metadata

`MobileAssociations` binds only reviewed public identity metadata: `Enabled` (default false),
`AppleApplicationId` (verified application identifier prefix plus bundle ID), `AndroidPackage`, and `AndroidSha256Fingerprints`.
Enabling requires the link host to be enabled with a valid origin, complete syntactically valid IDs,
and one to four unique canonical SHA-256 certificate fingerprints. Unknown keys refuse startup.
Identities and fingerprints use full-string validation; trailing newlines are invalid. No company
publisher value, certificate, private key or production hostname is generated or supplied by source.

The configured Host can then read `/.well-known/apple-app-site-association` and
`/.well-known/assetlinks.json` as JSON with no redirect, no-store and nosniff. Disabled associations
or a different Host return 404 with no document. The handler retains an immutable configuration
snapshot. Apple components cover payment-return paths and an operation query; Android metadata
publishes only the approved package/fingerprints, with the matching payment-return intent restriction
in the native app. These grant public navigation handling, never order/payment/account authorization.

Formats follow [Apple associated domains](https://developer.apple.com/documentation/xcode/supporting-associated-domains)
and [Android website associations](https://developer.android.com/training/app-links/configure-assetlinks).
Copy the Apple application identifier from the signed app entitlement; its prefix can differ from the
Team ID ([Apple details](https://developer.apple.com/documentation/bundleresources/applinks/details-swift.dictionary)).
Actual OS verification needs the real publisher/signing identities, reviewed HTTPS deployment and a
new signed installation/update with matching native declarations. Play App Signing may use a different
certificate from a local upload key. Source syntax tests cannot establish publisher ownership or OS
acceptance. Platform caches may retain prior association metadata; disablement is not proof of
immediate withdrawal on installed devices. Keep native payment activation gated independently.
