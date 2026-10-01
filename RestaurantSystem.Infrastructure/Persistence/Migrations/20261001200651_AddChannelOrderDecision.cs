using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelOrderDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelOrderDecisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    expected_version = table.Column<int>(type: "integer", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    available_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_canonical_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_report_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_order_decisions", x => x.id);
                    table.CheckConstraint("CK_ChannelOrderDecisions_Action", "\"action\" IN ('accept', 'deny')");
                    table.CheckConstraint("CK_ChannelOrderDecisions_Hash", "\"payload_hash\" ~ '^[a-f0-9]{64}$'");
                    table.CheckConstraint("CK_ChannelOrderDecisions_State", "\"state\" IN ('Pending', 'Leased', 'Unknown', 'Succeeded', 'Failed')");
                    table.ForeignKey(
                        name: "fk_channel_order_decisions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_order_decisions_order_id",
                table: "ChannelOrderDecisions",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOrderDecisions_operation_id",
                table: "ChannelOrderDecisions",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOrderDecisions_state_available_at",
                table: "ChannelOrderDecisions",
                columns: new[] { "state", "available_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelOrderDecisions");
        }
    }
}
