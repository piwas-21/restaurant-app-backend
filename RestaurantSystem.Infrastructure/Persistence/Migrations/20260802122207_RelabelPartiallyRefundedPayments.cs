using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Relabels partially-refunded payment rows from <c>PartiallyPaid</c> to
    /// <c>PartiallyRefunded</c> (backend issue #286).
    ///
    /// <c>RefundPaymentCommand</c> stored <c>PartiallyPaid</c> on a partial
    /// refund — an order-level word meaning "balance outstanding" — while the
    /// Z-report looked for <c>PartiallyRefunded</c>, which nothing wrote. The
    /// writer is now corrected, so any row already carrying the old label has
    /// to move with it or it stays invisible to the money report.
    ///
    /// <b>Scope is deliberately one column.</b> <c>orders.payment_status</c>
    /// uses the same enum and <c>PartiallyPaid</c> is CORRECT there (some
    /// tenders received, balance outstanding) — this must not touch it.
    ///
    /// <b>Pre-measured against production</b> (2026-08-02): all 18
    /// <c>order_payments</c> rows are <c>Pending</c> and none is refunded, so
    /// this updates zero rows there. It is written for the other tenant
    /// databases and for anyone replaying history, not because prod needs it.
    /// </summary>
    public partial class RelabelPartiallyRefundedPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE order_payments SET status = 'PartiallyRefunded' WHERE status = 'PartiallyPaid';");
        }

        /// <summary>
        /// Exact inverse. Before this migration no code path could write
        /// <c>PartiallyRefunded</c> to a payment, so every such row is one this
        /// migration or the corrected writer produced, and <c>PartiallyPaid</c>
        /// is precisely what the old writer would have stored for it.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE order_payments SET status = 'PartiallyPaid' WHERE status = 'PartiallyRefunded';");
        }
    }
}
