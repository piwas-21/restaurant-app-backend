using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

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
    public static void ReplaceSections(
        ApplicationDbContext context,
        MenuDefinition menuDefinition,
        IEnumerable<MenuSectionDto> sections,
        string auditIdentifier)
    {
        // A no-op on the create path, where the definition was constructed moments ago and holds no
        // sections. Cheaper than a second public overload that could be called where one was needed.
        context.MenuSections.RemoveRange(menuDefinition.Sections);
        AddSections(context, menuDefinition, sections, auditIdentifier);
    }

    private static void AddSections(
        ApplicationDbContext context,
        MenuDefinition menuDefinition,
        IEnumerable<MenuSectionDto> sections,
        string auditIdentifier)
    {
        var now = DateTime.UtcNow;

        foreach (var sectionDto in sections)
        {
            var section = new MenuSection
            {
                MenuDefinition = menuDefinition, // EF Core will handle the ID link
                Name = sectionDto.Name,
                Description = sectionDto.Description,
                DisplayOrder = sectionDto.DisplayOrder,
                IsRequired = sectionDto.IsRequired,
                MinSelection = sectionDto.MinSelection,
                MaxSelection = sectionDto.MaxSelection,
                CreatedAt = now,
                CreatedBy = auditIdentifier
            };

            context.MenuSections.Add(section);

            // NOT a dead guard, despite reading like the `Sections` one #191 removed:
            // MenuSectionDto.Items keeps its initializer, and STJ writes a literal `"items": null`
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
                    AdditionalPrice = itemDto.AdditionalPrice,
                    DisplayOrder = itemDto.DisplayOrder,
                    IsDefault = itemDto.IsDefault,
                    CreatedAt = now,
                    CreatedBy = auditIdentifier
                });
            }
        }
    }
}
