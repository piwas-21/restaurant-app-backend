using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// The single WRITE-side <see cref="MenuSectionDto"/> → <see cref="MenuSection"/> translator. Every
/// path that persists a bundle's sections goes through here: create-bundle, update-bundle, and
/// update-product on a Menu-type product. Its read-side counterparts are <c>ProductDtoMapper</c> and
/// <c>GetProductByIdQuery</c> — note there are two of them, so this is not a symmetric pair.
/// (<see cref="MenuBundleMapper"/> is a different contract: it projects the richer
/// <c>MenuBundleSectionDto</c> family for the bundle list/detail queries.)
///
/// It exists because those three paths carried TOKEN-IDENTICAL copies of the section+item build —
/// 197 duplicated lines by SonarCloud's own count, in 8 blocks — and #191 had to touch all three,
/// which would have re-attributed that duplication to the fix as NEW code. Deduplicating was the
/// honest way out; a cpd exclusion would have claimed the repetition was inherent, and it is not.
/// (Token-, not byte-identical: one copy carried a trailing comment and one sat a level deeper.
/// Tokens are what CPD measures, and what the 197 counts.)
/// </summary>
public static class MenuSectionWriter
{
    /// <summary>
    /// Applies the editor's ID-preserving collection patch. Existing IDs are accepted only when
    /// they belong to this definition; absent collection keys are handled by the caller as no-op,
    /// and explicit empty lists remove the corresponding rows.
    /// </summary>
    public static IReadOnlyList<(MenuSectionDto Input, MenuSection Entity)> ApplyPatch(
        ApplicationDbContext context,
        MenuDefinition menuDefinition,
        IReadOnlyCollection<MenuSectionDto> sections,
        string auditIdentifier,
        bool translationsOnly = false)
    {
        ValidatePatchIds(menuDefinition, sections, translationsOnly);
        var existingSections = menuDefinition.Sections.ToDictionary(section => section.Id);
        var retainedSections = new HashSet<Guid>();
        var written = new List<(MenuSectionDto Input, MenuSection Entity)>();
        var now = DateTime.UtcNow;

        foreach (var sectionDto in sections)
        {
            MenuSection section;
            if (sectionDto.Id is Guid sectionId)
            {
                section = existingSections[sectionId];
                retainedSections.Add(sectionId);
            }
            else
            {
                section = new MenuSection
                {
                    Id = Guid.NewGuid(),
                    MenuDefinition = menuDefinition,
                    CreatedAt = now,
                    CreatedBy = auditIdentifier
                };
                menuDefinition.Sections.Add(section);
                context.MenuSections.Add(section);
            }

            section.Name = sectionDto.Name;
            section.Description = sectionDto.Description;
            section.DisplayOrder = sectionDto.DisplayOrder;
            section.IsRequired = sectionDto.IsRequired;
            section.MinSelection = sectionDto.MinSelection;
            section.MaxSelection = sectionDto.MaxSelection;
            section.UpdatedAt = now;
            section.UpdatedBy = auditIdentifier;

            ApplySectionContent(context, section, sectionDto, auditIdentifier, now, translationsOnly);
            written.Add((sectionDto, section));
        }

        foreach (var removed in existingSections.Values.Where(section => !retainedSections.Contains(section.Id)))
        {
            context.MenuSections.Remove(removed);
            menuDefinition.Sections.Remove(removed);
        }

        menuDefinition.AuthoringVersion++;
        menuDefinition.VersionedSectionEditingStarted = true;
        menuDefinition.UpdatedAt = now;
        menuDefinition.UpdatedBy = auditIdentifier;
        return written;
    }

    /// <summary>
    /// Validates supplied section and item IDs without changing tracked entities. PATCH routes that
    /// add additional protection around section writes call this before their protection checks so
    /// malformed IDs keep the editor's established BadRequest response.
    /// </summary>
    public static void ValidatePatchIds(
        MenuDefinition menuDefinition,
        IReadOnlyCollection<MenuSectionDto> sections,
        bool translationsOnly = false)
    {
        var existingSections = menuDefinition.Sections.ToDictionary(section => section.Id);
        var retainedSections = new HashSet<Guid>();
        foreach (var sectionDto in sections)
        {
            if (sectionDto.Id is Guid sectionId)
            {
                if (!existingSections.TryGetValue(sectionId, out var existingSection))
                {
                    throw new BadRequestException($"Section '{sectionId}' does not belong to this menu");
                }

                if (!retainedSections.Add(sectionId))
                {
                    throw new BadRequestException($"Section '{sectionId}' appears more than once");
                }

                if (!translationsOnly && sectionDto.ItemsSpecified)
                {
                    ValidateItemIds(existingSection, sectionDto.Items ?? []);
                }
            }
            else if (!translationsOnly && sectionDto.ItemsSpecified)
            {
                foreach (var item in sectionDto.Items ?? [])
                {
                    if (item.Id is Guid itemId)
                    {
                        throw new BadRequestException(
                            $"Option '{itemId}' does not belong to a section being created");
                    }
                }
            }
        }
    }

