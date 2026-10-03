using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountPaymentStaffAuthorityTests
{
    [Theory]
    [InlineData(UserRole.Server, false, true, true, false, false)]
    [InlineData(UserRole.Server, true, false, true, false, false)]
    [InlineData(UserRole.Server, true, true, false, true, false)]
    [InlineData(UserRole.Server, true, true, true, false, true)]
    [InlineData(UserRole.Server, true, true, true, true, true)]
    [InlineData(UserRole.Cashier, true, false, true, false, true)]
    [InlineData(UserRole.Cashier, true, true, false, false, false)]
    [InlineData(UserRole.Cashier, true, false, false, true, true)]
    [InlineData(UserRole.Cashier, false, true, true, true, false)]
    [InlineData(UserRole.Admin, true, false, true, false, true)]
    [InlineData(UserRole.Admin, true, false, false, true, true)]
    [InlineData(UserRole.Admin, true, true, false, false, false)]
    public void New_collection_requires_role_specific_module_and_explicit_server_opt_in(
        UserRole role, bool payments, bool serverOptIn, bool serverModule, bool cashierModule, bool allowed)
    {
        var resolver = Create(role, payments, serverOptIn, serverModule, cashierModule);
        resolver.CanStartCollection.Should().Be(allowed);
        var require = () => resolver.RequireNewCollection();
        if (allowed) require.Should().NotThrow();
        else require.Should().Throw<NotFoundException>();
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    public void Original_staff_identity_remains_available_after_all_new_collection_switches_are_off(UserRole role)
    {
        var actorId = Guid.NewGuid();
        var resolver = Create(role, false, false, false, false, actorId: actorId);
        resolver.CanStartCollection.Should().BeFalse();
        resolver.ResolveStaffActor().ActorId.Should().Be(actorId);
        resolver.ResolveStaffActor().Kind.Should().Be(AccountPaymentActorKind.Staff);
    }

    [Theory]
    [InlineData(UserRole.Customer, true, false, false)]
    [InlineData(UserRole.KitchenStaff, true, false, false)]
    [InlineData(UserRole.Server, false, false, false)]
    [InlineData(UserRole.Admin, true, true, false)]
    [InlineData(UserRole.Cashier, true, false, true)]
    public void Invalid_staff_identity_never_gets_collection_or_recovery_authority(
        UserRole role, bool authenticated, bool apiToken, bool emptyActor)
    {
        var resolver = Create(role, true, true, true, true, authenticated, apiToken,
            emptyActor ? Guid.Empty : Guid.NewGuid());
        resolver.CanStartCollection.Should().BeFalse();
        var recover = () => resolver.ResolveStaffActor();
        recover.Should().Throw<ForbiddenException>();
        var collect = () => resolver.RequireNewCollection();
        collect.Should().Throw<ForbiddenException>();
    }

    private static AccountPaymentActorResolver Create(
        UserRole role, bool payments, bool serverOptIn, bool serverModule, bool cashierModule,
        bool authenticated = true, bool apiToken = false, Guid? actorId = null)
    {
        var actor = actorId ?? Guid.NewGuid();
        var user = new Mock<ICurrentUserService>();
        user.Setup(value => value.Role).Returns(role);
        user.Setup(value => value.IsAuthenticated).Returns(authenticated);
        user.Setup(value => value.IsApiToken).Returns(apiToken);
        user.Setup(value => value.UserId).Returns(actor);
        user.Setup(value => value.GetAuditIdentifier()).Returns(actor.ToString());
        var modules = new Mock<ITenantModules>();
        modules.Setup(value => value.IsEnabled(ModuleIds.Server)).Returns(serverModule);
        modules.Setup(value => value.IsEnabled(ModuleIds.Cashier)).Returns(cashierModule);
        return new(user.Object, new TenantFeatures(Options.Create(new TenantFeatureSettings
        {
            TableAccountPaymentsV1 = payments,
            ServerAccountCollectionV1 = serverOptIn
        })), modules.Object);
    }
}
