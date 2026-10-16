using System.Data;
using System.Globalization;
using RestaurantSystem.Domain.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public sealed class TableGuestJoinService : ITableGuestJoinService
{
    private const string UnavailableMessage = "Guest access is unavailable for this table visit.";
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly ICurrentUserService _currentUser;
    private readonly TableGuestVisitSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly decimal _paymentTolerance;

    public TableGuestJoinService(
        ApplicationDbContext context,
        ITenantFeatures features,
        ICurrentUserService currentUser,
        IOptions<TableGuestVisitSettings> settings,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? sessionSettings = null)
    {
        _context = context;
        _features = features;
        _currentUser = currentUser;
        _settings = settings.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paymentTolerance = (sessionSettings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
    }

    public async Task<TableGuestJoinDto> JoinAsync(
        string qrCodeData, CancellationToken cancellationToken)
    {
        EnsureJoinAllowed(qrCodeData);

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var tableId = await _context.Tables.AsNoTracking()
            .Where(table => table.IsActive && table.QRCodeData == qrCodeData)
            .Select(table => (Guid?)table.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!tableId.HasValue)
        {
            throw Unavailable();
        }

        // Re-read under the table lock so a QR rotated between lookup and lock acquisition cannot
        // authorize a join with its obsolete value.
        var table = await TableServiceSessionRowLock.LoadTableAsync(_context, tableId.Value, cancellationToken);
        if (table is null || !table.IsActive || !string.Equals(table.QRCodeData, qrCodeData, StringComparison.Ordinal))
        {
            throw Unavailable();
        }

        var sessionId = await _context.TableServiceSessions.AsNoTracking()
            .Where(value => value.TableId == table.Id && value.Status == TableServiceSessionStatus.Open
                && value.ReleasedAt == null)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        TableServiceSession? session;
        if (sessionId.HasValue)
        {
            session = await TableServiceSessionRowLock.LoadAsync(_context, sessionId.Value, cancellationToken);
        }
        else
        {
            var number = int.TryParse(table.TableNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0 ? (int?)parsed : null;
            var identity = new TableIdentity(table.Id, table.TableNumber, number);
            var failure = await TableServiceSessionOpenHelpers.ValidateCanCreateAsync(
                _context, table, identity, _paymentTolerance, _features.TableVisitReadinessV1, cancellationToken);
            if (failure is not null) throw Unavailable();
            var currency = await _context.RestaurantInfo.AsNoTracking()
                .Select(info => info.Currency).FirstOrDefaultAsync(cancellationToken);
            session = new TableServiceSession
            {
                Id = Guid.NewGuid(),
                TableId = table.Id,
                TableNumber = number,
                Currency = CurrencyCode.Normalize(currency),
                OpenedAt = now,
                CreatedAt = now,
                CreatedBy = _currentUser.GetAuditIdentifier(),
            };
            _context.TableServiceSessions.Add(session);
        }
        if (session?.Status != TableServiceSessionStatus.Open || session.ReleasedAt.HasValue
            || session.TableId != table.Id)
            throw Unavailable();

        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(token, out var tokenHash))
        {
            throw new CryptographicException("Participant credential could not be encoded.");
        }

        var expiresAt = now.AddHours(_settings.ParticipantLifetimeHours);
        _context.Set<TableGuestParticipant>().Add(new TableGuestParticipant
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedBy = _currentUser.GetAuditIdentifier(),
        });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TableGuestJoinDto(session.Id, token, expiresAt);
    }

    private void EnsureJoinAllowed(string qrCodeData)
    {
        if (!_features.TableGuestVisitsV1)
        {
            throw Unavailable();
        }
        if (string.IsNullOrWhiteSpace(qrCodeData) || qrCodeData.Length > 128)
        {
            throw Unavailable();
        }
    }

    private static NotFoundException Unavailable() => new(UnavailableMessage);
}
