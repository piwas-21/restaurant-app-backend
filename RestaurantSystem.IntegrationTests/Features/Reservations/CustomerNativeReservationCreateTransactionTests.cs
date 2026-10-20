using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCreateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Booking_and_receipt_commit_together_before_mail_and_provider_failure_does_not_change_acceptance(bool failMail)
    {
        var observed = false;
        _onMail = async row =>
        {
            await using var observer = DatabaseFixture.CreateContext();
            observed = await observer.Reservations.AnyAsync(value => value.Id == row.Id)
                && await observer.Set<CustomerReservationOperation>().AnyAsync(value => value.ReservationId == row.Id);
        };
        if (failMail)
            _email.Setup(value => value.SendReservationConfirmationEmailAsync(It.IsAny<CultureInfo>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReservationMailDetails>()))
                .ThrowsAsync(new IOException("Injected mail provider failure after booking acceptance."));
        var request = Request(); var first = await ResolveAsync(request);
        Assert.True(observed);
        Assert.Equal("Applied", first.GetProperty("outcome").GetString());
        Assert.Equal(first.GetRawText(), (await ResolveAsync(request)).GetRawText());
        Assert.Equal(1, _mailCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_create_attempts_cannot_duplicate_a_booking_or_claim_the_same_tables(bool sameOperation)
    {
        var first = Request(); var second = sameOperation ? first : Request();
        await PrepareAsync(first);
        if (!sameOperation) await PrepareAsync(second);
        var responses = await Task.WhenAll(SendAsync(first), SendAsync(second));
        Assert.Equal(sameOperation ? 2 : 1, responses.Count(value => value.StatusCode == HttpStatusCode.OK));
        Assert.Equal(sameOperation ? 0 : 1, responses.Count(value => value.StatusCode == HttpStatusCode.Conflict));
        await using var db = DatabaseFixture.CreateContext();
        Assert.Single(await db.Reservations.ToListAsync()); Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(1, _mailCalls);
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Failure_after_insert_rolls_back_both_graphs_clears_tracking_and_allows_the_original_attempt()
    {
        using var scope = Factory.Services.CreateScope();
        await using var failedDb = DatabaseFixture.CreateContext(new FailNativeSave());
        var failed = Service(scope.ServiceProvider, failedDb);
        var input = Request(); var proof = await PrepareAsync(input);
        var request = input with { PayloadHash = proof.PayloadHash, OwnerProof = proof.OwnerProof };
        await Assert.ThrowsAsync<IOException>(() => failed.CreateAsync(_session, request, default));
        Assert.Empty(failedDb.ChangeTracker.Entries());
        Assert.Equal(0, await failedDb.SaveChangesAsync());
        await using var observer = DatabaseFixture.CreateContext();
        Assert.Empty(await observer.Reservations.ToListAsync()); Assert.Empty(await observer.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
        Assert.Equal("Applied", (await ResolveAsync(request)).GetProperty("outcome").GetString());
        Assert.Single(await observer.Reservations.ToListAsync()); Assert.Single(await observer.Set<CustomerReservationOperation>().ToListAsync());
    }

    private static CustomerReservationOperationService Service(IServiceProvider services, ApplicationDbContext db)
    {
        var guest = new Mock<ICurrentUserService>();
        var guard = ActivatorUtilities.CreateInstance<CustomerReservationOperationGuard>(services, db, guest.Object);
        var writer = ActivatorUtilities.CreateInstance<ReservationDraftWriter>(services, db);
        var creator = ActivatorUtilities.CreateInstance<CustomerReservationCreator>(services, writer, guard);
        return ActivatorUtilities.CreateInstance<CustomerReservationOperationService>(services, db, guard, creator);
    }
    private sealed class FailNativeSave : SaveChangesInterceptor
    {
        private bool _failed;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_failed) return ValueTask.FromResult(result);
            _failed = true;
            throw new IOException("Injected booking/receipt insert failure.");
        }
    }
}
