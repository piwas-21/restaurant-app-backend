namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountStripeContext(string ConnectedAccountId, bool LiveMode);
