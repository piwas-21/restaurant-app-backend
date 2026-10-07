using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.User;

[Collection("Database Lane 3")]
public sealed class RetainedOrderInstructionsScrubberTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Erasure_removes_owned_instructions_and_withdraws_jobs_without_losing_financial_identity(bool hidden, bool previouslyWithdrawn)
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        await TestUserSeeder.SeedUserAsync(context, userId);
        await TestUserSeeder.SeedUserAsync(context, otherUserId);
        var owned = NewOrder(userId);
        owned.IsDeleted = hidden;
        var other = NewOrder(otherUserId);
        context.Orders.AddRange(owned, other);
        await context.SaveChangesAsync();
        var quote = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = owned.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRole = "Admin",
            PayloadHash = new string('a', 64),
            ExpectedOrderVersion = owned.Version,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            State = OrderAmendmentState.Quoted,
            RequestJson = "{\"reason\":\"Private amendment reason\",\"quantity\":2}",
            ChangesJson = "[{\"quantity\":2,\"specialInstructions\":\"Private frozen instruction\"}]",
            SourceSnapshotJson = "{\"total\":12.35,\"items\":[{\"specialInstructions\":\"Private source instruction\"}]}",
            SupplementSnapshotJson = "{\"pricingFingerprint\":\"original-proof\",\"specialInstructions\":\"Private addition instruction\"}",
            FinancialResolutionJson = "{\"potentialCreditMinor\":1235}",
            CreatedBy = "instruction-test"
        };
        var note = new OrderOperationalNote
        {
            Id = Guid.NewGuid(),
            OrderId = owned.Id,
            Text = "Private kitchen note",
            WithdrawnAt = previouslyWithdrawn ? DateTime.UtcNow.AddSeconds(-30) : null,
            Audience = OrderNoteAudience.Kitchen,
            ClientOperationId = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc),
            CreatedBy = "instruction-test"
        };
        var otherNote = new OrderOperationalNote
        {
            Id = Guid.NewGuid(),
            OrderId = other.Id,
            Text = "Other customer's kitchen note",
            Audience = OrderNoteAudience.Kitchen,
            ClientOperationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "instruction-test"
        };
        context.OrderAmendments.Add(quote);
        context.OrderOperationalNotes.AddRange(note, otherNote);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var originalFinancialJson = await context.OrderAmendments
            .Where(value => value.Id == quote.Id).Select(value => value.FinancialResolutionJson).SingleAsync();
        var feed = new PrinterFeedUpdatesQueryHandler(context,
            Microsoft.Extensions.Options.Options.Create(new RestaurantSystem.Api.Settings.PrinterFeedSettings()));
        var priorFeed = await feed.Handle(new PrinterFeedUpdatesQuery(null), CancellationToken.None);
        if (previouslyWithdrawn)
            Assert.True(priorFeed.Items.Single(value => value.JobId == note.Id).IsWithdrawn);
        var before = DateTime.UtcNow;
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await RetainedOrderInstructionsScrubber.ScrubAsync(context, userId, CancellationToken.None);
            await transaction.CommitAsync();
        }

        await using var verify = DatabaseFixture.CreateContext();
        var retained = await verify.OrderAmendments.SingleAsync(value => value.Id == quote.Id);
        Assert.Equal(quote.PayloadHash, retained.PayloadHash);
        Assert.Equal(originalFinancialJson, retained.FinancialResolutionJson);
        Assert.DoesNotContain("Private", retained.RequestJson + retained.ChangesJson
            + retained.SourceSnapshotJson + retained.SupplementSnapshotJson);
        Assert.Contains("12.35", retained.SourceSnapshotJson);
        Assert.Contains("original-proof", retained.SupplementSnapshotJson);
        Assert.InRange(retained.ExpiresAt, before, DateTime.UtcNow);
        var withdrawn = await verify.OrderOperationalNotes.SingleAsync(value => value.Id == note.Id);
        Assert.Equal("[erased]", withdrawn.Text);
        Assert.NotNull(withdrawn.WithdrawnAt);
        Assert.InRange(withdrawn.WithdrawnAt.Value, before, DateTime.UtcNow);
        var freshFeed = await new PrinterFeedUpdatesQueryHandler(verify,
            Microsoft.Extensions.Options.Options.Create(new RestaurantSystem.Api.Settings.PrinterFeedSettings()))
            .Handle(new PrinterFeedUpdatesQuery(null, priorFeed.NextUpdateCursor), CancellationToken.None);
        Assert.Equal(note.Id, Assert.Single(freshFeed.Items).JobId);
        Assert.Equal(note.ClientOperationId, withdrawn.ClientOperationId);
        Assert.Equal(note.CreatedAt, withdrawn.CreatedAt);
        Assert.Null((await verify.OrderItems.SingleAsync(value => value.OrderId == owned.Id)).SpecialInstructions);
        Assert.Equal("Private canonical instruction",
            (await verify.OrderItems.SingleAsync(value => value.OrderId == other.Id)).SpecialInstructions);
        var untouched = await verify.OrderOperationalNotes.SingleAsync(value => value.Id == otherNote.Id);
        Assert.Equal(otherNote.Text, untouched.Text);
        Assert.Null(untouched.WithdrawnAt);
    }

    private static Order NewOrder(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrderNumber = $"ERASE-{Guid.NewGuid():N}"[..20],
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        Total = 12.35m,
        RemainingAmount = 12.35m,
        CreatedAt = DateTime.UtcNow,
        OrderDate = DateTime.UtcNow,
        CreatedBy = "instruction-test",
        Items = [new OrderItem
        {
            Id = Guid.NewGuid(), ProductName = "Retained dish", Quantity = 1, UnitPrice = 12.35m,
            ItemTotal = 12.35m, SpecialInstructions = "Private canonical instruction", CreatedBy = "instruction-test"
        }]
    };
}
