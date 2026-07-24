using FluentValidation;

namespace RestaurantSystem.Api.Features.FloorPlan.Commands.SaveFloorPlanCommand;

/// <summary>
/// Hard bounds for a whole-document save (FLOOR-PLAN-REVAMP §5.1): plan
/// dimensions, grid size, collection caps, ≤ 200 vertices per wall, and the
/// allowed kind/shape vocabularies. Coordinate clamping into the plan bounds is
/// done in the service — the validator only rejects the structurally invalid.
/// </summary>
public class SaveFloorPlanCommandValidator : AbstractValidator<SaveFloorPlanCommand>
{
    public SaveFloorPlanCommandValidator()
    {
        RuleFor(c => c.Document.WidthMeters).InclusiveBetween(1m, 100m);
        RuleFor(c => c.Document.HeightMeters).InclusiveBetween(1m, 100m);
        RuleFor(c => c.Document.GridSizeCm)
            .Must(FloorPlanKinds.GridSizesCm.Contains).WithMessage("GridSizeCm must be 10, 25, 50 or 100.");

        RuleFor(c => c.Document.Walls).Must(w => w.Count <= 100).WithMessage("Too many walls (max 100).");
        RuleFor(c => c.Document.Items).Must(i => i.Count <= 500).WithMessage("Too many items (max 500).");
        RuleFor(c => c.Document.Tables).Must(t => t.Count <= 500).WithMessage("Too many tables (max 500).");

        RuleForEach(c => c.Document.Walls).ChildRules(wall =>
        {
            wall.RuleFor(w => w.Points.Count).InclusiveBetween(2, 200)
                .WithMessage("A wall must have between 2 and 200 vertices.");
            wall.RuleFor(w => w.Openings.Count).LessThanOrEqualTo(50)
                .WithMessage("Too many openings on a wall (max 50).");
            wall.RuleForEach(w => w.Openings).ChildRules(opening =>
                opening.RuleFor(o => o.Kind)
                    .Must(FloorPlanKinds.Openings.Contains).WithMessage("Invalid opening kind."));
        });

        RuleForEach(c => c.Document.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Kind)
                .Must(FloorPlanKinds.Items.Contains).WithMessage("Invalid item kind.");
            item.RuleFor(i => i.RotationDegrees).InclusiveBetween(0m, 360m);
        });

        RuleForEach(c => c.Document.Tables).ChildRules(table =>
        {
            table.RuleFor(t => t.Shape)
                .Must(FloorPlanKinds.TableShapes.Contains).WithMessage("Invalid table shape.");
            table.RuleFor(t => t.Rotation).InclusiveBetween(0, 360);
        });
    }
}
