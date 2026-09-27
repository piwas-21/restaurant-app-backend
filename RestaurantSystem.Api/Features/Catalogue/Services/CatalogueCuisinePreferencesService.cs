using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueCuisinePreferencesService(
    ApplicationDbContext context,
    ICurrentUserService currentUser) : ICatalogueCuisinePreferencesService
{
    private const int MaximumPreferences = 12;
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex SlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, RegexMatchTimeout);

    public async Task<CatalogueCuisinePreferencesDto> GetAsync(CancellationToken cancellationToken)
    {
        var row = await context.CatalogueCuisinePreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == CatalogueCuisinePreference.SingletonId, cancellationToken);
        return new CatalogueCuisinePreferencesDto(row?.Cuisines ?? []);
    }

    public async Task<CatalogueCuisinePreferencesDto> ReplaceAsync(
        IReadOnlyCollection<string> cuisines,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(cuisines);
        var row = await context.CatalogueCuisinePreferences
            .FirstOrDefaultAsync(x => x.Id == CatalogueCuisinePreference.SingletonId, cancellationToken);
        var isNew = row is null;

        if (row is null)
        {
            row = new CatalogueCuisinePreference
            {
                Id = CatalogueCuisinePreference.SingletonId,
                Cuisines = normalized,
                CreatedBy = currentUser.GetAuditIdentifier()
            };
            context.CatalogueCuisinePreferences.Add(row);
        }
        else
        {
            row.Cuisines = normalized;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = currentUser.GetAuditIdentifier();
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (isNew)
        {
            context.Entry(row).State = EntityState.Detached;
            var winner = await context.CatalogueCuisinePreferences
                .FirstOrDefaultAsync(x => x.Id == CatalogueCuisinePreference.SingletonId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            winner.Cuisines = normalized;
            winner.UpdatedAt = DateTime.UtcNow;
            winner.UpdatedBy = currentUser.GetAuditIdentifier();
            await context.SaveChangesAsync(cancellationToken);
            row = winner;
        }

        return new CatalogueCuisinePreferencesDto(row.Cuisines);
    }

    private static List<string> Normalize(IReadOnlyCollection<string> cuisines)
    {
        if (cuisines is null || cuisines.Count > MaximumPreferences || cuisines.Any(string.IsNullOrWhiteSpace))
        {
            throw new BadRequestException("Choose at most 12 valid cuisine slugs");
        }

        var normalized = cuisines.Select(value => value.Trim().ToLowerInvariant()).ToList();
        if (normalized.Any(value => value.Length > 64 || !SlugPattern.IsMatch(value)) ||
            normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Count)
        {
            throw new BadRequestException("Cuisine preferences must be unique lowercase slugs");
        }

        return normalized;
    }
}
