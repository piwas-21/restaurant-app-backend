using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public interface IOrderBillingAdjustmentWriter
{
    Task StageUnpaidCreditAsync(Order source, OrderAmendment amendment,
        OrderAmendmentFinancialPreviewDto preview, CancellationToken cancellationToken);
}
