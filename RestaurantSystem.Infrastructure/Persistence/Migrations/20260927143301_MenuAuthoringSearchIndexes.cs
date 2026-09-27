using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MenuAuthoringSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;");
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.menu_authoring_search_normalize(value text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                STRICT
                AS $function$
                    SELECT btrim(regexp_replace(
                        regexp_replace(
                            lower(public.unaccent('public.unaccent'::regdictionary, value)),
                            '[^[:alnum:]]+', ' ', 'g'),
                        '[[:space:]]+', ' ', 'g'))
                $function$;
                """);
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.menu_authoring_search_pattern(value text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                STRICT
                AS $function$
                    SELECT '%' || public.menu_authoring_search_normalize(value) || '%'
                $function$;
                """);
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.menu_authoring_search_prefix_pattern(value text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                STRICT
                AS $function$
                    SELECT public.menu_authoring_search_normalize(value) || '%'
                $function$;
                """);
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_products_menu_authoring_name_trgm
                    ON "Products" USING GIN (public.menu_authoring_search_normalize(name) public.gin_trgm_ops)
                    WHERE is_active AND is_available AND NOT is_deleted;
                CREATE INDEX ix_global_ingredients_menu_authoring_name_trgm
                    ON global_ingredients USING GIN (public.menu_authoring_search_normalize(default_name) public.gin_trgm_ops)
                    WHERE is_active AND archived_at IS NULL AND NOT is_deleted;
                CREATE INDEX ix_global_ingredient_translations_menu_authoring_name_trgm
                    ON global_ingredient_translations USING GIN (public.menu_authoring_search_normalize(name) public.gin_trgm_ops);
                CREATE INDEX ix_option_sets_menu_authoring_name_trgm
                    ON "OptionSets" USING GIN (public.menu_authoring_search_normalize(name) public.gin_trgm_ops);
                CREATE INDEX ix_option_set_translations_menu_authoring_name_trgm
                    ON "OptionSetTranslations" USING GIN (public.menu_authoring_search_normalize(name) public.gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_option_set_translations_menu_authoring_name_trgm;
                DROP INDEX IF EXISTS ix_option_sets_menu_authoring_name_trgm;
                DROP INDEX IF EXISTS ix_global_ingredient_translations_menu_authoring_name_trgm;
                DROP INDEX IF EXISTS ix_global_ingredients_menu_authoring_name_trgm;
                DROP INDEX IF EXISTS ix_products_menu_authoring_name_trgm;
                """);
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.menu_authoring_search_prefix_pattern(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.menu_authoring_search_pattern(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.menu_authoring_search_normalize(text);");
        }
    }
}
