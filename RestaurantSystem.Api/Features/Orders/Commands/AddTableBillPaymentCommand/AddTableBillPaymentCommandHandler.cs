using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
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
    private readonly ITableBillTargetResolver _targetResolver;
    private readonly ITableServiceSessionCurrencyPolicy _currencyPolicy;
    private readonly ILogger<AddTableBillPaymentCommandHandler> _logger;

    public AddTableBillPaymentCommandHandler(
        ApplicationDbContext context,
        IOrderPaymentApplicator paymentApplicator,
        ITableBillAssembler billAssembler,
        ITableBillPaymentOperationReplayResolver replayResolver,
        ILogger<AddTableBillPaymentCommandHandler> logger,
        ITableBillTargetResolver? targetResolver = null,
        ITableServiceSessionCurrencyPolicy? currencyPolicy = null)
    {
        _context = context;
        _paymentApplicator = paymentApplicator;
        _billAssembler = billAssembler;
        _replayResolver = replayResolver;
        _targetResolver = targetResolver ?? new TableBillTargetResolver(context);
        _currencyPolicy = currencyPolicy ?? new TableServiceSessionCurrencyPolicy(context);
        _logger = logger;
    }

    public async Task<ApiResponse<TableBillDto>> Handle(AddTableBillPaymentCommand command, CancellationToken cancellationToken)
    {
        var target = await _targetResolver.ResolveAsync(command.TableNumber, cancellationToken);
        if (target.IsAmbiguous)
        {
            return ApiResponse<TableBillDto>.FailureWithCode(
                target.Reason ?? "Use the explicit service session id for this table.",
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        command.ServiceSessionId = target.ServiceSessionId;
        var currency = await _currencyPolicy.ResolveAsync(
            command.ServiceSessionId, command.Currency, cancellationToken);
        if (!currency.Success)
        {
            return ApiResponse<TableBillDto>.FailureWithCode(
                currency.Error!, ErrorCodes.TableServiceSessionCurrencyMismatch);
        }
        command.Currency = currency.Currency;
        var replay = await _replayResolver.ResolveAsync(command, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var openOrders = await _context.Orders
            .Where(o => !o.IsDeleted
                && o.Type == OrderType.DineIn
                && o.TableNumber == command.TableNumber
                && o.ServiceSessionId == command.ServiceSessionId
                && !TableBillAssembler.ExcludedStatuses.Contains(o.Status))
            .OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber)
            .Select(o => new BillRound(o.Id, o.RemainingAmount))
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
            ServiceSessionId = command.ServiceSessionId,
            Currency = command.Currency,
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
            Currency = command.Currency,
        };

        var allocation = new AllocationResult(command.Amount, new List<string>());
        try
        {
            allocation = await TableBillPaymentAllocation.ApplyAsync(
                openOrders, tender, command.TableNumber, allocation, _paymentApplicator, _logger, cancellationToken);
            if (!allocation.Success)
            {
                return ApiResponse<TableBillDto>.Failure(
                    "The bill changed while the payment was being applied. Please review the bill and try again");
            }
            if (command.ServiceSessionId.HasValue)
            {
                var session = await _context.TableServiceSessions
                    .SingleAsync(value => value.Id == command.ServiceSessionId.Value, cancellationToken);
                session.Version++;
                await _context.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            await _context.Database.UseTransactionAsync(null, cancellationToken);
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

        var appliedTotal = command.Amount - Math.Max(0, allocation.Left);

        _logger.LogInformation(
            "Bill payment {Amount} applied across {OrderCount} orders on table {TableNumber}: {Orders}",
            appliedTotal, allocation.Orders.Count, command.TableNumber, string.Join(", ", allocation.Orders));

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
            return ApiResponse<TableBillDto>.Failure(
                "The payment was recorded, but the bill could not be refreshed. Please reopen the bill");
        }
        return ApiResponse<TableBillDto>.SuccessWithData(
            bill, $"Payment of {appliedTotal:0.00} applied across {allocation.Orders.Count} order(s)");
    }
}
