# Backend Sprint Plan

> Extracted from the RUMI system-wide sprint plan. Backend-specific tasks only.
> Updated 2026-04-03 with security audit and test coverage findings.

---

## Sprint 1: Critical Fixes & Foundations -- COMPLETE

| # | Task | Status |
|---|------|--------|
| 1.1 | Remove hardcoded admin email from `OrdersController.cs` -> `IOptions<EmailSettings>` | DONE |
| 1.2 | Remove hardcoded admin email from `CreateReservationCommand.cs` | DONE |
| 1.3 | Fix CORS `AllowAnyOrigin` -> environment-aware policy | DONE |
| 1.4 | Remove duplicate `ICurrentUserService` registration | DONE |
| 1.5 | Remove unused MediatR NuGet package | DONE |
| 1.6 | Clean `using MediatR;` from 7 files | DONE |
| 1.7 | Remove redundant localhost URL fallbacks in `EmailService.cs` | DONE |
| 1.11 | Add API key auth (`X-Api-Key`) to printer-feed endpoint | DONE |
| 1.12 | Replace `InvalidOperationException` with custom exceptions (34 replacements, 9 files) | DONE |
| 1.13 | Rename `UpdateStaffCommandCommandValidator` -> `UpdateStaffCommandValidator` | DONE |
| 1.14 | Add `GetAuditIdentifier()` to `ICurrentUserService`, replace 67 occurrences in 33 files | DONE |
| 1.15 | Parameterize admin email in 9 template files + 7 EmailService methods | DONE |
| 1.16 | Add `CorsSettings` + `PrinterSettings` to `appsettings.Development.json` | DONE |

---

## Sprint 1.5: CRITICAL Security Fixes (IMMEDIATE)

> From security audit: 5 CRITICAL + 8 HIGH severity findings. See `docs/SECURITY-AUDIT.md` for full details.

### CRITICAL Priority (block release)

| # | Task | Severity | File(s) |
|---|------|----------|---------|
| S1 | **Fix password reset** - add `Token` parameter to `ResetPasswordCommand`, validate against emailed token | CRITICAL | `Features/Auth/Commands/ResetPasswordCommand/` |
| S2 | **Fix S3 file permissions** - change `S3CannedACL.PublicRead` to `Private`, implement pre-signed URL generation for read access | CRITICAL | `Common/Services/S3FileStorageService.cs` |
| S3 | **Add security headers middleware** - HSTS, X-Frame-Options, X-Content-Type-Options, CSP, Referrer-Policy | CRITICAL | `Program.cs` (new middleware) |
| S4 | **Add rate limiting** - `Microsoft.AspNetCore.RateLimiting` on login (5/15min), forgot-password (3/hr), register (10/hr) | CRITICAL | `Program.cs`, auth controllers |
| S5 | **Validate X-Session-Id** - verify UUID format, reject malformed session IDs | CRITICAL | `Common/Middleware/SessionMiddleware.cs` (currently empty) |

### HIGH Priority (fix before production)

| # | Task | Severity | File(s) |
|---|------|----------|---------|
| S6 | **Protect SSE endpoints** - add `[Authorize]` with role checks to kitchen, stock, service SSE streams | HIGH | `Features/Orders/EventsController.cs` |
| S7 | **Protect test-broadcast** - add `[RequireAdmin]` to `POST events/test-broadcast` | HIGH | `Features/Orders/EventsController.cs` |
| S8 | **Environment-specific RequireHttpsMetadata** - `!builder.Environment.IsDevelopment()` | HIGH | `Program.cs` (line 170) |
| S9 | **Remove token logging** - delete `LogInformation("Password reset token...")` | HIGH | `Features/Auth/Commands/ForgotPasswordCommand/` |
| S10 | ~~**Add missing validators** - create FluentValidation validators for all commands currently missing them (~15 commands)~~ ✅ Done (MR !22) | HIGH | Multiple `Validator.cs` files to create |
| S11 | ~~**Add SSE connection limits** - max 10 connections per IP in `SseClientManager`~~ ✅ Done (MR !22) | HIGH | `Features/Orders/Services/OrderEventService.cs` |
| S12 | ~~**Hash refresh tokens** - store hash in DB instead of plaintext~~ ✅ Done (MR !22) | HIGH | `Features/Auth/`, `Domain/Entities/ApplicationUser.cs` |
| S13 | ~~**Invalidate tokens on password change** - clear refresh token when password changes~~ ✅ Done (MR !22) | HIGH | `Features/Auth/Commands/ChangePasswordCommand/` |

### Security Tests (alongside fixes)

| # | Task | Tests |
|---|------|-------|
| ST1 | `PasswordResetFlowTests.cs` - token required, expired rejected, wrong email rejected | 5 tests |
| ST2 | `AuthorizationTests.cs` - every endpoint tested for correct auth | 15 tests |
| ST3 | `RateLimitTests.cs` - login/register/forgot-password blocked after threshold | 4 tests |
| ST4 | `SessionSecurityTests.cs` - invalid UUID rejected, no cross-session access | 3 tests |

