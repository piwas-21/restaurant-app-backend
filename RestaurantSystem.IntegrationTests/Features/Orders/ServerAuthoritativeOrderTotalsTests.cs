using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// S0b — the server is the only authority on what an order costs.
///
/// <para>
/// <c>POST /api/Orders</c> is ANONYMOUS, and until this file existed it copied <c>Total</c> straight
/// out of the request body's <c>basketTotal</c>. Measured before the fix: a real product line with
/// <c>basketTotal: 0</c> answered 200 with <c>Total=0.00, RemainingAmount=0.00,
/// PaymentStatus=Completed</c> — a free order that the fidelity coordinator then rewarded, because
/// its award gate reads <c>order.PaymentStatus</c> and nothing else.
/// </para>
///
/// <para>
/// The obvious repair — "honour the basket fields only for staff" — was rejected because the
/// server's own arithmetic did not produce the number the customer was actually charged. Three
/// terms had to be reconciled first, and each has a case below: the <b>tip</b> (omitted by
/// <c>ApplyTotal</c>, present in the client's payable total), the <b>points credit</b> (redeemed
/// after <c>SaveChangesAsync</c>, so zero at pricing time), and <b>special rounding</b> (applied on
/// the compute path only, so switching paths could move a total even when every input agreed).
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class ServerAuthoritativeOrderTotalsTests : IntegrationTestBase
{
    private const decimal PizzaPrice = 12.99m;
    private Guid _pizzaId;

    public ServerAuthoritativeOrderTotalsTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- The hole itself -------------------------------------------------------------------

    /// <summary>
    /// The regression test for the measured defect. Posted as raw JSON on purpose: the properties
    /// are gone from the command, so a strongly-typed request could not express the attack at all
    /// and would pass against the vulnerable code. What is being pinned is that an extra body
    /// member is <i>ignored</i> — not merely that the DTO no longer declares it.
    /// </summary>
    [Fact]
    public async Task A_declared_total_of_zero_does_not_buy_an_order()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 0m } },
            basketSubTotal = 0m,
            basketTax = 0m,
            basketTotal = 0m,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await SingleOrderAsync();

        order.Total.Should().Be(PizzaPrice, "the price of the food is a fact about the catalogue, not about the request");
        order.RemainingAmount.Should().Be(PizzaPrice);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending,
            "PaymentStatus=Completed on an unpaid order is what opened the fidelity award");
    }

    /// <summary>
    /// The consequence the status field only stands for. Asserted on the transaction rows because
    /// "PaymentStatus is Pending" is a claim about a column — a future change that awards points on
    /// a different gate would leave the assertion above green and this one red.
    /// </summary>
    [Fact]
    public async Task A_declared_total_of_zero_awards_no_fidelity_points()
    {
        AuthenticateAsUser();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 0m } },
            basketTotal = 0m,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await context.FidelityPointsTransactions.AsNoTracking()
                .AnyAsync(t => t.TransactionType == TransactionType.Earned))
            .Should().BeFalse("nobody paid for this order");
    }

    /// <summary>
    /// The control. Every refusal above is also satisfied by an endpoint that prices everything at
    /// zero, or one that rejects the order outright.
    /// </summary>
    [Fact]
    public async Task An_honest_order_is_still_priced_from_the_catalogue()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 2, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = PizzaPrice * 2 } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice * 2);
    }

    /// <summary>
    /// A unit price is as client-supplied as a total was. Quantity comes from the request by
    /// design — what the customer wants is theirs to say — but the money attached to it is not.
    /// </summary>
    [Fact]
    public async Task A_declared_unit_price_does_not_set_the_line_total()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = 0.01m } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 0.01m } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice,
            "the catalogue price is the price");
    }

    /// <summary>
    /// The carve-out, and its edge. A cashier hand-builds lines the catalogue cannot express — a
    /// bundle's rolled-up price, an off-menu adjustment — so staff keep the ability to declare a
    /// price, exactly as they keep the ability to declare a non-cash tender.
    /// </summary>
    [Fact]
    public async Task Staff_may_still_declare_a_price()
    {
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Walk-in",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = 9.50m } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 9.50m } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SingleOrderAsync()).Total.Should().Be(9.50m, "the till prices what the catalogue cannot");
    }

    /// <summary>
    /// Having an account is not standing behind the till. Without this case the predicate could
    /// drift from <c>IsStaff</c> to <c>IsAuthenticated</c> and every other assertion here would
    /// still pass — while the hole reopened for every registered customer, which on a self-serve
    /// signup is the likeliest attacker of the lot. Same drift the tender guard's suite pins.
    /// </summary>
    [Fact]
    public async Task An_authenticated_customer_may_not_declare_a_price()
    {
        AuthenticateAsUser();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Customer",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = 0.01m } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 0.01m } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice);
    }

    /// <summary>
    /// <c>[JsonIgnore]</c> on <c>ItemsAreServerPriced</c> is the entire untrusted-pricing gate: it is
    /// what stops a caller simply asserting that its own prices came from the server. Remove the
    /// attribute, rename the property, or bind from anywhere but <c>[FromBody]</c>, and every other
    /// assertion in this file still passes while the hole reopens.
    /// </summary>
    [Fact]
    public async Task A_caller_cannot_declare_its_own_prices_server_priced()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = 0.01m } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 0.01m } },
            itemsAreServerPriced = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice,
            "only the from-basket handler may set that flag — a body value must not bind");
    }

    /// <summary>
    /// A composed line cannot be priced from the catalogue (the option prices live in the menu
    /// definition), so it is refused rather than repriced — repricing it at
    /// <c>product.BasePrice</c> would undercharge the bundle.
    /// </summary>
    [Fact]
    public async Task A_customer_cannot_hand_build_a_composed_line()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[]
            {
                new
                {
                    productId = _pizzaId,
                    quantity = 1,
                    unitPrice = 20.00m,
                    childItems = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = 5.00m } },
                }
            },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 20.00m } },
        });

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("composed item", "the refusal must name its reason");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Orders.AsNoTracking().AnyAsync())
            .Should().BeFalse("a guard that ran after the rows were written would still 'refuse'");
    }

    // ---- Points redemption -------------------------------------------------------------------

    /// <summary>
    /// Redemption is the one money term that cannot be known at pricing time — its row FKs the
    /// order, so it runs after the insert. The credit therefore has to be folded into an already
    /// persisted <c>Total</c>, and nothing else in this suite exercises that second pass.
    /// </summary>
    [Fact]
    public async Task A_redeemed_points_credit_reaches_the_persisted_total()
    {
        await SeedPointsBalanceAsync(500);
        AuthenticateAsUser();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Customer",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = PizzaPrice } },
            pointsToRedeem = 500,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 100 points = 1.00 (FidelityPointsService.CalculateDiscountFromPoints).
        var order = await SingleOrderAsync();
        order.FidelityPointsDiscount.Should().Be(5.00m);
        order.Total.Should().Be(PizzaPrice - 5.00m, "the credit must reach the row, not just the response");
        order.RemainingAmount.Should().Be(PizzaPrice - 5.00m, "UpdatePaymentSummary must re-run after repricing");
    }

    /// <summary>
    /// The failure direction. <c>RedeemAsync</c> swallows its exceptions, so a caller asking for
    /// points it does not have must end up paying FULL price — never discounted for points that
    /// were never taken. The fields are set on a tracked entity, so a later
    /// <c>SaveChangesAsync</c> in the same transaction would otherwise flush them.
    /// </summary>
    [Fact]
    public async Task Redeeming_points_the_customer_does_not_have_charges_full_price()
    {
        await SeedPointsBalanceAsync(10);
        AuthenticateAsUser();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Customer",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = PizzaPrice } },
            pointsToRedeem = 5000,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await SingleOrderAsync();
        order.FidelityPointsDiscount.Should().Be(0m);
        order.FidelityPointsRedeemed.Should().Be(0);
        order.Total.Should().Be(PizzaPrice);
    }

    // ---- The tip -----------------------------------------------------------------------------

    /// <summary>
    /// <c>ApplyTotal</c> never added <c>order.Tip</c>, while the checkout page's payable total did.
    /// Deriving the total server-side without reconciling that would have recorded every tipped
    /// order short by exactly the tip, and <c>RemainingAmount</c> would never reach zero — so no
    /// tipped order could ever read as fully paid.
    /// </summary>
    [Fact]
    public async Task The_tip_is_part_of_what_the_customer_owes()
    {
        AuthenticateAsAnonymous();

        const decimal tip = 3.50m;
        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = PizzaPrice + tip } },
            tip,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await SingleOrderAsync();
        order.Tip.Should().Be(tip);
        order.Total.Should().Be(PizzaPrice + tip,
            "RemainingAmount = Total − TotalPaid decides 'fully paid', so the tip has to be inside Total");
    }

    /// <summary>
    /// A NEGATIVE tip is the one request-controlled term that could still reach <c>Total</c>, and it
    /// reopens exactly the hole this file exists to close: the zero-clamp sits before the tip, so
    /// <c>tip: -12.99</c> on a 12.99 order lands <c>Total = 0</c> → <c>RemainingAmount = 0</c> →
    /// <c>PaymentStatus.Completed</c> → fidelity points awarded on an order nobody paid for.
    /// <para>
    /// This is a lever S0b itself created — the old compute path never added <c>Tip</c> at all, and
    /// the pre-calculated path took the whole total from the body, so there was no separate tip term
    /// to abuse.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_negative_tip_cannot_pay_for_an_order()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 0m } },
            tip = -PizzaPrice,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a tip is money the customer ADDS; a negative one is not a tip");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Orders.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    /// <summary>
    /// A bundle parent posted ALONE — no <c>ChildItems</c> — is the case the second half of the
    /// refusal guard exists for. Every other refusal test sends children, so the <c>||</c>
    /// short-circuits and <c>IsBundleAsync</c> never decides anything: delete that operand and those
    /// tests stay green while a bundle is billed at its bare <c>BasePrice</c>.
    /// </summary>
    [Fact]
    public async Task A_customer_cannot_order_a_bundle_parent_on_its_own()
    {
        var bundleId = await SeedBundleProductAsync();
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = bundleId, quantity = 1 } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = 8.00m } },
        });

        (await response.Content.ReadAsStringAsync()).Should().Contain("composed item");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Orders.AsNoTracking().AnyAsync())
            .Should().BeFalse("pricing it from BasePrice alone would undercharge the bundle");
    }

    // ---- The pricing formula, exercised directly ----------------------------------------------

    /// <summary>
    /// The interaction the plan called out as the reason S0b is not a one-liner: special rounding,
    /// the points credit and the tip all land on the same number, and the ORDER of the three is the
    /// whole behaviour.
    ///
    /// <para>
    /// Rounding fires only when a discount is active (<c>PriceRoundingUtility</c>), and it rounds
    /// to a whole unit. Applying it to the sale and then subtracting points reproduces exactly what
    /// the checkout page shows the customer; folding points in first would round the credit away.
    /// Adding the tip last keeps the customer's chosen figure intact — rounding a tip would be a
    /// change nobody asked for, in a number the customer picked deliberately.
    /// </para>
    /// </summary>
    [Theory]
    // items  delivery  disc  custDisc  points  tip     expected
    [InlineData(100.00, 0.00, 0.00, 0.00, 0.00, 0.00, 100.00)]  // nothing on: identity
    [InlineData(100.00, 5.00, 0.00, 0.00, 0.00, 0.00, 105.00)]  // delivery fee rides along
    [InlineData(100.00, 0.00, 0.00, 0.00, 0.00, 7.50, 107.50)]  // tip, no discount ⇒ no rounding
    [InlineData(100.00, 0.00, 0.00, 0.00, 10.00, 0.00, 90.00)]  // points credit
    // A discount switches rounding on. 100 − 10.40 = 89.60, fraction .60 ≥ .10 ⇒ ceiling 90.
    [InlineData(100.00, 0.00, 0.00, 10.40, 0.00, 0.00, 90.00)]
    // Same, fraction .05 < .10 ⇒ floor 89.
    [InlineData(100.00, 0.00, 0.00, 10.95, 0.00, 0.00, 89.00)]
    // Rounding applies to the SALE, then the points credit: ceil(89.60) = 90, − 5 = 85.
    [InlineData(100.00, 0.00, 0.00, 10.40, 5.00, 0.00, 85.00)]
    // ...and the tip is added after both, unrounded: 85 + 2.75.
    [InlineData(100.00, 0.00, 0.00, 10.40, 5.00, 2.75, 87.75)]
    // A points balance worth more than the food clamps at zero and never eats the tip.
    [InlineData(10.00, 0.00, 0.00, 0.00, 25.00, 4.00, 4.00)]
    public void The_total_is_the_rounded_sale_less_points_plus_tip(
        decimal items, decimal delivery, decimal discount, decimal customerDiscount,
        decimal points, decimal tip, decimal expected)
    {
        using var scope = Factory.Services.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<IOrderPricingService>();

        // SubTotal is stored net of extracted tax and RecalculateTotal recovers items as
        // SubTotal + Tax, so a non-zero tax here also pins that round-trip.
        var order = new Order
        {
            SubTotal = items - 2.00m,
            Tax = 2.00m,
            DeliveryFee = delivery,
            Discount = discount,
            CustomerDiscountAmount = customerDiscount,
            FidelityPointsDiscount = points,
            Tip = tip,
            CreatedBy = nameof(ServerAuthoritativeOrderTotalsTests),
        };

        pricing.RecalculateTotal(order);

        order.Total.Should().Be(expected);
    }

    /// <summary>
    /// The recompute runs a second time after points redemption, so it must be safe to run twice.
    /// It recomputes from the order's own columns rather than subtracting a delta — the version
    /// that subtracted would double-discount here.
    /// </summary>
    [Fact]
    public void Recalculating_twice_does_not_discount_twice()
    {
        using var scope = Factory.Services.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<IOrderPricingService>();

        var order = new Order
        {
            SubTotal = 50.00m,
            Tax = 0m,
            FidelityPointsDiscount = 5.00m,
            Tip = 1.00m,
            CreatedBy = nameof(ServerAuthoritativeOrderTotalsTests),
        };

        pricing.RecalculateTotal(order);
        var once = order.Total;
        pricing.RecalculateTotal(order);

        order.Total.Should().Be(once).And.Be(46.00m);
    }

    // ---- Delivery fee ------------------------------------------------------------------------

    /// <summary>
    /// The delivery fee defaults to 0, and that is a deliberate product decision rather than an
    /// oversight. The old hard-coded 5.00 fired only on the legacy compute path, which no client
    /// uses — every real customer order arrived via <c>/from-basket</c>, where the fee was never
    /// applied. Making the server authoritative would otherwise have started charging a fee no live
    /// tenant charges, as a silent side effect of a security fix.
    /// </summary>
    [Fact]
    public async Task A_delivery_order_is_not_charged_a_fee_by_default()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Delivery),
            customerName = "Guest",
            customerPhone = "+41000000000",
            deliveryAddress = new
            {
                addressLine1 = "Rue du Test 1",
                city = "Geneva",
                postalCode = "1200",
                country = "CH",
            },
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = PizzaPrice } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await SingleOrderAsync();
        order.DeliveryFee.Should().Be(0m, "OrderSettings:DeliveryFee defaults to 0; a tenant opts in per box");
        order.Total.Should().Be(PizzaPrice);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    /// <summary>
    /// A <c>ProductType.Menu</c> product — that type is what makes a product a bundle, and what
    /// <c>IsBundleAsync</c> keys on. No menu definition is needed: the guard refuses before any
    /// pricing is attempted.
    /// </summary>
    private async Task<Guid> SeedBundleProductAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var bundle = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Lunch Bundle",
            BasePrice = 8.00m,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 10,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            DisplayOrder = 90,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(ServerAuthoritativeOrderTotalsTests),
        };
        context.Products.Add(bundle);
        await context.SaveChangesAsync();
        return bundle.Id;
    }

    private async Task SeedPointsBalanceAsync(int points)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userId = Guid.Parse(TestAuthHandler.UserId);
        context.FidelityPointBalances.Add(new FidelityPointBalance
        {
            UserId = userId,
            CurrentPoints = points,
            LastUpdated = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(ServerAuthoritativeOrderTotalsTests),
        });
        await context.SaveChangesAsync();
    }

    private async Task<Order> SingleOrderAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().SingleAsync();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizzaId = (await context.Products.AsNoTracking().FirstAsync(p => p.Name == "Test Pizza")).Id;
    }
}
