using FluentValidation;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.AttachGlobalIngredientCommand;

/// <summary>
/// The bulk attach may only create OPTIONAL rows, and that is the load-bearing rule of slice S8.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured, not assumed.</b> An order placed before the S1 snapshot has no frozen ingredient
/// rows, so it renders through <c>OrderIngredientCustomizations.ProjectRecipe</c> against the LIVE
/// recipe. That method has a branch — <c>else if (!ing.IsOptional)</c> — which reports any required
/// ingredient missing from the line's saved id map as <c>Quantity = 0, IsRemoved = true</c>. So
/// attaching a REQUIRED ingredient to a product prints "NO &lt;name&gt;" on every historic receipt
/// and kitchen ticket for that product, for a removal nobody made. In bulk that is forty products
/// of rewritten history from one click, which is both plan D2 ("a past receipt never changes") and
/// plan §6's "irreversible bulk edit" in a single action.
/// </para>
/// <para>
/// The single-product editor is deliberately NOT restricted: an admin adding a required ingredient
/// there is looking at one product and has chosen it. What is refused is doing it blind, to many.
/// <c>AttachingARequiredIngredient_IsRefused</c> pins the refusal and
/// <c>ARequiredIngredientWouldHaveRewrittenHistory</c> is the control that proves the danger is
/// real rather than theoretical.
/// </para>
/// </remarks>
public class AttachGlobalIngredientCommandValidator : AbstractValidator<AttachGlobalIngredientCommand>
{
    /// <summary>
    /// A sane ceiling rather than a business rule: the whole batch is loaded with its ingredients
    /// and variations to be validated, and the live catalogue is 77 products.
    /// </summary>
    private const int MaxProductsPerBatch = 500;

    public AttachGlobalIngredientCommandValidator()
    {
        RuleFor(command => command.Body.ProductIds)
            .NotEmpty().WithMessage("Select at least one product.")
            .Must(ids => ids.Count <= MaxProductsPerBatch)
            .WithMessage($"A bulk attach covers at most {MaxProductsPerBatch} products at a time.");

        RuleFor(command => command.Body.ProductIds)
            .Must(ids => ids.TrueForAll(id => id != Guid.Empty))
            .WithMessage("A product id is empty.");

        RuleFor(command => command.Body.IsOptional)
            .Equal(true)
            .WithMessage(
                "A bulk attach may only add OPTIONAL ingredients. A required one would be reported "
                + "as removed on every order placed before this product was changed. Add a required "
                + "ingredient on the product itself.");

        RuleFor(command => command.Body.Price).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Body.MaxQuantity).InclusiveBetween(1, 99);
    }
}
