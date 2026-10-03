using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed partial class AccountCheckoutJournalTests
{
    [Fact]
    public async Task One_provider_charge_posts_its_exact_shared_order_slices_once()
    {
        var journal = await VerifyCapture(sharedScope: true);
        await using (var context = DatabaseFixture.CreateContext())
            await Poster(context).PostAsync(journal, CancellationToken.None);
        await using (var context = DatabaseFixture.CreateContext())
            await Poster(context).PostAsync(journal, CancellationToken.None);

        await using var readback = DatabaseFixture.CreateContext();
        var attempt = await readback.AccountPaymentAttempts
            .Include(value => value.Allocations).ThenInclude(value => value.OrderPayment)
            .SingleAsync(value => value.Id == journal.AttemptId);
        var payments = await readback.OrderPayments
            .Where(value => value.PaymentGateway == "Stripe" && value.TransactionId == "ch_posting")
            .ToListAsync();
        var allocations = attempt.Allocations;
        var savedJournal = await readback.AccountCheckoutJournals.SingleAsync(
            value => value.AttemptId == attempt.Id);

        attempt.State.Should().Be(AccountPaymentState.Captured);
        attempt.AmountMinor.Should().Be(334);
        attempt.ProviderChargeId.Should().Be("ch_posting");
        savedJournal.ProviderCapturedMinor.Should().Be(334);
        savedJournal.ProviderRefundedMinor.Should().Be(0);
        payments.Should().HaveCount(2);
        payments.Select(value => value.Amount).OrderBy(value => value)
            .Should().Equal(1.34m, 2.00m);
        payments.Sum(value => decimal.ToInt64(value.Amount * 100m)).Should().Be(334);
        payments.Should().OnlyContain(value => value.TransactionId == "ch_posting");

        allocations.Should().HaveCount(2);
        allocations.Select(value => value.OrderId).Distinct().Should().HaveCount(2);
        allocations.Select(value => value.OrderPaymentId).Distinct().Should().HaveCount(2);
        foreach (var allocation in allocations)
        {
            allocation.OrderPayment.Should().NotBeNull();
            allocation.OrderPayment!.OrderId.Should().Be(allocation.OrderId);
            allocation.AmountMinor.Should().Be(checked(allocation.MinorPerUnit * allocation.UnitCount));
            decimal.ToInt64(allocation.OrderPayment.Amount * 100m).Should().Be(allocation.AmountMinor);
        }

        var orders = await readback.Orders.Where(value => value.ServiceSessionId == attempt.ServiceSessionId)
            .ToListAsync();
        orders.Should().HaveCount(2);
        orders.Sum(value => value.TotalPaid).Should().Be(3.34m);
        orders.Sum(value => value.RemainingAmount).Should().Be(0m);
        (await new AccountDebtSnapshotReader(readback)
            .ReadAsync(attempt.ServiceSessionId, CancellationToken.None))
            .Debt.OutstandingMinor.Should().Be(0);
    }
}
