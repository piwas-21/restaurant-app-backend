using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using Sentry.Extensibility;
using System.Threading.RateLimiting;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Services;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Middleware;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Auth.Handlers;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Api.Features.Settings.FormFields.Services;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Api.Features.Settings.Services;
using RestaurantSystem.Api.Features.Groups.Interfaces;
using RestaurantSystem.Api.Features.Groups.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Extensions;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.ServiceDefaults;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddApiRegistration();

// Configure Kestrel for long-lived SSE connections
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
});

builder.Configuration.SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("app-secrets.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    // Re-add env vars LAST so they override the JSON files we just appended
    // (the default builder added env vars before our JSON sources, so without
    // this they'd be shadowed). Lets us point at Mailpit / a different DB
    // for E2E without touching app-secrets.json.
    .AddEnvironmentVariables();

// Error tracking (DEV-PHASES W3 follow-up): Sentry, env-gated on SENTRY_DSN —
// the same convention as the frontend's server-side wiring and the deploy
// repo's compose passthrough. With no DSN (the default) this block is skipped
// entirely, the SDK never initializes, and behaviour is exactly as before.
// Error tracking ONLY: performance tracing stays off and no PII / request
// bodies are captured.
var sentryDsn = builder.Configuration["SENTRY_DSN"];
if (!string.IsNullOrEmpty(sentryDsn))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.SendDefaultPii = false;                // no user identifiers, cookies, or client IPs
        options.MaxRequestBodySize = RequestSize.None; // never capture request bodies
        options.TracesSampleRate = 0;                  // errors only — tracing/performance off
        // SENTRY_ENVIRONMENT distinguishes the prod/staging boxes (both run
        // ASPNETCORE_ENVIRONMENT=Production); fall back to the host environment.
        var sentryEnvironment = builder.Configuration["SENTRY_ENVIRONMENT"];
        options.Environment = string.IsNullOrEmpty(sentryEnvironment)
            ? builder.Environment.EnvironmentName
            : sentryEnvironment;
        options.Release = typeof(Program).Assembly.GetName().Version?.ToString();
    });
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new StringEnumConverterFactory());
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddControllers(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new StringEnumConverterFactory());
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Restaurant System API",
        Version = "v1",
        Description = "A comprehensive restaurant management system API"
    });

    // Add JWT authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });

    // Avoid schema ID collisions when two DTOs share the same class name across namespaces
    c.CustomSchemaIds(t => t.FullName!.Replace("+", "."));
});


builder.AddRedisDistributedCache("redis");

builder.AddNpgsqlDataSource("restaurantdb", configureDataSourceBuilder: dataSourceBuilder =>
{
    dataSourceBuilder.EnableDynamicJson();
});

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
    options.UseNpgsql(dataSource, npgsqlOptions => npgsqlOptions
        .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name)
        .CommandTimeout(30));
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(opt =>
{
    opt.Password.RequiredLength = 8;
    opt.Password.RequireDigit = true;
    opt.Password.RequireLowercase = true;
    opt.Password.RequireUppercase = true;
    opt.Password.RequireNonAlphanumeric = true;
    opt.User.RequireUniqueEmail = true;
    opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    opt.Lockout.MaxFailedAccessAttempts = 5;
    opt.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddPasswordValidator<StrongPasswordValidator<ApplicationUser>>();

// Configure Data Protection to persist keys
// This ensures email verification and password reset tokens remain valid across pod restarts
// Keys are stored in a persistent directory that should be mounted as a volume in production
var keysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
if (!Directory.Exists(keysPath))
{
    Directory.CreateDirectory(keysPath);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("RestaurantSystem");

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
builder.Services.Configure<JwtSettings>(jwtSettings);

var jwtOptions = jwtSettings.Get<JwtSettings>();
if (jwtOptions != null)
{
    jwtOptions.Validate();
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = jwtOptions?.TokenValidationParameters ?? new TokenValidationParameters();

    options.Events = new JwtBearerEvents
    {
        // EventSource (SSE) cannot set headers, so the frontend passes the JWT
        // via ?token= query string for SSE endpoints. Extract it here.
        OnMessageReceived = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api/events") &&
                context.Request.Query.TryGetValue("token", out var token))
            {
                context.Token = token;
            }
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is SecurityTokenExpiredException)
            {
                context.Response.Headers.Append("Token-Expired", "true");
            }
            return Task.CompletedTask;
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();

            context.Response.StatusCode = 401;

            context.Response.ContentType = "application/json";

            var response = ApiResponse<object>.Failure("Authentication required", "You must be authenticated to access this resource");
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
        },
        OnForbidden = async context =>
        {
            // Handle authorization failures (403 Forbidden)
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var response = ApiResponse<object>.Failure("Access denied", "You don't have permission to access this resource");

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
        }
    };
});

