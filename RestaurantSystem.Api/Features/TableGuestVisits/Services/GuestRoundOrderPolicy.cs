using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

internal static class GuestRoundOrderPolicy
{
    public static void Validate(CreateOrderCommand command)
    {
        if (command.Type != OrderType.DineIn || command.TableNumber.HasValue
            || command.CustomerName is not null || command.CustomerEmail is not null
            || command.CustomerPhone is not null || command.PromoCode is not null
            || command.PointsToRedeem.HasValue || command.Tip != 0m || command.Notes is not null
            || command.DeliveryAddress is not null || command.Payments is null || command.Payments.Count != 0
            || command.HasUserLimitDiscount || command.UserLimitAmount != 0m
            || command.IsFocusOrder || command.Priority.HasValue || command.FocusReason is not null
            || !command.ItemsAreServerPriced)
        {
            throw new BadRequestException("Guest table rounds must use the visit-bound basket checkout.");
        }
    }

    public static async Task AttachVisitAsync(
        ApplicationDbContext context, Order order, TableServiceSession session,
        CancellationToken cancellationToken)
    {
        order.TableId = session.TableId;
        order.TableNumber = session.TableNumber;
        order.TableLabel = await context.Tables.AsNoTracking()
            .Where(table => table.Id == session.TableId)
            .Select(table => table.TableNumber)
            .SingleAsync(cancellationToken);
        order.ServiceSessionId = session.Id;
    }

    public static void RecordAccountChange(
        ApplicationDbContext context, ITableGuestRoundOperationStore operations,
        TableGuestRoundContext guestContext, TableServiceSession session,
        TableGuestParticipant participant, Order order, string auditId, DateTime createdAt)
    {
        session.RecordAccountChange();
        context.Set<TableGuestRoundOperation>().Add(operations.CreateOperation(
            guestContext, participant.Id, order.Id, auditId, createdAt));
    }
}
