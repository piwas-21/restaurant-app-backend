using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Only original-proof callers receive a minimal current payment decision; browser returns grant nothing.</summary>
public sealed class CustomerOrderCheckoutPaymentService(ApplicationDbContext context,
    ICustomerOrderCheckoutRecovery recovery, ICustomerOrderCheckoutOwnerScope owners,
    ICustomerOrderCheckoutCreationPolicy creation, IOptions<CustomerHostedCheckoutSettings> settings)
    : ICustomerOrderCheckoutPaymentService
{
    private const int MaximumHostedUrlLength = 8192;

    public async Task<CustomerOrderPaymentDto> RecoverAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken)
    {
        var evidence = await recovery.RecoverAsync(sessionId, proof, cancellationToken);
        // Recheck ownership after post-commit effects; erasure/account changes cannot expose the
        // private decision through a snapshot authorized earlier in the request.
        return await owners.RunAsync(sessionId, proof, async (journal, order, now) =>
        {
            var row = await context.OrderCheckoutSessions.AsNoTracking().SingleOrDefaultAsync(value => value.OrderId == order.Id,
                cancellationToken);
            var outcome = Outcome(row, journal, evidence);
            string? url = null;
            if (outcome == "pending" && row is null && evidence.IsOpen && evidence.PaymentStatus == "unpaid"
                && now < journal.ExpiresAt && TrustedPage(evidence.Url))
            {
                try
                {
                    var charge = await creation.ReadAsync(order, cancellationToken);
                    if (charge.Amount.Minor == journal.AmountMinor && charge.Amount.Currency == journal.Currency)
                    {
                        outcome = "ready";
                        url = evidence.Url;
                    }
                }
                catch (Exception error) when (error is NotFoundException or BadRequestException or ConflictException)
                {
                    // The request remains original and unresolved; withdrawal cannot become a retry offer.
                }
            }
            return new CustomerOrderPaymentDto(1, journal.OperationId, journal.OrderId, outcome, url, journal.ExpiresAt);
        }, cancellationToken);
    }

    private static string Outcome(OrderCheckoutSession? row, CustomerOrderCheckoutJournal journal, StripeCheckoutSession evidence)
    {
        if (row is null) return "pending";
        if (row.SessionId != evidence.Id || row.Currency != journal.Currency || row.AmountMinor != journal.AmountMinor
            || row.ConnectedAccountId != journal.ProviderAccountId || row.IdempotencyKey != journal.CreateIdempotencyKey
            || row.ReconciledAt is null) throw new ConflictException("The original payment needs financial reconciliation.");
        if (row.Status == CheckoutSessionStatus.Completed && evidence.IsComplete && evidence.IsPaid
            && evidence.NativeIntentEvidence?.IsSucceeded == true) return "paid";
        if (row.Status == CheckoutSessionStatus.Expired && evidence.IsExpired && evidence.PaymentStatus == "unpaid") return "expired";
        if (row.Status == CheckoutSessionStatus.Failed && evidence.IsComplete && evidence.PaymentStatus == "unpaid"
            && evidence.NativeIntentEvidence?.HasFailed == true) return "failed";
        return "pending";
    }

    private bool TrustedPage(string? url) => url is { Length: > 0 and <= MaximumHostedUrlLength }
        && !url.Any(char.IsControl) && !url.Contains('\\') && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && settings.Value.HostedPageOrigins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal);
}