var emailSettings = builder.Configuration.GetSection("EmailSettings");
builder.Services.Configure<EmailSettings>(emailSettings);

// Fail fast on misconfigured email (e.g. missing AdminEmail or provider
// credentials) instead of silently dropping mail at send time. Mirrors the
// JwtSettings.Validate() call above. Fall back to a default instance so a
// missing/unbindable section still triggers validation rather than skipping it.
(emailSettings.Get<EmailSettings>() ?? new EmailSettings()).Validate();

builder.Services.Configure<PrinterSettings>(builder.Configuration.GetSection("PrinterSettings"));

// Product modules this tenant bought (sofra ADR-010 / S11). The deploy repo's tenant
// compose template maps the registry's `modules:` list onto Modules__Enabled, and
// Modules__Enforce opts a tenant in; the legacy RUMI install has NEITHER, which
// TenantModules reads as UNRESTRICTED. Until that deploy-side mapping ships, nothing
// sets these keys and the whole feature is inert everywhere — which is the intended
// merge state, not an accident. Singleton because the answer is fixed for the process
// lifetime: a change lands via re-provision + restart, which is also the only way the
// tenant .env changes.
builder.Services.Configure<ModuleSettings>(builder.Configuration.GetSection("Modules"));
builder.Services.AddSingleton<ITenantModules, TenantModules>();

// Order-level pricing. DeliveryFee defaults to 0 so an absent section preserves what every live
// tenant charges today — see OrderSettings for why that is not the old 5.00 constant. A tenant
// opts in per box via OrderSettings__DeliveryFee.
builder.Services.Configure<RestaurantSystem.Api.Settings.OrderSettings>(
    builder.Configuration.GetSection(RestaurantSystem.Api.Settings.OrderSettings.SectionName));

// Tenant→diner Stripe Connect (ADR-011 Job B). Registered unconditionally and INERT unless a tenant
// has Stripe__Enabled plus a platform key plus a connected account — which is no tenant today, so
// this ships safe to the whole fleet. Singleton because the answer is fixed for the process
// lifetime, matching ITenantModules above: a change lands via re-provision + restart.
builder.Services.Configure<RestaurantSystem.Api.Settings.StripeSettings>(
    builder.Configuration.GetSection(RestaurantSystem.Api.Settings.StripeSettings.SectionName));
builder.Services.AddSingleton<RestaurantSystem.Api.Features.Payments.Interfaces.IStripeGateway,
    RestaurantSystem.Api.Features.Payments.Services.StripeGateway>();
// Scoped, unlike the gateway: this one reads EmailSettings/StripeSettings per request to build the
// return URLs, and holds no connection of its own — SessionService is constructed per call.
builder.Services.AddScoped<RestaurantSystem.Api.Features.Payments.Interfaces.IStripeCheckoutClient,
    RestaurantSystem.Api.Features.Payments.Services.StripeCheckoutClient>();
builder.Services.AddScoped<RestaurantSystem.Api.Features.Payments.Interfaces.ICheckoutSessionReuse,
    RestaurantSystem.Api.Features.Payments.Services.CheckoutSessionReuse>();
builder.Services.AddScoped<RestaurantSystem.Api.Features.Payments.Interfaces.ICheckoutSettlementWriter,
    RestaurantSystem.Api.Features.Payments.Services.CheckoutSettlementWriter>();

// Startup-seed credentials, consumed by UserSeeder in Infrastructure. An empty
// section means admin seeding is skipped (roles still seed) — see issue #116.
// Per-tenant provisioning injects SeedSettings__AdminEmail/__AdminPassword env
// vars (sofra ADR-003); env vars override JSON here.
builder.Services.Configure<RestaurantSystem.Infrastructure.Settings.SeedSettings>(builder.Configuration.GetSection("SeedSettings"));

