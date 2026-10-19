using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class OrderAddressOwnershipTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _foreign = Guid.NewGuid();
    private Guid _ownedAddress;
    private Guid _foreignAddress;
    public async Task InitializeAsync()
    {
        await database.ResetDatabaseAsync();
        await using var context = database.CreateContext();
        await TestUserSeeder.SeedUserAsync(context, _owner);
        await TestUserSeeder.SeedUserAsync(context, _foreign);
        var owned = Address(_owner, "Owned street");
        var foreign = Address(_foreign, "Foreign private street");
        _ownedAddress = owned.Id; _foreignAddress = foreign.Id;
        context.UserAddresses.AddRange(owned, foreign);
        await context.SaveChangesAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task Guest_cannot_copy_a_saved_customer_address()
    {
        await using var context = database.CreateContext();
        var copy = () => Factory(context).CreateAsync(new() { UseAddressId = _foreignAddress }, Guid.NewGuid(), null, default);
        await copy.Should().ThrowAsync<ForbiddenException>();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Customer_cannot_copy_another_owners_address_or_fall_back_to_inline_fields(bool inline)
    {
        await using var context = database.CreateContext();
        var copy = () => Factory(context).CreateAsync(new()
        {
            UseAddressId = _foreignAddress,
            AddressLine1 = inline ? "Inline replacement" : null,
            City = "Fixture city",
            PostalCode = "1000",
            Country = "CH"
        }, Guid.NewGuid(), _owner, default);
        await copy.Should().ThrowAsync<NotFoundException>();
        context.ChangeTracker.HasChanges().Should().BeFalse();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_deleted_explicit_address_does_not_silently_choose_the_default(bool deleted)
    {
        await using var context = database.CreateContext();
        var requested = Guid.NewGuid();
        if (deleted)
        {
            var address = Address(_owner, "Deleted street");
            address.IsDefault = false; address.IsDeleted = true; requested = address.Id;
            context.UserAddresses.Add(address);
            await context.SaveChangesAsync();
        }
        var copy = () => Factory(context).CreateAsync(new() { UseAddressId = requested }, Guid.NewGuid(), _owner, default);
        await copy.Should().ThrowAsync<NotFoundException>();
    }
    [Fact]
    public async Task Owned_saved_address_is_a_frozen_snapshot_for_the_new_order()
    {
        await using var context = database.CreateContext();
        var order = Guid.NewGuid();
        var snapshot = await Factory(context).CreateAsync(new() { UseAddressId = _ownedAddress }, order, _owner, default);
        snapshot.Should().NotBeNull();
        snapshot!.OrderId.Should().Be(order);
        snapshot.UserAddressId.Should().Be(_ownedAddress);
        snapshot.AddressLine1.Should().Be("Owned street");
        snapshot.City.Should().Be("Fixture city");
        snapshot.PostalCode.Should().Be("1000");
        snapshot.Country.Should().Be("CH");
        snapshot.CreatedBy.Should().Be("AddressFixture");
        (await context.UserAddresses.SingleAsync(address => address.Id == _ownedAddress)).AddressLine1 = "Later edit";
        snapshot.AddressLine1.Should().Be("Owned street");
    }
    [Fact]
    public async Task No_explicit_id_preserves_owned_default_and_guest_inline_paths()
    {
        await using var context = database.CreateContext();
        var factory = Factory(context);
        (await factory.CreateAsync(null, Guid.NewGuid(), _owner, default))!.UserAddressId.Should().Be(_ownedAddress);
        (await factory.CreateAsync(null, Guid.NewGuid(), null, default)).Should().BeNull();
        var inline = await factory.CreateAsync(new()
        {
            AddressLine1 = "Guest street",
            City = "Guest city",
            PostalCode = "2000",
            Country = "NL"
        }, Guid.NewGuid(), null, default);
        inline!.AddressLine1.Should().Be("Guest street");
        inline.UserAddressId.Should().BeNull();
    }
    private static OrderAddressFactory Factory(ApplicationDbContext context)
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(value => value.GetAuditIdentifier()).Returns("AddressFixture");
        return new(context, user.Object, NullLogger<OrderAddressFactory>.Instance);
    }
    internal static UserAddress Address(Guid userId, string street) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Label = "Fixture",
        AddressLine1 = street,
        City = "Fixture city",
        PostalCode = "1000",
        Country = "CH",
        IsDefault = true,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "AddressFixture"
    };
}
