using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed record CatalogueCandidateSearch(string TemplateType, string Name);

internal static class CatalogueImportCandidateLookup
{
    private const int CandidateLimit = 6;
    private const string RequestedNamesCte = """
        WITH requested(search_key) AS (
            SELECT DISTINCT value
            FROM unnest(@search_keys::text[]) AS requested_names(value)
        )
        """;

    private const string CategorySql = RequestedNamesCte + """
        SELECT requested.search_key AS "SearchKey",
               'category'::text AS "TemplateType",
               'Category'::text AS "EntityType",
               candidate.id AS "Id",
               candidate.name AS "Name",
               candidate.is_active AS "IsActive",
               NULL::text AS "CategoryName"
        FROM requested
        CROSS JOIN LATERAL (
            SELECT category.id, category.name, category.is_active
            FROM categories AS category
            WHERE category.is_deleted = false AND lower(category.name) = requested.search_key
            ORDER BY category.name, category.id
            LIMIT @candidate_limit
        ) AS candidate
        """;

    private const string IngredientSql = RequestedNamesCte + """
        SELECT requested.search_key AS "SearchKey",
               'ingredient'::text AS "TemplateType",
               'GlobalIngredient'::text AS "EntityType",
               candidate.id AS "Id",
               candidate.default_name AS "Name",
               candidate.is_active AS "IsActive",
               NULL::text AS "CategoryName"
        FROM requested
        CROSS JOIN LATERAL (
            SELECT ingredient.id, ingredient.default_name, ingredient.is_active
            FROM global_ingredients AS ingredient
            WHERE ingredient.is_deleted = false
              AND (lower(ingredient.default_name) = requested.search_key OR EXISTS (
                    SELECT 1 FROM global_ingredient_translations AS translation
                    WHERE translation.global_ingredient_id = ingredient.id
                      AND lower(translation.name) = requested.search_key))
            ORDER BY CASE WHEN lower(ingredient.default_name) = requested.search_key THEN 0 ELSE 1 END,
                     ingredient.default_name, ingredient.id
            LIMIT @candidate_limit
        ) AS candidate
        """;

    private const string OptionSetSql = RequestedNamesCte + """
        SELECT requested.search_key AS "SearchKey",
               'option-set'::text AS "TemplateType",
               'OptionSet'::text AS "EntityType",
               candidate.id AS "Id",
               candidate.name AS "Name",
               candidate.status = @active_status AS "IsActive",
               NULL::text AS "CategoryName"
        FROM requested
        CROSS JOIN LATERAL (
            SELECT option_set.id, option_set.name, option_set.status
            FROM "OptionSets" AS option_set
            WHERE lower(option_set.name) = requested.search_key OR EXISTS (
                SELECT 1 FROM "OptionSetTranslations" AS translation
                WHERE translation.option_set_id = option_set.id
                  AND lower(translation.name) = requested.search_key)
            ORDER BY CASE WHEN lower(option_set.name) = requested.search_key THEN 0 ELSE 1 END,
                     option_set.name, option_set.id
            LIMIT @candidate_limit
        ) AS candidate
        """;

    private const string ItemSql = RequestedNamesCte + """
        SELECT requested.search_key AS "SearchKey",
               'item'::text AS "TemplateType",
               'Product'::text AS "EntityType",
               candidate.id AS "Id",
               candidate.name AS "Name",
               candidate.is_active AS "IsActive",
               candidate.category_name AS "CategoryName"
        FROM requested
        CROSS JOIN LATERAL (
            SELECT product.id, product.name, product.is_active, category.category_name
            FROM "Products" AS product
            LEFT JOIN LATERAL (
                SELECT category.name AS category_name
                FROM product_categories AS link
                JOIN categories AS category ON category.id = link.category_id
                WHERE link.product_id = product.id AND category.is_deleted = false
                ORDER BY link.display_order, link.id
                LIMIT 1
            ) AS category ON true
            WHERE product.is_deleted = false AND product.type <> @menu_type
              AND (lower(product.name) = requested.search_key OR EXISTS (
                    SELECT 1 FROM product_descriptions AS description
                    WHERE description.product_id = product.id
                      AND lower(description.name) = requested.search_key))
            ORDER BY CASE WHEN lower(product.name) = requested.search_key THEN 0 ELSE 1 END,
                     product.name, product.id
            LIMIT @candidate_limit
        ) AS candidate
        """;

