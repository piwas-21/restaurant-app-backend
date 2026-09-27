using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService : IMenuAuthoringSearchService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly MenuAuthoringPaginationSettings _pagination;

    public MenuAuthoringSearchService(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOptions<MenuAuthoringPaginationSettings> pagination)
    {
        _context = context;
        _currentUser = currentUser;
        _pagination = pagination.Value;
    }

    private int PageSize(int requestedPageSize) => _pagination.Normalize(requestedPageSize);

    private static string LikePattern(string normalizedQuery) =>
        $"%{normalizedQuery.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";

}