// Tenant identity for the RestaurantInfo singleton, consumed by
// RestaurantInfoSeeder on the first boot of a fresh database only — see issue
// #120. Provisioning injects RestaurantInfoSeed__Name/__City/__Email from the
// tenant registry (sofra ADR-003); an empty section means the seeder is a no-op.
builder.Services.Configure<RestaurantSystem.Infrastructure.Settings.RestaurantInfoSeedSettings>(builder.Configuration.GetSection("RestaurantInfoSeed"));

// Order-email currency label. Per-tenant provisioning injects the
// Localization__Currency value, mapped from the tenant registry currency field
// via TENANT_CURRENCY per sofra ADR-003. The default CHF keeps the legacy RUMI
// install unchanged.
builder.Services.Configure<RestaurantSystem.Infrastructure.Settings.LocalizationSettings>(builder.Configuration.GetSection("Localization"));

builder.Services.AddFileStorage(builder.Configuration);
builder.Services.AddAuthorization();

// Trust the Caddy reverse-proxy's X-Forwarded-For header so the rate limiter partitions by real client IP
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate-limit policy values come from the RateLimiter section of
// appsettings.json (production defaults) overlaid by appsettings.Development.json
// (much higher dev limits so the Playwright E2E suite runs repeatedly
// without bouncing the API). See Settings/RateLimiterSettings.cs.
// Register IOptions<RateLimiterSettings> for DI consistency with the sibling
// settings (JwtSettings, EmailSettings, …). Not consumed by a handler today,
// but matches the pattern and keeps the [Range] annotations available to a
// future ValidateDataAnnotations() pipeline.
var rateLimiterSection = builder.Configuration.GetSection("RateLimiter");
builder.Services.Configure<RateLimiterSettings>(rateLimiterSection);
var rateLimiter = rateLimiterSection.Get<RateLimiterSettings>() ?? new RateLimiterSettings();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Emit a small JSON body + Retry-After on rejection. Without this the 429 body is
    // empty, which the SPA's fetch(...).json() throws on — turning a transient rate-limit
    // rejection into a spurious "session expired" logout on the client.
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Too many requests. Please slow down and try again shortly.\"}",
            cancellationToken);
    };

    // /api/Auth/login (+ google/apple-login)
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiter.AuthPermitLimit,
            Window = TimeSpan.FromMinutes(rateLimiter.AuthWindowMinutes),
            QueueLimit = 0
        }));

    // /api/Auth/refresh-token — its OWN partition so refresh bursts can't exhaust the
    // login bucket (an expired-token refresh stampede was 429-ing admins out of re-login).
    options.AddPolicy("auth-refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiter.AuthRefreshPermitLimit,
            Window = TimeSpan.FromMinutes(rateLimiter.AuthRefreshWindowMinutes),
            QueueLimit = 0
        }));

    // /api/Auth/forgot-password + reset-password
    options.AddPolicy("forgot-password", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiter.ForgotPasswordPermitLimit,
            Window = TimeSpan.FromHours(rateLimiter.ForgotPasswordWindowHours),
            QueueLimit = 0
        }));

    // /api/User/register/customer
    options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiter.RegisterPermitLimit,
            Window = TimeSpan.FromHours(rateLimiter.RegisterWindowHours),
            QueueLimit = 0
        }));

    // /api/orders/{orderId}/send-confirmation-email
    // Endpoint is [AllowAnonymous] to support guest checkout (see ADR-004).
    // Per-IP throttling caps the abuse surface for an attacker that has
    // scraped order IDs from receipts/URLs and tries to spam customers or
    // inflate SMTP cost via the admin-notification email.
    options.AddPolicy("confirmation-email", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiter.ConfirmationEmailPermitLimit,
            Window = TimeSpan.FromMinutes(rateLimiter.ConfirmationEmailWindowMinutes),
            QueueLimit = 0
        }));

    // /api/Payments/checkout-session — anonymous for guest checkout (ADR-004). Its own
    // partition so a burst here cannot drain another endpoint's bucket, and vice versa:
    // every permit spends a Stripe API call on the tenant's connected account.
    options.AddPolicy("checkout-session", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiter.CheckoutSessionPermitLimit,
            Window = TimeSpan.FromMinutes(rateLimiter.CheckoutSessionWindowMinutes),
            QueueLimit = 0
        }));
});

