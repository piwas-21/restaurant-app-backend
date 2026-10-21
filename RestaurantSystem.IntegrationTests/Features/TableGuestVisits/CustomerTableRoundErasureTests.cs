using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.User.Commands.DeleteUserCommand;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTableRoundErasureTests(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    private readonly CustomerTableRoundErasureInterceptor erasure = new();
    private readonly CustomerTableRoundSaveInterceptor saves = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(erasure, saves));
    }
    [Fact]
    public async Task Permanent_erasure_holds_User_before_deleting_baskets_and_refuses_the_waiting_native_round()
    {
        var customer = await SignInCustomerAsync();
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        erasure.ErasureLocked = async () => { entered.SetResult(); await release.Task; };
        erasure.Armed = true;
        var deletion = DeleteAsync(customer);
        Task<HttpResponseMessage>? submission = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            submission = SendAsync(RoundPath + "submit", Submission(intent, prepared));
            await WaitForDatabaseLockAsync(await erasure.NativeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            release.SetResult();
            (await deletion).Success.Should().BeTrue();
            (await submission).StatusCode.Should().Be(HttpStatusCode.NotFound);
            await AssertNoFinancialEffectsAsync();
            await AssertErasedAsync(customer, 0);
        }
        finally
        {
            release.TrySetResult();
            await deletion;
            if (submission is not null) await submission;
        }
    }
    [Fact]
    public async Task An_accepted_round_releases_User_before_waiting_permanent_erasure_and_retains_its_receipts()
    {
        var customer = await SignInCustomerAsync();
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        saves.Before = async _ => { entered.SetResult(); await release.Task; };
        erasure.Armed = true;
        var submission = SendAsync(RoundPath + "submit", Submission(intent, prepared));
        Task<ApiResponse<string>>? deletion = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            deletion = DeleteAsync(customer);
            await WaitForDatabaseLockAsync(await erasure.ErasureAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            release.SetResult();
            (await RoundResolutionAsync(await submission)).Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
            (await deletion).Success.Should().BeTrue();
            await AssertErasedAsync(customer, 1);
        }
        finally
        {
            release.TrySetResult();
            await submission;
            if (deletion is not null) await deletion;
        }
    }
    private async Task<ApiResponse<string>> DeleteAsync(Guid customer)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CustomMediator>()
            .SendCommand(new DeleteUserCommand(customer, true));
    }
    private async Task WaitForDatabaseLockAsync(int pid)
    {
        await using var observer = DatabaseFixture.CreateContext();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS \"Value\"").SingleAsync())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the actual PostgreSQL command must wait on the other transaction");
            await Task.Delay(10);
        }
    }
    private async Task AssertErasedAsync(Guid customer, int rounds)
    {
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Users.IgnoreQueryFilters().AnyAsync(value => value.Id == customer)).Should().BeFalse();
        (await verify.Baskets.IgnoreQueryFilters().AnyAsync(value => value.UserId == customer)).Should().BeFalse();
        (await verify.Orders.CountAsync()).Should().Be(rounds);
        (await verify.Set<CustomerOrderOperation>().CountAsync()).Should().Be(rounds);
        (await verify.Set<TableGuestRoundOperation>().CountAsync()).Should().Be(rounds);
        (await verify.OrderPayments.CountAsync()).Should().Be(0);
    }
}
