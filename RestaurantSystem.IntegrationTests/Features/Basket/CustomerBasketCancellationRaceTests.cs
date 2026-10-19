using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketCancellationRaceTests(DatabaseFixture fixture) : CustomerBasketCancellationFixture(fixture)
{
    private readonly TaskCompletionSource _saving = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _addEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancelEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private int _pause;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>((_, options) => options.AddInterceptors(new ReceiptPause(this)));
        var implementation = services.Last(row => row.ServiceType == typeof(ICustomerBasketOperationService)).ImplementationType!;
        services.RemoveAll<ICustomerBasketOperationService>();
        services.AddScoped(provider =>
        {
            var real = (ICustomerBasketOperationService)ActivatorUtilities.CreateInstance(provider, implementation);
            var proxy = new Mock<ICustomerBasketOperationService>(MockBehavior.Strict);
            proxy.Setup(service => service.AddAsync(It.IsAny<string>(), It.IsAny<AddToBasketDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((string session, AddToBasketDto request, Guid operation, CancellationToken token) =>
                {
                    _addEntered.TrySetResult();
                    return real.AddAsync(session, request, operation, token);
                });
            proxy.Setup(service => service.CancelAddAsync(It.IsAny<string>(), It.IsAny<AddToBasketDto>(), It.IsAny<CancellationToken>()))
                .Returns((string session, AddToBasketDto request, CancellationToken token) =>
                {
                    _cancelEntered.TrySetResult();
                    return real.CancelAddAsync(session, request, token);
                });
            return proxy.Object;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_and_cancellation_serialize_even_when_the_other_request_has_already_entered(bool cancelFirst)
    {
        var request = AddRequest();
        _pause = 1;
        var first = cancelFirst ? CancelAddAsync(request) : SendAsync("/api/customer-mobile/basket/items", request);
        Task<HttpResponseMessage>? second = null;
        try
        {
            await _saving.Task.WaitAsync(Timeout);
            second = cancelFirst ? SendAsync("/api/customer-mobile/basket/items", request) : CancelAddAsync(request);
            await (cancelFirst ? _addEntered : _cancelEntered).Task.WaitAsync(Timeout);
            var observation = Task.Delay(TimeSpan.FromSeconds(2));
            (await Task.WhenAny(second, observation)).Should().Be(observation,
                "the second entered request cannot decide before the first receipt transaction commits");
            using (var context = DatabaseFixture.CreateContext())
            {
                (await context.Baskets.CountAsync()).Should().Be(0);
                (await context.Set<CustomerBasketOperation>().CountAsync()).Should().Be(0);
            }
            _release.TrySetResult();
            var cancelled = await ResolutionAsync(await (cancelFirst ? first : second));
            var execution = await (cancelFirst ? second : first);
            cancelled.OperationId.Should().Be(request.OperationId!.Value);
            using var verify = DatabaseFixture.CreateContext();
            if (cancelFirst)
            {
                cancelled.Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
                execution.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await verify.Baskets.CountAsync()).Should().Be(0);
                (await verify.BasketItems.CountAsync()).Should().Be(0);
            }
            else
            {
                cancelled.Outcome.Should().Be(CustomerBasketOperationOutcome.Applied);
                (await BasketAsync(execution)).Items.Single().Quantity.Should().Be(2);
                cancelled.Basket!.Items.Single().Quantity.Should().Be(2);
                (await verify.Baskets.CountAsync()).Should().Be(1);
            }
            var saved = (await ReceiptsAsync()).Should().ContainSingle().Subject;
            saved.IsCancelled.Should().Be(cancelFirst);
        }
        finally
        {
            _release.TrySetResult();
            await first;
            if (second is not null) await second;
        }
    }

    private sealed class ReceiptPause(CustomerBasketCancellationRaceTests owner) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<CustomerBasketOperation>().Any(entry => entry.State == EntityState.Added) == true
                && Interlocked.Exchange(ref owner._pause, 0) == 1)
            {
                owner._saving.TrySetResult();
                await owner._release.Task.WaitAsync(Timeout, cancellationToken);
            }
            return result;
        }
    }
}