    private const string BundleSql = RequestedNamesCte + """
        SELECT requested.search_key AS "SearchKey",
               'bundle'::text AS "TemplateType",
               'MenuBundle'::text AS "EntityType",
               candidate.id AS "Id",
               candidate.name AS "Name",
               candidate.is_active AS "IsActive",
               candidate.category_name AS "CategoryName"
        FROM requested
        CROSS JOIN LATERAL (
            SELECT product.id, product.name, product.is_active, category.category_name
            FROM "Products" AS product
            LEFT JOIN LATERAL (
                SELECT category.name AS category_name
                FROM product_categories AS link
                JOIN categories AS category ON category.id = link.category_id
                WHERE link.product_id = product.id AND category.is_deleted = false
                ORDER BY link.display_order, link.id
                LIMIT 1
            ) AS category ON true
            WHERE product.is_deleted = false AND product.type = @menu_type
              AND (lower(product.name) = requested.search_key OR EXISTS (
                    SELECT 1 FROM product_descriptions AS description
                    WHERE description.product_id = product.id
                      AND lower(description.name) = requested.search_key))
            ORDER BY CASE WHEN lower(product.name) = requested.search_key THEN 0 ELSE 1 END,
                     product.name, product.id
            LIMIT @candidate_limit
        ) AS candidate
        """;

    public static async Task<Dictionary<(string Type, string Name), List<CatalogueLocalCandidateDto>>> LoadAsync(
        ApplicationDbContext context,
        IEnumerable<CatalogueCandidateSearch> requested,
        CancellationToken cancellationToken)
    {
        var searches = requested.Where(search => !string.IsNullOrWhiteSpace(search.Name) &&
                CatalogueImportReviewRules.ExpectedEntityType(search.TemplateType).Length > 0)
            .GroupBy(search => Key(search.TemplateType, search.Name))
            .Select(group => group.First())
            .ToArray();
        var result = searches.ToDictionary(
            search => Key(search.TemplateType, search.Name),
            _ => new List<CatalogueLocalCandidateDto>());

        await AddRowsAsync("category", CategorySql);
        await AddRowsAsync("ingredient", IngredientSql);
        await AddRowsAsync("option-set", OptionSetSql,
            new NpgsqlParameter("active_status", NpgsqlDbType.Integer) { Value = (int)OptionSetStatus.Active });
        await AddProductRowsAsync("item", ItemSql);
        await AddProductRowsAsync("bundle", BundleSql);

        foreach (var candidates in result.Values)
        {
            var ordered = candidates.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Id)
                .Take(CandidateLimit)
                .ToArray();
            candidates.Clear();
            candidates.AddRange(ordered);
        }

        return result;

        async Task AddRowsAsync(string templateType, string sql, params NpgsqlParameter[] extraParameters)
        {
            var names = NamesFor(templateType);
            if (names.Length == 0) return;

            var rows = await context.Database.SqlQueryRaw<CandidateLookupRow>(sql,
                    Parameters(names, extraParameters))
                .ToListAsync(cancellationToken);
            AddRows(rows);
        }

        async Task AddProductRowsAsync(string templateType, string sql)
        {
            var names = NamesFor(templateType);
            if (names.Length == 0) return;

            var rows = await context.Database.SqlQueryRaw<CandidateLookupRow>(sql,
                    Parameters(names, [new NpgsqlParameter("menu_type", (int)ProductType.Menu)]))
                .ToListAsync(cancellationToken);
            AddRows(rows);
        }

        string[] NamesFor(string templateType) => searches
            .Where(search => search.TemplateType == templateType)
            .Select(search => search.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(NameKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        void AddRows(IEnumerable<CandidateLookupRow> rows)
        {
            foreach (var row in rows)
            {
                var search = searches.FirstOrDefault(candidate =>
                    candidate.TemplateType == row.TemplateType &&
                    string.Equals(NameKey(candidate.Name), row.SearchKey, StringComparison.Ordinal));
                if (search is null) continue;

                var candidates = result[Key(search.TemplateType, search.Name)];
                if (candidates.All(existing => existing.Id != row.Id))
                {
                    candidates.Add(new CatalogueLocalCandidateDto(
                        row.EntityType, row.Id, row.Name, row.IsActive, row.CategoryName));
                }
            }
        }
    }

    public static (string Type, string Name) Key(string templateType, string name) =>
        (templateType, name.ToUpperInvariant());

    private static string NameKey(string name) => name.ToLowerInvariant();

    private static object[] Parameters(string[] names, NpgsqlParameter[] extraParameters) =>
    [
        new NpgsqlParameter("search_keys", names) { DataTypeName = "text[]" },
        new NpgsqlParameter("candidate_limit", NpgsqlDbType.Integer) { Value = CandidateLimit },
        .. extraParameters
    ];

    private sealed record CandidateLookupRow(
        string SearchKey,
        string TemplateType,
        string EntityType,
        Guid Id,
        string Name,
        bool IsActive,
        string? CategoryName);
}
