using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAmendmentsAndGuestVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplement_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    commit_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expected_order_version = table.Column<int>(type: "integer", nullable: false),
                    expected_account_revision = table.Column<long>(type: "bigint", nullable: true),
                    committed_account_revision = table.Column<long>(type: "bigint", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    committed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    supplement_snapshot_json = table.Column<string>(type: "jsonb", nullable: true),
                    financial_resolution_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendments", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_amendments_orders_source_order_id",
                        column: x => x.source_order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendments_orders_supplement_order_id",
                        column: x => x.supplement_order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendments_table_service_sessions_service_session_id",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "table_guest_admissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_guest_admissions", x => x.id);
                    table.UniqueConstraint("AK_table_guest_admissions_id_service_session_id", x => new { x.id, x.service_session_id });
                    table.ForeignKey(
                        name: "fk_table_guest_admissions_tableservicesessions_service_session~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "table_guest_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_guest_participants", x => x.id);
                    table.UniqueConstraint("AK_table_guest_participants_id_service_session_id", x => new { x.id, x.service_session_id });
                    table.ForeignKey(
                        name: "fk_table_guest_participants_table_guest_admissions_admission_id",
                        columns: x => new { x.admission_id, x.service_session_id },
                        principalTable: "table_guest_admissions",
                        principalColumns: new[] { "id", "service_session_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_table_guest_participants_tableservicesessions_service_sessi~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "table_guest_round_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_guest_round_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_table_guest_round_operations_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_table_guest_round_operations_table_guest_participants_parti~",
                        columns: x => new { x.participant_id, x.service_session_id },
                        principalTable: "table_guest_participants",
                        principalColumns: new[] { "id", "service_session_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_table_guest_round_operations_tableservicesessions_service_s~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendments_actor_user_id_client_operation_id",
                table: "order_amendments",
                columns: new[] { "actor_user_id", "client_operation_id" },
                unique: true,
                filter: "\"client_operation_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendments_service_session_id_state",
                table: "order_amendments",
                columns: new[] { "service_session_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendments_source_order_id_created_at",
                table: "order_amendments",
                columns: new[] { "source_order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendments_supplement_order_id",
                table: "order_amendments",
                column: "supplement_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_table_guest_admissions_service_session_id",
                table: "table_guest_admissions",
                column: "service_session_id",
                unique: true,
                filter: "\"revoked_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_table_guest_participants_admission_id_service_session_id",
                table: "table_guest_participants",
                columns: new[] { "admission_id", "service_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_table_guest_participants_service_session_id_revoked_at",
                table: "table_guest_participants",
                columns: new[] { "service_session_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_table_guest_participants_token_hash",
                table: "table_guest_participants",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_table_guest_round_operations_order_id",
                table: "table_guest_round_operations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_guest_round_operations_participant_id_service_session~",
                table: "table_guest_round_operations",
                columns: new[] { "participant_id", "service_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_table_guest_round_operations_service_session_id_operation_id",
                table: "table_guest_round_operations",
                columns: new[] { "service_session_id", "operation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_amendments");

            migrationBuilder.DropTable(
                name: "table_guest_round_operations");

            migrationBuilder.DropTable(
                name: "table_guest_participants");

            migrationBuilder.DropTable(
                name: "table_guest_admissions");
        }
    }
}
