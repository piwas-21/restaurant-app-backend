using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;

public class AddTableBillPaymentCommandHandler : ICommandHandler<AddTableBillPaymentCommand, ApiResponse<TableBillDto>>
{
    private const decimal Tolerance = 0.01m;
    private readonly ApplicationDbContext _context;
    private readonly IOrderPaymentApplicator _paymentApplicator;
    private readonly ITableBillAssembler _billAssembler;
    private readonly ITableBillPaymentOperationReplayResolver _replayResolver;
    private readonly ILogger<AddTableBillPaymentCommandHandler> _logger;

    public AddTableBillPaymentCommandHandler(
        ApplicationDbContext context,
        IOrderPaymentApplicator paymentApplicator,
        ITableBillAssembler billAssembler,
        ITableBillPaymentOperationReplayResolver replayResolver,
        ILogger<AddTableBillPaymentCommandHandler> logger)
    {
        _context = context;
        _paymentApplicator = paymentApplicator;
        _billAssembler = billAssembler;
        _replayResolver = replayResolver;
        _logger = logger;
    }

    public async Task<ApiResponse<TableBillDto>> Handle(AddTableBillPaymentCommand command, CancellationToken cancellationToken)
    {
        var replay = await _replayResolver.ResolveAsync(command, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        // One bill tender may create N payments. SERIALIZABLE makes the allocation atomic and
        // aborts a concurrent stale allocation before it can overstate the ledger.
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

        var operation = new TableBillPaymentOperation
        {
            Id = Guid.NewGuid(),
            CreatedBy = string.Empty,
            OperationId = command.OperationId,
            TableNumber = command.TableNumber,
            PaymentMethod = command.PaymentMethod,
            Amount = command.Amount,
            TransactionId = command.TransactionId,
            ReferenceNumber = command.ReferenceNumber,
            CardLastFourDigits = command.CardLastFourDigits,
            CardType = command.CardType,
            PaymentNotes = command.PaymentNotes,
        };
        _context.TableBillPaymentOperations.Add(operation);

        var tender = new OrderPaymentTender
        {
            TableBillPaymentOperationId = operation.Id,
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
                    // An unapplyable round invalidates the whole allocation.
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            _context.ChangeTracker.Clear();
            var winner = await _replayResolver.ResolveAsync(command, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
        catch (Exception ex) when (PostgresConcurrencyAborts.IsMatch(ex, out var sqlState))
        {
            // Nothing committed; the cashier must review the now-authoritative bill.
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
