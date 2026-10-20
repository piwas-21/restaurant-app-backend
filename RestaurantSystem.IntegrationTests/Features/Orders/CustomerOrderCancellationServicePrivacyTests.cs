using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerOrderCancellationServicePrivacyTests
{
    [Fact]
    public async Task A_notification_failure_preserves_the_canonical_result_and_logs_no_private_exception_details()
    {
        const string privateFailure = "private-notification-contact-and-credential";
        var intent = new CustomerOrderCancellationIntentDto
        {
            SchemaVersion = 1,
            OperationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            ExpectedVersion = 7,
            PayloadHash = new string('a', 64)
        };
        var owner = new CustomerOrderCancellationOwner(intent.OrderId, Guid.NewGuid(), new string('b', 64));
        var resolution = new CustomerOrderCancellationResolutionDto(1, intent.OperationId, intent.OrderId,
            CustomerOrderCancellationOutcome.Cancelled, 8, null);
        var owners = new Mock<ICustomerOrderCancellationOwners>();
        owners.Setup(value => value.Customer(intent.OrderId)).Returns(owner);
        var store = new Mock<ICustomerOrderCancellationStore>();
        store.Setup(value => value.SubmitAsync(owner, intent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerOrderCancellationWriteResult(resolution, true));
        var notifier = new Mock<ICustomerOrderCancellationNotifier>();
        notifier.Setup(value => value.NotifyAsync(intent.OrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException(privateFailure));
        var logger = new Mock<ILogger<CustomerOrderCancellationService>>();
        var service = new CustomerOrderCancellationService(owners.Object, store.Object, notifier.Object, logger.Object);
        (await service.SubmitCustomerAsync(intent, default)).Should().Be(resolution);
        var logged = logger.Invocations.Should().ContainSingle().Which;
        logged.Arguments[0].Should().Be(LogLevel.Warning);
        logged.Arguments[2].ToString().Should().Contain(nameof(IOException)).And.NotContain(privateFailure);
        logged.Arguments[3].Should().BeNull("raw exceptions must not enter logging providers");
    }
}