**Acceptance:**
- Password reset requires valid token from email
- S3 URLs return 403 without pre-signed signature
- `curl -I` shows all security headers
- 6th rapid login attempt returns 429
- SSE endpoints return 401 without auth token
- `dotnet test` passes all 27 security tests

---

## Sprint 2: God Class Decomposition -- Orders

**Goal:** Split `OrdersController.cs` (735 LOC) and `CreateOrderCommandHandler.cs` (730 LOC).

| # | Task | New Files | Target LOC |
|---|------|-----------|------------|
| 2.1 | Create `HtmlResponseBuilder` service | `Common/Services/HtmlResponseBuilder.cs`, `IHtmlResponseBuilder.cs` | ~80 |
| 2.2 | Create `PrinterFeedQuery` + handler | `Features/Orders/Queries/PrinterFeedQuery/` | ~60 |
| 2.3 | Extract `PrinterFeedController.cs` | `Features/Orders/PrinterFeedController.cs` | ~60 |
| 2.4 | Extract `OrderEmailController.cs` | `Features/Orders/OrderEmailController.cs` | ~80 |
| 2.5 | Extract `OrderQuickActionsController.cs` | `Features/Orders/OrderQuickActionsController.cs` | ~100 |
| 2.6 | Slim down `OrdersController.cs` to core CRUD | (modify existing) | ~120 |
| 2.7 | Create `OrderAddressFactory.cs` | `Features/Orders/Services/OrderAddressFactory.cs` | ~80 |
| 2.8 | Create `OrderItemFactory.cs` | `Features/Orders/Services/OrderItemFactory.cs` | ~80 |
| 2.9 | Create `OrderPricingService.cs` | `Features/Orders/Services/OrderPricingService.cs` | ~120 |
| 2.10 | Create `OrderNotificationService.cs` | `Features/Orders/Services/OrderNotificationService.cs` | ~60 |
| 2.11 | Slim down `CreateOrderCommandHandler.cs` to orchestrator | (modify existing) | ~150 |
| 2.12 | Register all new services in `Program.cs` | (modify existing) | -- |
| 2.13 | Convert `GlobalIngredientsController` to CQRS pattern | Refactor from direct DbContext to Commands/Queries | ~120 |
| 2.14 | Extract HTML templates from `ReservationsController` to `HtmlResponseBuilder` | Modify existing | ~150 |
| 2.15 | Extract `GroupDiscountController` discount-update logic to CQRS command handler (direct EF + SaveChangesAsync in action method — pre-existing thin-dispatcher violation flagged in MR !20 review) | `Features/Groups/Commands/UpdateGroupDiscountCommand/` | ~80 |

### Sprint 2 Tests

| # | Test File | Tests |
|---|-----------|-------|
| T2.1 | `AuthControllerTests.cs` | 12 (register, login, refresh, password flows) |
| T2.2 | `OrdersControllerTests.cs` | 15 (CRUD, status, payments, cancel) |
| T2.3 | `OrderStatusTransitionTests.cs` | 8 (valid/invalid transitions) |
| T2.4 | `PaymentFlowTests.cs` | 8 (cash, card, partial, refund) |
| T2.5 | `OrderPricingServiceTests.cs` (unit) | 8 (subtotal, tax, discount, rounding) |
| T2.6 | `OrderAddressFactoryTests.cs` (unit) | 5 (3 address paths, nulls) |

**Acceptance:** `dotnet build` passes, create order works, quick-confirm/cancel from email works, printer-feed returns orders, 56 new tests pass.

---

## Sprint 3: God Class Decomposition -- Basket & SSE

**Goal:** Split `BasketService.cs` (995 LOC) and `OrderEventService.cs` (734 LOC).

| # | Task | New Files | Target LOC |
|---|------|-----------|------------|
| 3.1 | Create `BasketPricingService.cs` | `Features/Basket/Services/BasketPricingService.cs` + interface | ~200 |
| 3.2 | Create `BasketItemFactory.cs` | `Features/Basket/Services/BasketItemFactory.cs` + interface | ~150 |
| 3.3 | Create `BasketMappingService.cs` | `Features/Basket/Services/BasketMappingService.cs` + interface | ~150 |
| 3.4 | Create `BasketCacheService.cs` | `Features/Basket/Services/BasketCacheService.cs` + interface | ~50 |
| 3.5 | Slim down `BasketService.cs` to orchestrator | (modify existing) | ~120 |
| 3.6 | Unify pricing: shared `IPricingCalculator` | `Common/Services/PricingCalculator.cs` + interface | ~100 |
| 3.7 | Extract `Models/SseModels.cs` | `Features/Orders/Models/SseModels.cs` | ~60 |
| 3.8 | Create `SseClientManager.cs` | `Features/Orders/Services/SseClientManager.cs` + interface | ~150 |
| 3.9 | Create `SseBroadcastService.cs` (merge duplicated methods) | `Features/Orders/Services/SseBroadcastService.cs` + interface | ~120 |
| 3.10 | Create `SseEventReplayService.cs` | `Features/Orders/Services/SseEventReplayService.cs` + interface | ~80 |
| 3.11 | Slim down `OrderEventService.cs` to facade | (modify existing) | ~100 |
| 3.12 | Create `AddressMapper.cs` | `Common/Services/AddressMapper.cs` | ~60 |
| 3.13 | Register all new services in `Program.cs` | (modify existing) | -- |

