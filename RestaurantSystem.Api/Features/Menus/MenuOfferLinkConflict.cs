using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// Maps database races on the offer-family relationship to the API contract shared by every
/// writer. Unique-index violations mean the requested alternative already exists (400); a
/// serializable transaction abort or deadlock means another writer won and the caller should
/// reload (409). The latter is intentionally not described as a duplicate: an abort can happen
/// before either transaction's relationship is visible and the database has not established that
/// the requested variation is already occupied.
/// </summary>
public static class MenuOfferLinkConflict
{
    public const string DuplicateMessage =
        "This parent offer already has a menu alternative for that variation";

    public const string ConcurrentMessage =
        "The offer family changed concurrently; reload and retry";

    public static void ThrowIfExpected(Exception exception)
    {
        if (IsOfferLinkUniqueViolation(exception))
        {
            throw new BadRequestException(DuplicateMessage);
        }

        if (IsTransactionConflict(exception))
        {
            throw new ConflictException(ConcurrentMessage, exception);
        }
    }

    private static bool IsOfferLinkUniqueViolation(Exception exception)
    {
        foreach (var postgres in FindPostgresExceptions(exception))
        {
            if (postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && postgres.ConstraintName is "ux_menu_definitions_parent_offer_product_id"
                    or "ux_menu_definitions_parent_offer_variation")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransactionConflict(Exception exception) =>
        FindPostgresExceptions(exception).Any(postgres => postgres.SqlState
            is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected);

    private static IEnumerable<PostgresException> FindPostgresExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                yield return postgres;
            }
        }
    }
}
