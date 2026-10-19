using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketCancellationRollbackTests(DatabaseFixture fixture) : CustomerBasketCancellationFixture(fixture)
{
    private int _failures = 1;
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>((_, options) => options.AddInterceptors(new InterruptReceipt(this)));
    }

    [Fact]
    public async Task A_failure_after_saving_a_seal_does_not_commit_it_or_report_a_success()
    {
        var request = AddRequest();
        (await CancelAddAsync(request)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReceiptsAsync()).Should().BeEmpty();
        using (var context = DatabaseFixture.CreateContext())
            (await context.Baskets.CountAsync()).Should().Be(0);
        var recovered = await ResolutionAsync(await CancelAddAsync(request));
        recovered.Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
        recovered.OperationId.Should().Be(request.OperationId!.Value);
        (await SendAsync("/api/customer-mobile/basket/items", request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceiptsAsync()).Should().ContainSingle().Which.IsCancelled.Should().BeTrue();
    }

    private sealed class InterruptReceipt(CustomerBasketCancellationRollbackTests owner) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<CustomerBasketOperation>().Any(entry => entry.Entity.IsCancelled) == true
                && Interlocked.Exchange(ref owner._failures, 0) == 1)
                throw new IOException("Interrupted after the cancellation receipt save and before commit.");
            return ValueTask.FromResult(result);
        }
    }
}
