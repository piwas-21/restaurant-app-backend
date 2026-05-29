using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using System.Threading.RateLimiting;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Services;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Middleware;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Auth.Handlers;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Api.Features.Settings.Services;
using RestaurantSystem.Api.Features.Groups.Interfaces;
using RestaurantSystem.Api.Features.Groups.Services;
using RestaurantSystem.Api.Settings;
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

builder.Services.Configure<PrinterSettings>(builder.Configuration.GetSection("PrinterSettings"));

builder.Services.AddFileStorage(builder.Configuration);
builder.Services.AddAuthorization();

// Trust the K8s nginx-ingress X-Forwarded-For header so rate limiter partitions by real client IP
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

    // /api/Auth/login + refresh-token
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimiter.AuthPermitLimit,
            Window = TimeSpan.FromMinutes(rateLimiter.AuthWindowMinutes),
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
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IBasketService, BasketService>();
builder.Services.AddScoped<IBasketPricingService, BasketPricingService>();
builder.Services.AddScoped<IBasketMappingService, BasketMappingService>();
builder.Services.AddScoped<IBasketItemFactory, BasketItemFactory>();
builder.Services.AddScoped<IBasketRepository, BasketRepository>();
builder.Services.AddScoped<IAnonymousBasketMerger, AnonymousBasketMerger>();
builder.Services.AddScoped<IBasketMergeService, BasketMergeService>();
builder.Services.AddScoped<IOrderMappingService, OrderMappingService>();
builder.Services.AddScoped<IOrderAddressFactory, OrderAddressFactory>();
builder.Services.AddScoped<IOrderItemFactory, OrderItemFactory>();
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

builder.Services.AddScoped<IQRCodeService, QRCodeService>();
builder.Services.AddScoped<IUserGroupService, UserGroupService>();
// HTML page builder for email-link landing endpoints (Sprint 2 task 2.1).
// Pure string composition — singleton lifetime is appropriate.
builder.Services.AddSingleton<IHtmlResponseBuilder, HtmlResponseBuilder>();
builder.Services.AddScoped<LoginEventHandler>();
// Register background services
builder.Services.AddHostedService<BasketCleanupService>();
builder.Services.AddHostedService<AccountCleanupService>();
builder.Services.AddHostedService<TableReservationCleanupService>();

// Register OrderEventService as singleton - both interface and concrete type share same instance
builder.Services.AddSingleton<ISseActivityLog, SseActivityLog>();
builder.Services.AddSingleton<ISseClientWriter, SseClientWriter>();
builder.Services.AddSingleton<ISseEventReplayService, SseEventReplayService>();
builder.Services.AddSingleton<OrderEventService>();
builder.Services.AddSingleton<IOrderEventService>(sp => sp.GetRequiredService<OrderEventService>());


var app = builder.Build();

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

app.MapControllers();

// Run migrations in all environments
await app.Services.EnsureDatabaseCreatedAsync();
await app.Services.MigrateApplicationDatabaseAsync();

app.Run();

public partial class Program { } // Add this at the end of Program.cs
