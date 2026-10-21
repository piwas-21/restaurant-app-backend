using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTableRoundTransactionTests(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    private readonly CustomerTableRoundSaveInterceptor saves = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.ConfigureDbContext<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>(options => options.AddInterceptors(saves));
    }
    [Theory]
    [InlineData("participant", HttpStatusCode.NotFound)]
    [InlineData("visit", HttpStatusCode.NotFound)]
    [InlineData("revision", HttpStatusCode.NotFound)]
    [InlineData("currency", HttpStatusCode.Conflict)]
    [InlineData("native", HttpStatusCode.NotFound)]
    [InlineData("expired", HttpStatusCode.NotFound)]
    [InlineData("customer", HttpStatusCode.NotFound)]
    [InlineData("dine-in", HttpStatusCode.BadRequest)]
    [InlineData("missing-native", HttpStatusCode.NotFound)]
    [InlineData("missing-round", HttpStatusCode.NotFound)]
    [InlineData("wrong-round", HttpStatusCode.NotFound)]
    public async Task Fresh_authority_and_complete_receipts_are_required_after_the_actual_final_save(string mode, HttpStatusCode expected)
    {
        if (mode == "customer") await SignInCustomerAsync();
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        saves.After = async context =>
        {
            (await context.Set<CustomerOrderOperation>().AsNoTracking().SingleAsync()).OrderId.Should().NotBeNull();
            (await context.Set<TableGuestRoundOperation>().AsNoTracking().CountAsync()).Should().Be(1);
            if (mode == "participant") await context.TableGuestParticipants.ExecuteUpdateAsync(update => update.SetProperty(value => value.RevokedAt, DateTime.UtcNow));
            if (mode == "visit") await context.TableServiceSessions.ExecuteUpdateAsync(update => update.SetProperty(value => value.ReleasedAt, DateTime.UtcNow));
            if (mode == "revision") await context.TableServiceSessions.ExecuteUpdateAsync(update => update.SetProperty(value => value.AccountRevision, intent.ExpectedAccountRevision));
            if (mode == "currency") await context.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(value => value.Currency, "EUR"));
            if (mode == "native") WithdrawNative();
            if (mode == "expired") await context.TableGuestParticipants.ExecuteUpdateAsync(update => update.SetProperty(value => value.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            if (mode == "customer") await context.Users.ExecuteUpdateAsync(update => update.SetProperty(value => value.Role, UserRole.Server));
            if (mode == "dine-in") await context.OrderTypeConfigurations.Where(value => value.OrderType == OrderType.DineIn)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsEnabled, false));
            if (mode == "missing-native") await context.Set<CustomerOrderOperation>().ExecuteDeleteAsync();
            if (mode == "missing-round") await context.Set<TableGuestRoundOperation>().ExecuteDeleteAsync();
            if (mode == "wrong-round") await context.Set<TableGuestRoundOperation>().ExecuteUpdateAsync(update => update.SetProperty(value => value.RequestHash, new string('0', 64)));
        };
        (await SendAsync(RoundPath + "submit", Submission(intent, prepared))).StatusCode.Should().Be(expected);
        saves.Triggered.Should().BeTrue();
        await AssertNoFinancialEffectsAsync();
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        (await verify.Set<TableGuestRoundOperation>().CountAsync()).Should().Be(0);
        var retainedBasket = await verify.Baskets.AsNoTracking().Include(value => value.Items)
            .SingleAsync(value => value.Id == intent.ExpectedBasketId);
        retainedBasket.Total.Should().Be(14m);
        retainedBasket.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        Notifications.Verify(value => value.CompleteAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task Failure_after_the_receipt_insert_rolls_back_every_graph_and_clears_tracking_before_an_unrelated_save()
    {
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        saves.After = _ => throw new IOException("Injected native round receipt failure.");
        using var scope = Factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<ICustomerTableRoundService>();
            var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
            await FluentActions.Invoking(() => service.SubmitAsync(Session, Visit.ParticipantToken, Submission(intent, prepared), default))
                .Should().ThrowAsync<IOException>();
            saves.Triggered.Should().BeTrue();
            context.ChangeTracker.Entries().Should().BeEmpty();
            (await context.SaveChangesAsync()).Should().Be(0);
            (await context.RestaurantInfo.SingleAsync()).Name = "Unrelated saved fixture";
            await context.SaveChangesAsync();
            (await context.Orders.CountAsync()).Should().Be(0);
            (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
            (await context.Set<TableGuestRoundOperation>().CountAsync()).Should().Be(0);
        }
        finally { accessor.HttpContext = previous; }
        var result = await RoundResolutionAsync(await SendAsync(RoundPath + "submit", Submission(intent, prepared)));
        result.Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_first_locked_submit_or_stop_decides_both_concurrent_callers_without_duplicate_effects(bool stopFirst)
    {
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        saves.Before = async _ => { entered.SetResult(); await release.Task; };
        var submitted = Submission(intent, prepared);
        var first = stopFirst ? ResolveRoundAsync(prepared, true) : SendAsync(RoundPath + "submit", submitted);
        Task<HttpResponseMessage>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = stopFirst ? SendAsync(RoundPath + "submit", submitted) : ResolveRoundAsync(prepared, true);
            release.SetResult();
            var results = await Task.WhenAll(first, second);
            var one = await RoundResolutionAsync(results[0]); var two = await RoundResolutionAsync(results[1]);
            one.Should().Be(two);
            one.Outcome.Should().Be(stopFirst ? CustomerOrderOperationOutcome.Cancelled : CustomerOrderOperationOutcome.Applied);
            await using var context = DatabaseFixture.CreateContext();
            (await context.Orders.CountAsync()).Should().Be(stopFirst ? 0 : 1);
            (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
            (await context.Set<TableGuestRoundOperation>().CountAsync()).Should().Be(stopFirst ? 0 : 1);
            (await context.OrderPayments.CountAsync()).Should().Be(0);
        }
        finally
        {
            release.TrySetResult();
            if (second is not null) await Task.WhenAll(first, second);
            else await first;
        }
    }
}
