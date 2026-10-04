namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record ZReportAccountCashMovementDto(
    DateTime SnapshotAtUtc,
    string Coverage,
    bool CoversWholeRestaurantTill,
    IReadOnlyList<ZReportAccountCashCurrencyDto> ByCurrency,
    IReadOnlyList<ZReportAccountCashCurrencyDto> UnresolvedByCurrency);

public sealed record ZReportAccountCashCurrencyDto(
    string Currency,
    int CollectionCount,
    long CollectedExactMinor,
    long CashReceivedMinor,
    long ChangeReturnedMinor,
    long CashDueMinor,
    int ReturnCount,
    long ExactRefundedMinor,
    long PhysicalCashReturnedMinor,
    long RefundAdjustmentMinor,
    int LegacyCaptureWithoutReceiptCount,
    long LegacyCaptureExactMinor,
    int LegacyReturnWithoutPhysicalEvidenceCount,
    long LegacyExactRefundMinor,
    int UnresolvedReturnCount,
    long UnresolvedExactRefundMinor,
    long UnconfirmedPhysicalCashMinor);
