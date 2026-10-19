using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketChannelRollbackTests(DatabaseFixture fixture) : CustomerBasketChannelFixture(fixture)
{
    private Guid _operationId;
    private int _failures = 1;
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>((_, options) => options.AddInterceptors(new InterruptReceipt(this)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_interrupted_receipt_rolls_back_channel_creation_or_confirmed_removals(bool existing)
    {
        var basket = existing ? await AddAsync() : null;
        if (existing) await MaskAsync((int)OrderChannels.DineIn);
        var request = Channel(basket, removals: basket is null ? [] : [basket.Items[0].Id!.Value]);
        _operationId = request.OperationId;
        (await SwitchAsync(request)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReceiptsAsync()).Should().BeEmpty();
        var current = await ReadAsync();
        current.OrderType.Should().BeNull();
        if (existing)
        {
            current.Id.Should().Be(basket!.Id);
            current.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
            current.SubTotal.Should().Be(14m);
        }
        else
        {
            current.Id.Should().BeEmpty();
            using var scope = Factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync()).Should().Be(0);
        }
        var recovered = await BasketAsync(await SwitchAsync(request));
        recovered.OrderType.Should().Be(OrderType.Takeaway);
        recovered.Items.Should().BeEmpty();
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    private sealed class InterruptReceipt(CustomerBasketChannelRollbackTests owner) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<CustomerBasketOperation>()
                .Any(entry => entry.Entity.OperationId == owner._operationId) == true
                && Interlocked.Exchange(ref owner._failures, 0) == 1)
                throw new IOException("Interrupted after channel receipt persistence and before commit.");
            return ValueTask.FromResult(result);
        }
    }
}
