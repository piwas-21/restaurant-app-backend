namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionRefusalCodes
{
    internal const string QuoteExpired = "quoteExpired";
    internal const string SourceVersionConflict = "sourceVersionConflict";
    internal const string AccountRevisionConflict = "accountRevisionConflict";
    internal const string QuoteChanged = "quoteChanged";
}