### Sprint 3 Tests

| # | Test File | Tests |
|---|-----------|-------|
| T3.1 | `BasketControllerTests.cs` | 12 (add, remove, update, pricing, merge, validation) |
| T3.2 | `BasketPricingServiceTests.cs` (unit) | 10 (subtotal, tax, discount, fidelity, rounding) |
| T3.3 | `ProductsControllerTests.cs` (expand) | 10 (CRUD, variations, images, search) |
| T3.4 | `CategoriesControllerTests.cs` | 6 (CRUD, reorder, images) |
| T3.5 | `BasketItemFactoryTests.cs` (unit) | 6 (create, dedup, matching) |
| T3.6 | `PricingCalculatorTests.cs` (unit) | 8 (shared pricing, combinations) |

**Acceptance:** `dotnet build` passes, basket fully works, SSE real-time updates work, pricing unified, 52 new tests pass.

---

## Sprint 4-5 (Backend Tests): Remaining Feature Coverage

| # | Test File | Tests | Feature |
|---|-----------|-------|---------|
| T4.1 | `ReservationsControllerTests.cs` | 10 | Create, approve, reject, cancel, time conflicts, capacity |
| T4.2 | `SettingsTests.cs` | 8 | Tax config, order types, working hours |
| T4.3 | `UserManagementTests.cs` | 8 | Profile, password change, account deletion |
| T4.4 | `GroupsTests.cs` | 6 | Group CRUD, memberships, QR codes |
| T4.5 | `MenusControllerTests.cs` | 8 | Bundle CRUD, sections, availability |

**Target after Sprint 5:** 80%+ code coverage, ~188 total tests.

---

## Sprint 7 (Backend portion): Final Cleanup

| # | Task |
|---|------|
| 7.20a | Audit `null!` usages (55 instances in DTOs) -- replace with `required` or `= string.Empty` |
| 7.20b | Decompose `ReservationsController` (383 LOC) -- extract quick-action endpoints, use `HtmlResponseBuilder` |
| 7.20c | Decompose remaining over-limit services: `UserGroupService` (481), `FidelityPointsService` (361), `CustomerDiscountService` (338), `OrderMappingService` (313) |
| 7.20d | Decompose remaining over-limit controllers: `CustomerDiscountsController` (315), `TaxConfigurationController` (208), `GlobalIngredientsController` (206) |
| 7.20e | Fix over-limit DTOs: `MenuBundleDto` (110), `UserDto` (87), `ZReportDto` (73), `OrderDto` (65) -- split into sub-DTOs |
| 7.20f | Fix direct claim extraction -- replace `User.FindFirst(ClaimTypes.NameIdentifier)` with `ICurrentUserService` injection in all controllers |
| 7.20g | Fix password policy inconsistency -- `ChangePasswordCommand` validator requires 6 chars, Identity requires 8 |
| 7.20h | Restrict Swagger to development environment only |

---

## All God Classes & Target State

| File | Current LOC | Limit | Target LOC | Sprint |
|------|-------------|-------|------------|--------|
| `BasketService.cs` | 995 | 300 | 120 | Sprint 3 |
| `OrdersController.cs` | 735 | 150 | 120 | Sprint 2 |
| `OrderEventService.cs` | 734 | 300 | 100 | Sprint 3 |
| `CreateOrderCommandHandler.cs` | 730 | 200 | 150 | Sprint 2 |
| `UserGroupService.cs` | 481 | 300 | 200 | Sprint 7 |
| `ReservationsController.cs` | 383 | 150 | 120 | Sprint 7 |
| `FidelityPointsService.cs` | 361 | 300 | 200 | Sprint 7 |
| `CustomerDiscountService.cs` | 338 | 300 | 200 | Sprint 7 |
| `CustomerDiscountsController.cs` | 315 | 150 | 120 | Sprint 7 |
| `OrderMappingService.cs` | 313 | 300 | 200 | Sprint 7 |
| `EmailTestController.cs` | 254 | 150 | 120 | Sprint 7 |
| `TaxConfigurationController.cs` | 208 | 150 | 120 | Sprint 7 |
| `GlobalIngredientsController.cs` | 206 | 150 | 120 | Sprint 2 |

---

## Test Coverage Roadmap

| Phase | Sprint | New Tests | Cumulative | Est. Coverage |
|-------|--------|-----------|------------|---------------|
| Security tests | Sprint 1.5 | 27 | 67 | ~25% |
| Auth + Orders | Sprint 2 | 56 | 123 | ~45% |
| Basket + Products | Sprint 3 | 52 | 175 | ~65% |
| Reservations + Settings | Sprint 4-5 | 40 | 215 | **80%+** |

See `docs/TEST-COVERAGE-PLAN.md` for full details.
