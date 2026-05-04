# Backend Development Guidelines

> .NET 10 | Clean Architecture + CQRS | PostgreSQL | EF Core 10

---

## Architecture Overview

```
RestaurantSystem.sln
  RestaurantSystem.Api/          # API layer (Controllers, CQRS, Services, DTOs)
  RestaurantSystem.Domain/       # Domain layer (Entities, Enums, Interfaces)
  RestaurantSystem.Infrastructure/ # Infrastructure (DbContext, EF Configs, Migrations, Seeders)
  RestaurantSystem.IntegrationTests/
```

### Key Patterns
- **CQRS** via `CustomMediator` (NOT MediatR - it was removed)
- **Feature folders**: Each domain feature has its own Controller, Commands/, Queries/, Dtos/, Services/, Interfaces/
- **Soft delete**: All entities use `IsDeleted` with a global query filter
- **FluentValidation**: Automatic request validation via `RequestValidationBehavior<T>`

---

## File Length Limits

| File Type | Max LOC | Rationale |
|---|---|---|
| Controller | 150 | Thin dispatchers to CQRS handlers only |
| Command/Query Handler | 200 | Single responsibility per handler |
| Service class | 300 | Beyond this, split by concern |
| Entity (Domain) | 100 | Lean models; behavior in domain services |
| DTO / Record | 60 | Data containers only |
| Validator | 60 | One validator per command/query |
| Configuration class | 50 | Settings containers |
| Email Template | 120 | Consider Razor views for larger templates |

---

## Naming Conventions

### Files & Classes
- **Controllers**: `{Feature}Controller.cs` - e.g., `OrdersController.cs`
- **Commands**: `{Action}{Entity}Command.cs` in `Commands/{CommandName}/` folder
- **Queries**: `{Action}{Entity}Query.cs` in `Queries/{QueryName}/` folder
- **Handlers**: `{CommandName}Handler.cs` in same folder as command/query
- **Validators**: `{CommandName}Validator.cs` - never double-suffix (NOT `CommandCommandValidator`)
- **Services**: `{Feature}Service.cs` with `I{Feature}Service.cs` interface
- **DTOs**: `{Entity}Dto.cs` for responses, `Create{Entity}Dto.cs` / `Update{Entity}Dto.cs` for requests

### Code Style
- PascalCase for public members, `_camelCase` for private fields
- Async methods suffix with `Async`
- Private readonly fields injected via constructor

---

## Controller Rules

1. Controllers are **thin dispatchers only** - no business logic
2. Max 150 LOC per controller
3. All operations go through `CustomMediator` (`_mediator.SendCommand()` / `_mediator.SendQuery()`)
4. No raw `DbContext` injection in controllers - use CQRS handlers
5. Return `ApiResponse<T>` for all endpoints
6. Use proper authorization attributes: `[Authorize]`, `[RequireAdmin]`, `[RequireAdminOrCashier]`, `[AllowAnonymous]`

```csharp
// GOOD
[HttpPost]
public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder([FromBody] CreateOrderCommand command)
{
    var result = await _mediator.SendCommand(command);
    return Ok(result);
}

// BAD - business logic in controller
[HttpPost]
public async Task<ActionResult> CreateOrder([FromBody] CreateOrderDto dto)
{
    var order = new Order { ... };
    _context.Orders.Add(order);
    await _context.SaveChangesAsync();
    // ...
}
```

---

## Exception Handling

### Custom Exceptions Only
Use the exceptions in `/Common/Exceptions/`:

| Exception | HTTP Status | When to Use |
|---|---|---|
| `NotFoundException` | 404 | Entity not found in database |
| `BadRequestException` | 400 | Validation failure, business rule violation |
| `ForbiddenException` | 403 | Authorization failure |

**Never** use `InvalidOperationException` for user-facing errors. It maps to 500 which is incorrect.

```csharp
// GOOD
throw new NotFoundException($"Product {id} not found");
throw new BadRequestException("Insufficient points");

// BAD
throw new InvalidOperationException("Product not found");
throw new Exception("Something went wrong");
```

