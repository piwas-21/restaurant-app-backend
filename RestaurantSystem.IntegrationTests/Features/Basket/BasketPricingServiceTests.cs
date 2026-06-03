using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;
using DomainBasket = RestaurantSystem.Domain.Entities.Basket;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Pure unit tests for BasketPricingService (no DbContext). They lock in the
// totals behaviour extracted from BasketService.RecalculateBasketTotalsAsync
// (Sprint 3 task 3.1) so the decomposition is provably behaviour-preserving.
public class BasketPricingServiceTests
{
    private readonly Mock<ICustomerDiscountService> _discount = new(MockBehavior.Strict);

    private BasketPricingService CreateSut() =>
        new(_discount.Object, NullLogger<BasketPricingService>.Instance);

    private static DomainBasket NewBasket(
        Guid? userId = null,
        decimal manualDiscount = 0,
        decimal deliveryFee = 0,
        params decimal[] itemTotals)
    {
        var basket = new DomainBasket
        {
            Id = Guid.NewGuid(),
            SessionId = "s",
            UserId = userId,
            Discount = manualDiscount,
            DeliveryFee = deliveryFee,
            CreatedBy = "test",
        };
        foreach (var t in itemTotals)
        {
            basket.Items.Add(new BasketItem { Id = Guid.NewGuid(), ItemTotal = t, Quantity = 1, UnitPrice = t, CreatedBy = "test" });
        }
        return basket;
    }

    private void SetupNoDiscount(Guid userId) =>
        _discount.Setup(d => d.FindBestApplicableDiscountAsync(userId, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((CustomerDiscountRule?)null);

    [Fact]
    public async Task NullBasket_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateSut().ApplyTotalsAsync(null!));
    }

    [Fact]
    public async Task EmptyBasket_AllTotalsZero()
    {
        var basket = NewBasket();
        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(0m, basket.SubTotal);
        Assert.Equal(0m, basket.CustomerDiscount);
        Assert.Equal(0m, basket.Tax);
        Assert.Equal(0m, basket.Total);
    }

    [Fact]
    public async Task SubTotal_IsSumOfItemTotals()
    {
        var basket = NewBasket(itemTotals: new[] { 10.50m, 5.25m, 4.00m });
        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(19.75m, basket.SubTotal);
    }

