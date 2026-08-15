using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// <see cref="IPreferredLanguageCapture"/> over the account row and
/// <see cref="IEmailLanguageResolver"/>. Scoped, because it reads the database; the resolver it
/// wraps is a singleton and holds no state of its own.
/// </summary>
public sealed class PreferredLanguageCapture : IPreferredLanguageCapture
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailLanguageResolver _resolver;
    private readonly ICurrentUserService _currentUser;

    public PreferredLanguageCapture(
        ApplicationDbContext context,
        IEmailLanguageResolver resolver,
        ICurrentUserService currentUser)
    {
        _context = context;
        _resolver = resolver;
        _currentUser = currentUser;
    }

    public async Task<string> ForUserAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        // A staff-entered order — the phone order, the POS, a reservation typed in at the counter —
        // is NOT the guest's request, and `userId` on those paths is the STAFF account (the
        // customer link is [JsonIgnore] and unset). Taking rank 2 or 3 there would freeze the
        // restaurant's own UI language onto the guest's row and then mail the guest in it: the
        // same failure §1 forbids for the operator alerts, pointing the other way. The tenant's
        // language is the honest answer when nobody has told us the guest's.
        if (_currentUser.IsStaff)
        {
            return _resolver.TenantDefault;
        }

        // Projected, not loaded: this runs on the order-creation path, and the language is the
        // only column that matters here. IgnoreQueryFilters is deliberately NOT used — a
        // soft-deleted account's preference has no claim on a new row.
        var stored = userId is null
            ? null
            : await _context.Users
                .Where(user => user.Id == userId)
                .Select(user => user.PreferredLanguage)
                .FirstOrDefaultAsync(cancellationToken);

        // FromRequest() is read HERE, synchronously on the request's own thread, and the resolved
        // VALUE is what travels onward — never the accessor. This is the one place in the codebase
        // that is entitled to rank 3: the caller is the guest's own write request.
        return _resolver.Resolve(null, stored, _resolver.FromRequest());
    }
}
