using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

public sealed partial class OptionSetCatalogService : IOptionSetCatalogService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly MenuAuthoringPaginationSettings _pagination;
    private readonly OptionSetAuthoringSettings _settings;
    private readonly ITranslationProvenanceWriter _translationProvenance;

    public OptionSetCatalogService(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOptions<MenuAuthoringPaginationSettings> pagination,
        IOptions<OptionSetAuthoringSettings> settings,
        ITranslationProvenanceWriter translationProvenance)
    {
        _context = context;
        _currentUser = currentUser;
        _pagination = pagination.Value;
        _settings = settings.Value;
        _translationProvenance = translationProvenance;
    }

    private int PageSize(int requestedPageSize) => _pagination.Normalize(requestedPageSize);
}
