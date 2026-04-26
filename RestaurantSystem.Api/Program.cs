using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
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
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

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

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

//builder.Services.AddStackExchangeRedisCache(options =>
//{
//    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
//    options.InstanceName = "RestaurantSystem";
//});

builder.Services.AddDistributedMemoryCache();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        dataSource,
        npgsqlOptions => npgsqlOptions
            .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name)
            .CommandTimeout(30)
    ));

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

var secret = jwtSettings["Secret"];
var key = Encoding.UTF8.GetBytes(secret!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = jwtOptions?.TokenValidationParameters ?? new TokenValidationParameters();

    options.Events = new JwtBearerEvents
    {
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

builder.Services.AddFileStorage(builder.Configuration);
builder.Services.AddAuthorization();

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
builder.Services.AddScoped<IBasketMergeService, BasketMergeService>();
builder.Services.AddScoped<IOrderMappingService, OrderMappingService>();
builder.Services.AddScoped<IPointEarningRuleService, PointEarningRuleService>();
builder.Services.AddScoped<IFidelityPointsService, FidelityPointsService>();
builder.Services.AddScoped<ICustomerDiscountService, CustomerDiscountService>();
builder.Services.AddScoped<ITaxConfigurationService, TaxConfigurationService>();
// Settings Services
builder.Services.AddScoped<IOrderTypeConfigurationService, OrderTypeConfigurationService>();
builder.Services.AddScoped<IWorkingHoursService, WorkingHoursService>();

builder.Services.AddScoped<IQRCodeService, QRCodeService>();
builder.Services.AddScoped<IUserGroupService, UserGroupService>();
builder.Services.AddScoped<LoginEventHandler>();
// Register background services
builder.Services.AddHostedService<BasketCleanupService>();
builder.Services.AddHostedService<AccountCleanupService>();
builder.Services.AddHostedService<TableReservationCleanupService>();

// Register OrderEventService as singleton - both interface and concrete type share same instance
builder.Services.AddSingleton<OrderEventService>();
builder.Services.AddSingleton<IOrderEventService>(sp => sp.GetRequiredService<OrderEventService>());


var app = builder.Build();

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

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseValidationExceptionHandling();

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint for Kubernetes liveness/readiness probes
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "restaurant-system-api"
}))
.WithName("HealthCheck")
.WithOpenApi();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "restaurant-system-api"
}))
.WithName("ApiHealthCheck")
.WithOpenApi();

app.MapControllers();

// Run migrations in all environments
await app.Services.EnsureDatabaseCreatedAsync();
await app.Services.MigrateApplicationDatabaseAsync();

app.Run();

public partial class Program { } // Add this at the end of Program.cs
