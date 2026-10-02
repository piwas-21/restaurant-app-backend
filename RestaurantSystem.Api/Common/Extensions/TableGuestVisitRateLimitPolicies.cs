using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class TableGuestVisitRateLimitPolicies
{
    public const string AccountPolicyName = "table-guest-account";
    public const string RoundPolicyName = "table-guest-round";

    public static RateLimiterOptions AddTableGuestVisitCredentialPolicies(this RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(AccountPolicyName, context => CreateParticipantLimiter(context,
            context.RequestServices.GetRequiredService<IOptions<TableGuestVisitSettings>>()
                .Value.AccountReadsPerMinute));
        options.AddPolicy(RoundPolicyName, context => CreateParticipantLimiter(context,
            context.RequestServices.GetRequiredService<IOptions<TableGuestVisitSettings>>()
                .Value.RoundAttemptsPerMinute));
        var coarseLimiter = PartitionedRateLimiter.Create<HttpContext, string>(CreateCoarsePartition);
        options.GlobalLimiter = options.GlobalLimiter is null
            ? coarseLimiter
            : PartitionedRateLimiter.CreateChained(options.GlobalLimiter, coarseLimiter);
        return options;
    }

    private static RateLimitPartition<string> CreateCoarsePartition(HttpContext context)
    {
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy is not (AccountPolicyName or RoundPolicyName))
            return RateLimitPartition.GetNoLimiter("other-endpoints");

        var settings = context.RequestServices.GetRequiredService<IOptions<TableGuestVisitSettings>>().Value;
        var permits = policy == AccountPolicyName
            ? settings.AccountReadsPerIpPerMinute
            : settings.RoundAttemptsPerIpPerMinute;
        var key = $"{policy}:ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        // Global acquisition runs before the endpoint token bucket, bounding random canonical
        // token partitions even though token syntax is not proof of participant authorization.
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => FixedWindow(permits));
    }

    internal static string ResolvePartitionKey(HttpContext context)
    {
        var token = context.Request.Headers["X-Table-Participant"].ToString();
        if (TableGuestCredentialCrypto.TryHashParticipantToken(token, out var digest))
        {
            return $"participant:{digest}";
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static RateLimitPartition<string> CreateParticipantLimiter(
        HttpContext context, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolvePartitionKey(context),
            _ => FixedWindow(permitsPerMinute));

    private static FixedWindowRateLimiterOptions FixedWindow(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };
}