builder.Services.AddInfrastructureRegistration();

// CORS: Use configured origins in production, allow all in development.
// Fail-safe: refuse to start in non-Development if CorsSettings:AllowedOrigins is missing/empty —
// silent fallback to AllowAnyOrigin in production would be a misconfiguration disguised as a working deploy.
var corsOrigins = builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>();
if (!builder.Environment.IsDevelopment() && (corsOrigins == null || corsOrigins.Length == 0))
{
    throw new InvalidOperationException(
        "CorsSettings:AllowedOrigins must be configured with at least one origin in non-Development environments.");
}
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Non-null in the non-Development branch — the fail-safe above throws otherwise.
            policy.WithOrigins(corsOrigins!)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
// Lets ApplicationDbContext (Infrastructure, which cannot see ICurrentUserService) backfill audit
// columns with the acting user. NOT forwarded to ICurrentUserService: that is a dependency CYCLE —
// CurrentUserService needs UserManager, which needs IUserStore, which AddEntityFrameworkStores
// binds back to ApplicationDbContext. It hangs the host rather than throwing. See
// HttpContextAuditIdentityProvider, which depends on IHttpContextAccessor and nothing else.
builder.Services.AddScoped<IAuditIdentityProvider, HttpContextAuditIdentityProvider>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddEmailSender(builder.Configuration);   // IEmailSender transport (Smtp | Resend)
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailBrandingProvider, EmailBrandingProvider>();
builder.Services.AddScoped<IBasketService, BasketService>();
builder.Services.AddScoped<IBasketPricingService, BasketPricingService>();
builder.Services.AddScoped<ILineCustomizationBuilder, LineCustomizationBuilder>();
builder.Services.AddScoped<IBasketMappingService, BasketMappingService>();
builder.Services.AddScoped<IBasketItemFactory, BasketItemFactory>();
builder.Services.AddScoped<IBasketRepository, BasketRepository>();
builder.Services.AddScoped<IBasketChannelService, BasketChannelService>();
builder.Services.AddScoped<IOrderChannelGuard, OrderChannelGuard>();
builder.Services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
builder.Services.AddScoped<IAnonymousBasketMerger, AnonymousBasketMerger>();
builder.Services.AddScoped<IBasketMergeService, BasketMergeService>();
builder.Services.AddScoped<IOrderMappingService, OrderMappingService>();
builder.Services.AddScoped<IOrderAddressFactory, OrderAddressFactory>();
builder.Services.AddScoped<IOrderItemFactory, OrderItemFactory>();
builder.Services.AddScoped<IBasketToOrderTranslator, BasketToOrderTranslator>();
builder.Services.AddScoped<IOrderPricingService, OrderPricingService>();
builder.Services.AddScoped<IOrderNotificationService, OrderNotificationService>();
builder.Services.AddScoped<IOrderPaymentBuilder, OrderPaymentBuilder>();
builder.Services.AddScoped<IOrderTableReservationService, OrderTableReservationService>();
builder.Services.AddScoped<IOrderFidelityCoordinator, OrderFidelityCoordinator>();
builder.Services.AddScoped<IPointEarningRuleService, PointEarningRuleService>();
builder.Services.AddScoped<IFidelityPointsService, FidelityPointsService>();
builder.Services.AddScoped<ICustomerDiscountService, CustomerDiscountService>();
builder.Services.AddScoped<ITaxConfigurationService, TaxConfigurationService>();
// Settings Services
builder.Services.AddScoped<IOrderTypeConfigurationService, OrderTypeConfigurationService>();
builder.Services.AddScoped<IWorkingHoursService, WorkingHoursService>();
builder.Services.AddScoped<IFormFieldConfigurationService, FormFieldConfigurationService>();
builder.Services.AddScoped<IFormFieldRequirementService, FormFieldRequirementService>();
// First-run setup checklist (SOFRA-ONBOARDING-PLAN O4) — the singleton row's reader +
// concurrency-safe writer, shared by the query and both commands.
builder.Services
    .AddScoped<RestaurantSystem.Api.Features.Setup.Services.ISetupChecklistStore,
        RestaurantSystem.Api.Features.Setup.Services.SetupChecklistStore>();
// Per-app-instance "registry rows seeded" marker — pure in-memory flag, singleton lifetime.
builder.Services.AddSingleton<IFormFieldSeedState, FormFieldSeedState>();

