using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public sealed class TableBillPaymentSessionCoordinator : ITableBillPaymentSessionCoordinator
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly decimal _paymentTolerance;
    private readonly TimeProvider _timeProvider;

    public TableBillPaymentSessionCoordinator(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOptions<TableServiceSessionSettings>? settings = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TableServiceSession?> LockOpenAsync(
        Guid? serviceSessionId, CancellationToken cancellationToken)
    {
        if (!serviceSessionId.HasValue)
        {
            return null;
        }

        var session = await TableServiceSessionRowLock.LoadAsync(
            _context, serviceSessionId.Value, cancellationToken);
        return session?.Status == TableServiceSessionStatus.Open ? session : null;
    }

    public async Task CompleteAsync(
        TableServiceSession? session, Guid paymentOperationKey, CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return;
        }

        await TableServicePaymentHandoffResolution.ResolveIfSettledAsync(
            _context,
            session.Id,
            paymentOperationKey,
            _currentUser.GetAuditIdentifier(),
            _paymentTolerance,
            _timeProvider,
            cancellationToken);
        session.Version++;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
