using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Section identity is part of an idempotent staff order's operation payload.</summary>
public sealed class StaffOrderOperationFingerprintTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid ChildProductId = Guid.NewGuid();

    [Fact]
    public void Changing_a_bundle_child_section_changes_the_operation_fingerprint()
    {
        var first = Request(Guid.NewGuid());
        var second = Request(Guid.NewGuid());

        StaffOrderOperationFingerprint.Create(first, releaseToKitchen: false)
            .Should().NotBe(StaffOrderOperationFingerprint.Create(second, releaseToKitchen: false));
    }

    [Fact]
    public void Changing_the_table_service_session_changes_the_operation_fingerprint()
    {
        var first = Request(Guid.NewGuid()) with { ServiceSessionId = Guid.NewGuid() };
        var second = first with { ServiceSessionId = Guid.NewGuid() };

        StaffOrderOperationFingerprint.Create(first, releaseToKitchen: false)
            .Should().NotBe(StaffOrderOperationFingerprint.Create(second, releaseToKitchen: false));
    }

    private static StaffCounterOrderRequest Request(Guid sectionId) => new()
    {
        Type = OrderType.Takeaway,
        Items =
        [
            new CreateOrderItemDto
            {
                ProductId = ProductId,
                Quantity = 1,
                ChildItems =
                [new CreateOrderItemDto
                {
                    ProductId = ChildProductId, Quantity = 1, SectionId = sectionId
                }]
            }
        ]
    };
}
