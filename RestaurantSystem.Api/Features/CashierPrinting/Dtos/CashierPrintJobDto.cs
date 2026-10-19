using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.CashierPrinting.Dtos;

public sealed record CashierPrintJobDto(Guid JobId, string Status, bool IsAutomatic,
    DateTime CreatedAt, OrderDto? Receipt = null);

public sealed record QueueCashierPrintRequest([property: JsonRequired] Guid OperationId, Guid? OrderId,
    Guid? ServiceSessionId, [property: JsonRequired] int ExpectedVersion);

public sealed record CompleteCashierPrintRequest(string Status);
