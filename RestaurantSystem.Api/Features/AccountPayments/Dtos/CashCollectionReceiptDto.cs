namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Physical tender evidence without the private audit identity or replay hash.</summary>
public sealed record CashCollectionReceiptDto(
    string PolicyVersion,
    string Currency,
    long ExactAmountMinor,
    long AdjustmentMinor,
    long DueAmountMinor,
    long ReceivedMinor,
    long ChangeMinor,
    DateTime CapturedAt);
