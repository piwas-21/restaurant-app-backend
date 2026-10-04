using RestaurantSystem.Api.Features.OrderAmendments.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public interface IOrderAmendmentResolutionService
{
    Task<OrderAmendmentResolutionContextDto> ContextAsync(Guid orderId, Guid amendmentId,
        CancellationToken cancellationToken);

    Task<OrderAmendmentResolutionQuoteDto> QuoteAsync(Guid orderId, Guid amendmentId,
        OrderAmendmentResolutionQuoteRequest request, CancellationToken cancellationToken);

    Task<OrderAmendmentResolutionStartOutcomeDto> StartAsync(Guid orderId, Guid amendmentId,
        OrderAmendmentResolutionStartRequest request, CancellationToken cancellationToken);

    Task<OrderAmendmentResolutionResultDto> ConfirmTillAsync(Guid operationId,
        ManualTillConfirmationRequest request, CancellationToken cancellationToken);

    Task<OrderAmendmentResolutionResultDto> LookupAsync(Guid operationId,
        CancellationToken cancellationToken);

    Task<OrderAmendmentResolutionStartOutcomeDto> LookupByClientKeyAsync(Guid orderId, Guid amendmentId,
        Guid clientOperationId, CancellationToken cancellationToken);

    Task<OrderAmendmentResolutionResultDto> RecoverAsync(Guid operationId,
        CancellationToken cancellationToken);

    Task<OrderAmendmentResolutionRecoveryDto> RecoverForAmendmentAsync(Guid orderId, Guid amendmentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderAmendmentResolutionRecoveryDto>> ListRecoverableForOrderAsync(Guid orderId,
        CancellationToken cancellationToken);
}
