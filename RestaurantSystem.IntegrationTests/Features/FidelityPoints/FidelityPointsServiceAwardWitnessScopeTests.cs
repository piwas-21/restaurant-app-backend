using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public partial class FidelityPointsServiceTests
{
    [Fact]
    public async Task AwardWitnessInsert_RequiresLinkedOwnerAndEvaluatedCandidate()
    {
        var linkedOrderId = await SeedAwardOrderAsync(_testUserId, 80, 40m);
        _context.OrderBillingAwardWitnesses.Add(new OrderBillingAwardWitness
        {
            Id = Guid.NewGuid(),
            OrderId = linkedOrderId,
            OwnerLinkId = Guid.NewGuid(),
            Outcome = OrderBillingAwardOutcome.Awarded,
            CandidatePoints = 80,
            AppliedPoints = 80,
            SuppressedPoints = 0,
            EarnedTransactionId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "OrderBillingAwardBoundary"
        });
        var invalidOwner = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(invalidOwner.GetBaseException()).SqlState);
        _context.ChangeTracker.Clear();

        var unevaluatedOrderId = await SeedAwardOrderAsync(_testUserId, null, 40m, evaluated: false);
        _context.OrderBillingAwardWitnesses.Add(new OrderBillingAwardWitness
        {
            Id = Guid.NewGuid(),
            OrderId = unevaluatedOrderId,
            OwnerLinkId = Guid.NewGuid(),
            Outcome = OrderBillingAwardOutcome.EvaluatedZero,
            CandidatePoints = 0,
            AppliedPoints = 0,
            SuppressedPoints = 0,
            EarnedTransactionId = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "OrderBillingAwardBoundary"
        });
        var unevaluated = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(unevaluated.GetBaseException()).SqlState);
    }
}
