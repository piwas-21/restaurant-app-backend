using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService : IMenuAuthoringSearchService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public MenuAuthoringSearchService(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    private static string LikePattern(string normalizedQuery) =>
        $"%{normalizedQuery.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";

}
