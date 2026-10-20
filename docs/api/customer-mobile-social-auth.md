# Native Google customer accounts

These native-only endpoints remain unavailable unless `CustomerNativeSocial:GoogleEnabled`, valid
shared-app audiences and current native `accounts` capability are enabled. Public bootstrap reports
`flags.nativeGoogleAuthV1`; missing configuration leaves it false. Existing web social endpoints
keep their contracts. No tenant is activated by this source change.

All four POSTs are under `/api/customer-mobile/auth/google`, share the existing `auth` IP budget,
use no-store responses and reject unknown JSON fields. Every object requires `schemaVersion: 1`.
Proofs, ID tokens and passwords belong only in request bodies; do not put them in routes, analytics,
logs, persisted queries or browser return URLs.

| Route | Other fields | Authority and response |
| --- | --- | --- |
| `challenge` | None | Anonymous login challenge; `ApiResponse<NativeSocialChallengeDto>` |
| `complete` | `challengeId`, `proof`, `idToken`; optional `firstName`, `lastName` | Original login proof plus nonce-bound Google token; `ApiResponse<AuthResponse>` |
| `link/challenge` | `currentPassword` | Current authenticated Customer and fresh password reauthentication; challenge DTO |
| `link/complete` | `challengeId`, `proof`, `idToken` | Original authenticated Customer/link proof and unchanged security stamp; `ApiResponse<string>`, no new session |

The challenge DTO contains `schemaVersion`, `challengeId`, independent random `proof` and `nonce`,
and `expiresAt`. Proof/nonce each contain 32 random bytes encoded as 43 URL-safe characters. Only
the SHA-256 proof digest is stored. Challenge lifetime defaults to 180 seconds and is bounded to
60–300 seconds. Time is captured after issuance lock waits. The client must supply the exact nonce
to its Google SDK; the server requires that nonce from the signed token, an allowed audience,
Google issuer, stable bounded subject, expiry and issue time. Google's existing signature library
owns trusted keys/signature validation. No database transaction spans key fetching/token validation.

The selected React Native package's Universal custom-nonce API requires a license. The owner has
confirmed none is available yet. Backend tests use offline provider-boundary fixtures; they do not
prove installed SDK behavior, publisher configuration or real provider sign-in. The client package
must be verified before installation; an unbound token cannot substitute for this contract.

Persisted identity is `(tenantId, provider, issuer, subject)`, with a unique provider binding per
restaurant account. Provider email is contact only. A new subject matching any existing local email
returns `NATIVE_SOCIAL_LINK_REQUIRED`; the customer must sign into that restaurant account and
explicitly link after local password reauthentication. No automatic email linking occurs. Returning
subjects keep their original contact/profile even if provider email changes or disappears. Current
Customer role, confirmed email, account existence and absence of scheduled deletion are required.
A new account additionally requires verified provider email and bounded nonblank first/last names;
missing names return `NATIVE_SOCIAL_PROFILE_REQUIRED`. Start a fresh challenge after collecting them.

After signature/nonce validation, consumption commits separately under the provider/challenge locks.
A second transaction revalidates the original stored frame, consumption instant, current capability,
expiry and account authority before binding/creating an account and issuing its refresh session.
Link execution locks User before its challenge, matching erasure. Business refusal or session-storage
failure cannot resurrect the consumed nonce; account/binding/session effects roll back together.
An ambiguous client outcome requires fresh sign-in/challenge, never replay or assumed success.
Optional existing anonymous-basket merge runs after the committed login/session transaction.

There are at most 1,000 active challenges per tenant. Before its issuance transaction/user lock, an autocommit statement removes up to 100 expired rows; a
minute cleanup worker erases up to 1,000 expired rows even when social sign-in is later disabled.
Cleanup holds no User lock after acquiring challenge rows. Physical account erasure cascades both
bindings and owned challenges; anonymous login challenges expire independently. Tokens, credentials
and provider grants are never stored in these tables.

Settings: `GoogleEnabled` (default false), `GoogleClientIds` (1–10 unique bounded OAuth audiences
when enabled), `ChallengeLifetimeSeconds`. Migration
`20261020102803_AddCustomerNativeSocialIdentities` adds only the two private tables, constraints and
indexes. Rollback first takes both table locks without waiting, then refuses existing identity bindings or live challenges.
An active reader/writer makes rollback fail closed; no concurrent authority can be lost between its guard and DROP.
It must be applied through the normal release procedure before enablement. This work has
not applied it to staging or production. Apple grant custody/revocation and installed Google/Apple
acceptance remain separate open plan work.
