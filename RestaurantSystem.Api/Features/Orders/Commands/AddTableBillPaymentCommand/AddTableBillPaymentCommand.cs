using System.Data;
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
    public required PaymentMethod PaymentMethod { get; set; }
    public required decimal Amount { get; set; }
    public string? TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; }
    public string? PaymentNotes { get; set; }
}

public class AddTableBillPaymentCommandHandler : ICommandHandler<AddTableBillPaymentCommand, ApiResponse<TableBillDto>>
{
    /// <summary>Same rounding tolerance the per-order payment status recompute uses.</summary>
    private const decimal Tolerance = 0.01m;

    private readonly ApplicationDbContext _context;
    private readonly IOrderPaymentApplicator _paymentApplicator;
    private readonly ITableBillAssembler _billAssembler;
    private readonly ILogger<AddTableBillPaymentCommandHandler> _logger;

    public AddTableBillPaymentCommandHandler(
        ApplicationDbContext context,
        IOrderPaymentApplicator paymentApplicator,
        ITableBillAssembler billAssembler,
        ILogger<AddTableBillPaymentCommandHandler> logger)
    {
        _context = context;
        _paymentApplicator = paymentApplicator;
        _billAssembler = billAssembler;
        _logger = logger;
    }

    public async Task<ApiResponse<TableBillDto>> Handle(AddTableBillPaymentCommand command, CancellationToken cancellationToken)
    {
        // The allocation is ONE money movement spread over N orders: it commits wholly or not at
        // all, and two bill payments on the same table may not interleave (a double-tap or two
        // till devices would otherwise over-allocate, flag orders Overpaid and overstate the
        // Z-report — ApplyToOrderAsync deliberately allows overpayment on a single order).
        // SERIALIZABLE gives both: the tenders commit atomically, and Postgres aborts the loser
        // of two concurrent allocations with a serialization failure, mapped to the same
        // "review the bill and try again" refusal below.
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var openOrders = await _context.Orders
            .Where(o => !o.IsDeleted
                && o.Type == OrderType.DineIn
                && o.TableNumber == command.TableNumber
                && !TableBillAssembler.ExcludedStatuses.Contains(o.Status))
            .OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber)
            .Select(o => new { o.Id, o.RemainingAmount })
            .ToListAsync(cancellationToken);

        if (openOrders.Count == 0)
        {
            return ApiResponse<TableBillDto>.Failure($"No open orders found for table {command.TableNumber}");
        }

        var billRemaining = openOrders.Sum(o => Math.Max(0, o.RemainingAmount));
        if (billRemaining <= 0)
        {
            return ApiResponse<TableBillDto>.Failure(
                $"Table {command.TableNumber} has no outstanding balance on its bill");
        }

        if (command.Amount > billRemaining + Tolerance)
        {
            return ApiResponse<TableBillDto>.Failure(
                $"Payment amount exceeds the bill's remaining balance of {billRemaining:0.00}");
        }

        var tender = new OrderPaymentTender
        {
            PaymentMethod = command.PaymentMethod,
            TransactionId = command.TransactionId,
            ReferenceNumber = command.ReferenceNumber,
            CardLastFourDigits = command.CardLastFourDigits,
            CardType = command.CardType,
            PaymentNotes = command.PaymentNotes,
        };

        var appliedTo = new List<string>();
        var leftToApply = command.Amount;
        try
        {
            foreach (var order in openOrders)
            {
                if (leftToApply <= 0)
                {
                    break;
                }

                var outstanding = Math.Max(0, order.RemainingAmount);
                if (outstanding <= 0)
                {
                    continue; // already-settled round (or overpaid one): allocation never reaches back
                }

                var share = Math.Min(outstanding, leftToApply);
                var result = await _paymentApplicator.ApplyToOrderAsync(
                    order.Id, tender with { Amount = share }, cancellationToken);

                if (result.Outcome != OrderPaymentApplicationOutcome.Applied || result.Order == null)
                {
                    // The rows were read inside this transaction; reaching an unapplyable order
                    // means a concurrent change the snapshot could not show (cancelled round).
                    // Roll back — the tenders recorded so far MUST NOT stand half-settled.
                    _logger.LogWarning(
                        "Bill payment for table {TableNumber} rolled back on order {OrderId}: {Outcome}",
                        command.TableNumber, order.Id, result.Outcome);
                    return ApiResponse<TableBillDto>.Failure(
                        "The bill changed while the payment was being applied. Please review the bill and try again");
                }

                appliedTo.Add(result.Order.OrderNumber);
                leftToApply -= share;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConcurrencyAborts.IsMatch(ex, out var sqlState))
        {
            // Lost the race against a concurrent bill payment (serialization/deadlock). Nothing
            // committed; the refusal tells the cashier to look again — the bill they hold is stale.
            // The abort can surface raw (our own queries) or EF-wrapped inside a
            // DbUpdateException raised by ApplyToOrderAsync's SaveChangesAsync.
            _logger.LogWarning(ex,
                "Bill payment for table {TableNumber} lost the concurrency race: {SqlState}",
                command.TableNumber, sqlState);
            return ApiResponse<TableBillDto>.Failure(
                "The bill changed while the payment was being applied. Please review the bill and try again");
        }

        // A sub-tolerance slack may remain; slack is NEVER charged — the message reports what the till actually took.
        var appliedTotal = command.Amount - Math.Max(0, leftToApply);

        _logger.LogInformation(
            "Bill payment {Amount} applied across {OrderCount} orders on table {TableNumber}: {Orders}",
            appliedTotal, appliedTo.Count, command.TableNumber, string.Join(", ", appliedTo));

        // Re-assemble AFTER the commit so the response reflects the post-payment bill. Money is
        // already committed here: whatever happens, the response must not deny the tenders —
        // the refusals below say "recorded", and the retry guard ("no outstanding balance")
        // keeps the cashier from paying twice.
        TableBillDto? bill;
        try
        {
            bill = await _billAssembler.AssembleAsync(command.TableNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bill for table {TableNumber} could not be re-read after a committed payment",
                command.TableNumber);
            return ApiResponse<TableBillDto>.Failure(
                "The payment was recorded, but the bill could not be refreshed. Please reopen the bill");
        }

        if (bill == null)
        {
            // Every order got settled and completed right after our commit — possible only
            // through a concurrent table-clear; the tenders themselves are committed.
            return ApiResponse<TableBillDto>.Failure(
                "The payment was recorded, but the bill could not be refreshed. Please reopen the bill");
        }

        return ApiResponse<TableBillDto>.SuccessWithData(
            bill, $"Payment of {appliedTotal:0.00} applied across {appliedTo.Count} order(s)");
    }
}
