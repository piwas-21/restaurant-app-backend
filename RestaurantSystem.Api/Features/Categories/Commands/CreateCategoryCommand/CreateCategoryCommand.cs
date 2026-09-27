using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;

public record CreateCategoryCommand(
    string Name,
    string? Description,
    bool IsActive,
    int DisplayOrder,
    // OrderChannels bitmask; null = available on every order type (the default, so existing
    // clients that omit it are unrestricted). Written by the admin channel matrix.
    int? AvailableOrderTypes = null,
    // Partner request 2026-09-06: keep the category orderable on its own tab but out of the
    // guest "All" list. Default false — every category that existed before this flag stays
    // exactly where it was.
    bool IsHiddenFromAllTab = false,
    Dictionary<string, CategoryContentDto>? Translations = null,
    string? SourceLocale = null,
    TranslationOwnerMetadataDto? TranslationMetadata = null
) : ICommand<ApiResponse<CategoryDto>>;

public class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, ApiResponse<CategoryDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateCategoryCommandHandler> _logger;
    private readonly ITranslationProvenanceWriter _translationProvenanceWriter;

    public CreateCategoryCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<CreateCategoryCommandHandler> logger,
        ITranslationProvenanceWriter translationProvenanceWriter)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
        _translationProvenanceWriter = translationProvenanceWriter;
    }

    public async Task<ApiResponse<CategoryDto>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        // Check if category with same name exists (case-insensitive)
        var existingCategory = await _context.Categories
            .Where(c => !c.IsDeleted)
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, command.Name), cancellationToken);

        if (existingCategory != null)
        {
            return ApiResponse<CategoryDto>.Failure("Category with this name already exists");
        }

        var max = await _context.Categories
            .Where(c => !c.IsDeleted)
            .MaxAsync(c => (int?)c.DisplayOrder, cancellationToken) ?? 0;

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            AvailableOrderTypes = command.AvailableOrderTypes,
            Description = command.Description,
            IsActive = command.IsActive,
            IsHiddenFromAllTab = command.IsHiddenFromAllTab,
            SourceLocale = CategoryTranslationMapper.NormalizeSourceLocale(command.SourceLocale),
            DisplayOrder = max + 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        CategoryTranslationMapper.Replace(
            _context, category, command.Translations, _currentUserService.GetAuditIdentifier());
        _context.Categories.Add(category);
        await _translationProvenanceWriter.RecordAsync(
            "category", category.Id, command.TranslationMetadata,
            CategoryTranslationMapper.ToTextMap(category), cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var categoryDto = new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            IsActive = category.IsActive,
            DisplayOrder = category.DisplayOrder,
            IsHiddenFromAllTab = category.IsHiddenFromAllTab,
            AvailableOrderTypes = category.AvailableOrderTypes,
            Translations = CategoryTranslationMapper.ToDto(category.Translations),
            SourceLocale = category.SourceLocale,
            ProductCount = 0,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };

        categoryDto = await TranslationReadMetadata.ApplyAsync(_context, categoryDto, cancellationToken);
        _logger.LogInformation("Category {CategoryId} created successfully", category.Id);
        return ApiResponse<CategoryDto>.SuccessWithData(categoryDto, "Category created successfully");
    }
}
