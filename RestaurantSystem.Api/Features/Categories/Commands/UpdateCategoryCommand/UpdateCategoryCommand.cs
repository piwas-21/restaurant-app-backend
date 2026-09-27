using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryCommand;

public record UpdateCategoryCommand : ICommand<ApiResponse<CategoryDto>>
{
    [JsonConstructor]
    public UpdateCategoryCommand()
    {
    }

    public UpdateCategoryCommand(
        Guid Id,
        string Name,
        string? Description,
        bool IsActive,
        int? DisplayOrder = null)
    {
        this.Id = Id;
        this.Name = Name;
        this.Description = Description;
        this.IsActive = IsActive;
        this.DisplayOrder = DisplayOrder;
    }

    [JsonRequired]
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    [JsonRequired]
    public bool IsActive { get; init; }

    // NOTE: DisplayOrder is accepted but deliberately NOT assigned by this handler —
    // ReorderCategoriesCommand owns ordering. Left as-is to avoid clobbering a tenant's order
    // from an unrelated edit; the dead parameter is tracked as follow-up debt. Nullable so an
    // omitted value is distinguishable from a posted 0 rather than silently becoming one (S6964).
    public int? DisplayOrder { get; init; }

    // OrderChannels bitmask; null = every order type. Written by the admin channel matrix.
    public int? AvailableOrderTypes { get; init; }

    // Partner request 2026-09-06: keep the category orderable on its own tab but out of the
    // guest "All" list. The admin edit form always posts it (the PUT is a full replace).
    [JsonRequired]
    public bool IsHiddenFromAllTab { get; init; }
    public Dictionary<string, CategoryContentDto>? Translations { get; init; }

    private string? _sourceLocale;
    public string? SourceLocale
    {
        get => _sourceLocale;
        init
        {
            _sourceLocale = value;
            SourceLocaleWasSpecified = true;
        }
    }

    [JsonIgnore]
    public bool SourceLocaleWasSpecified { get; private set; }

    public TranslationOwnerMetadataDto? TranslationMetadata { get; init; }
}

public class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand, ApiResponse<CategoryDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateCategoryCommandHandler> _logger;
    private readonly ITranslationProvenanceWriter _translationProvenanceWriter;

    public UpdateCategoryCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UpdateCategoryCommandHandler> logger,
        ITranslationProvenanceWriter translationProvenanceWriter)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
        _translationProvenanceWriter = translationProvenanceWriter;
    }

    public async Task<ApiResponse<CategoryDto>> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        // ThenInclude is load-bearing, not tidiness: the response's ProductCount dereferences
        // `pc.Product` AFTER materialisation, in memory. Lazy loading is off, so without this the
        // navigation is null and every update of a category that has at least one product threw a
        // NullReferenceException — a 500 on the admin's rename, active-toggle AND the order-type
        // channel matrix, i.e. on every real category. A category with NO products succeeded,
        // because the Count lambda then never ran, which is why this survived so long.
        var category = await _context.Categories
            .AsSplitQuery()
            .Include(c => c.ProductCategories)
                .ThenInclude(pc => pc.Product)
            .Include(c => c.Translations)
            .FirstOrDefaultAsync(c => c.Id == command.Id && !c.IsDeleted, cancellationToken);

        if (category == null)
        {
            return ApiResponse<CategoryDto>.Failure("Category not found");
        }

        TranslationContentVersion.EnsureCurrent(category,
            command.TranslationMetadata?.ExpectedContentVersion,
            command.TranslationMetadata?.AcceptedSuggestionIds?.Count ?? 0);

        // Check if another category with the same name exists (case-insensitive)
        var duplicateCategory = await _context.Categories
            .Where(c => !c.IsDeleted && c.Id != command.Id)
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, command.Name), cancellationToken);

        if (duplicateCategory != null)
        {
            return ApiResponse<CategoryDto>.Failure("Another category with this name already exists");
        }

        category.Name = command.Name;
        category.Description = command.Description;
        category.IsActive = command.IsActive;
        category.IsHiddenFromAllTab = command.IsHiddenFromAllTab;
        category.AvailableOrderTypes = command.AvailableOrderTypes;
        category.SourceLocale = command.SourceLocaleWasSpecified
            ? CategoryTranslationMapper.NormalizeSourceLocale(command.SourceLocale)
            : category.SourceLocale;
        CategoryTranslationMapper.Replace(
            _context, category, command.Translations, _currentUserService.GetAuditIdentifier());
        category.UpdatedAt = DateTime.UtcNow;
        category.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _translationProvenanceWriter.RecordAsync(
            "category", category.Id, command.TranslationMetadata,
            CategoryTranslationMapper.ToTextMap(category), cancellationToken);
        if (command.SourceLocaleWasSpecified && category.SourceLocale is null)
        {
            await _translationProvenanceWriter.ClearSourceLocaleAsync(
                "category", category.Id, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var categoryDto = new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ImageUrl = category.ImageUrl,
            IsActive = category.IsActive,
            DisplayOrder = category.DisplayOrder,
            IsHiddenFromAllTab = category.IsHiddenFromAllTab,
            AvailableOrderTypes = category.AvailableOrderTypes,
            Translations = CategoryTranslationMapper.ToDto(category.Translations),
            SourceLocale = category.SourceLocale,
            ProductCount = category.ProductCategories.Count(pc => !pc.Product.IsDeleted && pc.Product.IsActive),
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };

        categoryDto = await TranslationReadMetadata.ApplyAsync(_context, categoryDto, cancellationToken);
        _logger.LogInformation("Category {CategoryId} updated successfully", category.Id);
        return ApiResponse<CategoryDto>.SuccessWithData(categoryDto, "Category updated successfully");
    }
}
