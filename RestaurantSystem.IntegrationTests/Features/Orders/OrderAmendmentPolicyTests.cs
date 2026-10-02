using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using Moq;
using RestaurantSystem.Api.Features.OrderAmendments.Commands;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentPolicyTests
{
    [Fact]
    public void QuoteValidator_RequiresNonemptySourceItemAndOneBasedRangeForVoid()
    {
        var validator = new QuoteOrderAmendmentCommandValidator();
        var valid = QuoteRequest(new OrderAmendmentLineChangeRequest
        {
            OrderItemId = Guid.NewGuid(),
            Kind = OrderAmendmentChangeKind.Void,
            StartOrdinal = 1,
            Quantity = 1
        });
        var invalid = QuoteRequest(new OrderAmendmentLineChangeRequest
        {
            OrderItemId = Guid.Empty,
            Kind = OrderAmendmentChangeKind.Void,
            StartOrdinal = 0,
            Quantity = 1
        });

        validator.Validate(valid).IsValid.Should().BeTrue();
        validator.Validate(invalid).IsValid.Should().BeFalse();
    }

    [Fact]
    public void QuoteValidator_RequiresWholeLineSnapshotForInstructionChange()
    {
        var validator = new QuoteOrderAmendmentCommandValidator();
        var valid = QuoteRequest(new OrderAmendmentLineChangeRequest
        {
            OrderItemId = Guid.NewGuid(),
            Kind = OrderAmendmentChangeKind.InstructionChange,
            Current = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }
        });
        var invalid = QuoteRequest(new OrderAmendmentLineChangeRequest
        {
            OrderItemId = Guid.NewGuid(),
            Kind = OrderAmendmentChangeKind.InstructionChange,
            StartOrdinal = 1,
            Quantity = 1,
            Current = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }
        });

        validator.Validate(valid).IsValid.Should().BeTrue();
        validator.Validate(invalid).IsValid.Should().BeFalse();
    }

    [Fact]
    public void QuoteValidator_PreservesThe500CharacterInstructionBoundaryRecursively()
    {
        var validator = new QuoteOrderAmendmentCommandValidator();
        var instruction = new string('x', 500);
        var accepted = QuoteRequest(new OrderAmendmentLineChangeRequest
        {
            OrderItemId = Guid.NewGuid(),
            Kind = OrderAmendmentChangeKind.InstructionChange,
            Current = new CreateOrderItemDto
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1,
                SpecialInstructions = instruction,
                ChildItems = [new CreateOrderItemDto
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    SpecialInstructions = instruction
                }]
            }
        });
        var rejected = QuoteRequest(accepted.Request.Changes[0] with
        {
            Current = accepted.Request.Changes[0].Current! with
            {
                ChildItems = [new CreateOrderItemDto
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    SpecialInstructions = instruction + "x"
                }]
            }
        });

        validator.Validate(accepted).IsValid.Should().BeTrue();
        validator.Validate(rejected).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task FinancialPreview_AllocatesFrozenDiscountByOrdinalAndExcludesTipAndDeliveryFee()
    {
        var source = SourceOrder();
        var lineId = Guid.NewGuid();
        source.Items.Add(new OrderItem
        {
            Id = lineId,
            Quantity = 3,
            ItemTotal = 1m,
            ProductName = "Soup",
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        });
        source.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            Quantity = 1,
            ItemTotal = 2m,
            ProductName = "Salad",
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        });
        var resolver = new Mock<IOrderDisplayCurrencyResolver>();
        resolver.Setup(value => value.Resolve(source)).Returns("CHF");
        var service = new OrderAmendmentFinancialResolutionService(resolver.Object);

        var firstUnit = await service.PreviewAsync(source,
            [Void(lineId, start: 1, quantity: 1)], null, CancellationToken.None);
        var secondUnit = await service.PreviewAsync(source,
            [Void(lineId, start: 2, quantity: 1)], null, CancellationToken.None);

        firstUnit.Currency.Should().Be("CHF");
        firstUnit.RemovedUnitValueMinor.Should().Be(34,
            "the first one-based unit owns the allocation remainder");
        secondUnit.RemovedUnitValueMinor.Should().Be(33);
        firstUnit.PotentialCreditMinor.Should().Be(34);
        firstUnit.CreditState.Should().Be(OrderAmendmentCreditState.BalanceReduction);
    }

    [Fact]
    public async Task FinancialPreview_SubtractsTipAndDeliveryFeeFromSupplementValue()
    {
        var source = SourceOrder();
        var supplement = new Order
        {
            Type = OrderType.Delivery,
            Total = 14.50m,
            Tip = 1.50m,
            DeliveryFee = 4m,
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        };
        var resolver = new Mock<IOrderDisplayCurrencyResolver>();
        resolver.Setup(value => value.Resolve(source)).Returns("CHF");
        var service = new OrderAmendmentFinancialResolutionService(resolver.Object);

        var preview = await service.PreviewAsync(source, [], supplement, CancellationToken.None);

        preview.AddedAmountMinor.Should().Be(900);
        preview.NetAccountDeltaMinor.Should().Be(900);
        preview.PotentialCreditMinor.Should().Be(0);
    }

    [Fact]
    public async Task FinancialPreview_InstructionOnlyNeedsNoInventedCurrencyOrFinancialResolution()
    {
        var source = SourceOrder();
        var resolver = new Mock<IOrderDisplayCurrencyResolver>();
        resolver.Setup(value => value.Resolve(source)).Returns((string?)null);
        var service = new OrderAmendmentFinancialResolutionService(resolver.Object);
        var itemId = Guid.NewGuid();
        var item = new OrderItemDto { Id = itemId, ProductName = "Soup", Quantity = 1 };

        var preview = await service.PreviewAsync(source,
            [new OrderAmendmentChangeSnapshot(itemId, OrderAmendmentChangeKind.InstructionChange,
                0, 0, true, item, item)], null, CancellationToken.None);

        preview.Currency.Should().BeNull();
        preview.ResolutionStatus.Should().Be(OrderAmendmentFinancialResolutionStatus.NotRequired);
        preview.NetAccountDeltaMinor.Should().Be(0);
    }

    [Fact]
    public async Task FinancialPreview_RequiresGatewayRefundWithoutClaimingItWasReturned()
    {
        var source = SourceOrder();
        var lineId = Guid.NewGuid();
        source.Items.Add(new OrderItem
        {
            Id = lineId,
            Quantity = 1,
            ItemTotal = 10m,
            ProductName = "Burger",
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        });
        source.Payments.Add(new OrderPayment
        {
            Amount = 10m,
            Status = PaymentStatus.Completed,
            PaymentGateway = "Stripe",
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        });
        var resolver = new Mock<IOrderDisplayCurrencyResolver>();
        resolver.Setup(value => value.Resolve(source)).Returns("CHF");
        var service = new OrderAmendmentFinancialResolutionService(resolver.Object);

        var preview = await service.PreviewAsync(source,
            [Void(lineId, start: 1, quantity: 1)], null, CancellationToken.None);

        preview.RefundState.Should().Be(OrderAmendmentRefundState.GatewayRefundRequired);
        preview.CreditState.Should().Be(OrderAmendmentCreditState.PendingAllocationReview);
        preview.ResolutionStatus.Should().Be(OrderAmendmentFinancialResolutionStatus.Pending);
    }

    [Fact]
    public void SanitizeText_ReplacesControlCharactersAndCollapsesWhitespace()
    {
        OrderAmendmentPolicy.SanitizeText("  reason\u0001\twith\ncontrols  ", 50)
            .Should().Be("reason with controls");
    }

    [Fact]
    public void LiveOrderWithFullyRefundedAggregateCannotBeAmended()
    {
        var source = SourceOrder();
        source.Status = OrderStatus.Confirmed;
        source.PaymentStatus = PaymentStatus.Refunded;
        var request = AdditionRequest(source);

        var exception = Assert.Throws<ConflictException>(() =>
            OrderAmendmentPolicy.ValidateOrderContext(source, request));

        exception.Message.Should().Contain("refund activity");
    }

    [Fact]
    public void PartialOrUnresolvedTenderRefundCannotBeAmendedEvenWhenAggregateLooksSettled()
    {
        var source = SourceOrder();
        source.Status = OrderStatus.Confirmed;
        source.PaymentStatus = PaymentStatus.Completed;
        source.Payments.Add(new OrderPayment
        {
            Amount = 10m,
            Status = PaymentStatus.Completed,
            RefundedAmount = 2m,
            RefundDate = DateTime.UtcNow,
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        });

        Assert.Throws<ConflictException>(() =>
            OrderAmendmentPolicy.ValidateOrderContext(source, AdditionRequest(source)));
    }

    [Fact]
    public void PartiallyRefundedTenderCannotBeAmended()
    {
        var source = SourceOrder();
        source.PaymentStatus = PaymentStatus.PartiallyPaid;
        source.Payments.Add(new OrderPayment
        {
            Amount = 10m,
            Status = PaymentStatus.PartiallyRefunded,
            RefundedAmount = 2m,
            RefundDate = DateTime.UtcNow,
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        });

        Assert.Throws<ConflictException>(() =>
            OrderAmendmentPolicy.ValidateOrderContext(source, AdditionRequest(source)));
    }

    [Fact]
    public void PreparingLineCorrection_AllowsServerOnlyAfterAcknowledgementAndReason()
    {
        var source = SourceOrder();
        source.Status = OrderStatus.Preparing;
        var user = new Mock<ICurrentUserService>();
        user.Setup(value => value.IsAdmin).Returns(false);
        user.Setup(value => value.Role).Returns(UserRole.Server);
        var request = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = source.Version,
            Reason = "Guest requested a correction",
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = Guid.NewGuid(),
                Kind = OrderAmendmentChangeKind.Void,
                StartOrdinal = 1,
                Quantity = 1
            }]
        };

        var exception = Assert.Throws<BadRequestException>(() =>
            OrderAmendmentPolicy.ValidateChangeAuthority(source, request, user.Object));
        exception.Message.Should().Contain("acknowledge");

        OrderAmendmentPolicy.ValidateChangeAuthority(source,
            request with { PreparingOverrideAcknowledged = true }, user.Object);
    }

    [Fact]
    public void ServedLineCorrection_RemainsRestrictedToCashierOrAdmin()
    {
        var source = SourceOrder();
        source.Status = OrderStatus.Delivered;
        var user = new Mock<ICurrentUserService>();
        user.Setup(value => value.IsAdmin).Returns(false);
        user.Setup(value => value.Role).Returns(UserRole.Server);
        var request = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = source.Version,
            Reason = "Guest reported a missing item",
            PreparingOverrideAcknowledged = true,
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = Guid.NewGuid(),
                Kind = OrderAmendmentChangeKind.Void,
                StartOrdinal = 1,
                Quantity = 1
            }]
        };

        Assert.Throws<ForbiddenException>(() =>
            OrderAmendmentPolicy.ValidateChangeAuthority(source, request, user.Object));
    }

    [Fact]
    public void MarketplaceOrder_RequiresConsentAndAllowsOnlyLocalAdditions()
    {
        var source = SourceOrder();
        source.ExternalReference = new ExternalOrderReference
        {
            Provider = "Marketplace",
            ExternalStoreId = "store",
            ExternalOrderId = "external-order",
            ExternalDisplayId = "display",
            ExternalState = "Accepted",
            LastEventAt = DateTime.UtcNow,
            Currency = "CHF",
            MerchantTotal = 12m,
            PayloadHash = new string('a', 64),
            FulfillmentType = "Takeaway",
            CreatedBy = nameof(OrderAmendmentPolicyTests)
        };
        var addition = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 };
        var consented = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = source.Version,
            LocalProviderSupplementConsent = true,
            ProviderConsentNote = "Staff will reconcile the provider separately.",
            Additions = [addition]
        };

        OrderAmendmentPolicy.ValidateOrderContext(source, consented);
        Assert.Throws<BadRequestException>(() => OrderAmendmentPolicy.ValidateOrderContext(
            source, consented with { LocalProviderSupplementConsent = false }));
        Assert.Throws<BadRequestException>(() => OrderAmendmentPolicy.ValidateOrderContext(
            source, consented with
            {
                Changes = [new OrderAmendmentLineChangeRequest
                {
                    OrderItemId = Guid.NewGuid(), Kind = OrderAmendmentChangeKind.Void,
                    StartOrdinal = 1, Quantity = 1
                }]
            }));
    }

    private static QuoteOrderAmendmentCommand QuoteRequest(OrderAmendmentLineChangeRequest change) =>
        new(Guid.NewGuid(), new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = 1,
            Changes = [change]
        });

    private static OrderAmendmentQuoteRequest AdditionRequest(Order source) => new()
    {
        ExpectedOrderVersion = source.Version,
        Additions = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
    };

    private static OrderAmendmentChangeSnapshot Void(Guid itemId, int start, int quantity)
    {
        var item = new OrderItemDto { Id = itemId, ProductName = "Frozen item", Quantity = quantity };
        return new OrderAmendmentChangeSnapshot(
            itemId, OrderAmendmentChangeKind.Void, start, quantity, false, item, null);
    }

    private static Order SourceOrder() => new()
    {
        Type = OrderType.Takeaway,
        Total = 8.01m,
        Tip = 1m,
        DeliveryFee = 4m,
        Items = [],
        Payments = [],
        CreatedBy = nameof(OrderAmendmentPolicyTests)
    };
}