### Middleware Stack (order matters)
1. `ExceptionHandlingMiddleware` - catches all unhandled exceptions
2. `ValidationExceptionHandlingMiddleware` - catches FluentValidation errors
3. `SessionMiddleware` - manages X-Session-Id header
4. Authentication -> Authorization

---

## Configuration Rules

### No Hardcoded Values
All configuration MUST come from `IOptions<T>` pattern or `IConfiguration`:

```csharp
// GOOD
public class MyHandler
{
    private readonly EmailSettings _emailSettings;
    public MyHandler(IOptions<EmailSettings> emailSettings)
    {
        _emailSettings = emailSettings.Value;
    }
}

// BAD
private const string AdminEmail = "someone@example.com";
var url = "http://localhost:3000";
```

### Configuration Sources (priority order)
1. Environment variables
2. `app-secrets.json` (git-ignored)
3. `appsettings.{Environment}.json`
4. `appsettings.json`

### Key Settings Classes
- `EmailSettings` - SMTP config, admin email, frontend/backend URLs
- `JwtSettings` - JWT auth configuration
- `AWSSettings` - S3 file storage
- `CorsSettings:AllowedOrigins` - CORS allowed origins array (production)
- `PrinterSettings:ApiKey` - API key for printer-feed endpoint

---

## Audit Identity

Always use `GetAuditIdentifier()` for `CreatedBy` / `UpdatedBy` / `DeletedBy` fields:

```csharp
// GOOD
CreatedBy = _currentUserService.GetAuditIdentifier()

// BAD
CreatedBy = _currentUserService.UserId?.ToString() ?? "System"
```

---

## CQRS Pattern

### Command (Write Operation)
```
Features/{Feature}/Commands/{CommandName}/
  {CommandName}.cs          # Command record
  {CommandName}Handler.cs   # Handler implementation
  {CommandName}Validator.cs # FluentValidation rules
```

```csharp
// Command
public record CreateOrderCommand(...) : ICommand<ApiResponse<OrderDto>>;

// Handler
public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, ApiResponse<OrderDto>>
{
    public async Task<ApiResponse<OrderDto>> Handle(CreateOrderCommand command, CancellationToken ct) { ... }
}
```

### Query (Read Operation)
```
Features/{Feature}/Queries/{QueryName}/
  {QueryName}.cs           # Query record
  {QueryName}Handler.cs    # Handler implementation
```

---

## Service Registration

All services must have interfaces and be registered in `Program.cs`:

```csharp
// Scoped (per-request) - default for business services
builder.Services.AddScoped<IMyService, MyService>();

// Singleton - for long-lived services like SSE
builder.Services.AddSingleton<OrderEventService>();
builder.Services.AddSingleton<IOrderEventService>(sp => sp.GetRequiredService<OrderEventService>());
```

---

## Database Access

### EF Core Patterns
- Use `async/await` for all database operations
- Thread `CancellationToken` through all handlers
- Use `.Include()` for explicit eager loading - avoid N+1
- Use `AsNoTracking()` for read-only queries
- Wrap multi-step writes in transactions

