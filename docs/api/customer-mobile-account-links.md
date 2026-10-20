# Native customer account email actions

These additive routes reuse the tenant's Identity verification/reset tokens and email templates.
They remain unavailable unless `CustomerNativeAccounts:Enabled` and native `accounts` are both
enabled for an active tenant with a supported bootstrap/currency. Bootstrap publishes the boolean
`flags.nativeAccountLinksV1`; missing configuration leaves it false. No tenant is enabled here.

All five routes are anonymous POSTs under `/api/customer-mobile/auth`, use no-store responses and
strict JSON objects. Every object requires `schemaVersion: 1`; unknown fields are refused.

| Route | Other required fields | Existing abuse budget |
| --- | --- | --- |
| `register` | `firstName`, `lastName`, `email`, `password`, `confirmPassword` | `register` |
| `request-verification` | `email` | `email-verification` |
| `request-password-reset` | `email` | `forgot-password` |
| `verify` | `proof` | `email-verification` |
| `reset-password` | `proof`, `newPassword`, `confirmPassword` | `forgot-password` |

Responses use `ApiResponse<string>`. The two email-request acknowledgements reveal no account existence,
verification state or delivery outcome. Only current Customer accounts receive native action mail;
staff accounts cannot borrow the native rail. Native registration creates a Customer and sends a
verification mail without issuing access/refresh tokens. It returns no account or token DTO.
Registration retains the existing `EmailAlreadyExists` refusal for an occupied address.
Existing web endpoints keep their request/response and delivery behavior. The native resend shares
both the web route's IP budget and its persisted per-address cooldown, including registration.

Native mail links use the configured canonical HTTPS origin and public tenant slug:
`/r/{slug}/verify#proof={opaque}` or `/r/{slug}/recover#proof={opaque}`. The fragment never enters the
shared service's HTTP request. No email, password or raw Identity token appears in the public path
or query. The protected payload binds schema, immutable tenant and user IDs, current email, original
Identity token, action purpose and issue/expiry instants. Proofs are URL-safe, bounded to 4,096
characters and expire after the configured 5–120 minutes (default 60). Persisted Data Protection
keys must remain available across tenant restarts; they remain tenant-side.

The shared service validates its configured Host, public directory entry, contract and active
lifecycle. Its static no-store/no-referrer landing performs no account action and reflects no grant.
The association document adds only the two account paths alongside payment return. Signing,
deployment, actual OS association and installed app acceptance remain separate release gates.

Completion requires an explicit app action. The app sends the fragment proof only in the POST body
to the directory-approved tenant backend, outside persisted caches, analytics and logs. The backend
checks current activation, tenant, action, expiry, live Customer role, account and unchanged email,
then validates the original Identity token. It never signs in automatically. Password reset changes
the password/audit and revokes existing refresh credentials in one transaction before the change
notification. Replaying a consumed or invalidated reset token cannot change another password.

`CustomerNativeAccounts` settings are `Enabled`, `LinkOrigin`, `TenantSlug` and
`LinkLifetimeMinutes`. No caller-supplied redirect or delivery hint is accepted. Native mail reuses
the configured SMTP/Resend transport with bounded retries, but its separate private sender never
logs rendered bodies, grants, recipients or raw provider exceptions; log-only mode withholds the
message entirely. No migration, provider configuration, deployment or external email is performed
by the source test suite.
