using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    [Fact]
    public async Task Customer_erasure_preserves_referenced_removal_and_award_facts_while_scrubbing_text()
    {
        var amendment = await SeedPrivateSuppressedAmendmentAsync();
        await _service.AwardAcceptedOrderAsync(amendment.SourceOrderId);
        await using (var context = _fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new RetainedCustomerDataScrubber(context).ScrubAsync(_testUserId, CancellationToken.None);
            await context.Users.Where(user => user.Id == _testUserId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        await using var verify = _fixture.CreateContext();
        var retained = await verify.OrderAmendments.AsNoTracking().SingleAsync(value => value.Id == amendment.Id);
        var change = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(retained.ChangesJson).Single();
        Assert.Equal(amendment.SourceOrderId, retained.SourceOrderId);
        Assert.Equal(amendment.PayloadHash, retained.PayloadHash);
        Assert.Equal(amendment.CommitPayloadHash, retained.CommitPayloadHash);
        Assert.Equal(1, change.StartOrdinal);
        Assert.Equal(1, change.Quantity);
        Assert.Equal(2, change.Previous.Quantity);
        Assert.Equal(20m, change.Previous.UnitPrice);
        Assert.Equal(40m, change.Previous.ItemTotal);
        Assert.Null(change.Previous.SpecialInstructions);
        Assert.Equal("{}", retained.FinancialResolutionJson);
        Assert.DoesNotContain("private", retained.RequestJson + retained.SourceSnapshotJson
            + retained.SupplementSnapshotJson + retained.CommitResultJson);
        Assert.Null((await verify.Orders.SingleAsync(value => value.Id == amendment.SourceOrderId)).UserId);
        Assert.Null((await verify.OrderItems.SingleAsync(value => value.Id == change.OrderItemId)).SpecialInstructions);
        var witness = await verify.OrderBillingAwardWitnesses.SingleAsync(value => value.OrderId == amendment.SourceOrderId);
        Assert.Equal(80, witness.CandidatePoints);
        Assert.Equal(40, witness.AppliedPoints);
        Assert.Equal(40, witness.SuppressedPoints);
        Assert.Equal(40, (await verify.OrderBillingUnitAwardSuppressions.SingleAsync(
            value => value.OrderId == amendment.SourceOrderId)).SuppressedEarnedPoints);
    }

    [Fact]
    public async Task Referenced_text_redaction_with_matching_audit_cannot_commit_without_customer_erasure()
    {
        var amendment = await SeedPrivateSuppressedAmendmentAsync();
        await using (var context = _fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await RetainedOrderInstructionsScrubber.ScrubAsync(context, _testUserId, CancellationToken.None);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        }
        await using var verify = _fixture.CreateContext();
        Assert.Contains("private", (await verify.OrderAmendments.AsNoTracking()
            .SingleAsync(value => value.Id == amendment.Id)).RequestJson);
        Assert.True(await verify.Users.AnyAsync(user => user.Id == _testUserId));
    }

    [Fact]
    public async Task Erasure_audit_cannot_change_referenced_financial_structure()
    {
        var amendment = await SeedPrivateSuppressedAmendmentAsync();
        await using var context = _fixture.CreateContext();
        var retained = await context.OrderAmendments.SingleAsync(value => value.Id == amendment.Id);
        retained.SourceSnapshotJson = "{\"reason\":null,\"itemTotal\":41}";
        retained.UpdatedBy = "RetainedOrderInstructionsScrubber";
        retained.UpdatedAt = DateTime.UtcNow;
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(failure.GetBaseException()).SqlState);
    }

    [Fact]
    public async Task Exact_text_redaction_cannot_also_change_referenced_financial_resolution()
    {
        var amendment = await SeedPrivateSuppressedAmendmentAsync();
        await using var context = _fixture.CreateContext();
        var retained = await context.OrderAmendments.SingleAsync(value => value.Id == amendment.Id);
        retained.RequestJson = RetainedOrderPayloadRedactor.Redact(retained.RequestJson)!;
        retained.ChangesJson = RetainedOrderPayloadRedactor.Redact(retained.ChangesJson)!;
        retained.SourceSnapshotJson = RetainedOrderPayloadRedactor.Redact(retained.SourceSnapshotJson)!;
        retained.SupplementSnapshotJson = RetainedOrderPayloadRedactor.Redact(retained.SupplementSnapshotJson);
        retained.CommitResultJson = RetainedOrderPayloadRedactor.Redact(retained.CommitResultJson);
        retained.FinancialResolutionJson = "{\"potentialCreditMinor\":1}";
        retained.UpdatedBy = "RetainedOrderInstructionsScrubber";
        retained.UpdatedAt = DateTime.UtcNow;
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(failure.GetBaseException()).SqlState);
    }

    private async Task<OrderAmendment> SeedPrivateSuppressedAmendmentAsync()
    {
        var orderId = await SeedAwardOrderAsync(_testUserId, 80, 40m, quantity: 2);
        var unit = await _context.OrderBillingSnapshotUnits.AsNoTracking()
            .Where(value => value.OrderId == orderId).OrderBy(value => value.UnitOrdinal).FirstAsync();
        _context.OrderItems.Local.Single(value => value.Id == unit.OrderItemId)
            .SpecialInstructions = "private item instructions";
        var amendment = CreateCommittedVoid(orderId, Guid.NewGuid(), unit.OrderItemId, 1, 1);
        var change = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson).Single();
        change.Previous.SpecialInstructions = "private removed-unit instructions";
        amendment.ChangesJson = OrderAmendmentJson.Serialize(new[] { change });
        amendment.RequestJson = "{\"reason\":\"private request\",\"quantity\":1}";
        amendment.SourceSnapshotJson = "{\"reason\":\"private source\",\"itemTotal\":40}";
        amendment.SupplementSnapshotJson = "{\"specialInstructions\":\"private supplement\",\"itemTotal\":0}";
        amendment.CommitResultJson = "{\"reason\":\"private replay\",\"itemTotal\":40}";
        _context.OrderAmendments.Add(amendment);
        await _context.SaveChangesAsync();
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await new OrderBillingAwardSuppressionWriter(_context)
            .RecordRemovedUnitsAsync(orderId, amendment.Id, CancellationToken.None);
        await transaction.CommitAsync();
        return amendment;
    }
}
