using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Auth.Commands.CompleteNativePasswordResetCommand;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class CustomerNativeAccountResetContextTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    private readonly RefuseUpdate _validator = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddSingleton<IUserValidator<ApplicationUser>>(_validator);
    }

    [Fact]
    public async Task An_unsuccessful_identity_update_detaches_its_mutated_password_and_stamp()
    {
        var user = await CustomerAsync(confirmed: true);
        var proof = await SendLinkAsync(user, "recover");
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _validator.Armed = true;
        var result = await scope.ServiceProvider.GetRequiredService<CustomMediator>().SendCommand(
            new CompleteNativePasswordResetCommand(1, proof, NewPassword, NewPassword));
        result.Success.Should().BeFalse();
        _validator.Triggered.Should().BeTrue();
        context.ChangeTracker.Entries().Should().BeEmpty();
        await context.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = (await users.FindByIdAsync(user.Id.ToString()))!;
        after.SecurityStamp.Should().Be(user.SecurityStamp);
        (await users.CheckPasswordAsync(after, Password)).Should().BeTrue();
        (await users.CheckPasswordAsync(after, NewPassword)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unrelated_work_and_caller_transactions_are_refused_and_preserved(bool callerTransaction)
    {
        var user = await CustomerAsync(confirmed: true);
        var proof = await SendLinkAsync(user, "recover");
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = callerTransaction ? await context.Database.BeginTransactionAsync() : null;
        if (!callerTransaction) (await context.RestaurantInfo.SingleAsync()).Currency = "EUR";
        var reset = () => scope.ServiceProvider.GetRequiredService<CustomMediator>().SendCommand(
            new CompleteNativePasswordResetCommand(1, proof, NewPassword, NewPassword));
        await reset.Should().ThrowAsync<ConflictException>();
        if (callerTransaction)
        {
            context.Database.CurrentTransaction.Should().BeSameAs(transaction);
            await transaction!.RollbackAsync();
        }
        else context.ChangeTracker.HasChanges().Should().BeTrue();
    }

    private sealed class RefuseUpdate : IUserValidator<ApplicationUser>
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
        {
            if (!Armed) return Task.FromResult(IdentityResult.Success);
            Armed = false;
            Triggered = true;
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "TestValidationRefusal", Description = "Account update refused by test control" }));
        }
    }
}
