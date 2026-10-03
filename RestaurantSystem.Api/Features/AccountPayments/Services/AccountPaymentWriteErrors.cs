using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountPaymentWriteErrors
{
    internal static bool IsOperationKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var name
        } && name?.Contains("operation_id", StringComparison.OrdinalIgnoreCase) == true;

    internal static ConflictException OperationKeyConflict(DbUpdateException exception) =>
        new("The operation id has already been used.", exception);
}
