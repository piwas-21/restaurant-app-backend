using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerOrderCancellationOwnersTests
{
    [Fact]
    public void Customer_authority_captures_the_authenticated_principal_without_looking_up_a_guest_proof()
    {
        var userId = Guid.NewGuid();
        var current = Current(userId);
        var operations = new Mock<ICustomerOrderOperationStore>(MockBehavior.Strict);
        var owners = new CustomerOrderCancellationOwners(current.Object, Mock.Of<ICustomerCheckoutGuard>(), operations.Object);
        var orderId = Guid.NewGuid();
        var owner = owners.Customer(orderId);
        owner.OrderId.Should().Be(orderId);
        owner.UserId.Should().Be(userId);
        owner.OwnerHash.Should().Be(CustomerOrderCancellationHash.Owner(userId, Guid.Empty));
        operations.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(UserRole.Admin, false)]
    [InlineData(UserRole.Cashier, false)]
    [InlineData(UserRole.Server, false)]
    [InlineData(UserRole.KitchenStaff, false)]
    [InlineData(UserRole.Customer, true)]
    public void Staff_and_machine_credentials_cannot_become_customer_cancellation_authority(UserRole role, bool apiToken)
    {
        var current = Current(Guid.NewGuid(), role, apiToken);
        var owner = new CustomerOrderCancellationOwners(current.Object, Mock.Of<ICustomerCheckoutGuard>(), Mock.Of<ICustomerOrderOperationStore>());
        var resolve = () => owner.Customer(Guid.NewGuid());
        resolve.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Missing_authentication_cannot_select_a_customer_history_order()
    {
        var current = Current(Guid.NewGuid());
        current.SetupGet(value => value.IsAuthenticated).Returns(false);
        var owners = new CustomerOrderCancellationOwners(current.Object, Mock.Of<ICustomerCheckoutGuard>(), Mock.Of<ICustomerOrderOperationStore>());
        var resolve = () => owners.Customer(Guid.NewGuid());
        resolve.Should().Throw<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_authority_uses_the_accepted_checkout_mapping_and_exact_current_customer_or_guest(bool customer)
    {
        var session = Guid.NewGuid().ToString("D");
        Guid? userId = customer ? Guid.NewGuid() : null;
        var proof = Proof();
        var orderId = Guid.NewGuid();
        var guard = new Mock<ICustomerCheckoutGuard>(MockBehavior.Strict);
        guard.Setup(value => value.EnsureOwner(session)).Returns(userId);
        var operations = new Mock<ICustomerOrderOperationStore>(MockBehavior.Strict);
        operations.Setup(value => value.RecoverAsync(session, proof.OperationId, proof.PayloadHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerOrderOperationResolution(CustomerOrderOperationOutcome.Applied, orderId));
        var owners = new CustomerOrderCancellationOwners(Mock.Of<ICurrentUserService>(), guard.Object, operations.Object);
        var owner = await owners.OriginalAsync(session, proof, CancellationToken.None);
        owner.OrderId.Should().Be(orderId);
        owner.UserId.Should().Be(userId);
        owner.OwnerHash.Should().Be(CustomerOrderCancellationHash.Owner(userId, Guid.Parse(session)));
        operations.VerifyAll();
    }

    [Theory]
    [InlineData(CustomerOrderOperationOutcome.Unseen)]
    [InlineData(CustomerOrderOperationOutcome.Cancelled)]
    [InlineData(CustomerOrderOperationOutcome.Applied)]
    public async Task Missing_or_unaccepted_checkout_mapping_never_grants_order_authority(CustomerOrderOperationOutcome outcome)
    {
        var guard = new Mock<ICustomerCheckoutGuard>();
        guard.Setup(value => value.EnsureOwner(It.IsAny<string>())).Returns((Guid?)null);
        var operations = new Mock<ICustomerOrderOperationStore>();
        operations.Setup(value => value.RecoverAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerOrderOperationResolution(outcome, null));
        var owners = new CustomerOrderCancellationOwners(Mock.Of<ICurrentUserService>(), guard.Object, operations.Object);
        var resolve = () => owners.OriginalAsync(Guid.NewGuid().ToString("D"), Proof(), CancellationToken.None);
        await resolve.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Rejected_authentication_cannot_fall_back_to_an_original_guest_capability()
    {
        var guard = new Mock<ICustomerCheckoutGuard>();
        guard.Setup(value => value.EnsureOwner(It.IsAny<string>())).Throws(new UnauthorizedAccessException());
        var operations = new Mock<ICustomerOrderOperationStore>(MockBehavior.Strict);
        var owners = new CustomerOrderCancellationOwners(Mock.Of<ICurrentUserService>(), guard.Object, operations.Object);
        var resolve = () => owners.OriginalAsync(Guid.NewGuid().ToString("D"), Proof(), CancellationToken.None);
        await resolve.Should().ThrowAsync<UnauthorizedAccessException>();
        operations.VerifyNoOtherCalls();
    }

    private static Mock<ICurrentUserService> Current(Guid userId, UserRole role = UserRole.Customer, bool apiToken = false)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(value => value.IsAuthenticated).Returns(true);
        mock.SetupGet(value => value.UserId).Returns(userId);
        mock.SetupGet(value => value.Role).Returns(role);
        mock.SetupGet(value => value.IsApiToken).Returns(apiToken);
        return mock;
    }

    private static CustomerOrderRecoveryDto Proof() => new()
    {
        SchemaVersion = 1,
        OperationId = Guid.NewGuid(),
        PayloadHash = new string('a', 64)
    };
}
