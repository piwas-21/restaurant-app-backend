using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public abstract class CustomerOrderOperationFixture(DatabaseFixture database) : IAsyncLifetime
{
    protected DatabaseFixture Database { get; } = database;
    protected string Session { get; } = Guid.NewGuid().ToString("D");
    protected Guid Operation { get; } = Guid.NewGuid();
    protected const string Payload = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public Task InitializeAsync() => Database.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    protected static CustomerOrderOperationStore Store(ApplicationDbContext context,
        Guid? userId = null, bool authenticated = false, UserRole? role = null,
        bool machine = false, bool rejectedAuthorization = false)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(value => value.UserId).Returns(userId);
        user.SetupGet(value => value.IsAuthenticated).Returns(authenticated);
        user.SetupGet(value => value.Role).Returns(role);
        user.SetupGet(value => value.IsApiToken).Returns(machine);
        var http = new DefaultHttpContext();
        if (rejectedAuthorization) http.Request.Headers.Authorization = "Bearer rejected-fixture";
        return new(context, new BasketWriteCoordinator(context), user.Object,
            new HttpContextAccessor { HttpContext = http });
    }
    protected static async Task<Guid> SaveOrderAsync(ApplicationDbContext context)
    {
        var id = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = id,
            OrderNumber = id.ToString("N")[..20],
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "OperationFixture"
        });
        await context.SaveChangesAsync();
        return id;
    }
}
