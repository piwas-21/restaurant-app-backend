using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

public sealed partial class OptionSetCatalogService
{
    private async Task ValidateEntriesAsync(
        OptionSetKind kind,
        IReadOnlyList<OptionSetEntryDto> entries,
        CancellationToken cancellationToken,
        IReadOnlySet<Guid>? stagedProductIds = null)
    {
        var errors = await OptionSetEntryValidator.ValidateManyAsync(
            _context,
            kind,
            entries,
            _settings.MaximumEntriesPerOptionSet,
            stagedProductIds: stagedProductIds,
            cancellationToken: cancellationToken);
        if (errors.FirstOrDefault(error => error is not null) is string error)
        {
            throw new BadRequestException(error);
        }
    }

    private void ValidateEntryCount(OptionSetWriteRequestDto request)
    {
        if (request.Entries.Count > _settings.MaximumEntriesPerOptionSet)
        {
            throw new BadRequestException(
                $"An option set may contain at most {_settings.MaximumEntriesPerOptionSet} entries");
        }
    }

    private static void ValidateHeader(OptionSetWriteRequestDto request, bool creating)
    {
        if (!Enum.IsDefined(request.Kind) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            throw new BadRequestException("Choose a valid option-set kind and a name up to 120 characters");
        }

        if (!Enum.IsDefined(request.Status) || (creating && request.Status != OptionSetStatus.Active))
        {
            throw new BadRequestException("New option sets must start active");
        }
    }

    private async Task SaveWithConflictTranslationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("This option set changed. Reload it and review the current values.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new ConflictException("An option set with the same name or canonical entry already exists", exception);
        }
    }
}
