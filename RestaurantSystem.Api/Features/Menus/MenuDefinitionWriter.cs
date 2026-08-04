using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// The single UPDATE-side owner of a <see cref="MenuDefinition"/>'s own columns — the
/// create-if-absent step and the schedule block — for the two PUT handlers.
///
/// <para><b>It is NOT the only writer of those columns.</b>
/// <c>CreateMenuBundleCommandHandler</c> builds its definition inline as an object initializer and
/// does not call this class, so the schedule is assigned in TWO places, not one. That is a real
/// hazard and the reason it is named here rather than left for a grep: add an eighth day flag or
/// any other schedule column, wire it into <see cref="Upsert"/> alone, and every bundle created
/// through <c>POST /api/Menus</c> silently stores the entity default while every bundle UPDATED
/// stores the request value. Adding a column means editing both. (Routing create through
/// <see cref="Upsert"/> would collapse them, but it would also stamp <c>UpdatedAt</c>/<c>UpdatedBy</c>
/// on a freshly created row, which that path deliberately leaves null — a separate decision from
/// #296.) Contrast <see cref="MenuSectionWriter"/>, which really does have all three callers.</para>
///
/// <para><see cref="MenuSectionWriter"/> owns the child sections. Where both run they run in that
/// order, definition then sections, because the sections need a definition to hang from — but
/// create-bundle calls MenuSectionWriter alone.</para>
///
/// It exists for the same reason MenuSectionWriter does. SonarCloud measured the create + schedule
/// block as 25 duplicated lines in UpdateProductCommand.cs against 23 in UpdateMenuBundleCommand.cs
/// — one block, token-identical apart from how each caller resolves the existing row. #296 had to
/// de-indent the product copy by one level to lift it out of the detailed-ingredients branch, and
/// Sonar's new-code detection is SCM-based: a whitespace-only touch would have re-attributed all 25
/// lines to that fix as NEW duplication. Extracting moves the whole block to one place, which
/// genuinely clears it. A cpd exclusion would have claimed the repetition was inherent, and it is
/// not — the only real difference between the callers is the resolve, which is why that stays a
/// parameter.
/// </summary>
public static class MenuDefinitionWriter
{
    /// <summary>
    /// Returns the definition for <paramref name="productId"/> with <paramref name="dto"/>'s schedule
    /// applied, creating and tracking one when <paramref name="existing"/> is null.
    ///
    /// <para><b>The caller resolves <paramref name="existing"/>, and how it does so is
    /// load-bearing.</b> Whatever it passes is what <see cref="MenuSectionWriter.ReplaceSections"/>
    /// then receives, and that method requires <c>Sections</c> to have been loaded: the collection is
    /// non-nullable and always initialized, so an un-included one reads as EMPTY rather than null and
    /// the replace silently appends instead of replacing. The bundle handler satisfies this from its
    /// <c>ThenInclude(md =&gt; md!.Sections)</c> navigation; the product handler cannot — its product
    /// query includes MenuDefinition without the sections — so it issues a second query that does.
    /// That asymmetry is the reason this takes a resolved definition instead of loading one itself.</para>
    ///
    /// <para>Callers that then persist sections must still call
    /// <see cref="MenuSectionWriter.ReplaceSections"/>; this method deliberately does not, so that
    /// class keeps the single public entry point its own contract depends on.</para>
    /// </summary>
    public static MenuDefinition Upsert(
        ApplicationDbContext context,
        MenuDefinition? existing,
        Guid productId,
        MenuDefinitionDto dto,
        string auditIdentifier)
    {
        var menuDefinition = existing;

        if (menuDefinition == null)
        {
            menuDefinition = new MenuDefinition
            {
                ProductId = productId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            };
            context.MenuDefinitions.Add(menuDefinition);
        }

        menuDefinition.IsAlwaysAvailable = dto.IsAlwaysAvailable;
        menuDefinition.StartTime = dto.StartTime;
        menuDefinition.EndTime = dto.EndTime;
        menuDefinition.AvailableMonday = dto.AvailableMonday;
        menuDefinition.AvailableTuesday = dto.AvailableTuesday;
        menuDefinition.AvailableWednesday = dto.AvailableWednesday;
        menuDefinition.AvailableThursday = dto.AvailableThursday;
        menuDefinition.AvailableFriday = dto.AvailableFriday;
        menuDefinition.AvailableSaturday = dto.AvailableSaturday;
        menuDefinition.AvailableSunday = dto.AvailableSunday;
        menuDefinition.UpdatedAt = DateTime.UtcNow;
        menuDefinition.UpdatedBy = auditIdentifier;

        return menuDefinition;
    }
}
