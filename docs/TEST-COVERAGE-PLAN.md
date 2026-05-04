# Backend Test Coverage Plan

> Target: 80%+ code coverage | Current: ~15% estimated

---

## Current State

### Infrastructure (Solid)
- **Framework:** xUnit 2.9.3
- **Database:** PostgreSQL via Testcontainers (real DB, not in-memory)
- **Isolation:** Respawn cleans tables between tests
- **HTTP Testing:** WebApplicationFactory with custom auth handler
- **Mocking:** Moq for external dependencies
- **Assertions:** FluentAssertions
- **Coverage tool:** coverlet.collector (installed but not configured)

### Existing Tests (~40 total)

| Feature | Tests | Type | Coverage |
|---------|-------|------|----------|
| Products (Controller) | 1 | Integration | Minimal - GET only |
| Basket -> Order flow | 3 | Integration (E2E) | Moderate - happy path |
| FidelityPoints service | 11 | Service | Comprehensive |
| PointEarningRules service | 13 | Service | Comprehensive |
| CustomerDiscounts service | 12 | Service | Comprehensive |
| **TOTAL** | **~40** | | |

### Features with ZERO Tests (12+)

| Feature | Priority | Reason |
|---------|----------|--------|
| **Authentication** | CRITICAL | Login, register, token refresh, password reset, Google/Apple OAuth |
| **Orders (full scope)** | CRITICAL | Status transitions, payments, cancellation, refunds |
| **Basket (full scope)** | HIGH | Add/remove/update items, pricing, caching, merge |
| **Reservations** | HIGH | CRUD, time slot conflicts, status transitions |
| **Categories** | MEDIUM | CRUD, reordering, images |
| **Products** | MEDIUM | CRUD, variations, images, ingredients |
| **Menus** | MEDIUM | Bundle CRUD, section management |
| **User Management** | MEDIUM | Profile, roles, account deletion |
| **Groups** | MEDIUM | Group CRUD, memberships, QR codes |
| **Settings** | MEDIUM | Tax config, order types, working hours |
| **Email** | LOW | Template rendering (mock SMTP) |
| **SSE/Events** | LOW | Connection management, broadcast |

### Critical Gap: No CI/CD Test Execution

`.gitlab-ci.yml` has no `dotnet test` step. Tests exist but never run automatically.

---

## Test Architecture

### Layer 1: Unit Tests (NEW - ~60% of new tests)
Fast, isolated, mock all dependencies.

```
RestaurantSystem.UnitTests/           # NEW PROJECT
  Features/
    Orders/
      Services/
        OrderPricingServiceTests.cs
        OrderAddressFactoryTests.cs
      Validators/
        CreateOrderCommandValidatorTests.cs
    Basket/
      Services/
        BasketPricingServiceTests.cs
        BasketItemFactoryTests.cs
    Auth/
      Validators/
        LoginCommandValidatorTests.cs
    ...
  Common/
    Services/
      PricingCalculatorTests.cs
      AddressMapperTests.cs
```

**Pattern:**
```csharp
public class OrderPricingServiceTests
{
    private readonly Mock<ApplicationDbContext> _contextMock;
    private readonly OrderPricingService _sut;

    [Fact]
    public void CalculateSubTotal_WithMultipleItems_ReturnsSumOfItemTotals()
    {
        // Arrange
        var items = new[] { new OrderItem { Quantity = 2, UnitPrice = 10.00m } };
        // Act
        var result = _sut.CalculateSubTotal(items);
        // Assert
        result.Should().Be(20.00m);
    }
}
```

### Layer 2: Integration Tests (Existing + Expanded - ~30% of new tests)
Real database, full HTTP pipeline, test endpoint behavior.

```
RestaurantSystem.IntegrationTests/    # EXISTING PROJECT
  Features/
    Auth/
      AuthControllerTests.cs          # NEW
      PasswordResetFlowTests.cs       # NEW
    Orders/
      OrdersControllerTests.cs        # NEW
      OrderStatusTransitionTests.cs   # NEW
      PaymentFlowTests.cs             # NEW
      BasketToOrderIntegrationTest.cs # EXISTING
    Basket/
      BasketControllerTests.cs        # NEW
    Reservations/
      ReservationsControllerTests.cs  # NEW
    Products/
      ProductsControllerTests.cs      # EXISTING (expand)
    Categories/
      CategoriesControllerTests.cs    # NEW
    ...
```

