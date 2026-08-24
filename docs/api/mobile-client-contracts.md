# Mobile client API contracts

> **Audience:** anyone writing a client against this API — in particular the mobile-app developer.
> **Scope:** the backend changes made for the mobile API audit of 2026-08-23 — the four that shipped with
> the audit, the signed reservation links that followed it, and the one known hole still **not** fixed.
> **This file is the contract.** It is assembled from the code on the four feature branches, not from a
> plan. Where a design note and the code disagreed, the code won.

## Release state — read this first

**None of this is live yet.** Every change below sits on an **open pull request against `develop`**. The
API reaches production only through a `develop` → `main` release, so until that release merges:

- production and staging still serve the **old** behaviour;
- the endpoints marked "new" answer `404`;
- the swagger document still carries the old schema ids.

| Change | PR | Branch |
|---|---|---|
| `GET /api/Auth/has-password` + `POST /api/Auth/set-password` | **#403** | `feat/auth-has-password-set-password` |
| Readable swagger `schemaId`s | **#404** | `fix/swagger-readable-schema-ids` |
| `PUT /api/Reservations/{id}/mine` + cancel ownership fix | **#405** | `feat/customer-update-own-reservation` |
| Apple identity-token verification + name refresh | **#406** | `fix/apple-login-token-verification` |
| Signed reservation quick-action email links (#402) | **#409** | `fix/signed-reservation-quick-actions` |

[#402](https://github.com/piwas-21/restaurant-app-backend/issues/402) was raised by the same audit and is now
**fixed** — see [§4b](#4b-reservation-quick-action-email-links-are-signed--402).
[#407](https://github.com/piwas-21/restaurant-app-backend/issues/407) is still **open**; see
[§5](#5-known-gaps-that-are-not-fixed).

---

## 0. Conventions that apply to every endpoint here

### 0.1 The response envelope

Almost every response is the `ApiResponse<T>` envelope:

```jsonc
// success
{ "success": true,  "message": "Operation completed successfully", "data": { }          }
// failure
{ "success": false, "message": "<one human sentence>", "errors": ["<reason>", "..."], "errorCode": "SomeCode" }
```

- `errors` is an **array of strings**, one entry per broken rule.
- `errorCode` is **omitted from the JSON when it is absent**. Never assume the key exists.
- `errorCode` values are stable English PascalCase constants. **Branch on `errorCode`, never on `message`
  or on `errors[0]`.** Message text is not part of the contract and is not localised.
- When a controller does not pass its own message, `message` is the literal `"Operation failed"` and the
  real reason is in `errors[0]`. Read `errorCode` first, then `errors`, then `message`.

### 0.2 The second failure shape — `ValidationProblemDetails`

Three layers can refuse a body, and between them they answer in **two different shapes**:

| Layer | Where it runs | Response |
|---|---|---|
| JSON deserialization (`[JsonRequired]` on a DTO member) | **before** model validation | RFC 7807 `ValidationProblemDetails`, HTTP 400 — but `errors` is keyed **`"$"`**, not by field name |
| MVC model validation (`DataAnnotations` on a DTO: `[Required]`, `[EmailAddress]`, `[MaxLength]`, `[Range]`) | during model binding, **before** the handler | RFC 7807 `ValidationProblemDetails` — `application/problem+json`, HTTP 400, with `errors` as an **object keyed by field name** |
| FluentValidation (command validators) | inside the dispatch pipeline | the `ApiResponse` envelope of §0.1, HTTP 400 |

DataAnnotations run **first**, so where both layers state a rule, the `ValidationProblemDetails` shape is
what a client sees. A client must therefore accept both shapes on a 400. Example of the first:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "CustomerEmail": ["The CustomerEmail field is not a valid e-mail address."] }
}
```

Only `PUT /api/Reservations/{id}/mine` in this document carries DataAnnotations. The Apple and password
endpoints validate through FluentValidation only, so they always answer with the envelope.

The same route is also the only one with `[JsonRequired]` members (`reservationDate`, `startTime`,
`endTime` — §1.1). **Omitting one of those three is refused before any field-level validation runs**, and
the refusal names the field inside the message rather than in the key — measured, not guessed:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "$": ["JSON deserialization for type '…UpdateMyReservationDto' was missing required properties including: 'endTime'."]
  }
}
```

A client that reads `errors["endTime"]` will find nothing on this failure. Read `errors["$"]` too, or
simply always send the three fields.

### 0.3 Types on the wire

- **Enums are strings**, both ways in and out: `"Pending"`, `"Customer"`. A number is also accepted on
  input, but send the string.
- `TimeSpan` is `"HH:mm:ss"` — `"19:00:00"`. An ISO date-time does **not** bind to a `TimeSpan`.
- `DateTime` is ISO 8601. Where a field is a **calendar day**, send `YYYY-MM-DDT00:00:00Z`.
- Property names are camelCase.

### 0.4 Environment note on `errors[0]`

In a **Development** build the shared exception middleware puts the stringified exception into
`errors[0]` instead of the reason. Both deployed environments run Production, where `errors[0]` is the
reason. This is one more argument for branching on `errorCode`.

---

## 1. A customer can edit their own reservation — PR #405

### 1.1 `PUT /api/Reservations/{id}/mine`

| | |
|---|---|
| Method + path | `PUT /api/Reservations/{id}/mine` |
| Auth | **Required** — `Authorization: Bearer <accessToken>`. Any authenticated role. |
| Caller identity | read from the token inside the handler. It is never taken from the body, the query string or the route. |
| Module gate | the controller carries `[RequireModule(Reservations)]`. A tenant without that module gets `404` + `errorCode: "ModuleNotEnabled"`. |
| Route constraint | `{id}` must be a GUID (`{id:guid}`). A non-GUID gives a plain routing `404` with no envelope. |
| Content-Type | `application/json` |
| Rate limit | none |

The admin route `PUT /api/Reservations/{id}` is unchanged and stays `[Authorize(Roles = "Admin")]`.
`/mine` means "my own booking" and nothing else — **staff get no override on it**.

#### Request

```json
{
  "customerName": "Grace Hopper",
  "customerEmail": "grace@example.com",
  "customerPhone": "+41794445566",
  "reservationDate": "2030-05-17T00:00:00Z",
  "startTime": "19:00:00",
  "endTime": "21:00:00",
  "numberOfGuests": 2,
  "specialRequests": "Window seat"
}
```

| Field | Type | Required | Rules |
|---|---|---|---|
| `customerName` | string | yes | 1–100 chars (`[Required]`, `[MaxLength(100)]`) |
| `customerEmail` | string | yes | valid email, ≤ 255 (`[Required]`, `[EmailAddress]`, `[MaxLength(255)]`) |
| `customerPhone` | string \| null | no by default | ≤ 20. **May be required per tenant** — the admin form-field config (form key `Reservation`) is applied here exactly as on create |
| `reservationDate` | string (date-time) | yes (`[JsonRequired]`) | a **calendar day at midnight**, e.g. `2030-05-17T00:00:00Z`. See §1.3. Omitting it is a 400, **not** a silent `0001-01-01` |
| `startTime` | string `HH:mm:ss` | yes (`[JsonRequired]`) | wall-clock time on that day |
| `endTime` | string `HH:mm:ss` | yes (`[JsonRequired]`) | must be **after** `startTime`. Omitting it is a 400, **not** a silent `00:00` |
| `numberOfGuests` | int | yes | `[Range(1, 20)]`, and it must still fit the table already assigned |
| `specialRequests` | string \| null | no by default | ≤ 1000. May be required per tenant, same mechanism as `customerPhone` |

**There is no `status`, no `tableId` and no `notes` field.** Sending them is not an error — unknown JSON
members are ignored — but they change nothing. A test pins that
(`Status_and_tableId_in_the_body_change_nothing`).

#### Success — `200`

```json
{
  "success": true,
  "message": "Reservation updated successfully",
  "data": {
    "id": "0f2b0d1e-....",
    "customerId": "cd6c41d9-97e1-4fb4-9bee-ab6a9b460471",
    "customerName": "Grace Hopper",
    "customerEmail": "grace@example.com",
    "customerPhone": "+41794445566",
    "tableId": "aaaa1111-....",
    "tableNumber": "T-12",
    "reservationDate": "2030-05-17T00:00:00Z",
    "startTime": "19:00:00",
    "endTime": "21:00:00",
    "numberOfGuests": 2,
    "status": "Pending",
    "specialRequests": "Window seat",
    "notes": null,
    "createdAt": "2026-08-20T10:11:12.1314Z"
  }
}
```

`data` is the standard `ReservationDto` — the same shape `POST /api/Reservations` returns. `status` is one
of `Pending` | `Confirmed` | `Cancelled` | `Completed` | `NoShow`.

#### Errors

| HTTP | `errorCode` | `message` (Production) | When |
|---|---|---|---|
| `401` | — | — (empty ASP.NET challenge) | no token, expired token, invalid token |
| `404` | `ReservationNotFound` | `Reservation not found` | the id does not exist, **or** it is another user's booking, **or** it is an ownerless walk-in booking (`customerId == null`) |
| `404` | `ModuleNotEnabled` | module refusal | the tenant has no reservations module |
| `400` | `ReservationNotEditable` | `A cancelled reservation can no longer be changed` (the status is interpolated), or `A past reservation can no longer be changed` | the stored status is `Cancelled`, `Completed` or `NoShow`, **or** the booked day is already before the restaurant's today |
| `400` | `ReservationDateInPast` | `Cannot make reservations for past dates` | the **submitted** day is before the restaurant's today |
| `400` | `ReservationTableCapacityExceeded` | `Table T-12 can only accommodate 4 guests` | the new party size is larger than the assigned table's capacity. A retry cannot succeed — the party must shrink or call the restaurant |
| `400` | `ReservationSlotUnavailable` | `Table T-12 is not available for the selected time slot` | the new day/time overlaps another `Pending`/`Confirmed` booking on the same table |
| `400` | *(none)* | the broken FluentValidation rules joined with `; ` | `endTime <= startTime`, a `reservationDate` that is not midnight, an empty required tenant form field |
| `400` | *(no envelope)* | `ValidationProblemDetails` — see §0.2 | a DataAnnotations failure: missing or over-long `customerName`, malformed `customerEmail`, `customerPhone` over 20 chars, `numberOfGuests` outside 1–20 |
| `400` | *(no envelope)* | `ValidationProblemDetails` with `errors` keyed **`"$"`** — see §0.2 | `reservationDate`, `startTime` or `endTime` **absent from the JSON**. They are `[JsonRequired]`, so the body never binds and the handler never runs |
| `500` | — | `An error occurred while processing your request` | unexpected server error |

**`404`, never `403`, for a booking that is not yours.** A distinct `403` would confirm that the id exists
and turn the route into an oracle for enumerating other guests' reservations. The refusal is word for word
the missing-reservation one.

### 1.2 Behaviour a client must handle

1. **A re-shaped `Confirmed` booking drops back to `Pending`.** If `reservationDate`, `startTime`,
   `endTime` or `numberOfGuests` changes on a `Confirmed` reservation, the saved status becomes `Pending`
   — the restaurant approved the old numbers, not the new ones, so it must approve again. A
   **contact-details-only** edit (name, email, phone, special requests) keeps `Confirmed`.
   **Always re-render the status from `data.status`.**
2. **No table is ever re-assigned.** The DTO carries no `tableId` and the endpoint never moves the party.
   A party that no longer fits is refused with `ReservationTableCapacityExceeded`.
3. **The availability rules are exactly the create path's.** Party size against the table's capacity, and
   an overlap check against `Pending`/`Confirmed` bookings **on the same table and day**, excluding the
   booking being edited. Opening hours are **not** checked here — the create path does not check them
   either. Build the picker from `GET /api/Reservations/available-slots`.
   Whether the assigned table is still active is also **not** re-checked.
4. **"Today" is the restaurant's day**, from the tenant clock and its timezone — not UTC, not the device.
   The comparison is at day granularity.
5. **Ownership is by `customerId`.** A booking created while signed out has no owner and stays
   uneditable through this route even when the email matches.
6. **Concurrency: last write wins.** There is no ETag and no row version on a reservation.
7. **No email is sent on this path — to anybody.** See [§5.1](#51-407--a-guest-edit-sends-no-mail).

### 1.3 `reservationDate` is a calendar day, and this route is strict

A reservation is a **calendar day** plus two wall-clock times. It is never an instant and is never
converted through a timezone.

This endpoint **rejects any `reservationDate` whose time-of-day is not `00:00:00`** with a `400` and the
message *"Reservation date must be a calendar day at midnight UTC, e.g. 2030-05-17T00:00:00Z"*.

Why strict: a client that sends its own local midnight with an offset (`2030-05-17T00:00:00+02:00`) parses
to `2030-05-16T22:00:00Z` on the server, and silently moving a real booking one day back is far worse than
a loud `400`.

- Send `YYYY-MM-DDT00:00:00Z`. Build the string by concatenation. Do **not** round-trip a local `Date`
  through `toISOString()`.
- `2030-05-18T00:00:00` (no `Z`) is accepted and books that very day — the server stamps it UTC.
- `startTime` / `endTime` are `"HH:mm:ss"`.

### 1.4 Same PR: `POST /api/Reservations/{id}/cancel` now enforces ownership

**Before this PR it enforced nothing.** The route was `[Authorize]`, the handler only checked that the
reservation existed, and the controller carried a `// TODO: enforce non-admins can only cancel their own
reservations`. **Any signed-in customer could cancel any reservation whose id they had.**

New behaviour — the response shape (`ApiResponse<bool>`) is unchanged:

| Caller | Reservation | Result |
|---|---|---|
| staff (Admin / Cashier / KitchenStaff / Server) | any | cancels — unchanged |
| customer | their own (`customerId` equals the caller) | cancels — unchanged |
| customer | someone else's, or an ownerless walk-in | **refused**, see below |
| anonymous | – | `401` — unchanged |
| the restaurant's `quick-reject` email link | any | cancels — `[AllowAnonymous]`, the one documented ownership opt-out. Since #402 the link must also carry a valid `?token=` (§4b) |

The refusal is **byte-identical to a genuinely missing id**:

```json
{ "success": false, "message": "Operation failed", "errors": ["Reservation not found"] }
```

**Note the status difference between the two routes.** Cancel keeps its long-standing `400` +
`"Reservation not found"` for a missing or foreign reservation — changing it to `404` would break the
existing web client — while the new `/mine` route answers `404` + `errorCode: "ReservationNotFound"`.
Neither reveals whether the id exists.

A successful cancel also sends the guest the "reservation rejected" mail, as before.

### 1.5 Client checklist

- Point the edit screen at `PUT /api/Reservations/{id}/mine`, sending the eight fields of §1.1.
- Send the day as UTC midnight and the times as `"HH:mm:ss"`.
- Hide the edit action for a past or terminal booking; close the form on `ReservationNotFound` or
  `ReservationNotEditable`.
- Offer only slots where the booking's **own table** is free — the party is never re-seated, so "any
  table that fits" is the wrong question.
- Re-render the status from `data.status` and tell the user in words when the booking went back to
  `Pending`, because nobody is mailed about it.
- Cap the guest picker at **20**. The DTO refuses more.

### 1.6 Test evidence

`RestaurantSystem.IntegrationTests/Features/Reservations/UpdateMyReservationTests.cs` and
`CancelReservationOwnershipTests.cs`.

---

## 2. `has-password` / `set-password` — PR #403

### 2.1 Why they exist

An account created with Google or Apple sign-in has **no password hash**.
`POST /api/Auth/change-password` verifies `currentPassword`, so it can never succeed for such an account.
Before this change the only way for a social-login user to get a password was to run "forgot password"
against their own address.

`GET /api/Auth/has-password` tells the client which form to show. `POST /api/Auth/set-password` is the
flow for the passwordless case.

### 2.2 `GET /api/Auth/has-password`

| | |
|---|---|
| Method + path | `GET /api/Auth/has-password` |
| Auth | **Required** — `Authorization: Bearer <accessToken>`. The account is resolved from the token only. |
| Request | no body, no query parameters, no user identifier anywhere |
| Rate limit | none (same as `change-password`) |
| Side effects | none — safe to call on every screen open |

Success — `200`:

```json
{ "success": true, "message": "Operation completed successfully", "data": true }
```

`data` is `true` when the account has a usable password and `false` for a social-login-only account. It is
read with Identity's own `HasPasswordAsync`, i.e. the real password-hash column.

| Status | Body | When |
|---|---|---|
| `401` | empty (ASP.NET Core challenge) | no token, expired token, invalid token |
| `401` | `{"success":false,"message":"User not authenticated","errors":["User not authenticated"]}` | the token is valid but its account no longer exists (deleted or soft-deleted) |

### 2.3 `POST /api/Auth/set-password`

| | |
|---|---|
| Method + path | `POST /api/Auth/set-password` |
| Auth | **Required** — `Authorization: Bearer <accessToken>`. The account is resolved from the token only. |
| Content-Type | `application/json` |
| Rate limit | none (same as `change-password`) |

Request:

```json
{ "newPassword": "<the new password>", "confirmPassword": "<the same value>" }
```

Both fields are required and must be identical, and the value must satisfy the policy in §2.4. **Any other property in the body is ignored** — there is no user id, no email,
no current password. A pinned test posts a `userId` and an `email` naming another account and proves the
caller's own account is the one changed.

Success — `200`:

```json
{ "success": true, "message": "Operation completed successfully", "data": "Password set successfully" }
```

Errors:

| Status | `errorCode` | `message` | When |
|---|---|---|---|
| `400` | `PasswordAlreadySet` | `This account already has a password. Use change-password to change it.` | the account already has a password |
| `400` | *(absent)* | the broken rules joined with `; ` | validation failed: missing field, weak password, `confirmPassword` mismatch |
| `400` | *(absent)* | ASP.NET Identity's own rejection text | the policy passed but Identity refused the password (repeated characters, common password) |
| `401` | *(absent)* | — | no token, invalid token, or the token's account no longer exists |

The already-set refusal in full:

```json
{
  "success": false,
  "message": "This account already has a password. Use change-password to change it.",
  "errors": ["This account already has a password. Use change-password to change it."],
  "errorCode": "PasswordAlreadySet"
}
```

A validation failure in full:

```json
{
  "success": false,
  "message": "Password must be at least 8 characters long; Password must contain at least one uppercase letter",
  "errors": [
    "Password must be at least 8 characters long",
    "Password must contain at least one uppercase letter"
  ]
}
```

**Client rule:** branch on `errorCode === "PasswordAlreadySet"`, never on the English sentence. It means
"your account already has a password" — switch the screen to the change-password flow and re-read
`has-password`.

### 2.4 Password policy

The same shared rules as register, reset-password and change-password
(`RestaurantSystem.Api/Common/Validation/PasswordRules.cs`), with the same message strings:

- at least 8 characters — `Password must be at least 8 characters long`
- one uppercase letter — `Password must contain at least one uppercase letter`
- one lowercase letter — `Password must contain at least one lowercase letter`
- one digit — `Password must contain at least one digit`
- one special character — `Password must contain at least one special character`

Plus, when the field is missing: `New password is required`, `Password confirmation is required`,
`Passwords do not match`.

There is **no maximum length**. On top of these, Identity's own strong-password validator runs when the
password reaches the user manager (repeated characters, common passwords); its rejection also comes back
as a `400` with the reasons in `errors[]`.

### 2.5 Behaviour a client must handle

1. **Refusing an account that already has a password is the security property, not an inconvenience.**
   Without it a stolen access token could silently replace the password of a normal email+password
   account — exactly what `change-password`'s current-password check prevents.
2. **Refresh tokens are invalidated on success**, exactly as `change-password` does it. **Every other
   session must re-authenticate.** Treat your own stored refresh token as dead after a successful call.
3. **Access tokens are NOT revoked.** The JWT pipeline does not validate the Identity security stamp, so
   an already-issued access token stays valid until it expires. Same as `change-password`.
4. **A "password changed" mail is sent** to the account holder, in the account's language. A send failure
   is logged and swallowed — it never fails the request.
5. **No rate-limit policy is applied**, matching `change-password`. There is nothing to brute-force: the
   call needs a valid bearer token, guesses no secret, and can only ever succeed **once** per account.
6. **On a `400`, nothing is written.** The account keeps whatever password state it had.

### 2.6 Suggested client flow

1. On opening the change-password screen, call `GET /api/Auth/has-password`.
2. `data: true` → show current + new + confirm, submit to `POST /api/Auth/change-password`.
3. `data: false` → hide the current-password field, submit to `POST /api/Auth/set-password`.
4. On `errorCode: "PasswordAlreadySet"` → re-read `has-password` and switch to the change-password form.
5. After a successful `set-password`, discard the stored refresh token.

### 2.7 Test evidence

`RestaurantSystem.IntegrationTests/Features/Auth/SetPasswordTests.cs`.

---

## 3. Sign in with Apple — PR #406

### 3.1 What changed

Before: the handler called `JwtSecurityTokenHandler.ReadToken`, which **decodes** a JWT and verifies
nothing, and its audience check was commented out. Anyone could post an **unsigned** JWT carrying any
`email` claim and receive that account's access and refresh token — including accounts opened with a
password.

Now the identity token is verified before any claim in it is believed:

- **RS256 signature** against Apple's published keys, fetched over a typed HTTP client with a timeout and
  **cached process-wide** (default 60 minutes). A token naming an unknown key id (Apple rotated its keys)
  forces **one** re-fetch, floored at 5 minutes so crafted tokens cannot make the server hammer Apple.
- **Signed tokens only**, algorithm pinned to `RS256` — `alg: none` is refused.
- `iss` must be `https://appleid.apple.com`.
- `aud` must be one of the **configured client ids** (a list: iOS bundle id, web service id, …).
- `exp` / `nbf` enforced, with 60 s clock skew.
- The token must carry a `sub`.
- **Missing configuration fails CLOSED.** With no client id configured every call is refused and the
  server logs an error. The old "skip the check when config is missing" behaviour is gone.

### 3.2 The endpoint

| | |
|---|---|
| Method + path | `POST /api/Auth/apple-login` |
| Auth | **None** (`[AllowAnonymous]`) |
| Content-Type | `application/json` |
| Optional header | `X-Session-Id: <guest session id>` — merges the anonymous basket into the account, unchanged |
| Rate limit | policy `auth` — **5 requests / 15 min per IP** in production; `429` past that |

#### Request

```json
{
  "idToken": "<the identityToken Apple returned to the app>",
  "firstName": "Ada",
  "lastName": "Lovelace"
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `idToken` | string | **yes** | Apple's `identityToken`. Empty ⇒ `400`. |
| `firstName` | string \| null | no | Max 100 chars. Apple only sends a name on an Apple ID's **first** authorisation. |
| `lastName` | string \| null | no | Max 100 chars. |

#### Success — `200`

```json
{
  "success": true,
  "message": "Operation completed successfully",
  "data": {
    "userId": "1f1c1d64-....",
    "firstName": "Ada",
    "lastName": "Lovelace",
    "email": "user@example.com",
    "role": "Customer",
    "accessToken": "<jwt>",
    "refreshToken": "<opaque refresh token>",
    "expiration": "2026-08-24T10:11:12Z"
  }
}
```

Unchanged shape — the same `AuthResponse` as `login` and `google-login`.

#### Errors

| Status | `errorCode` | `message` | `errors[0]` | When |
|---|---|---|---|---|
| `400` | `InvalidAppleToken` | `Invalid token` | `The provided Apple token is invalid.` | bad signature, unsigned (`alg:none`), wrong `iss`, wrong `aud`, expired, malformed, empty, or no `sub`. **One code for every cause on purpose** — which check failed is a server-log detail, not something to tell an anonymous caller |
| `400` | *(none)* | the broken rules joined with `; `, e.g. `Identity token is required` | same text | `idToken` missing or empty; a name longer than 100 chars |
| `400` | *(none)* | `Email missing` | `Could not retrieve email from Apple token.` | the verified token carries no `email` claim (the app requested no email scope). Unchanged behaviour |
| `400` | *(none)* | `Registration failed` | Identity's own error text | ASP.NET Identity refused to create the account |
| `503` | `AppleLoginUnavailable` | `Apple login unavailable` | `Apple sign-in is temporarily unavailable.` | **our** fault, not the token's: Apple sign-in is not configured on this deployment, or Apple's key endpoint could not be reached and no cached key set is held. Retryable |
| `429` | *(none)* | rate-limit response | — | past 5 attempts / 15 min from one IP |
| `500` | *(none)* | `An error occurred while processing your request` | same text | unexpected server error. No exception detail is returned any more — it used to leak the exception message |

**Why not `401`.** A mobile client typically refreshes its session on **any** `401` and logs the user out
when that refresh fails, so answering a refused Apple token with `401` would turn a bad login into a
spurious logout. A rejected token is a `400`; a server-side condition is a `503`.

**Client change needed:** none on the happy path, but two are recommended. Failures that used to arrive as
`200` + `"success": false` now arrive as a real non-2xx status. Show a "try again later" message when
`errorCode === "AppleLoginUnavailable"`, and a "sign-in failed" message for `InvalidAppleToken`.

### 3.3 Name behaviour

| Case | Stored name after login |
|---|---|
| New account, request carries a name | that name, trimmed |
| New account, no name (every login after the first) | placeholder **`Apple` `User`** |
| Existing account, request carries a non-empty name | **the incoming name wins**; it is written to the account and the response shows it |
| Existing account, no name in the request | the stored name is kept — a silent login never wipes a name |

A name counts as "carried" when it is non-empty after trimming, and each half is decided separately. The
write is best-effort: if Identity refuses the update the sign-in still succeeds and the failure is logged.

**Placeholders are deliberate.** The profile-update validator requires a non-empty first **and** last
name, so an account with an empty name could not save any other profile field (phone, preferred language)
without inventing a name first. Order confirmations and admin alerts also print the account name.

Keep sending the name — that is what makes the refresh work. A separate `PUT /api/User/profile` "repair"
call after login is now redundant, but harmless: it writes the value the server already stored.

### 3.4 `nonce`

Verified only as far as it can be. The command does not transport the raw nonce, so there is nothing to
compare the `nonce` claim against; the claim is parsed and carried internally. Replay is bounded by `exp`
(Apple issues short-lived identity tokens) and by TLS. If the client starts sending the raw nonce it
generated, the field can be added to the command and compared — ask for it, it is a small change.

### 3.5 Ops requirement — it changes what the API answers

Configuration section `Authentication:Apple`. The shipped `appsettings.json` leaves `ClientIds` **empty**,
so a deployment that sets nothing refuses **every** Apple login with `503 AppleLoginUnavailable`. This is
by design.

| Key | Environment variable | Default | Meaning |
|---|---|---|---|
| `ClientIds` | `Authentication__Apple__ClientIds__0`, `…__1`, … | *(empty ⇒ refuse everything)* | Accepted `aud` values. Set index `0` to the **iOS bundle identifier the app declares**. Add the web Service ID as `…__1` when Apple sign-in ships on the website |
| `ClientId` | `Authentication__Apple__ClientId` | — | legacy single-value form, still honoured and merged into the list |
| `Issuer` | `Authentication__Apple__Issuer` | `https://appleid.apple.com` | protocol constant; override only for tests |
| `JwksUri` | `Authentication__Apple__JwksUri` | `https://appleid.apple.com/auth/keys` | protocol constant |
| `JwksCacheMinutes` | `Authentication__Apple__JwksCacheMinutes` | `60` | key-cache lifetime |
| `JwksTimeoutSeconds` | `Authentication__Apple__JwksTimeoutSeconds` | `10` | HTTP timeout for the key fetch (clamped 1–60) |
| `ClockSkewSeconds` | `Authentication__Apple__ClockSkewSeconds` | `60` | `exp`/`nbf` tolerance |

Two consequences a client author should know:

- A deployment that has not set a client id answers `503` to every Apple login. That is a configuration
  problem, not a user problem — say "try again later", never "your Apple account was rejected".
- **If the iOS bundle id ever changes, the deployment must be updated in the same release**, or Apple
  login fails with `400 InvalidAppleToken` on an `aud` mismatch.

Outbound network access to `https://appleid.apple.com/auth/keys` must be open.

### 3.6 Test evidence

`RestaurantSystem.IntegrationTests/Features/Auth/`: `AppleIdentityTokenVerifierTests`,
`AppleSigningKeyProviderTests`, `AppleLoginEndpointTests` — including a test proving that an unsigned
token can no longer take over a password account.

---

## 4. Readable swagger `schemaId`s — PR #404

### 4.1 What changed, in one line

The schema ids in the published OpenAPI document are now **short and namespace-free**, so a generated
client can finally be produced from them.

| Before | After |
|---|---|
| `RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand.CreateOrderCommand` | `CreateOrderCommand` |
| ``RestaurantSystem.Api.Common.Models.ApiResponse`1[[RestaurantSystem.Api.Features.Orders.Dtos.OrderDto, RestaurantSystem.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]`` | `ApiResponseOfOrderDto` |

### 4.2 The document

| | |
|---|---|
| Method + path | `GET /api/swagger/v1/swagger.json` |
| Auth | **none** — public in every environment (unchanged) |
| Request body | none |
| Success | `200`, `application/json`, an OpenAPI 3 document |
| Errors | none specific to this change. `404` means the path is wrong; `500` means generation threw — see §4.4 |
| Swagger UI | `GET /api/swagger` (unchanged) |

**Only `components.schemas` keys change.** No path, no `operationId`, no HTTP method, no request field, no
response field and no status code moves. `$ref` targets follow the keys because they are generated from
them. The wire format of every response is byte-for-byte what it was.

### 4.3 Naming rules — the whole contract

Implemented by `RestaurantSystem.Api/Common/Swagger/SwaggerSchemaIdGenerator.cs`. The id is a pure
function of the CLR type, so it is **stable across runs, processes and machines**.

| Input | Rule | Example |
|---|---|---|
| Ordinary type | short type name | `OrderDto` |
| Generic type | `<Name>Of<Arg>` | `ApiResponse<OrderDto>` → `ApiResponseOfOrderDto` |
| Nested generic | recursive, left to right | `ApiResponse<PagedResult<OrderDto>>` → `ApiResponseOfPagedResultOfOrderDto` |
| Generic over a collection | the collection is named too | `ApiResponse<List<OrderDto>>` → `ApiResponseOfListOfOrderDto` |
| Generic over a primitive | the CLR name | `ApiResponse<bool>` → `ApiResponseOfBoolean`; `ApiResponse<string>` → `ApiResponseOfString` |
| Multi-argument generic | arguments joined with `And` | `Dictionary<string,int>` → `DictionaryOfStringAndInt32` |
| Nullable value type | collapses onto the underlying type | `int?` → `Int32` |
| Array | `Array` suffix | `OrderDto[]` → `OrderDtoArray` |
| Nested CLR type | the declaring type is prefixed | `Outer.Inner` → `OuterInner` |

Every id matches **`^[A-Za-z][A-Za-z0-9_]*$`** — a valid identifier in TypeScript, Dart, Kotlin and
Swift. No namespace, no assembly name, no `Version=`, no backtick, bracket, plus, comma, equals or space.
An id that would not start with a letter is prefixed with `Type`.

### 4.4 Collisions and the failure mode

There is **no hash suffix and no silent disambiguation**. Two different CLR types that reduce to the same
id are a **build failure**, not a renamed schema: swagger generation throws and the whole document fails,
so `GET /api/swagger/v1/swagger.json` returns `500` and the UI is blank. A CI test reproduces that and
names the clashing pair.

**Current state: zero collisions** — all 282 schemas in the v1 document have distinct short names. The one
genuine same-short-name pair in the assembly was dead code (an unreferenced
`Features/Products/Dtos/UpdateProductVariationDto`) and was deleted in this PR.

If a clash appears later the fix is, in order: rename or merge the duplicated DTO; only if that is
impossible, add an explicit, documented, feature-prefixed name (`OrdersItemDto`) — never a hash.

### 4.5 Why `type => type.Name` alone does not work

Measured against this API it generates nothing: 78 closed `ApiResponse<T>` and 7 closed `PagedResult<T>`
all reduce to ``ApiResponse`1`` / ``PagedResult`1``, and Swashbuckle refuses a schema id already taken by
another type. `type.Name` also leaves the backtick in the id, which is not a valid identifier. Hence the
`…Of…` composite.

### 4.6 Two documents exist — use the right one

The API also exposes a **second** OpenAPI document at `/openapi/v1.json`, produced by
`Microsoft.AspNetCore.OpenApi` rather than by Swashbuckle. It has its own naming and is **unchanged** by
this PR. **Generate from `/api/swagger/v1/swagger.json`.**

### 4.7 Blast radius

Nothing else in the platform consumes these names: the web frontend and the printer app both use
hand-written clients, and no contract test or DTO-drift check reads schema ids. The only consumer is the
mobile client.

**Plan the regeneration as one mechanical refactor**, after the release. Expect a large rename across
generated types and hooks, plus field drift accumulated since the last generation.

### 4.8 Test evidence

`RestaurantSystem.IntegrationTests/Common/SwaggerSchemaIdGeneratorTests.cs` (each naming rule) and
`RestaurantSystem.IntegrationTests/Infrastructure/SwaggerDocumentTests.cs` (the real v1 document:
generation succeeds, every key is a plain identifier, no key contains a namespace or a version, and no
two distinct CLR types claim one key).

---

## 4b. Reservation quick-action email links are signed — #402

### 4b.1 What changed

`GET /api/reservations/{id}/quick-approve` and `GET /api/reservations/{id}/quick-reject` used to act on
a bare reservation id with no authentication of any kind. The id is **not** a secret:
`POST /api/Reservations` is `[AllowAnonymous]` and returns the whole `ReservationDto`, `id` included, to
whoever made the booking. So the guest could **self-approve their own pending booking**, receive the
"approved" mail, and hold the table the restaurant never agreed to.

Both routes now require a signed token:

```
GET /api/reservations/{id}/quick-approve?token={token}
GET /api/reservations/{id}/quick-reject?token={token}
```

The token is `{unixExpiry}.{base64url(HMAC-SHA256)}` over the reservation id, the action, and the
booking's **current status**, keyed server-side. It is minted by the backend and written into the
restaurant's alert mail; nothing else can produce one.

### 4b.2 What a client must know

**No client should call these routes.** They are mail links that render an HTML page, not an API. They
are listed here because the mobile team's audit raised the gap (`BACKEND-NOTES.md` §5) and because the
behaviour a person sees has changed:

- A request with a missing, wrong, or expired token renders a plain HTML page — "This link can no
  longer be used" — with a link to the reservations dashboard. **HTTP 200, never a stack trace.**
- The same page, byte for byte, is returned for a reservation id that does not exist. The route is not
  an existence oracle.
- A link is **one-shot**. The status is part of what is signed, so approving a booking also retires the
  reject button in the same mail. Changing a decision afterwards is a dashboard action, where there is
  an authenticated caller to hold responsible.
- Links expire (`ReservationQuickActions:LinkLifetimeDays`, default 7 days).

### 4b.3 Migration — links already in the inbox

Alert mails sent before this release carry no token. A token-less link is still honoured while its own
reservation is younger than `ReservationQuickActions:LegacyLinkGraceDays` (default 14 days, measured
from that reservation's `CreatedAt`), and every such use is logged at warning level. Two config values
close the window early; see the backend README §"Reservation quick-action links".

### 4b.4 Test evidence

`RestaurantSystem.IntegrationTests/Features/Reservations/ReservationQuickActionLinksTests.cs` (the
signature itself: expiry, tamper, wrong action, wrong booking, replay, key separation, grace window),
`…/ReservationQuickActionLinkAuthorizationTests.cs` (the two routes end to end, including the
"unknown id answers exactly what a bad token answers" assertion),
`…/ReservationQuickActionLegacyLinkTests.cs` (the migration window) and
`RestaurantSystem.IntegrationTests/Common/Templates/ReservationAdminNotificationLinkTests.cs` (no
template emits a token-less link).

---

## 5. Known gaps that are NOT fixed

These are open. Do not build a client that assumes they are closed.

### 5.1 #407 — a guest edit sends no mail

`PUT /api/Reservations/{id}/mine` sends **no email, to nobody**. There is no "reservation changed"
template for the guest and none for the restaurant, and inventing one was out of scope.

The consequence matters for the UX: when a guest edits a **`Confirmed`** booking it drops back to
`Pending` (§1.2.1) and **the restaurant is not told**. Staff see it only in the reservations list. A
client must therefore say in words that the booking is waiting for approval again — the guest would
otherwise believe the change was accepted.

Tracked as [issue #407](https://github.com/piwas-21/restaurant-app-backend/issues/407).

---

## 6. Corrections to earlier notes

If you were handed an earlier draft of these contracts, three statements in it were wrong. The code
below is what ships.

1. **`POST /api/Auth/apple-login`, the `500` row.** An earlier note said the message is `Login failed`.
   That was the old handler's `catch`, which PR #406 **deleted**. An unexpected exception now reaches the
   shared exception middleware, so a Production deployment answers `500` with
   `message: "An error occurred while processing your request"`.
2. **`POST /api/Auth/apple-login`, the validation `400` row.** An earlier note said `message` is
   `Operation failed`. It is not: the validation pipeline throws with the broken rules joined by `; `, and
   the middleware puts that string in `message`. So `message` reads `Identity token is required`.
3. **`PUT /api/Reservations/{id}/mine`, the field-validation row.** An earlier note folded DataAnnotations
   failures into the `ApiResponse` envelope. They are not in it: the controller is `[ApiController]` and
   the API sets no custom invalid-model-state factory, so a DataAnnotations failure is answered by MVC
   model validation as RFC 7807 `ValidationProblemDetails` **before** the handler runs. See §0.2 — a
   client must accept both 400 shapes on that route.
