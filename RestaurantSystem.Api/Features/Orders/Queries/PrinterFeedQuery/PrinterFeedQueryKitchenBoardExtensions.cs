using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

internal static class PrinterFeedQueryKitchenBoardExtensions
{
    internal static IQueryable<Order> WithoutCompletedInitialBoardWork(
        this IQueryable<Order> orders, ApplicationDbContext context) =>
        orders.Where(order => !context.KitchenBoardWorkCompletions.Any(work =>
            work.OrderId == order.Id
            && work.WorkItemId == order.Id
            && work.Kind == KitchenBoardWorkKind.InitialOrder));
}
