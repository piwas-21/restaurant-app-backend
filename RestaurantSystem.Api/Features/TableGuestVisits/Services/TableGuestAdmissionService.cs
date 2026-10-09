using System.Data;
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

public sealed class TableGuestAdmissionService : ITableGuestAdmissionService
{
    private const string UnavailableMessage = "Guest access is unavailable for this table visit.";
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly ICurrentUserService _currentUser;
    private readonly TableGuestVisitSettings _settings;
    private readonly TimeProvider _timeProvider;

    public TableGuestAdmissionService(
        ApplicationDbContext context,
        ITenantFeatures features,
        ICurrentUserService currentUser,
        IOptions<TableGuestVisitSettings> settings,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _features = features;
        _currentUser = currentUser;
        _settings = settings.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TableGuestAdmissionCodeDto> CreateCodeAsync(
        Guid serviceSessionId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var session = await TableServiceSessionRowLock.LoadAsync(_context, serviceSessionId, cancellationToken);
        if (session?.Status != TableServiceSessionStatus.Open || session.ReleasedAt.HasValue || !session.TableId.HasValue)
        {
            throw Unavailable();
        }

        var hasQr = await _context.Tables.AnyAsync(table => table.Id == session.TableId
            && table.IsActive && table.QRCodeData != null, cancellationToken);
        if (!hasQr)
        {
            throw Unavailable();
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var admissions = await _context.Set<TableGuestAdmission>()
            .Where(value => value.ServiceSessionId == session.Id && value.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var admission in admissions)
        {
            admission.RevokedAt = now;
        }

        var expiresAt = now.AddHours(_settings.AdmissionLifetimeHours);
        var credential = TableGuestCredentialCrypto.CreateAdmissionCode();
        _context.Set<TableGuestAdmission>().Add(new TableGuestAdmission
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            CodeHash = credential.Hash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedBy = _currentUser.GetAuditIdentifier(),
        });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TableGuestAdmissionCodeDto(credential.Code, expiresAt);
    }

    public async Task<TableGuestJoinDto> JoinAsync(
        string qrCodeData, string admissionCode, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (qrCodeData.Length is < 1 or > 128
            || !TableGuestCredentialCrypto.TryNormalizeAdmissionCode(admissionCode, out _))
        {
            throw Unavailable();
        }

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
        if (!sessionId.HasValue)
        {
            throw Unavailable();
        }

        var session = await TableServiceSessionRowLock.LoadAsync(_context, sessionId.Value, cancellationToken);
        if (session?.Status != TableServiceSessionStatus.Open || session.ReleasedAt.HasValue
            || session.TableId != table.Id)
        {
            throw Unavailable();
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var admission = await _context.Set<TableGuestAdmission>().SingleOrDefaultAsync(value =>
            value.ServiceSessionId == session.Id && value.RevokedAt == null && value.ExpiresAt > now,
            cancellationToken);
        if (admission is null || !TableGuestCredentialCrypto.VerifyAdmissionCode(admissionCode, admission.CodeHash))
        {
            throw Unavailable();
        }

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
            AdmissionId = admission.Id,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedBy = _currentUser.GetAuditIdentifier(),
        });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TableGuestJoinDto(session.Id, token, expiresAt);
    }

    private void EnsureEnabled()
    {
        if (!_features.TableGuestVisitsV1)
        {
            throw Unavailable();
        }
    }

    private static NotFoundException Unavailable() => new(UnavailableMessage);
}