    private static void ValidateItemIds(MenuSection section, IReadOnlyCollection<MenuSectionItemDto> items)
    {
        var existingItems = section.Items.ToDictionary(item => item.Id);
        var retainedItems = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item.Id is not Guid itemId)
            {
                continue;
            }

            if (!existingItems.ContainsKey(itemId))
            {
                throw new BadRequestException($"Option '{itemId}' does not belong to section '{section.Id}'");
            }

            if (!retainedItems.Add(itemId))
            {
                throw new BadRequestException($"Option '{itemId}' appears more than once");
            }
        }
    }

    private static void ApplySectionContent(
        ApplicationDbContext context,
        MenuSection section,
        MenuSectionDto sectionDto,
        string auditIdentifier,
        DateTime now,
        bool translationsOnly)
    {
        if (sectionDto.TranslationsSpecified)
        {
            ReplaceTranslations(context, section, sectionDto.Translations ?? [], auditIdentifier, now);
        }

        if (sectionDto.ItemsSpecified && !translationsOnly)
        {
            ApplyItems(context, section, sectionDto.Items ?? [], auditIdentifier, now);
        }
    }

    /// <summary>
    /// Replaces every section of <paramref name="menuDefinition"/> with <paramref name="sections"/>.
    /// A full replace, matching the PUT contract: an empty list clears them all, which is exactly
    /// what the bundle form's section editor produces when the user deletes the last one. It is the
    /// ONLY public entry point on purpose — an add-without-clear overload would be a silent
    /// duplicate-sections footgun for any future update-path caller that picked the wrong one.
    ///
    /// <para><b>The caller MUST have loaded <c>Include(p =&gt; p.MenuDefinition).ThenInclude(md =&gt;
    /// md.Sections)</c></b> (or be passing a definition it just constructed). Sections is
    /// non-nullable and always initialized, so an un-included collection reads as EMPTY, not null:
    /// the removal would quietly remove nothing, the add would append, and the caller would get
    /// DUPLICATED sections with no exception anywhere — the same silent-permissive-include shape
    /// this class was created to stop. Both current callers load it.</para>
    ///
    /// <para>Items do NOT need including: they cascade in the database
    /// (<c>fk_menu_section_items_menu_sections_menu_section_id</c> is <c>ReferentialAction.Cascade</c>).
    /// That relies on <see cref="MenuSection"/> deriving from <c>Entity</c> and not
    /// <c>SoftDeleteEntity</c> — were it ever to gain <c>ISoftDelete</c>, the context's
    /// delete-to-soft-delete interception would turn this into an UPDATE and the cascade would never
    /// fire.</para>
    ///
    /// The removal is unconditional: the <c>Sections != null</c> check one caller used to carry
    /// could never be false, and the other caller already ran without it.
    /// </summary>
    public static IReadOnlyList<(MenuSectionDto Input, MenuSection Entity)> ReplaceSections(
        ApplicationDbContext context,
        MenuDefinition menuDefinition,
        IEnumerable<MenuSectionDto> sections,
        string auditIdentifier)
    {
        // A no-op on the create path, where the definition was constructed moments ago and holds no
        // sections. Cheaper than a second public overload that could be called where one was needed.
        context.MenuSections.RemoveRange(menuDefinition.Sections);
        var written = AddSections(context, menuDefinition, sections, auditIdentifier);

        if (context.Entry(menuDefinition).State != EntityState.Added)
        {
            menuDefinition.AuthoringVersion++;
        }
        return written;
    }

    private static List<(MenuSectionDto Input, MenuSection Entity)> AddSections(
        ApplicationDbContext context,
        MenuDefinition menuDefinition,
        IEnumerable<MenuSectionDto> sections,
        string auditIdentifier)
    {
        var now = DateTime.UtcNow;
        var written = new List<(MenuSectionDto Input, MenuSection Entity)>();

        foreach (var sectionDto in sections)
        {
            var section = new MenuSection
            {
                Id = Guid.NewGuid(),
                MenuDefinition = menuDefinition, // EF Core will handle the ID link
                Name = sectionDto.Name,
                Description = sectionDto.Description,
                DisplayOrder = sectionDto.DisplayOrder,
                IsRequired = sectionDto.IsRequired,
                MinSelection = sectionDto.MinSelection,
                MaxSelection = sectionDto.MaxSelection,
                Translations = BuildTranslations(sectionDto.Translations ?? [], auditIdentifier, now),
                CreatedAt = now,
                CreatedBy = auditIdentifier
            };

            context.MenuSections.Add(section);
            written.Add((sectionDto, section));

            // NOT a dead guard, despite reading like the `Sections` one #191 removed:
            // The DTO's backing list has an empty default, and STJ writes a literal `"items": null`
            // straight over it (RespectNullableAnnotations is off — the very mechanism that made
            // `sections: null` the one preserving payload before #191). Nothing validates Items, so
            // this is all that stands between such a body and an NRE; removing it is a measured 500,
            // pinned by MenuDefinitionSectionsRequiredTests.SectionWithNullItems_IsAcceptedAsNoItems.
            if (sectionDto.Items == null)
            {
                continue;
            }

            foreach (var itemDto in sectionDto.Items)
            {
                context.MenuSectionItems.Add(new MenuSectionItem
                {
                    MenuSection = section,
                    ProductId = itemDto.ProductId,
                    ProductVariationId = itemDto.ProductVariationId,
                    AdditionalPrice = itemDto.AdditionalPrice,
                    DisplayOrder = itemDto.DisplayOrder,
                    IsDefault = itemDto.IsDefault,
                    CreatedAt = now,
                    CreatedBy = auditIdentifier
                });
            }
        }
        return written;
    }

    private static void ReplaceTranslations(
        ApplicationDbContext context,
        MenuSection section,
        IReadOnlyDictionary<string, MenuSectionTranslationDto> translations,
        string auditIdentifier,
        DateTime now)
    {
        var normalized = NormalizeTranslations(translations);
        context.MenuSectionTranslations.RemoveRange(section.Translations);
        section.Translations.Clear();

        foreach (var (languageCode, translation) in normalized)
        {
            var entity = new MenuSectionTranslation
            {
                MenuSection = section,
                LanguageCode = languageCode,
                Name = translation.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(translation.Description)
                    ? null
                    : translation.Description.Trim(),
                CreatedAt = now,
                CreatedBy = auditIdentifier
            };
            section.Translations.Add(entity);
            context.MenuSectionTranslations.Add(entity);
        }
    }

    private static List<MenuSectionTranslation> BuildTranslations(
        IReadOnlyDictionary<string, MenuSectionTranslationDto> translations,
        string auditIdentifier,
        DateTime now) => NormalizeTranslations(translations)
        .Select(pair => new MenuSectionTranslation
        {
            LanguageCode = pair.Key,
            Name = pair.Value.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(pair.Value.Description) ? null : pair.Value.Description.Trim(),
            CreatedAt = now,
            CreatedBy = auditIdentifier
        })
        .ToList();

    private static Dictionary<string, MenuSectionTranslationDto> NormalizeTranslations(
        IReadOnlyDictionary<string, MenuSectionTranslationDto> translations)
    {
        var result = new Dictionary<string, MenuSectionTranslationDto>(StringComparer.Ordinal);
        foreach (var (rawLanguageCode, translation) in translations)
        {
            var languageCode = rawLanguageCode.Trim().ToLowerInvariant();
            if (!MenuSectionLocale.IsValidTag(languageCode))
            {
                throw new BadRequestException($"Invalid menu section language tag '{rawLanguageCode}'");
            }

            if (translation is null || string.IsNullOrWhiteSpace(translation.Name) || translation.Name.Length > 100)
            {
                throw new BadRequestException($"A translated section name of at most 100 characters is required for '{rawLanguageCode}'");
            }

            if (translation.Description?.Length > 500)
            {
                throw new BadRequestException($"The translated section description for '{rawLanguageCode}' cannot exceed 500 characters");
            }

            if (!result.TryAdd(languageCode, translation))
            {
                throw new BadRequestException($"Duplicate menu section language tag '{rawLanguageCode}'");
            }
        }

        return result;
    }

    private static void ApplyItems(
        ApplicationDbContext context,
        MenuSection section,
        IReadOnlyCollection<MenuSectionItemDto> items,
        string auditIdentifier,
        DateTime now)
    {
        var existingItems = section.Items.ToDictionary(item => item.Id);
        var retainedItems = new HashSet<Guid>();

        foreach (var itemDto in items)
        {
            MenuSectionItem item;
            if (itemDto.Id is Guid itemId)
            {
                item = existingItems[itemId];
                retainedItems.Add(itemId);
            }
            else
            {
                item = new MenuSectionItem
                {
                    Id = Guid.NewGuid(),
                    MenuSection = section,
                    CreatedAt = now,
                    CreatedBy = auditIdentifier
                };
                section.Items.Add(item);
                context.MenuSectionItems.Add(item);
            }

            item.ProductId = itemDto.ProductId;
            item.ProductVariationId = itemDto.ProductVariationId;
            item.AdditionalPrice = itemDto.AdditionalPrice;
            item.DisplayOrder = itemDto.DisplayOrder;
            item.IsDefault = itemDto.IsDefault;
            item.UpdatedAt = now;
            item.UpdatedBy = auditIdentifier;
        }

        foreach (var removed in existingItems.Values.Where(item => !retainedItems.Contains(item.Id)))
        {
            context.MenuSectionItems.Remove(removed);
            section.Items.Remove(removed);
        }
    }
}
