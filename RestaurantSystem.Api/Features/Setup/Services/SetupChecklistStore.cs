using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Setup.Services;

/// <inheritdoc />
public class SetupChecklistStore : ISetupChecklistStore
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SetupChecklistStore(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public Task<SetupChecklistState?> GetAsync(CancellationToken cancellationToken) =>
        _context.SetupChecklistState
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SetupChecklistState.SingletonId, cancellationToken);

    public async Task ApplyAsync(
        Action<SetupChecklistState> mutate, CancellationToken cancellationToken)
    {
        var (state, isNew) = await LoadOrCreateAsync(cancellationToken);
        mutate(state);

        if (!isNew)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Detach ours before looking: the failed INSERT is still tracked as Added,
            // and any later SaveChanges on this context would retry it and fail again.
            _context.Entry(state).State = EntityState.Detached;

            // Re-read before deciding this was the race. A DbUpdateException from
            // anything else — a bad value, a dropped connection — must still surface
            // rather than be swallowed as "someone beat us to it".
            var winner = await _context.SetupChecklistState
                .FirstOrDefaultAsync(s => s.Id == SetupChecklistState.SingletonId, cancellationToken);
            if (winner is null) throw;

            // Another request inserted the singleton between our read and our save. Its
            // row is the real one, so re-apply the mutation on top of it — the caller's
            // change lands instead of being lost to a 500 on a double-clicked checkbox.
            mutate(winner);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }


    private async Task<(SetupChecklistState State, bool IsNew)> LoadOrCreateAsync(
        CancellationToken cancellationToken)
    {
        var existing = await _context.SetupChecklistState
            .FirstOrDefaultAsync(s => s.Id == SetupChecklistState.SingletonId, cancellationToken);
        if (existing is not null) return (existing, false);

        // The fixed id is what makes the primary key enforce singleton-ness, and what
        // turns a lost race into a catchable violation instead of a second row.
        var created = new SetupChecklistState
        {
            Id = SetupChecklistState.SingletonId,
            CreatedBy = _currentUser.GetAuditIdentifier(),
        };
        _context.SetupChecklistState.Add(created);
        return (created, true);
    }
}
