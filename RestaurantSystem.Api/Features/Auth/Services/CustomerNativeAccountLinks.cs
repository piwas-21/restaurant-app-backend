using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Auth.Services;

/// <summary>Purpose- and tenant-bound email actions. Possession never signs a customer in.</summary>
public sealed class CustomerNativeAccountLinks(IDataProtectionProvider protection, TimeProvider time,
    IOptions<CustomerMobileSettings> mobile, IOptions<CustomerNativeAccountSettings> settings,
    CustomMediator mediator, ApplicationDbContext context) : ICustomerNativeAccountLinks
{
    public const int MaximumProofLength = 4096;
    public const string Verify = "verify";
    public const string Recover = "recover";

    public async Task RequireEnabledAsync(int schemaVersion, CancellationToken cancellationToken)
    {
        if (schemaVersion != 1) throw Invalid();
        var bootstrap = (await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken)).Data;
        if (bootstrap?.Flags.GetValueOrDefault("nativeAccountLinksV1") != true)
            throw new NotFoundException("Native account links are unavailable.");
    }

    public string Create(ApplicationUser user, string token, string action)
    {
        var issued = time.GetUtcNow().UtcTicks;
        var payload = new CustomerNativeAccountProof(1, mobile.Value.TenantId, user.Id,
            user.Email ?? string.Empty, token, issued,
            issued + TimeSpan.FromMinutes(settings.Value.LinkLifetimeMinutes).Ticks);
        RequirePayload(payload);
        var proof = Protector(action).Protect(JsonSerializer.Serialize(payload));
        if (!ValidProof(proof)) throw Invalid();
        // Fragments never reach the shared service's HTTP request/access log or Referer header.
        return $"{settings.Value.LinkOrigin}/r/{settings.Value.TenantSlug}/{action}#proof={proof}";
    }

    public CustomerNativeAccountProof Read(string proof, string action)
    {
        if (!ValidProof(proof)) throw Invalid();
        CustomerNativeAccountProof? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CustomerNativeAccountProof>(Protector(action).Unprotect(proof));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            throw Invalid();
        }
        if (payload is null) throw Invalid();
        RequirePayload(payload);
        return payload;
    }

    public async Task<string> RequireCurrentEmailAsync(CustomerNativeAccountProof proof, CancellationToken cancellationToken)
    {
        // A deleted/reassigned email or a changed database role cannot borrow the original grant.
        var email = await context.Users.AsNoTracking().Where(user => user.Id == proof.UserId
            && !user.IsDeleted && user.Role == UserRole.Customer).Select(user => user.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.Equals(email, proof.Email, StringComparison.Ordinal)) throw Invalid();
        return proof.Email;
    }

    private IDataProtector Protector(string action)
    {
        if (action is not (Verify or Recover) || !settings.Value.Enabled || !settings.Value.IsValid()
            || !mobile.Value.Enabled || !mobile.Value.IsValid() || mobile.Value.TenantId == Guid.Empty)
            throw Invalid();
        return protection.CreateProtector("RestaurantSystem.CustomerNativeAccountLinks.v1",
            mobile.Value.TenantId.ToString("D"), action);
    }

    private void RequirePayload(CustomerNativeAccountProof payload)
    {
        var now = time.GetUtcNow().UtcTicks;
        if (payload.SchemaVersion != 1 || payload.TenantId != mobile.Value.TenantId || payload.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(payload.Email) || payload.Email.Length > 256
            || string.IsNullOrWhiteSpace(payload.Token) || payload.Token.Length > 2048
            || payload.IssuedAtTicks <= 0 || payload.IssuedAtTicks > now || payload.ExpiresAtTicks <= now
            || payload.ExpiresAtTicks <= payload.IssuedAtTicks
            || payload.ExpiresAtTicks - payload.IssuedAtTicks > TimeSpan.FromMinutes(settings.Value.LinkLifetimeMinutes).Ticks)
            throw Invalid();
    }

    public static bool ValidProof(string? value) => value is { Length: >= 96 and <= MaximumProofLength }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static BadRequestException Invalid() => new("This account link is invalid or has expired.");
}
