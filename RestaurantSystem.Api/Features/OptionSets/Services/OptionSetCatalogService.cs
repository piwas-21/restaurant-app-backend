using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

public sealed partial class OptionSetCatalogService : IOptionSetCatalogService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public OptionSetCatalogService(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }
}
