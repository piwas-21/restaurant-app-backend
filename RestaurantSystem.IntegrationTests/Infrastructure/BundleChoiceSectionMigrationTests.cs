using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class BundleChoiceSectionMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Audit = nameof(BundleChoiceSectionMigrationTests);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Actual_backfill_groups_only_unique_memberships_and_preserves_money_and_identities()
    {
        await using var context = fixture.CreateContext();
        var parentProduct = Product("Menu Tacos 3");
        parentProduct.Type = ProductType.Menu;
        var meat = Product("Kebab");
        var ambiguous = Product("Shared option");
        var definition = new MenuDefinition { Id = Guid.NewGuid(), Product = parentProduct, CreatedBy = Audit };
        var meats = Section(definition, meat, ambiguous);
        var other = Section(definition, ambiguous);
        definition.Sections = [meats, other];
        context.Products.AddRange(parentProduct, meat, ambiguous);
        context.MenuDefinitions.Add(definition);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "BACKFILL-CHOICES",
            CreatedBy = Audit,
            Type = OrderType.Takeaway,
            Total = 19m,
            SubTotal = 19m,
            OrderDate = DateTime.UtcNow
        };
        var parent = new OrderItem
        {
            Id = Guid.NewGuid(),
            Order = order,
            Product = parentProduct,
            ProductName = parentProduct.Name,
            Quantity = 1,
            UnitPrice = 19m,
            ItemTotal = 19m,
            CreatedBy = Audit
        };
        var child = OrderChild(order, parent, meat, OrderItemKind.BundleChild);
        var uncertain = OrderChild(order, parent, ambiguous, OrderItemKind.BundleChild);
        var paidSide = OrderChild(order, parent, meat, OrderItemKind.SideItem);
        order.Items = [parent, child, uncertain, paidSide];
        context.Orders.Add(order);
        var basket = new Basket { Id = Guid.NewGuid(), SessionId = "backfill", Total = 19m, CreatedBy = Audit };
        var basketParent = new BasketItem
        {
            Id = Guid.NewGuid(),
            Basket = basket,
            Product = parentProduct,
            Quantity = 1,
            UnitPrice = 19m,
            ItemTotal = 19m,
            CreatedBy = Audit
        };
        var basketChild = new BasketItem
        {
            Id = Guid.NewGuid(),
            Basket = basket,
            ParentBasketItem = basketParent,
            Product = meat,
            Quantity = 2,
            SpecialInstructions = "Keep onions",
            CreatedBy = Audit
        };
        var basketUncertain = new BasketItem
        {
            Id = Guid.NewGuid(),
            Basket = basket,
            ParentBasketItem = basketParent,
            Product = ambiguous,
            Quantity = 1,
            CreatedBy = Audit
        };
        basket.Items = [basketParent, basketChild, basketUncertain];
        context.Baskets.Add(basket);
        await context.SaveChangesAsync();
        // Run the shipped migration's actual PostgreSQL statements, against hostile historical
        // rows with unknown sections. Do not reproduce the matching algorithm in the oracle.
        foreach (var sql in new PreserveBundleChoiceSections().UpOperations.OfType<SqlOperation>())
            await context.Database.ExecuteSqlRawAsync(sql.Sql);
        context.ChangeTracker.Clear();
        var lines = await context.OrderItems.Where(item => item.OrderId == order.Id).ToListAsync();
        lines.Should().HaveCount(4);
        lines.Single(item => item.Id == child.Id).SectionId.Should().Be(meats.Id);
        lines.Single(item => item.Id == uncertain.Id).SectionId.Should().BeNull();
        lines.Single(item => item.Id == paidSide.Id).SectionId.Should().BeNull();
        lines.Single(item => item.Id == parent.Id).ItemTotal.Should().Be(19m);
        lines.Single(item => item.Id == child.Id).Quantity.Should().Be(2);
        var savedOrder = await context.Orders.SingleAsync(item => item.Id == order.Id);
        savedOrder.Total.Should().Be(19m);
        var savedChild = await context.BasketItems.SingleAsync(item => item.Id == basketChild.Id);
        savedChild.SectionId.Should().Be(meats.Id);
        savedChild.Quantity.Should().Be(2);
        savedChild.SpecialInstructions.Should().Be("Keep onions");
        (await context.BasketItems.SingleAsync(item => item.Id == basketUncertain.Id)).SectionId.Should().BeNull();
    }

    private static Product Product(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedBy = Audit,
        IsActive = true,
        IsAvailable = true
    };

    private static MenuSection Section(MenuDefinition definition, params Product[] products)
    {
        var section = new MenuSection { Id = Guid.NewGuid(), MenuDefinition = definition, Name = "Choices", CreatedBy = Audit };
        section.Items = products.Select(product => new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSection = section,
            Product = product,
            CreatedBy = Audit
        }).ToList();
        return section;
    }

    private static OrderItem OrderChild(Order order, OrderItem parent, Product product, OrderItemKind kind) => new()
    {
        Id = Guid.NewGuid(),
        Order = order,
        ParentOrderItem = parent,
        Product = product,
        ProductName = product.Name,
        Quantity = 2,
        Kind = kind,
        CreatedBy = Audit
    };
}
