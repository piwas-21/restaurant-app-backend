using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Writes one session tender and allocates it oldest-round-first.</summary>
public sealed class TableServiceSessionPaymentWriter : ITableServiceSessionPaymentWriter
{
    private readonly decimal _paymentTolerance;
    private readonly ApplicationDbContext _context;
    private readonly IOrderPaymentApplicator _payments;

    public TableServiceSessionPaymentWriter(
        ApplicationDbContext context,
        IOrderPaymentApplicator payments,
        IOptions<TableServiceSessionSettings>? settings = null)
    {
        _context = context;
        _payments = payments;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
    }

    public async Task<SessionPaymentWriteResult> ApplyAsync(
        TableServiceSession session,
        AddTableServiceSessionPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var currencyResult = await ResolveCurrencyAsync(session, command, cancellationToken);
        if (!currencyResult.Success)
        {
            return currencyResult;
        }

        var orders = await _context.Orders
            .Where(order => !order.IsDeleted
                && order.ServiceSessionId == session.Id
                && order.Status != OrderStatus.Cancelled
                && order.RemainingAmount > 0)
            .OrderBy(order => order.OrderDate)
            .ThenBy(order => order.OrderNumber)
            .Select(order => new BillRound(order.Id, order.RemainingAmount))
            .ToListAsync(cancellationToken);
        var remaining = orders.Sum(order => Math.Max(0, order.RemainingAmount));
        if (remaining <= 0)
        {
            return new SessionPaymentWriteResult(false, 0, 0, "The service session has no outstanding balance.");
        }

        if (command.Amount > remaining + _paymentTolerance)
        {
            return new SessionPaymentWriteResult(
                false, 0, 0, $"Payment amount exceeds the session's remaining balance of {remaining:0.00}");
        }

        var operation = new TableBillPaymentOperation
        {
            Id = Guid.NewGuid(),
            OperationId = command.OperationId,
            TableNumber = session.TableNumber,
            ServiceSessionId = session.Id,
            ExpectedVersion = command.ExpectedVersion,
            Currency = command.Currency,
            PaymentMethod = command.PaymentMethod,
            Amount = command.Amount,
            TransactionId = command.TransactionId,
            ReferenceNumber = command.ReferenceNumber,
            CardLastFourDigits = command.CardLastFourDigits,
            CardType = command.CardType,
            PaymentNotes = command.PaymentNotes,
            CreatedBy = string.Empty,
        };
        _context.TableBillPaymentOperations.Add(operation);

        var left = command.Amount;
        var count = 0;
        foreach (var order in orders)
        {
            if (left <= 0)
            {
                break;
            }

            var share = Math.Min(order.RemainingAmount, left);
            var result = await _payments.ApplyToOrderAsync(order.Id, new OrderPaymentTender
            {
                Amount = share,
                PaymentMethod = command.PaymentMethod,
                Currency = command.Currency,
                TransactionId = command.TransactionId,
                ReferenceNumber = command.ReferenceNumber,
                CardLastFourDigits = command.CardLastFourDigits,
                CardType = command.CardType,
                PaymentNotes = command.PaymentNotes,
                TableBillPaymentOperationId = operation.Id,
            }, cancellationToken);
            if (result.Outcome != OrderPaymentApplicationOutcome.Applied || result.Order is null)
            {
                return new SessionPaymentWriteResult(
                    false, 0, 0,
                    "The session changed while the payment was being applied. Please review the bill and try again");
            }

            left -= share;
            count++;
        }

        return new SessionPaymentWriteResult(true, command.Amount - Math.Max(0, left), count);
    }

    private async Task<SessionPaymentWriteResult> ResolveCurrencyAsync(
        TableServiceSession session,
        AddTableServiceSessionPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var known = await _context.OrderPayments
            .AsNoTracking()
            .Where(payment => payment.Order.ServiceSessionId == session.Id && payment.Currency != null)
            .Select(payment => payment.Currency!)
            .ToListAsync(cancellationToken);
        var knownCurrencies = known
            .Select(CurrencyCode.Normalize)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (session.Currency is not null)
        {
            var expected = CurrencyCode.Normalize(session.Currency);
            session.Currency = expected;
            if (!string.Equals(command.Currency ?? expected, expected, StringComparison.OrdinalIgnoreCase)
                || knownCurrencies.Any(value => !string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)))
            {
                return new SessionPaymentWriteResult(false, 0, 0,
                    $"Payment currency must match the session currency ({expected}).", true);
            }
            command.Currency = expected;
        }
        else if (command.Currency is not null)
        {
            if (knownCurrencies.Any(value => !string.Equals(value, command.Currency,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return new SessionPaymentWriteResult(false, 0, 0,
                    "Payment currency conflicts with a known currency in this session.", true);
            }
            session.Currency = command.Currency;
        }
        else if (knownCurrencies.Count > 1)
        {
            return new SessionPaymentWriteResult(false, 0, 0,
                "The session contains conflicting known currencies.", true);
        }

        return new SessionPaymentWriteResult(true, 0, 0);
    }

    private sealed record BillRound(Guid Id, decimal RemainingAmount);
}
