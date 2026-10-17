using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.BillCharges.Commands;

public sealed record AddOrderBillChargesCommand(AddBillChargesRequest Request) : ICommand<ApiResponse<OrderDto>>
{
    [JsonIgnore] public Guid OrderId { get; set; }
}
public sealed class AddOrderBillChargesCommandHandler(ApplicationDbContext context, IBillChargeService charges,
    IOrderDisplayCurrencyResolver currencyResolver, IOrderResponseProjector responses)
    : ICommandHandler<AddOrderBillChargesCommand, ApiResponse<OrderDto>>
{
    public async Task<ApiResponse<OrderDto>> Handle(AddOrderBillChargesCommand command, CancellationToken cancellationToken)
    {
        var input = command.Request;
        ValidateRequest(input);
        await using var scope = await OrderAccountMutationScope.BeginAsync(context, command.OrderId, cancellationToken);
        var order = await context.Orders.Include(o => o.Payments).Include(o => o.ServiceSession).Include(o => o.ExternalReference)
            .SingleOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order not found.");
        var existing = OrderBillChargeReader.Read(order).Where(c => c.OperationId == input.OperationId).ToList();
        if (existing.Count > 0)
        {
            ValidateReplay(input, existing);
            await scope.CommitAsync(cancellationToken);
            return ApiResponse<OrderDto>.SuccessWithData(await responses.ProjectAsync(order, cancellationToken));
        }
        await AssertCanAddAsync(order, input.ExpectedVersion, cancellationToken);
        var currency = await OrderNativeAcceptedCurrency.ReadOrderCurrencyEvidenceAsync(context, order, cancellationToken)
            ?? order.ServiceSession?.Currency ?? currencyResolver.Resolve(order);
        await charges.ApplyAsync(order, input.Charges, input.OperationId, currency, cancellationToken);
        order.PaymentStatus = PaymentStatus.Completed;
        if (order.RemainingAmount > 0)
            order.PaymentStatus = order.TotalPaid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Pending;
        scope.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(await responses.ProjectAsync(order, cancellationToken));
    }
    private static void ValidateRequest(AddBillChargesRequest input)
    {
        if (input.OperationId == Guid.Empty || input.ExpectedVersion <= 0 || input.Charges is not { Count: > 0 } || input.Charges.Any(c => c is null) || !input.Charges.Any(c => c.Amount > 0))
            throw new BadRequestException("A bill charge needs an operation identity, current order version and selected fields.");
    }

    private static void ValidateReplay(AddBillChargesRequest input, List<OrderBillCharge> existing)
    {
        var positive = input.Charges.Where(c => c.Amount > 0).ToList();
        if (existing.Count != positive.Count || existing.Any(c => !positive.Any(i => i.Key == c.Key && i.Amount == c.Amount)))
            throw new ConflictException("This operation identity already belongs to different bill charges.");
    }

    private async Task AssertCanAddAsync(Order order, int expectedVersion, CancellationToken cancellationToken)
    {
        if (order.Version != expectedVersion) throw new ConflictException("The order changed. Refresh before adding charges.");
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded || order.ExternalReference is not null
            || order.ServiceSessionId.HasValue && order.ServiceSession?.Status != TableServiceSessionStatus.Open
            || order.Payments.Any(p => p.IsRefunded || p.RefundedAmount > 0 || p.Status == PaymentStatus.Processing))
            throw new ConflictException("Resolve the closed bill, refund or pending payment before adding charges.");
        var attempts = order.ServiceSessionId.HasValue
            ? await context.AccountPaymentAttempts.Where(a => a.ServiceSessionId == order.ServiceSessionId)
                .Select(a => a.State).ToListAsync(cancellationToken) : [];
        if (attempts.Any(state => state.HoldsReservation()))
            throw new ConflictException("Confirm the pending table payment result before changing the bill.");
        await RestaurantSystem.Api.Features.OrderAmendments.Services.OrderBillingCreditConsistency.AssertAsync(context, [order.Id], cancellationToken);
        var resolutions = await context.OrderAmendments.AsNoTracking().Where(a => a.SourceOrderId == order.Id
            && a.State == OrderAmendmentState.Committed).Select(a => a.FinancialResolutionJson).ToListAsync(cancellationToken);
        if (resolutions.Any(RestaurantSystem.Api.Features.OrderAmendments.Services.OrderAmendmentFinancialGuard.IsUnresolved))
            throw new ConflictException("Resolve the previous correction before adding bill charges.");
    }

}