    [Fact]
    public async Task AnonymousBasket_NoDiscountLookup()
    {
        // Strict mock: if FindBestApplicableDiscountAsync were called, the test fails.
        var basket = NewBasket(itemTotals: new[] { 12.00m });
        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(0m, basket.CustomerDiscount);
        Assert.Equal(12.00m, basket.Total);
        _discount.Verify(d => d.FindBestApplicableDiscountAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyGuidUser_TreatedAsAnonymous()
    {
        var basket = NewBasket(userId: Guid.Empty, itemTotals: new[] { 8.00m });
        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(0m, basket.CustomerDiscount);
        _discount.Verify(d => d.FindBestApplicableDiscountAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoggedInUser_NoApplicableDiscount_TotalEqualsSubtotal()
    {
        var userId = Guid.NewGuid();
        SetupNoDiscount(userId);
        var basket = NewBasket(userId: userId, itemTotals: new[] { 10.00m, 5.55m });

        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(15.55m, basket.SubTotal);
        Assert.Equal(0m, basket.CustomerDiscount);
        Assert.Equal(15.55m, basket.Total); // no-discount path rounds to 2dp
    }

    [Fact]
    public async Task LoggedInUser_WithDiscount_AppliesAmountAndSpecialRounding()
    {
        var userId = Guid.NewGuid();
        var rule = new CustomerDiscountRule { Id = Guid.NewGuid(), UserId = userId, Name = "VIP", IsActive = true, CreatedBy = "test" };
        _discount.Setup(d => d.FindBestApplicableDiscountAsync(userId, 15.75m, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(rule);
        _discount.Setup(d => d.CalculateDiscountAmount(rule, 15.75m)).Returns(2.00m);

        var basket = NewBasket(userId: userId, itemTotals: new[] { 15.75m });
        await CreateSut().ApplyTotalsAsync(basket);

        // amountAfterDiscount = 15.75 - 2.00 - 0 = 13.75; +0 delivery; hasDiscount=true
        // fractional 0.75 >= 0.10 -> Ceiling -> 14.00
        Assert.Equal(2.00m, basket.CustomerDiscount);
        Assert.Equal(14.00m, basket.Total);
    }

    [Fact]
    public async Task ZeroDiscountAmount_UsesTwoDecimalRounding()
    {
        var userId = Guid.NewGuid();
        var rule = new CustomerDiscountRule { Id = Guid.NewGuid(), UserId = userId, Name = "VIP", IsActive = true, CreatedBy = "test" };
        _discount.Setup(d => d.FindBestApplicableDiscountAsync(userId, 20.05m, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(rule);
        _discount.Setup(d => d.CalculateDiscountAmount(rule, 20.05m)).Returns(0.00m);

        // discount amount 0 -> HasActiveDiscount(0) == false -> no special rounding (round 2dp)
        var basket = NewBasket(userId: userId, itemTotals: new[] { 20.05m });
        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(20.05m, basket.Total);
    }

    [Fact]
    public async Task WithActiveDiscount_FractionUnderTenCents_FloorsDown()
    {
        var userId = Guid.NewGuid();
        var rule = new CustomerDiscountRule { Id = Guid.NewGuid(), UserId = userId, Name = "VIP", IsActive = true, CreatedBy = "test" };
        _discount.Setup(d => d.FindBestApplicableDiscountAsync(userId, 13.50m, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(rule);
        _discount.Setup(d => d.CalculateDiscountAmount(rule, 13.50m)).Returns(0.45m);

        // hasDiscount = true (0.45 > 0). amountAfterDiscount = 13.50 - 0.45 = 13.05;
        // fractional 0.05 < 0.10 -> Math.Floor -> 13.00
        var basket = NewBasket(userId: userId, itemTotals: new[] { 13.50m });
        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(0.45m, basket.CustomerDiscount);
        Assert.Equal(13.00m, basket.Total);
    }

    [Fact]
    public async Task ManualPromoDiscount_SubtractedFromTotal()
    {
        var basket = NewBasket(manualDiscount: 3.00m, itemTotals: new[] { 20.00m });
        await CreateSut().ApplyTotalsAsync(basket);

        // anonymous, no customer discount: 20.00 - 0 - 3.00 + 0 = 17.00
        Assert.Equal(17.00m, basket.Total);
    }

    [Fact]
    public async Task DeliveryFee_AddedToTotal()
    {
        var basket = NewBasket(deliveryFee: 4.50m, itemTotals: new[] { 10.00m });
        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(14.50m, basket.Total);
    }

    [Fact]
    public async Task TaxAlwaysZero_DeferredToOrderCreation()
    {
        var userId = Guid.NewGuid();
        SetupNoDiscount(userId);
        var basket = NewBasket(userId: userId, deliveryFee: 2m, manualDiscount: 1m, itemTotals: new[] { 30m });

        await CreateSut().ApplyTotalsAsync(basket);

        Assert.Equal(0m, basket.Tax);
    }

    [Fact]
    public async Task DiscountLookup_UsesSubtotalNotPostDiscountAmount()
    {
        var userId = Guid.NewGuid();
        SetupNoDiscount(userId);
        var basket = NewBasket(userId: userId, manualDiscount: 5.00m, deliveryFee: 2.00m, itemTotals: new[] { 40.00m });

        await CreateSut().ApplyTotalsAsync(basket);

        // The best-discount lookup must be keyed on the raw subtotal (40.00),
        // before manual discount / delivery fee are applied.
        _discount.Verify(d => d.FindBestApplicableDiscountAsync(userId, 40.00m, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- CalculateIngredientCustomizationPrice ----

    private static ProductIngredient Ing(
        Guid id, decimal price, bool optional = true, bool includedInBase = false,
        int maxQty = 1, bool active = true) =>
        new()
        {
            Id = id,
            Price = price,
            IsOptional = optional,
            IsIncludedInBasePrice = includedInBase,
            MaxQuantity = maxQty,
            IsActive = active,
            CreatedBy = "test",
        };

    [Fact]
    public void Customization_NullIngredients_ReturnsZero() =>
        Assert.Equal(0m, CreateSut().CalculateIngredientCustomizationPrice(null, null, null));

    [Fact]
    public void Customization_RegularOptional_AddsWhenSelected()
    {
        var id = Guid.NewGuid();
        var ings = new[] { Ing(id, 1.50m) };
        // selected, default qty 1 -> +1.50
        Assert.Equal(1.50m, CreateSut().CalculateIngredientCustomizationPrice(ings, new[] { id }, null));
        // not selected -> 0
        Assert.Equal(0m, CreateSut().CalculateIngredientCustomizationPrice(ings, new List<Guid>(), null));
    }

    [Fact]
    public void Customization_RegularOptional_MultipliesByQuantity()
    {
        var id = Guid.NewGuid();
        var ings = new[] { Ing(id, 2.00m, maxQty: 5) };
        var qty = new Dictionary<Guid, int> { [id] = 3 };
        Assert.Equal(6.00m, CreateSut().CalculateIngredientCustomizationPrice(ings, new[] { id }, qty));
    }

    [Fact]
    public void Customization_IncludedInBase_DeselectDeducts()
    {
        var id = Guid.NewGuid();
        var ings = new[] { Ing(id, 1.20m, includedInBase: true) };
        // deselected -> -1.20
        Assert.Equal(-1.20m, CreateSut().CalculateIngredientCustomizationPrice(ings, new List<Guid>(), null));
        // selected qty 1 -> no change
        Assert.Equal(0m, CreateSut().CalculateIngredientCustomizationPrice(ings, new[] { id }, null));
    }

    [Fact]
    public void Customization_IncludedInBase_ExtraQuantityAddsBeyondFreeOne()
    {
        var id = Guid.NewGuid();
        var ings = new[] { Ing(id, 1.00m, includedInBase: true, maxQty: 5) };
        var qty = new Dictionary<Guid, int> { [id] = 3 };
        // selected qty 3 -> +price*(3-1) = +2.00
        Assert.Equal(2.00m, CreateSut().CalculateIngredientCustomizationPrice(ings, new[] { id }, qty));
    }

    [Fact]
    public void Customization_QuantityClampedToMax()
    {
        var id = Guid.NewGuid();
        var ings = new[] { Ing(id, 1.00m, maxQty: 2) };
        var qty = new Dictionary<Guid, int> { [id] = 99 };
        // clamped to 2 -> +2.00 (not +99)
        Assert.Equal(2.00m, CreateSut().CalculateIngredientCustomizationPrice(ings, new[] { id }, qty));
    }

    [Fact]
    public void Customization_NullSelected_TreatsAllAsDeselected()
    {
        // null selectedIngredientIds must behave like "nothing selected":
        // included-in-base -> deducted; regular optional -> no charge.
        var included = Ing(Guid.NewGuid(), 1.00m, includedInBase: true);
        var regular = Ing(Guid.NewGuid(), 2.00m);
        Assert.Equal(-1.00m, CreateSut().CalculateIngredientCustomizationPrice(new[] { included, regular }, null, null));
    }

    [Fact]
    public void Customization_NegativeQuantity_ClampsToZero()
    {
        // Security: a tampered payload with a negative quantity must not reduce
        // the price — it clamps to 0 (contributes nothing).
        var id = Guid.NewGuid();
        var ings = new[] { Ing(id, 1.50m, maxQty: 5) };
        var qty = new Dictionary<Guid, int> { [id] = -5 };
        Assert.Equal(0m, CreateSut().CalculateIngredientCustomizationPrice(ings, new[] { id }, qty));
    }

    [Fact]
    public void Customization_IgnoresNonOptionalAndInactive()
    {
        var nonOptional = Ing(Guid.NewGuid(), 5m, optional: false);
        var inactive = Ing(Guid.NewGuid(), 5m, optional: true, active: false);
        var ids = new[] { nonOptional.Id, inactive.Id };
        Assert.Equal(0m, CreateSut().CalculateIngredientCustomizationPrice(new[] { nonOptional, inactive }, ids, null));
    }
}
