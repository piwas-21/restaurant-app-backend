using System.Linq.Expressions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueSourceRevisionPredicate
{
    public static Expression<Func<CatalogueTemplateAdoption, bool>> ForAdoptions(
        IEnumerable<(string TemplateId, int Revision)> revisions) =>
        Build<CatalogueTemplateAdoption>(
            revisions,
            nameof(CatalogueTemplateAdoption.SourceTemplateId),
            nameof(CatalogueTemplateAdoption.SourceRevision));

    public static Expression<Func<CatalogueMatchDecision, bool>> ForMatchDecisions(
        IEnumerable<(string TemplateId, int Revision)> revisions) =>
        Build<CatalogueMatchDecision>(
            revisions,
            nameof(CatalogueMatchDecision.SourceTemplateId),
            nameof(CatalogueMatchDecision.SourceRevision));

    private static Expression<Func<TEntity, bool>> Build<TEntity>(
        IEnumerable<(string TemplateId, int Revision)> requested,
        string templateIdProperty,
        string revisionProperty)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "source");
        var templateId = Expression.Property(parameter, templateIdProperty);
        var revision = Expression.Property(parameter, revisionProperty);
        var pairPredicates = requested.Distinct().Select(pair => Expression.AndAlso(
            Expression.Equal(templateId, Expression.Constant(pair.TemplateId)),
            Expression.Equal(revision, Expression.Constant(pair.Revision))));
        var predicates = pairPredicates.ToArray();
        return Expression.Lambda<Func<TEntity, bool>>(OrBalanced(predicates, 0, predicates.Length), parameter);
    }

    private static Expression OrBalanced(Expression[] predicates, int start, int count)
    {
        if (count == 0) return Expression.Constant(false);
        if (count == 1) return predicates[start];

        var leftCount = count / 2;
        return Expression.OrElse(
            OrBalanced(predicates, start, leftCount),
            OrBalanced(predicates, start + leftCount, count - leftCount));
    }
}
