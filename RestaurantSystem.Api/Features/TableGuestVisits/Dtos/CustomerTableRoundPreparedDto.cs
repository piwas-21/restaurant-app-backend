using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

public sealed record CustomerTableRoundPreparedDto(int SchemaVersion, CustomerTableRoundProofDto Proof,
    CustomerCheckoutQuoteDto Quote, string? TableLabel);

public sealed record CustomerTableRoundResolutionDto(int SchemaVersion, Guid OperationId, Guid ServiceSessionId,
    CustomerOrderOperationOutcome Outcome, Guid? OrderId);
