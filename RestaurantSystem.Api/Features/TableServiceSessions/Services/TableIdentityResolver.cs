using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public sealed class TableIdentityResolver : ITableIdentityResolver
{
    private readonly ApplicationDbContext _context;

    public TableIdentityResolver(ApplicationDbContext context) => _context = context;

    public async Task<TableIdentity> ResolveActiveAsync(
        Guid? tableId,
        int? tableNumber,
        CancellationToken cancellationToken)
    {
        if (tableId.HasValue)
        {
            var table = await _context.Tables
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == tableId.Value, cancellationToken);
            if (table is null)
            {
                throw new NotFoundException(
                    "The selected table was not found.", ErrorCodes.TableServiceTableNotFound);
            }

            if (!table.IsActive)
            {
                throw new BadRequestException(
                    "The selected table is inactive.", ErrorCodes.TableServiceTableInactive);
            }

            var identity = ToIdentity(table.TableNumber);
            if (tableNumber.HasValue && (!identity.Number.HasValue || tableNumber.Value != identity.Number.Value))
            {
                throw new BadRequestException(
                    "The table id and table number do not identify the same table.",
                    ErrorCodes.TableServiceTableMismatch);
            }

            return new TableIdentity(table.Id, identity.Label, identity.Number);
        }

        if (!tableNumber.HasValue)
        {
            throw new BadRequestException(
                "A stable table id or table number is required.",
                ErrorCodes.TableServiceTableRequired);
        }

        var legacyLabel = tableNumber.Value.ToString(CultureInfo.InvariantCulture);
        var legacyTable = await _context.Tables
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TableNumber == legacyLabel, cancellationToken);
        if (legacyTable is null)
        {
            throw new NotFoundException(
                "The selected table was not found.", ErrorCodes.TableServiceTableNotFound);
        }

        if (!legacyTable.IsActive)
        {
            throw new BadRequestException(
                "The selected table is inactive.", ErrorCodes.TableServiceTableInactive);
        }

        return new TableIdentity(legacyTable.Id, legacyTable.TableNumber, tableNumber.Value);
    }

    private static (string Label, int? Number) ToIdentity(string label)
    {
        var isCanonicalNumber = int.TryParse(
            label, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0
            && number.ToString(CultureInfo.InvariantCulture) == label;
        return (label, isCanonicalNumber ? number : null);
    }
}
