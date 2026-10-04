using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class AccountCashRefundIntegrationTests
{
    private async Task AssertSavedCashReturnIntegrityAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var operation = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .SingleAsync(value => value.SourceOrderId == _cases[0].OrderId);
        var saved = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionResultDto>(operation.ResultJson!);
        var leg = saved.RefundLegs.Single();
        var returned = leg.CashReturn!;
        var invalidReturns = new CashReturnEvidenceDto?[]
        {
            returned with { ConfirmedAt = returned.ConfirmedAt.AddTicks(10) },
            returned with { CashReturnedMinor = returned.CashReturnedMinor + 1 },
            returned with { ExactRefundAmountMinor = returned.ExactRefundAmountMinor + 1 },
            returned with { RefundAdjustmentMinor = returned.RefundAdjustmentMinor + 1 },
            null
        };
        foreach (var invalid in invalidReturns)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var corrupted = saved with { RefundLegs = [leg with { CashReturn = invalid }] };
            var json = OrderAmendmentJson.Serialize(corrupted);
            (await context.OrderAmendmentResolutionOperations.Where(value => value.Id == operation.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.ResultJson, json)))
                .Should().Be(1);
            var read = () => AccountCashRefundHistoryReader.ReadAsync(context, [_attemptId], CancellationToken.None);
            await read.Should().ThrowAsync<ConflictException>().WithMessage("*inconsistent refund history*");
            await transaction.RollbackAsync();
        }
        var unchanged = await AccountCashRefundHistoryReader.ReadAsync(context, [_attemptId], CancellationToken.None);
        unchanged[_attemptId].RefundedExactMinor.Should().Be(333);
        unchanged[_attemptId].RefundedCashMinor.Should().Be(335);
    }
}
