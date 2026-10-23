using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_notification_watches",
                columns: table => new
                {
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_notification_watches", x => new { x.subject_kind, x.subject_id });
                    table.CheckConstraint("ck_customer_notification_watches_subject", "subject_kind IN ('order', 'reservation') AND subject_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "customer_notification_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "clock_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_notification_events", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_customer_notification_events_customer_notification_watches_~",
                        columns: x => new { x.subject_kind, x.subject_id },
                        principalTable: "customer_notification_watches",
                        principalColumns: new[] { "subject_kind", "subject_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_notification_events_occurred_at_event_id",
                table: "customer_notification_events",
                columns: new[] { "occurred_at", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_notification_events_subject_kind_subject_id",
                table: "customer_notification_events",
                columns: new[] { "subject_kind", "subject_id" });
            InstallCapture(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveCapture(migrationBuilder);
            migrationBuilder.DropTable(
                name: "customer_notification_events");

            migrationBuilder.DropTable(
                name: "customer_notification_watches");
        }
    }
}
