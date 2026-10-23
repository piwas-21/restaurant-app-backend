using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fully_refunded_native_contribution_keeps_original_hash_authority_for_staff_cancellation(bool changedHash)
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var journal = await context.AccountCheckoutJournals.SingleAsync(row => row.AttemptId == _attemptId);
            var original = AccountCheckoutReplayPayload.Frozen(journal);
            var native = new NativeAccountCheckoutScope(Guid.NewGuid(), Guid.NewGuid());
            var request = original with
            {
                NativeScope = native,
                ReturnBaseUrl = native.ReturnBase("https://links.example.test", "fixture", _attemptId)
            };
            context.Set<CustomerTablePaymentOperation>().Add(new()
            {
                OperationId = native.OperationId,
                AttemptId = _attemptId,
                OwnerHash = new string('a', 64),
                PayloadHash = new string('b', 64),
                CompletedAt = journal.StartedAt
            });
            journal.NativeOperationId = native.OperationId; journal.NativeTenantId = native.TenantId;
            journal.ReturnBaseUrl = request.ReturnBaseUrl;
            journal.CreatePayloadHash = changedHash ? original.IdempotencyKey : AccountCheckoutReplayPayload.Hash(request);
            await context.SaveChangesAsync();
        }
        var before = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        before.Reversals.Sum(row => row.AmountMinor).Should().Be(2000);
        using var response = await CancelAsync("cancel", before.OrderVersion);
        response.StatusCode.Should().Be(changedHash ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        var after = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        after.OrderStatus.Should().Be(changedHash ? before.OrderStatus : OrderStatus.Cancelled);
        after.Should().BeEquivalentTo(before,
            options => options.Excluding(row => row.OrderStatus).Excluding(row => row.OrderVersion));
        _refundState.CreateCalls.Should().Be(1, "staff cancellation cannot repeat a provider refund");
    }
}
