using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public sealed class TableGuestVisitRevoker : ITableGuestVisitRevoker
{
    private readonly ApplicationDbContext _context;

    public TableGuestVisitRevoker(ApplicationDbContext context) => _context = context;

    public async Task RevokeForSessionAsync(
        Guid serviceSessionId, DateTime revokedAt, CancellationToken cancellationToken)
    {
        var admissions = await _context.Set<TableGuestAdmission>()
            .Where(value => value.ServiceSessionId == serviceSessionId && value.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var participants = await _context.Set<TableGuestParticipant>()
            .Where(value => value.ServiceSessionId == serviceSessionId && value.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var admission in admissions)
        {
            admission.RevokedAt = revokedAt;
        }

        foreach (var participant in participants)
        {
            participant.RevokedAt = revokedAt;
        }
    }
}