```csharp
// GOOD - explicit includes, async, cancellation
var order = await _context.Orders
    .Include(o => o.Items)
    .Include(o => o.Payments)
    .AsNoTracking()
    .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

// GOOD - transaction for multi-step writes
using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
try
{
    // ... operations ...
    await _context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

### Null Safety
- **No `null!`** in DTOs - use `required` modifier or `= string.Empty`
- Keep `null!` only where EF Core requires it (navigation properties in entity constructors)

---

## Security Checklist

- [ ] All endpoints have proper `[Authorize]` or `[AllowAnonymous]`
- [ ] CORS uses configured origins in production (never `AllowAnyOrigin`)
- [ ] Printer-feed endpoint validates API key via `X-Api-Key` header
- [ ] No hardcoded secrets, emails, or URLs in source code
- [ ] Passwords validated: 8+ chars, upper/lower/digit/special
- [ ] Account lockout after 5 failed attempts
- [ ] JWT tokens expire, refresh mechanism in place

---

## Authorization Attributes

All custom attributes in `/Common/Authorization/`:

| Attribute | Allowed Roles |
|---|---|
| `[RequireAdmin]` | Admin |
| `[RequireCashier]` | Cashier |
| `[RequireKitchenStaff]` | KitchenStaff |
| `[RequireServer]` | Server |
| `[RequireStaff]` | Admin, Cashier, KitchenStaff, Server |
| `[RequireAdminOrCashier]` | Admin, Cashier |
| `[RequireKitchenOrServer]` | KitchenStaff, Server |
| `[RequireRole(...)]` | Custom role list |

SSE endpoints (`/api/events/*`) MUST have `[Authorize]` with appropriate roles.

---

## API Response Format

All API endpoints MUST return `ApiResponse<T>`:

```csharp
// Standard success
return Ok(ApiResponse<OrderDto>.SuccessWithData(order));

// Standard error
return BadRequest(ApiResponse<string>.Failure("Validation failed"));
```

**Exceptions** (non-ApiResponse allowed):
- File download endpoints (`Content()` / `File()`)
- HTML page endpoints (quick-confirm/cancel from email links)
- Health check endpoints (`/health`, `/api/health`)

---

## Caching

- `IDistributedCache` registered (currently DistributedMemoryCache, Redis commented out)
- Cache key prefix convention: `"{feature}:{identifier}"` (e.g., `"basket:{userId}"`)
- Default TTL: 30 minutes for baskets
- Caching is currently disabled in BasketService due to race condition - do not enable without fixing
- When Redis is enabled: use `CorsSettings:Redis` connection string

---

## Logging Conventions

- Use structured logging: `_logger.LogInformation("Order {OrderId} created by {UserId}", id, userId)`
- **Error**: Unrecoverable failures, exceptions caught at boundaries
- **Warning**: Recoverable issues (email send failure, retry succeeded)
- **Information**: Business events (order created, payment added, user login)
- **Debug**: Internal state, query details (development only)
- Do not log sensitive data (tokens, passwords, PII in plaintext)
- Do not use emojis in log messages (causes issues with some log aggregators)

---

## Enum Serialization

Custom `StringEnumConverterFactory` in `/Common/Converters/`:
- Enums serialize as **strings** (not integers) in JSON
- Supports both `[EnumMember]` attribute and string values on deserialization
- Configured globally in `Program.cs` for both controller and minimal API JSON options

---

## Global Query Filters

- **Soft delete**: All entities implementing `ISoftDelete` have `Where(e => !e.IsDeleted)` applied globally
- **Opt-out**: Entities implementing `IExcludeFromGlobalFilter` bypass the filter
- **Manual bypass**: Use `.IgnoreQueryFilters()` only when explicitly querying deleted data (e.g., admin audit)

---

## Email Templates

- Templates accept `contactEmail` parameter (defaults to configured admin email)
- `EmailService` methods pass `_emailSettings.AdminEmail` as `contactEmail`
- Frontend/backend URLs come from `EmailSettings.FrontendBaseUrl` / `BackendBaseUrl`
- No hardcoded email addresses in template HTML/text bodies

---

## Testing

- See `docs/TEST-COVERAGE-PLAN.md` for full strategy
- **Integration tests**: `RestaurantSystem.IntegrationTests/` - real PostgreSQL via Testcontainers + Respawn
- **Unit tests** (planned): `RestaurantSystem.UnitTests/` - mocked dependencies
- **Run**: `dotnet test --settings .runsettings`
- **Coverage target**: 80%+ overall
- **CI/CD**: Tests MUST run in GitLab pipeline before merge

### Test Authentication
- `AuthenticateAsAdmin()` - sets admin claims via `X-Test-Admin: true` header
- `AuthenticateAsUser()` - default customer claims
- All tests inherit from `IntegrationTestBase` which seeds data and resets DB

---

## Security

See `docs/SECURITY-AUDIT.md` for full audit findings.

Key rules:
- **Rate limiting** required on all auth endpoints
- **Security headers** must be set (HSTS, X-Frame-Options, CSP, etc.)
- **Never log tokens** or sensitive data in plaintext
- **RequireHttpsMetadata** must be true in production
- **S3 files** must be private (pre-signed URLs for access)
- **Refresh tokens** must be hashed in database
- **X-Session-Id** must be validated as UUID format
