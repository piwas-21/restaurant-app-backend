namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

internal static class OrderBillingAwardJournalConstraintSql
{
    internal const string Witness = """
        candidate_points >= 0 AND applied_points >= 0 AND suppressed_points >= 0
        AND candidate_points = applied_points + suppressed_points
        AND (
            (outcome = 'Awarded' AND candidate_points > 0 AND applied_points > 0
                AND earned_transaction_id IS NOT NULL)
            OR (outcome = 'EvaluatedZero' AND candidate_points = 0 AND applied_points = 0
                AND suppressed_points = 0 AND earned_transaction_id IS NULL)
            OR (outcome = 'FullySuppressed' AND candidate_points > 0 AND applied_points = 0
                AND suppressed_points = candidate_points AND earned_transaction_id IS NULL)
        )
        """;

    internal const string UnitSuppression = "suppressed_earned_points > 0";
}
