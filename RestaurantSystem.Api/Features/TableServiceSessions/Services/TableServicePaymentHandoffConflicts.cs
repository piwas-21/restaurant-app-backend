using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public static class TableServicePaymentHandoffConflicts
{
    public static bool IsOperation(DbUpdateException exception) =>
        IsUnique(exception, "operation_id");

    public static bool IsPending(DbUpdateException exception) =>
        IsUnique(exception, "service_session_id");

    private static bool IsUnique(DbUpdateException exception, string constraintPart) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName?.Contains(constraintPart, StringComparison.OrdinalIgnoreCase) == true;
}
