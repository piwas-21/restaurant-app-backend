# Shared customer mobile service

Independent .NET 10 host for the shared Sofra native application. It does not load tenant
application services, connection strings, customer accounts or the private provisioning registry.
The current slice serves only the public directory and health; transactional push persistence,
tenant-authenticated registration/dispatch and central link resolution remain in the mobile plan.

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