### Layer 3: Security Tests (NEW - ~10% of new tests)
Verify authorization, rate limiting, input validation.

```
RestaurantSystem.IntegrationTests/
  Security/
    AuthorizationTests.cs        # Every endpoint tested for correct auth
    RateLimitTests.cs            # Verify rate limiter blocks
    InputValidationTests.cs      # Malicious inputs rejected
    SessionSecurityTests.cs      # X-Session-Id validation
```

---

## Test Implementation Plan

### Phase 1: Security Tests (Sprint 1.5 - Alongside Security Fixes)

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `PasswordResetFlowTests.cs` | 5 | Valid token required, expired token rejected, wrong email rejected, token single-use, new password enforced |
| `AuthorizationTests.cs` | 15 | Every `[Authorize]` endpoint returns 401 without token, SSE endpoints protected, admin-only endpoints return 403 for non-admin |
| `RateLimitTests.cs` | 4 | Login blocked after 5 attempts, forgot-password blocked after 3, register blocked after 10, rate limit resets after window |
| `SessionSecurityTests.cs` | 3 | Invalid UUID rejected, session merge only for own session, basket not accessible across sessions |

**Total: ~27 tests**

### Phase 2: Auth & Core Flow Tests (Sprint 2)

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `AuthControllerTests.cs` | 12 | Register (valid/duplicate email/weak password), login (valid/wrong password/locked), refresh token (valid/expired/reused), logout |
| `OrdersControllerTests.cs` | 15 | Create order (dine-in/takeaway/delivery), get orders (pagination/filters), get by ID (found/not found), update status (valid transitions/invalid), cancel (valid/already cancelled), delete (admin only/non-admin 403) |
| `OrderStatusTransitionTests.cs` | 8 | Pending->Confirmed, Confirmed->Preparing, Preparing->Ready, Ready->Completed, invalid transitions rejected, status history recorded |
| `PaymentFlowTests.cs` | 8 | Add payment (cash/card/partial), payment completes order, refund payment (admin only), overpayment rejected, insufficient payment |

**Total: ~43 tests**

### Phase 3: Basket & Products Tests (Sprint 3)

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `BasketControllerTests.cs` | 12 | Add item (product/menu bundle), update quantity, remove item, clear basket, pricing recalculation, merge on login, menu section validation (min/max), session-based access |
| `BasketPricingServiceTests.cs` | 10 | Subtotal calculation, tax calculation per order type, delivery fee, user discount, customer discount, fidelity points, rounding, menu item pricing, ingredient customization pricing |
| `ProductsControllerTests.cs` | 10 | CRUD (create/read/update/delete), variations, images, availability toggle, search/filter, category filter |
| `CategoriesControllerTests.cs` | 6 | CRUD, reorder, image upload, products-by-category |

**Total: ~38 tests**

### Phase 4: Reservations, Settings, Users (Sprint 4-5)

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `ReservationsControllerTests.cs` | 10 | Create (valid/conflict/past date), approve/reject, cancel, release table, time slot validation, capacity check |
| `SettingsTests.cs` | 8 | Tax config CRUD, order type config, working hours CRUD, validation rules |
| `UserManagementTests.cs` | 8 | Get profile, update profile, change password, request account deletion, confirm deletion, admin user list, role changes |
| `GroupsTests.cs` | 6 | Create group, add/remove members, group discounts, QR code generation |
| `MenusControllerTests.cs` | 8 | Create bundle, update sections, add/remove items, pricing, availability |

**Total: ~40 tests**

### Phase 5: Unit Tests for Extracted Services (Sprints 2-3 as services are created)

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `OrderPricingServiceTests.cs` | 8 | Subtotal, tax, discount, fidelity, rounding, edge cases (zero, negative) |
| `OrderAddressFactoryTests.cs` | 5 | From saved address, from DTO, from default, missing fields, null handling |
| `OrderItemFactoryTests.cs` | 6 | Product item, menu bundle item, recursive children, missing product, variation lookup |
| `BasketItemFactoryTests.cs` | 6 | Create product item, create menu item, dedup matching, quantity merge |
| `AddressMapperTests.cs` | 4 | FromUserAddress, FromDto, ToDto, null handling |
| `PricingCalculatorTests.cs` | 8 | Shared pricing logic, tax rates, discount combinations |
| `HtmlResponseBuilderTests.cs` | 4 | Success page, error page, info page, redirect URL |

