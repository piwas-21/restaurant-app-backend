using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Order creation is ANONYMOUS — <c>POST /api/Orders</c> and <c>/from-basket</c> carry no
/// <c>[Authorize]</c>, and <c>Program.cs</c> registers no fallback policy. Until this file existed,
/// the payment a caller declared there was taken at face value twice over:
/// <list type="number">
/// <item>any method other than <c>Cash</c> was written straight to <c>PaymentStatus.Completed</c>, so
/// a stranger could hand themselves a paid order; and</item>
/// <item><c>TransactionId</c>/<c>ReferenceNumber</c>/<c>CardLastFourDigits</c>/<c>CardType</c>/
/// <c>PaymentGateway</c> were copied verbatim from the request body into the ledger, so the
/// fabricated payment came with a fabricated reference to match.</item>
/// </list>
/// <para>
/// The only thing standing in the way was <c>disabled: true</c> on the non-cash radios in
/// <c>frontend/src/config/paymentMethods.ts</c> — a client-side default, not a control. Nothing in
/// the suite posted a non-cash order, which is why the hole survived a payment refactor and a
/// dedicated authorization pass.
/// </para>
/// <para>
/// The staff cases are here deliberately. A blanket "no non-cash tenders" rule would satisfy every
/// refusal below and quietly take the cashier's till with it.
/// </para>
/// </summary>
public class AnonymousOrderPaymentHardeningTests : IntegrationTestBase
{
    private Guid _pizzaId;

    public AnonymousOrderPaymentHardeningTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// Every tender a guest is not allowed to assert.
    /// </summary>
    /// <remarks>
    /// <c>OnlinePayment</c> USED to be in this list, with a note saying that if a future change ever
    /// made its case pass, it had to be because the allow-list widened deliberately rather than
    /// because the guard was dropped. S5 is that deliberate widening, and the note did its job — the
    /// case failed the moment the allow-list changed. What makes it safe is not the method name but
    /// where it settles: an online tender is created <c>Processing</c>, counts for nothing, and can
    /// only be completed by the settle path, which re-fetches from Stripe before it writes. The
    /// properties that replace this row are in
    /// <see cref="A_guest_may_declare_an_online_tender_and_it_is_not_paid"/> and
    /// <see cref="The_amount_of_an_online_tender_is_the_orders_not_the_callers"/>.
    /// </remarks>
    [Theory]
    [InlineData(PaymentMethod.CreditCard)]
    [InlineData(PaymentMethod.DebitCard)]
    [InlineData(PaymentMethod.MobilePayment)]
    [InlineData(PaymentMethod.BankTransfer)]
    public async Task A_guest_cannot_declare_a_non_cash_tender_when_placing_an_order(PaymentMethod method)
    {
        AuthenticateAsAnonymous();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(method, 12.99m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"{method} settles somewhere the order-creation request cannot see");
    }

