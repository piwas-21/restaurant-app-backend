namespace RestaurantSystem.Api.Features.Products.Dtos;

public record MenuDefinitionDto
{
    /// <summary>
    /// The 400 both write paths answer with when <see cref="Sections"/> is absent or null. Shared
    /// because five call sites (two validators, three handlers) must agree on it and one of them is
    /// asserted by an integration test — five copies would drift silently.
    /// </summary>
    public const string SectionsRequiredMessage = "Menu definition sections are required (send [] to remove them all)";

    public Guid? Id { get; init; }
    public bool IsAlwaysAvailable { get; init; }
    public TimeSpan? StartTime { get; init; }
    public TimeSpan? EndTime { get; init; }

    public bool AvailableMonday { get; init; }
    public bool AvailableTuesday { get; init; }
    public bool AvailableWednesday { get; init; }
    public bool AvailableThursday { get; init; }
    public bool AvailableFriday { get; init; }
    public bool AvailableSaturday { get; init; }
    public bool AvailableSunday { get; init; }

    /// <summary>
    /// Nullable, and deliberately WITHOUT an initializer (#191). With `= new()` an absent JSON key
    /// deserialized to `[]`, so the three write handlers could not tell "I am not sending sections"
    /// from "delete every section" — and both wiped. The write paths now require the key
    /// (MenuBundleCommandValidatorBase / UpdateProductCommandValidator), so `null` is a 400 rather
    /// than a third silent meaning, and `[]` keeps its one honest meaning: clear them all.
    ///
    /// Response mappers (ProductDtoMapper, GetProductByIdQuery) always assign it, so the serialized
    /// contract is unchanged — this nullability describes the REQUEST direction only.
    ///
    /// The two rules enforcing it keep a null definition away from the accessor in DIFFERENT ways,
    /// and the difference is easy to misread. The bundle rule guards with
    /// <c>.When(x =&gt; x.MenuDefinition != null)</c>; the product rule's <c>.When()</c> carries only
    /// <c>x.Type == ProductType.Menu</c> — so a non-Menu product carrying a menu definition is
    /// exempt there — and handles the null case INSIDE its <c>Must</c> predicate instead, which is
    /// why it needs no null-forgiving operator. Either way the accessor is unreachable on null, and
    /// that is load-bearing rather than defensive: MenuDefinition is declared non-nullable on the
    /// bundle commands but STJ still leaves it null when the key is absent, so an unguarded
    /// accessor NREs — measured as a 500, pinned by
    /// <c>MenuDefinitionSectionsRequiredTests.OmittedMenuDefinition_Is400_NotServerError</c>.
    /// </summary>
    public List<MenuSectionDto>? Sections { get; init; }
}
