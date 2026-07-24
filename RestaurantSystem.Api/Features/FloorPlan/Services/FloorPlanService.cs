using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FloorPlan.Services;

/// <inheritdoc />
public class FloorPlanService : IFloorPlanService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public FloorPlanService(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<FloorPlanDocumentDto?> GetDefaultAsync(CancellationToken cancellationToken)
    {
        var plan = await LoadDefaultPlanAsync(cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var tables = await LoadTablesAsync(plan.Id, cancellationToken);
        return FloorPlanDocumentMapper.ToDocumentDto(plan, tables);
    }

    public async Task<SaveFloorPlanResult> SaveAsync(Guid planId, FloorPlanDocumentDto document, CancellationToken cancellationToken)
    {
        var plan = await _context.FloorPlans
            .Include(p => p.Walls).ThenInclude(w => w.Openings)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

        if (plan is null)
        {
            return SaveFloorPlanResult.NotFound($"Floor plan '{planId}' was not found.");
        }

        // Optimistic concurrency: the client echoes the UpdatedAt it loaded; a
        // mismatch means someone else saved in between (FLOOR-PLAN-REVAMP §4.3).
        if (document.UpdatedAt != plan.UpdatedAt)
        {
            return SaveFloorPlanResult.Conflict("The plan was changed by someone else. Reload and try again.");
        }

        var auditId = _currentUser.GetAuditIdentifier();
        ApplyPlanScalars(plan, document);

        // The document PUT owns geometry only — walls, openings and items are
        // replaced wholesale (the seam that lets undo/redo + one Save work
        // without a transactional nightmare, §5.2).
        _context.FloorPlanOpenings.RemoveRange(plan.Walls.SelectMany(w => w.Openings));
        _context.FloorPlanWalls.RemoveRange(plan.Walls);
        _context.FloorPlanItems.RemoveRange(plan.Items);

        var zWall = 0;
        foreach (var wallDto in document.Walls)
        {
            plan.Walls.Add(FloorPlanDocumentMapper.BuildWall(wallDto, plan.WidthMeters, plan.HeightMeters, zWall++, auditId));
        }

        var zItem = 0;
        foreach (var itemDto in document.Items)
        {
            plan.Items.Add(FloorPlanDocumentMapper.BuildItem(itemDto, plan.WidthMeters, plan.HeightMeters, zItem++, auditId));
        }

        await ApplyTableGeometryAsync(plan, document, cancellationToken);

        // Advance the concurrency token (the audit hook only covers sync SaveChanges).
        plan.UpdatedAt = DateTime.UtcNow;
        plan.UpdatedBy = auditId;

        await _context.SaveChangesAsync(cancellationToken);

        var saved = await GetByIdAsync(plan.Id, cancellationToken);
        return SaveFloorPlanResult.Ok(saved!);
    }

    private async Task ApplyTableGeometryAsync(Domain.Entities.FloorPlan plan, FloorPlanDocumentDto document, CancellationToken cancellationToken)
    {
        var ids = document.Tables.Select(t => t.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var tables = await _context.Tables
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        foreach (var dto in document.Tables)
        {
            // Unknown table ids are ignored (§5.2) — the document never
            // creates or deletes tables, only repositions existing ones.
            if (!tables.TryGetValue(dto.Id, out var table))
            {
                continue;
            }

            FloorPlanDocumentMapper.ApplyTableGeometry(table, dto, plan.WidthMeters, plan.HeightMeters);
            table.FloorPlanId = plan.Id;
        }
    }

    private static void ApplyPlanScalars(Domain.Entities.FloorPlan plan, FloorPlanDocumentDto document)
    {
        plan.Name = string.IsNullOrWhiteSpace(document.Name) ? plan.Name : document.Name.Trim();
        plan.WidthMeters = Math.Clamp(document.WidthMeters, 1m, 100m);
        plan.HeightMeters = Math.Clamp(document.HeightMeters, 1m, 100m);
        plan.GridSizeCm = FloorPlanKinds.GridSizesCm.Contains(document.GridSizeCm) ? document.GridSizeCm : plan.GridSizeCm;
        plan.BackgroundStyle = string.IsNullOrWhiteSpace(document.BackgroundStyle) ? plan.BackgroundStyle : document.BackgroundStyle;
    }

    private async Task<FloorPlanDocumentDto?> GetByIdAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await _context.FloorPlans
            .Include(p => p.Walls).ThenInclude(w => w.Openings)
            .Include(p => p.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var tables = await LoadTablesAsync(plan.Id, cancellationToken);
        return FloorPlanDocumentMapper.ToDocumentDto(plan, tables);
    }

    private Task<Domain.Entities.FloorPlan?> LoadDefaultPlanAsync(CancellationToken cancellationToken) =>
        _context.FloorPlans
            .Include(p => p.Walls).ThenInclude(w => w.Openings)
            .Include(p => p.Items)
            .AsNoTracking()
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.DisplayOrder)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<Table>> LoadTablesAsync(Guid planId, CancellationToken cancellationToken) =>
        await _context.Tables
            .Where(t => t.FloorPlanId == planId)
            .OrderBy(t => t.TableNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
}
