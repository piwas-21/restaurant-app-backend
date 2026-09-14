using System.Data;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;

/// <summary>
/// Takes ONE tender against a table's WHOLE bill (every open order at the table,
/// see <see cref="GetTableBillQuery.GetTableBillQuery"/>) and spreads it across
/// those orders oldest-round-first. A guest pays once for the table; the till
/// must not make the waiter repeat the amount per ordering round.
/// </summary>
/// <remarks>
/// Overpayment is rejected (mirrors the cashier dialog's rule): allocation only ever
/// moves money forward onto outstanding orders, never backwards into an overpaid one.
/// </remarks>
public record AddTableBillPaymentCommand : ICommand<ApiResponse<TableBillDto>>
{
    public required int TableNumber { get; set; }

    /// <summary>Set internally when the legacy route resolves one explicit visit.</summary>
    [JsonIgnore]
    public Guid? ServiceSessionId { get; set; }
    public required PaymentMethod PaymentMethod { get; set; }
    public required decimal Amount { get; set; }
    public string? Currency { get; set; }
    [JsonRequired]
    public Guid OperationId { get; set; }
    public string? TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; }
    public string? PaymentNotes { get; set; }
}
