using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Queries.GetCustomerAddressesQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Addresses;

public sealed partial class CustomerAddressesTests
{
    [Fact]
    public async Task Many_saved_addresses_use_one_bounded_untracked_database_projection()
    {
        var owner = await CustomerAsync();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.UserAddresses.AddRange(Enumerable.Range(1, 500).Reverse().Select(index => Address(owner, index)));
            await seed.SaveChangesAsync();
        }
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.UserId).Returns(owner);
        actor.SetupGet(value => value.IsAuthenticated).Returns(true);
        actor.SetupGet(value => value.Role).Returns(UserRole.Customer);
        var reads = new AddressReadCapture();
        await using var context = DatabaseFixture.CreateContext(reads);
        (await context.UserAddresses.CountAsync()).Should().Be(500);
        reads.Queries.Should().ContainSingle("a known read proves that the interceptor is attached");
        reads.Queries.Clear();
        var handler = new GetCustomerAddressesQueryHandler(context, actor.Object);
        var page = await handler.Handle(new(1), CancellationToken.None);
        page.Success.Should().BeTrue();
        page.Data!.Items.Select(value => value.Id).Should().Equal(Enumerable.Range(1, 20).Select(Id));
        page.Data.NextAfterId.Should().Be(Id(20));
        reads.Queries.Should().ContainSingle().Which.Should().Contain("LIMIT");
        context.ChangeTracker.Entries().Should().BeEmpty("private list reads must not track the customer's address collection");
    }

    private sealed class AddressReadCapture : DbCommandInterceptor
    {
        public List<string> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
