namespace RestaurantSystem.Domain.Common.Enums;

public enum AccountPaymentState
{
    Quoted = 0,
    Reserved = 1,
    Starting = 2,
    Processing = 3,
    Captured = 4,
    CancelRequested = 5,
    Released = 6,
    Failed = 7,
    ReconciliationRequired = 8
}

public enum AccountPaymentMode { Items = 1, Amount = 2, Equal = 3 }
public enum AccountPaymentActorKind { Staff = 1, GuestParticipant = 2 }

public static class AccountPaymentStateRules
{
    /// <summary>States known to have no captured or unresolved contribution scope; unknown values stay protected.</summary>
    public static AccountPaymentState[] KnownUnprotectedStates =>
        [AccountPaymentState.Quoted, AccountPaymentState.Released, AccountPaymentState.Failed];

    public static bool HoldsReservation(this AccountPaymentState state) => state switch
    {
        AccountPaymentState.Quoted or AccountPaymentState.Captured
            or AccountPaymentState.Released or AccountPaymentState.Failed => false,
        _ => true
    };

    public static bool BlocksClose(this AccountPaymentState state) => state.HoldsReservation();

    /// <summary>Expiry is not proof of non-payment after a provider request may have escaped.</summary>
    public static bool CanReleaseLocally(this AccountPaymentState state) =>
        state is AccountPaymentState.Quoted or AccountPaymentState.Reserved;
}
