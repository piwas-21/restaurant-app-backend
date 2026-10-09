using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class ZReportTableAccountTipReaderTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public ZReportTableAccountTipReaderTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Captured_table_and_account_tips_are_grouped_by_currency_and_method_in_the_capture_window()
    {
        var start = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(8);
        var capturedAt = start.AddHours(1);
        var sessionId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            seed.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 61,
                Currency = "CHF",
                Status = TableServiceSessionStatus.Open,
                Version = 1,
                AccountRevision = 1,
                OpenedAt = capturedAt,
                CreatedAt = capturedAt,
                CreatedBy = nameof(ZReportTableAccountTipReaderTests),
            });
            seed.TableBillPaymentOperations.AddRange(
                TableOperation(62, PaymentMethod.Cash, "chf", 125, capturedAt),
                TableOperation(63, PaymentMethod.Cash, "EUR", 75, capturedAt.AddMinutes(1)),
                TableOperation(64, PaymentMethod.CreditCard, "CHF", 500, end));
            seed.AccountPaymentAttempts.AddRange(
                AccountAttempt(sessionId, AccountPaymentState.Captured, PaymentMethod.Cash,
                    "CHF", 50, capturedAt.AddMinutes(2)),
                AccountAttempt(sessionId, AccountPaymentState.Reserved, PaymentMethod.Cash,
                    "CHF", 700, capturedAt.AddMinutes(3)));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var totals = await ZReportTableAccountTipReader.ReadAsync(context, start, end, CancellationToken.None);

        totals.Should().HaveCount(2);
        totals.Should().ContainSingle(value => value.Currency == "CHF"
            && value.PaymentMethod == PaymentMethod.Cash && value.TipMinor == 175);
        totals.Should().ContainSingle(value => value.Currency == "EUR"
            && value.PaymentMethod == PaymentMethod.Cash && value.TipMinor == 75);
        totals.Should().NotContain(value => value.PaymentMethod == PaymentMethod.CreditCard,
            "the report window is half-open and captured account state is required");
    }

    [Fact]
    public async Task Missing_or_invalid_currency_is_preserved_as_unknown()
    {
        var start = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc);
        var capturedAt = start.AddHours(1);
        var sessionId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            seed.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 65,
                Currency = "CHF",
                Status = TableServiceSessionStatus.Open,
                Version = 1,
                AccountRevision = 1,
                OpenedAt = capturedAt,
                CreatedAt = capturedAt,
                CreatedBy = nameof(ZReportTableAccountTipReaderTests),
            });
            seed.TableBillPaymentOperations.AddRange(
                TableOperation(66, PaymentMethod.Cash, null, 125, capturedAt),
                TableOperation(67, PaymentMethod.CreditCard, "???", 75, capturedAt));
            seed.AccountPaymentAttempts.Add(AccountAttempt(sessionId, AccountPaymentState.Captured,
                PaymentMethod.Cash, "???", 50, capturedAt.AddMinutes(1)));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var totals = await ZReportTableAccountTipReader.ReadAsync(
            context, start, start.AddHours(8), CancellationToken.None);

        totals.Should().ContainSingle(value => value.Currency == null
            && value.PaymentMethod == PaymentMethod.Cash && value.TipMinor == 175);
        totals.Should().ContainSingle(value => value.Currency == null
            && value.PaymentMethod == PaymentMethod.CreditCard && value.TipMinor == 75);
    }

    private static TableBillPaymentOperation TableOperation(
        int tableNumber, PaymentMethod method, string? currency, long tipMinor, DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            TableNumber = tableNumber,
            PaymentMethod = method,
            Amount = 10m,
            Currency = currency,
            TipMinor = tipMinor,
            CreatedAt = createdAt,
            CreatedBy = nameof(ZReportTableAccountTipReaderTests),
        };

    private static AccountPaymentAttempt AccountAttempt(
        Guid sessionId, AccountPaymentState state, PaymentMethod method, string currency,
        long tipMinor, DateTime completedAt) => new()
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Full,
            State = state,
            PaymentMethod = method,
            Version = state == AccountPaymentState.Captured ? 2 : 1,
            ExpectedAccountRevision = 1,
            AmountMinor = 1000,
            TipMinor = tipMinor,
            Currency = currency,
            PayloadHash = new string('a', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = completedAt.AddHours(1),
            CompletedAt = state == AccountPaymentState.Captured ? completedAt : null,
            CreatedAt = completedAt,
            CreatedBy = nameof(ZReportTableAccountTipReaderTests),
        };
}
