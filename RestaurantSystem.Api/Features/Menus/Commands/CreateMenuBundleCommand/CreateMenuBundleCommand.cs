using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;

public record CreateMenuBundleCommand(
    string Name,
    string? Description,
    decimal BasePrice,
    bool IsActive,
    bool IsAvailable,
    bool IsSpecial,
    int PreparationTimeMinutes,
    int DisplayOrder,
    List<Guid>? CategoryIds,
    Guid? PrimaryCategoryId,
    MenuDefinitionDto MenuDefinition,
    ProductDescriptionsDto Content,
    int? AvailableOrderTypes = null,
    // Nullable-with-default for positional-record compatibility, NOT for the leave-alone
    // semantics its sibling on the update command has: on create there is nothing stored to
    // leave alone, so this is assigned as given and null simply yields an unlabelled bundle.
    // See IMenuBundleCommandFields for the contract the two paths do and do not share.
    List<string>? Allergens = null,
    TranslationOwnerMetadataDto? TranslationMetadata = null
) : ICommand<ApiResponse<ProductDto>>, IMenuBundleCommandFields
{
    private CustomerStepManifestDto? _customerStepManifest;

    [JsonIgnore]
    public bool CustomerStepManifestSpecified { get; private set; }

    public CustomerStepManifestDto? CustomerStepManifest
    {
        get => _customerStepManifest;
        init
        {
            _customerStepManifest = value;
            CustomerStepManifestSpecified = true;
        }
    }
}
