using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed record OrderAmendmentUnitScope(
    Guid OrderItemId,
    int StartOrdinal,
    int Count,
    bool WholeLine);

public interface IOrderAmendmentReservationGuard
{
    Task AssertUnitsMutableAsync(
        Guid orderId,
        IReadOnlyList<OrderAmendmentUnitScope> scopes,
        CancellationToken cancellationToken);
}

/// <summary>Fail-closed fallback while account payment reservations are not composed into the host.</summary>
public sealed class UnavailableOrderAmendmentReservationGuard : IOrderAmendmentReservationGuard
{
    public Task AssertUnitsMutableAsync(
        Guid orderId,
        IReadOnlyList<OrderAmendmentUnitScope> scopes,
        CancellationToken cancellationToken)
    {
        if (scopes.Count > 0)
        {
            throw new RestaurantSystem.Api.Common.Exceptions.ConflictException(
                "Existing order items cannot be amended until payment reservations can be checked. Refresh and try again.");
        }

        return Task.CompletedTask;
    }
}

public interface IOrderAmendmentFinancialResolution
{
    Task<OrderAmendmentFinancialPreviewDto> PreviewAsync(
        Order source,
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        Order? supplement,
        CancellationToken cancellationToken);

    Task StageAsync(
        OrderAmendment amendment,
        Order source,
        OrderAmendmentFinancialPreviewDto preview,
        CancellationToken cancellationToken);
}

public interface IOrderAmendmentQuoteService
{
    Task<OrderAmendmentQuoteDto> QuoteAsync(
        Guid orderId,
        OrderAmendmentQuoteRequest request,
        CancellationToken cancellationToken);
}

public interface IOrderAmendmentCommitService
{
    Task<OrderAmendmentCommitDto> CommitAsync(
        Guid orderId,
        OrderAmendmentCommitRequest request,
        CancellationToken cancellationToken);
}

public interface IOrderAmendmentQueryService
{
    Task<IReadOnlyList<OrderAmendmentHistoryDto>> ListAsync(
        Guid orderId,
        CancellationToken cancellationToken);

    Task<OrderAmendmentOperationLookupDto> LookupAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task<OrderAmendmentCommitDto> MaterializeCommittedResultAsync(
        OrderAmendment amendment,
        CancellationToken cancellationToken);
}

public enum OrderAmendmentOperationStatus
{
    Unknown = 0,
    Committed = 1
}

public sealed record OrderAmendmentOperationLookupDto(
    Guid OperationId,
    OrderAmendmentOperationStatus Status,
    OrderAmendmentCommitDto? Result);