**Total: ~41 tests**

---

## Coverage Configuration

### Create `.runsettings` file

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>cobertura,opencover</Format>
          <Exclude>[*.IntegrationTests]*,[*.UnitTests]*</Exclude>
          <ExcludeByFile>**/Migrations/**,**/obj/**</ExcludeByFile>
          <SingleHit>false</SingleHit>
          <UseSourceLink>true</UseSourceLink>
          <IncludeTestAssembly>false</IncludeTestAssembly>
          <SkipAutoProps>true</SkipAutoProps>
          <DeterministicReport>true</DeterministicReport>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

### Run commands

```bash
# Run all tests with coverage
dotnet test --settings .runsettings --collect:"XPlat Code Coverage"

# Generate HTML report (requires reportgenerator tool)
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:/**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Html

# Quick coverage summary
dotnet test --settings .runsettings --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
```

---

## CI/CD Integration

Add to `.gitlab-ci.yml`:

```yaml
test:
  stage: test
  image: mcr.microsoft.com/dotnet/sdk:10.0
  services:
    - postgres:16
  variables:
    POSTGRES_DB: restaurant_test
    POSTGRES_USER: test
    POSTGRES_PASSWORD: test
    ConnectionStrings__DefaultConnection: "Host=postgres;Database=restaurant_test;Username=test;Password=test"
  script:
    - dotnet test --settings .runsettings --collect:"XPlat Code Coverage" --logger "junit;LogFilePath=test-results.xml"
  artifacts:
    when: always
    paths:
      - "**/test-results.xml"
      - "**/coverage.cobertura.xml"
    reports:
      junit: "**/test-results.xml"
      coverage_report:
        coverage_format: cobertura
        path: "**/coverage.cobertura.xml"
  coverage: '/Total\s*\|\s*(\d+\.?\d*)%/'
```

---

## Coverage Targets

### By Feature (Target: 80%+ overall)

| Feature | Current | Phase | Target |
|---------|---------|-------|--------|
| Auth | 0% | Phase 1-2 | 90% |
| Orders | ~10% | Phase 2 | 85% |
| Basket | ~5% | Phase 3 | 85% |
| Products | ~5% | Phase 3 | 80% |
| Reservations | 0% | Phase 4 | 80% |
| FidelityPoints | ~70% | Existing | 85% |
| Categories | 0% | Phase 3 | 80% |
| Settings | 0% | Phase 4 | 75% |
| Users | 0% | Phase 4 | 75% |
| Groups | 0% | Phase 4 | 70% |
| Menus | 0% | Phase 4 | 75% |
| Common/Services | 0% | Phase 5 | 90% |
| **Overall** | **~15%** | | **80%+** |

### By Test Type

| Type | Current | Target | Tests to Write |
|------|---------|--------|----------------|
| Unit | 0 | ~41 | 41 new |
| Integration | ~40 | ~120 | ~80 new |
| Security | 0 | ~27 | 27 new |
| **Total** | **~40** | **~188** | **~148 new** |

---

## Test Data Management

### Seed Data Strategy
- **Minimal seeds**: IntegrationTestBase seeds 3 categories + 2 products
- **Per-test data**: Each test creates its own specific data
- **Factories**: Consider creating `TestOrderFactory`, `TestProductFactory`, `TestUserFactory` for common object creation
- **Respawn**: Cleans all data between tests (except migrations table)

### Authentication in Tests
- `AuthenticateAsAdmin()` - sets `X-Test-Admin: true` header
- `AuthenticateAsUser()` - default customer claims
- Consider adding: `AuthenticateAsServer()`, `AuthenticateAsKitchenStaff()`, `AuthenticateAsCashier()`

---

## Estimated Timeline

| Phase | Sprint | Tests | Cumulative Coverage |
|-------|--------|-------|---------------------|
| Phase 1 (Security) | Sprint 1.5 | 27 | ~25% |
| Phase 2 (Auth + Orders) | Sprint 2 | 43 | ~45% |
| Phase 3 (Basket + Products) | Sprint 3 | 38 | ~60% |
| Phase 4 (Reservations + Settings) | Sprint 4-5 | 40 | ~75% |
| Phase 5 (Unit tests for new services) | Sprint 2-3 | 41 | **80%+** |