    /// <summary>
    /// The status code above is the claim; this is what it stands for. A guard that ran after the
    /// handler had written would satisfy the refusal while the order sat in the database paid.
    /// <c>AddPayments</c> runs inside the handler's transaction and before <c>SaveChangesAsync</c>,
    /// so the rollback is the thing being pinned here.
    /// </summary>
    [Fact]
    public async Task A_refused_tender_leaves_no_order_and_no_payment_behind()
    {
        AuthenticateAsAnonymous();

        await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.CreditCard, 12.99m));

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await context.Orders.AsNoTracking().AnyAsync())
            .Should().BeFalse("the handler's transaction must roll back, not half-commit");
        (await context.OrderPayments.AsNoTracking().AnyAsync())
            .Should().BeFalse();
    }

    /// <summary>
    /// <c>/from-basket</c> is the endpoint the checkout page actually posts to. It repackages into
    /// <c>CreateOrderCommand</c> and forwards <c>Payments</c> untouched, so it inherits the guard —
    /// but that is an implementation detail today and a regression tomorrow if the two paths diverge.
    /// <para>
    /// The basket is stocked first, and the assertion is on the REASON rather than the status code.
    /// Both matter: an empty basket 400s on its own, so the obvious version of this test passed
    /// unchanged with the guard removed — refused for a reason that had nothing to do with payment.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_from_basket_entry_point_refuses_the_same_tender()
    {
        AuthenticateAsAnonymous();
        Client.DefaultRequestHeaders.Remove("X-Session-Id");
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());

        var stocked = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _pizzaId,
            Quantity = 1
        });
        stocked.StatusCode.Should().Be(HttpStatusCode.OK,
            "an empty basket would refuse the order before the tender is ever looked at");

        var response = await PostAsJsonAsync("/api/orders/from-basket", new CreateOrderFromBasketCommand
        {
            Type = OrderType.Takeaway,
            CustomerName = "Guest",
            Payments = [new CreateOrderPaymentDto { PaymentMethod = PaymentMethod.CreditCard, Amount = 12.99m }]
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain(nameof(PaymentMethod.CreditCard),
                "the refusal must be about the tender, not about some earlier validation failure");
    }

    /// <summary>
    /// Cash is what a guest may legitimately declare, and it must still work — the refusals above
    /// are all satisfied by an endpoint that rejects every order.
    /// </summary>
    [Fact]
    public async Task A_guest_may_still_place_a_cash_order_and_it_is_not_paid()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.Cash, 12.99m));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = await context.Orders.AsNoTracking().SingleAsync();
        var payment = await context.OrderPayments.AsNoTracking().SingleAsync();

        payment.Status.Should().Be(PaymentStatus.Pending, "cash is counted at the till, not on the wire");
        order.TotalPaid.Should().Be(0m, "a Pending tender is not captured, so it cannot count as money held");
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
    }

    /// <summary>
    /// The widening S5 made, and the reason it is safe. A guest may now say "I will pay online",
    /// but saying it moves no money: the tender lands <c>Processing</c>, which
    /// <c>PaymentStatus.IsCaptured()</c> excludes, so <c>TotalPaid</c> stays 0 and the order is
    /// still unpaid. Only the settle path — which asks Stripe first — can complete it.
    /// </summary>
    [Fact]
    public async Task A_guest_may_declare_an_online_tender_and_it_is_not_paid()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.OnlinePayment, 12.99m));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payment = await context.OrderPayments.AsNoTracking().SingleAsync();
        var order = await context.Orders.AsNoTracking().SingleAsync();

        payment.Status.Should().Be(PaymentStatus.Processing,
            "Processing, not Pending — AddPaymentToOrder deletes every Pending tender on an order, "
            + "so a cashier taking cash would otherwise erase the record that money is live at Stripe");
        payment.Status.IsCaptured().Should().BeFalse();
        order.TotalPaid.Should().Be(0m, "declaring an intent to pay is not paying");
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
    }

    /// <summary>
    /// The declared amount is the last money field a caller still controlled on this anonymous
    /// endpoint. A tender for 0.01 against a 12.99 order would settle as <c>PartiallyPaid</c>
    /// against a Stripe charge that took the full total — so the amount comes from the order.
    /// </summary>
    [Fact]
    public async Task The_amount_of_an_online_tender_is_the_orders_not_the_callers()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.OnlinePayment, 0.01m));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payment = await context.OrderPayments.AsNoTracking().SingleAsync();
        var order = await context.Orders.AsNoTracking().SingleAsync();

        payment.Amount.Should().Be(order.Total).And.Be(12.99m,
            "the server priced this order; the request only named a method");
    }

    /// <summary>
    /// The kitchen-feed property, and the reason the tender has to exist at CREATION rather than
    /// being minted at settle. Dine-in normally auto-confirms here, and <c>PrinterFeedQuery</c>
    /// prints any <c>Confirmed</c> order — so without this the ticket for an unpaid order is on the
    /// pass before Stripe is ever called, and no later code can un-print it.
    /// </summary>
    [Fact]
    public async Task A_dine_in_order_paying_online_does_not_auto_confirm()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(
            PaymentMethod.OnlinePayment, 12.99m, OrderType.DineIn));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = await context.Orders.AsNoTracking().SingleAsync();
        order.Status.Should().Be(OrderStatus.Pending, "an unpaid order must not reach the kitchen feed");
    }

    /// <summary>
    /// The control for the case above. Suppressing the dine-in auto-confirm for EVERY order would
    /// satisfy it while silently changing how every cash dine-in order in the restaurant behaves.
    /// </summary>
    [Fact]
    public async Task A_dine_in_cash_order_still_auto_confirms()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(
            PaymentMethod.Cash, 12.99m, OrderType.DineIn));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = await context.Orders.AsNoTracking().SingleAsync();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    /// <summary>
    /// The five gateway fields are gone from <c>CreateOrderPaymentDto</c> rather than merely ignored,
    /// so a body carrying them binds without them. Asserted against the persisted row, because
    /// "the DTO no longer has the property" is a fact about the source, not about what got written —
    /// a permissive binder or a re-added property would break this and nothing else.
    /// </summary>
    [Fact]
    public async Task Gateway_metadata_in_the_request_body_never_reaches_the_ledger()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = 12.99m } },
            payments = new[]
            {
                new
                {
                    paymentMethod = nameof(PaymentMethod.Cash),
                    amount = 12.99m,
                    transactionId = "pi_forged_by_the_client",
                    referenceNumber = "REF-FORGED",
                    cardLastFourDigits = "4242",
                    cardType = "Visa",
                    paymentGateway = "Stripe"
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await context.OrderPayments.AsNoTracking().SingleAsync();

        payment.TransactionId.Should().BeNull("a reference the client invented is not evidence of a payment");
        payment.ReferenceNumber.Should().BeNull();
        payment.CardLastFourDigits.Should().BeNull();
        payment.CardType.Should().BeNull();
        payment.PaymentGateway.Should().BeNull();
    }

    /// <summary>
    /// A signed-in customer is not staff. Without this case the guard's predicate could be widened
    /// from <c>IsStaff</c> to <c>IsAuthenticated</c> and every assertion in this file would still
    /// pass — while the hole reopened for every registered customer, which on a self-serve signup
    /// is the likeliest attacker of the lot.
    /// </summary>
    [Fact]
    public async Task An_authenticated_customer_is_not_staff()
    {
        AuthenticateAsUser();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.CreditCard, 12.99m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "having an account is not the same as standing behind the till");
    }

    /// <summary>
    /// Conversely: narrowing the predicate from <c>IsStaff</c> to <c>IsAdmin</c> also passes every
    /// other case here, because the only staff identity used elsewhere in this file is Admin — and
    /// it would take the cashier, the kitchen display and the floor view down with it. These are the
    /// three roles that no other assertion can see. Same drift <c>ICurrentUserService.IsStaff</c>
    /// warns about in its own remarks.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    [InlineData(UserRole.Server)]
    public async Task Every_staff_role_may_declare_a_non_cash_tender(UserRole role)
    {
        AuthenticateAsRole(role);

        var response = await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.CreditCard, 12.99m));

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} is back-of-house");
    }

    /// <summary>
    /// The control for the theory at the top: the till takes cards, and it does so through the
    /// staff-only add-payment endpoint that carries a real transaction reference. Without this,
    /// deleting non-cash support outright would look like a clean pass.
    /// </summary>
    [Fact]
    public async Task Staff_can_still_take_a_card_payment_on_the_till()
    {
        AuthenticateAsAnonymous();
        var created = await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.Cash, 12.99m));
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orderId = (await context.Orders.AsNoTracking().SingleAsync()).Id;

        AuthenticateAsAdmin();
        var paid = await PostAsJsonAsync($"/api/Orders/{orderId}/payments", new
        {
            paymentMethod = nameof(PaymentMethod.CreditCard),
            amount = 12.99m,
            transactionId = "till-terminal-0099"
        });

        paid.StatusCode.Should().Be(HttpStatusCode.OK, "the cashier has the card terminal in hand");

        using var after = Factory.Services.CreateScope();
        var afterContext = after.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var card = await afterContext.OrderPayments.AsNoTracking()
            .SingleAsync(p => p.PaymentMethod == PaymentMethod.CreditCard);

        card.Status.Should().Be(PaymentStatus.Completed);
        card.TransactionId.Should().Be("till-terminal-0099", "the staff path is where a real reference belongs");
    }

    /// <summary>
    /// Staff are exempt from the tender allow-list, but not from the "nothing is paid at creation"
    /// rule — the till completes through <c>AddPaymentToOrder</c>, which is where the reference and
    /// the human who took the money both live.
    /// </summary>
    [Fact]
    public async Task A_staff_created_order_is_also_unpaid_until_the_payment_path_runs()
    {
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync("/api/orders", NewOrder(PaymentMethod.CreditCard, 12.99m));
        response.StatusCode.Should().Be(HttpStatusCode.OK, "staff may declare any tender");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payment = await context.OrderPayments.AsNoTracking().SingleAsync();
        var order = await context.Orders.AsNoTracking().SingleAsync();

        payment.Status.Should().Be(PaymentStatus.Pending);
        order.TotalPaid.Should().Be(0m);
    }

    private CreateOrderCommand NewOrder(
        PaymentMethod method, decimal amount, OrderType type = OrderType.Takeaway) => new()
        {
            Type = type,
            CustomerName = "Guest",
            TableNumber = type == OrderType.DineIn ? 1 : null,
            Items = [new CreateOrderItemDto { ProductId = _pizzaId, Quantity = 1, UnitPrice = 12.99m }],
            Payments = [new CreateOrderPaymentDto { PaymentMethod = method, Amount = amount }]
        };

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizzaId = (await context.Products.AsNoTracking().FirstAsync(p => p.Name == "Test Pizza")).Id;

        // TestAuthHandler.StaffUserId is deliberately absent from the shared seed, on the stated
        // grounds that "nothing that authenticates through this handler requires the caller's own
        // row to exist". That holds right up until the caller CREATES something owned by them:
        // Order.UserId is a real foreign key, so the staff-role cases below 500 on the insert
        // rather than exercising the tender guard they exist to pin. Seeded here rather than in
        // TestDataSeeder so the shared fixture keeps its current shape for every other suite.
        context.Users.Add(new ApplicationUser
        {
            Id = Guid.Parse(TestAuthHandler.StaffUserId),
            UserName = TestAuthHandler.StaffUserName,
            NormalizedUserName = TestAuthHandler.StaffUserName.ToUpperInvariant(),
            Email = TestAuthHandler.StaffUserName,
            NormalizedEmail = TestAuthHandler.StaffUserName.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "Back",
            LastName = "OfHouse",
            Role = UserRole.Cashier,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed",
            RefreshToken = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await context.SaveChangesAsync();
    }
}