builder.Services.AddScoped<IQRCodeService, QRCodeService>();
// One-off admin maintenance: re-runs the resize-on-upload pipeline over images stored before it
// existed. Dry-run by default (see ImageMaintenanceController).
builder.Services.AddScoped<RestaurantSystem.Api.Features.Maintenance.Interfaces.IImageBackfillService,
    RestaurantSystem.Api.Features.Maintenance.Services.ImageBackfillService>();
builder.Services.AddScoped<RestaurantSystem.Api.Features.FloorPlan.Services.IFloorPlanService, RestaurantSystem.Api.Features.FloorPlan.Services.FloorPlanService>();
builder.Services.AddScoped<IGroupMembershipService, GroupMembershipService>();
builder.Services.AddScoped<IMembershipQrService, MembershipQrService>();
builder.Services.AddScoped<IUserGroupService, UserGroupService>();
// HTML page builder for email-link landing endpoints (Sprint 2 task 2.1).
// Pure string composition — singleton lifetime is appropriate.
builder.Services.AddSingleton<IHtmlResponseBuilder, HtmlResponseBuilder>();
builder.Services.AddScoped<LoginEventHandler>();
// Register background services
builder.Services.Configure<ReservationRetentionSettings>(builder.Configuration.GetSection("ReservationRetention"));
builder.Services.Configure<DeviceTelemetryRetentionSettings>(builder.Configuration.GetSection("DeviceTelemetryRetention"));
builder.Services.Configure<FleetPushSettings>(builder.Configuration.GetSection("FleetPush"));
builder.Services.AddHostedService<BasketCleanupService>();
builder.Services.AddHostedService<AccountCleanupService>();
builder.Services.AddHostedService<TableReservationCleanupService>();
builder.Services.AddHostedService<ReservationRetentionService>();
builder.Services.AddHostedService<DeviceTelemetryRetentionService>();
builder.Services.AddHostedService<FleetSummaryPushService>();

// Register OrderEventService as singleton - both interface and concrete type share same instance
builder.Services.AddSingleton<ISseActivityLog, SseActivityLog>();
builder.Services.AddSingleton<ISseClientWriter, SseClientWriter>();
builder.Services.AddSingleton<ISseEventReplayService, SseEventReplayService>();
builder.Services.AddSingleton<ISseClientManager, SseClientManager>();
builder.Services.AddSingleton<ISseBroadcastService, SseBroadcastService>();
builder.Services.AddSingleton<OrderEventService>();
builder.Services.AddSingleton<IOrderEventService>(sp => sp.GetRequiredService<OrderEventService>());


var app = builder.Build();

// Resolve the module set NOW rather than on the first gated request. A lazy singleton would
// emit its "enforcement ON — enabled: …" line, and any warning about an unrecognised id,
// hours after boot or never — and that line is the only operator-visible confirmation that a
// re-provision + restart actually took effect. It belongs in the startup log where it is read.
app.Services.GetRequiredService<ITenantModules>();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapOpenApi();
app.UseSwagger(c =>
{
    c.RouteTemplate = "api/swagger/{documentName}/swagger.json";
});
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/api/swagger/v1/swagger.json", "Restaurant System API v1");
    c.RoutePrefix = "api/swagger"; // Swagger UI at /api/swagger
});

app.UseExceptionHandling();

app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseForwardedHeaders();

app.UseCors("AllowAll");

app.UseRateLimiter();

app.UseMiddleware<SessionMiddleware>();

app.UseValidationExceptionHandling();

app.UseAuthentication();
app.UseAuthorization();

// /health is mapped by app.MapDefaultEndpoints() above (Aspire ServiceDefaults
// → MapHealthChecks("/health")). Re-mapping it here would throw AmbiguousMatchException
// at runtime. Kubernetes liveness/readiness probes hit the same /health path.

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "restaurant-system-api"
}))
.WithName("ApiHealthCheck");

// Build-identity endpoints: public /api/version + admin-gated /api/diagnostics.
app.MapVersionEndpoints();

app.MapControllers();

// Run migrations in all environments
await app.Services.EnsureDatabaseCreatedAsync();
await app.Services.MigrateApplicationDatabaseAsync();

app.Run();

public partial class Program { } // Add this at the end of Program.cs
