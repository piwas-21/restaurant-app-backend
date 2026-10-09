using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAmendmentLoyaltyCompensation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_fidelity_points_transactions_asp_net_users_user_id",
                table: "fidelity_points_transactions");

            migrationBuilder.DropIndex(
                name: "IX_order_amendment_resolution_operations_source_order_id",
                table: "order_amendment_resolution_operations");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "fidelity_points_transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "original_transaction_id",
                table: "fidelity_points_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_order_billing_snapshots_order_id_id",
                table: "order_billing_snapshots",
                columns: new[] { "order_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_order_billing_snapshot_units_order_id_id",
                table: "order_billing_snapshot_units",
                columns: new[] { "order_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_order_billing_snapshot_owner_links_order_id_id",
                table: "order_billing_snapshot_owner_links",
                columns: new[] { "order_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_order_amendments_source_order_id_id",
                table: "order_amendments",
                columns: new[] { "source_order_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_order_amendment_resolution_operations_source_order_id_id",
                table: "order_amendment_resolution_operations",
                columns: new[] { "source_order_id", "id" });

            migrationBuilder.CreateTable(
                name: "order_amendment_loyalty_owner_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_loyalty_owner_holds", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_owner_holds_order_amendment_resolut~",
                        columns: x => new { x.source_order_id, x.operation_id },
                        principalTable: "order_amendment_resolution_operations",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_owner_holds_order_billing_snapshot_~",
                        columns: x => new { x.source_order_id, x.owner_link_id },
                        principalTable: "order_billing_snapshot_owner_links",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_billing_award_witnesses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    candidate_points = table.Column<int>(type: "integer", nullable: false),
                    applied_points = table.Column<int>(type: "integer", nullable: false),
                    suppressed_points = table.Column<int>(type: "integer", nullable: false),
                    earned_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_award_witnesses", x => x.id);
                    table.UniqueConstraint("AK_order_billing_award_witnesses_order_id_id", x => new { x.order_id, x.id });
                    table.CheckConstraint("ck_order_billing_award_witness_values", "candidate_points >= 0 AND applied_points >= 0 AND suppressed_points >= 0\nAND candidate_points = applied_points + suppressed_points\nAND (\n    (outcome = 'Awarded' AND candidate_points > 0 AND applied_points > 0\n        AND earned_transaction_id IS NOT NULL)\n    OR (outcome = 'EvaluatedZero' AND candidate_points = 0 AND applied_points = 0\n        AND suppressed_points = 0 AND earned_transaction_id IS NULL)\n    OR (outcome = 'FullySuppressed' AND candidate_points > 0 AND applied_points = 0\n        AND suppressed_points = candidate_points AND earned_transaction_id IS NULL)\n)");
                    table.ForeignKey(
                        name: "FK_order_billing_award_witnesses_order_billing_snapshot_owner_~",
                        columns: x => new { x.order_id, x.owner_link_id },
                        principalTable: "order_billing_snapshot_owner_links",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_award_witnesses_order_billing_snapshots_order~",
                        column: x => x.order_id,
                        principalTable: "order_billing_snapshots",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_billing_unit_award_suppressions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suppressed_earned_points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_unit_award_suppressions", x => x.id);
                    table.CheckConstraint("ck_order_billing_unit_award_suppression_points", "suppressed_earned_points > 0");
                    table.ForeignKey(
                        name: "FK_order_billing_unit_award_suppressions_order_amendments_orde~",
                        columns: x => new { x.order_id, x.amendment_id },
                        principalTable: "order_amendments",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_unit_award_suppressions_order_billing_snapsho~",
                        column: x => x.order_id,
                        principalTable: "order_billing_snapshots",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_unit_award_suppressions_order_billing_snapsh~1",
                        columns: x => new { x.order_id, x.snapshot_unit_id },
                        principalTable: "order_billing_snapshot_units",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_amendment_loyalty_compensations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    award_witness_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_transaction_points = table.Column<int>(type: "integer", nullable: false),
                    required_points = table.Column<int>(type: "integer", nullable: false),
                    plan_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_loyalty_compensations", x => x.id);
                    table.UniqueConstraint("AK_order_amendment_loyalty_compensations_source_order_id_id", x => new { x.source_order_id, x.id });
                    table.CheckConstraint("ck_order_amendment_loyalty_compensation_points", "source_order_id <> '00000000-0000-0000-0000-000000000000'::uuid AND snapshot_id <> '00000000-0000-0000-0000-000000000000'::uuid AND owner_link_id <> '00000000-0000-0000-0000-000000000000'::uuid AND original_transaction_id <> '00000000-0000-0000-0000-000000000000'::uuid AND operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND original_transaction_points <> 0 AND required_points > 0 AND ((kind = 'EarnedClawback' AND original_transaction_points > 0) OR (kind = 'RedemptionRestoration' AND original_transaction_points < 0))");
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensations_order_amendment_resol~",
                        columns: x => new { x.source_order_id, x.operation_id },
                        principalTable: "order_amendment_resolution_operations",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensations_order_amendments_sour~",
                        columns: x => new { x.source_order_id, x.amendment_id },
                        principalTable: "order_amendments",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensations_order_billing_award_w~",
                        columns: x => new { x.source_order_id, x.award_witness_id },
                        principalTable: "order_billing_award_witnesses",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensations_order_billing_snapsho~",
                        columns: x => new { x.source_order_id, x.owner_link_id },
                        principalTable: "order_billing_snapshot_owner_links",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensations_order_billing_snapsh~1",
                        columns: x => new { x.source_order_id, x.snapshot_id },
                        principalTable: "order_billing_snapshots",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_billing_award_unit_coverages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    award_witness_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eligible_earned_points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_award_unit_coverages", x => x.id);
                    table.CheckConstraint("ck_order_billing_award_unit_coverage_points", "eligible_earned_points > 0");
                    table.ForeignKey(
                        name: "FK_order_billing_award_unit_coverages_order_billing_award_witn~",
                        columns: x => new { x.order_id, x.award_witness_id },
                        principalTable: "order_billing_award_witnesses",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_award_unit_coverages_order_billing_snapshot_u~",
                        columns: x => new { x.order_id, x.snapshot_unit_id },
                        principalTable: "order_billing_snapshot_units",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_amendment_loyalty_compensation_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    compensation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points_delta = table.Column<int>(type: "integer", nullable: false),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_loyalty_compensation_postings", x => x.id);
                    table.CheckConstraint("ck_order_amendment_loyalty_compensation_posting_delta", "movement_transaction_id <> '00000000-0000-0000-0000-000000000000'::uuid AND points_delta <> 0");
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensation_postings_order_amendme~",
                        column: x => x.compensation_id,
                        principalTable: "order_amendment_loyalty_compensations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_amendment_loyalty_compensation_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    compensation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_loyalty_compensation_units", x => x.id);
                    table.CheckConstraint("ck_order_amendment_loyalty_compensation_unit_points", "points > 0");
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensation_units_order_amendment_~",
                        columns: x => new { x.source_order_id, x.compensation_id },
                        principalTable: "order_amendment_loyalty_compensations",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_compensation_units_order_billing_sn~",
                        columns: x => new { x.source_order_id, x.snapshot_unit_id },
                        principalTable: "order_billing_snapshot_units",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_amendment_loyalty_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compensation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_loyalty_reservations", x => x.id);
                    table.CheckConstraint("ck_order_amendment_loyalty_reservation_state", "state IN ('HeldShortfall', 'Reserved', 'Consumed', 'Released')");
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_reservations_order_amendment_loyalt~",
                        columns: x => new { x.source_order_id, x.compensation_id },
                        principalTable: "order_amendment_loyalty_compensations",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_reservations_order_amendment_resolu~",
                        columns: x => new { x.source_order_id, x.operation_id },
                        principalTable: "order_amendment_resolution_operations",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_loyalty_reservations_order_billing_snapshot~",
                        columns: x => new { x.source_order_id, x.owner_link_id },
                        principalTable: "order_billing_snapshot_owner_links",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fidelity_points_transactions_original_transaction_id",
                table: "fidelity_points_transactions",
                column: "original_transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensation_postings_compensation_~",
                table: "order_amendment_loyalty_compensation_postings",
                column: "compensation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensation_postings_movement_tran~",
                table: "order_amendment_loyalty_compensation_postings",
                column: "movement_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensation_units_compensation_id_~",
                table: "order_amendment_loyalty_compensation_units",
                columns: new[] { "compensation_id", "snapshot_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensation_units_snapshot_unit_id~",
                table: "order_amendment_loyalty_compensation_units",
                columns: new[] { "snapshot_unit_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensation_units_source_order_id_~",
                table: "order_amendment_loyalty_compensation_units",
                columns: new[] { "source_order_id", "compensation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensation_units_source_order_id~1",
                table: "order_amendment_loyalty_compensation_units",
                columns: new[] { "source_order_id", "snapshot_unit_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensations_amendment_id_original~",
                table: "order_amendment_loyalty_compensations",
                columns: new[] { "amendment_id", "original_transaction_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensations_source_order_id_amend~",
                table: "order_amendment_loyalty_compensations",
                columns: new[] { "source_order_id", "amendment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensations_source_order_id_award~",
                table: "order_amendment_loyalty_compensations",
                columns: new[] { "source_order_id", "award_witness_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensations_source_order_id_opera~",
                table: "order_amendment_loyalty_compensations",
                columns: new[] { "source_order_id", "operation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensations_source_order_id_origi~",
                table: "order_amendment_loyalty_compensations",
                columns: new[] { "source_order_id", "original_transaction_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensations_source_order_id_owner~",
                table: "order_amendment_loyalty_compensations",
                columns: new[] { "source_order_id", "owner_link_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_compensations_source_order_id_snaps~",
                table: "order_amendment_loyalty_compensations",
                columns: new[] { "source_order_id", "snapshot_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_owner_holds_owner_link_id_released_~",
                table: "order_amendment_loyalty_owner_holds",
                columns: new[] { "owner_link_id", "released_at" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_owner_holds_source_order_id_operati~",
                table: "order_amendment_loyalty_owner_holds",
                columns: new[] { "source_order_id", "operation_id", "owner_link_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_owner_holds_source_order_id_owner_l~",
                table: "order_amendment_loyalty_owner_holds",
                columns: new[] { "source_order_id", "owner_link_id" },
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_reservations_compensation_id",
                table: "order_amendment_loyalty_reservations",
                column: "compensation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_reservations_operation_id",
                table: "order_amendment_loyalty_reservations",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_reservations_owner_link_id_state",
                table: "order_amendment_loyalty_reservations",
                columns: new[] { "owner_link_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_reservations_source_order_id_compen~",
                table: "order_amendment_loyalty_reservations",
                columns: new[] { "source_order_id", "compensation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_reservations_source_order_id_operat~",
                table: "order_amendment_loyalty_reservations",
                columns: new[] { "source_order_id", "operation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_loyalty_reservations_source_order_id_owner_~",
                table: "order_amendment_loyalty_reservations",
                columns: new[] { "source_order_id", "owner_link_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_award_unit_coverages_award_witness_id_snapsho~",
                table: "order_billing_award_unit_coverages",
                columns: new[] { "award_witness_id", "snapshot_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_award_unit_coverages_order_id_award_witness_id",
                table: "order_billing_award_unit_coverages",
                columns: new[] { "order_id", "award_witness_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_award_unit_coverages_order_id_snapshot_unit_id",
                table: "order_billing_award_unit_coverages",
                columns: new[] { "order_id", "snapshot_unit_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_award_unit_coverages_snapshot_unit_id",
                table: "order_billing_award_unit_coverages",
                column: "snapshot_unit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_award_witnesses_earned_transaction_id",
                table: "order_billing_award_witnesses",
                column: "earned_transaction_id",
                unique: true,
                filter: "\"earned_transaction_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_award_witnesses_order_id",
                table: "order_billing_award_witnesses",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_award_witnesses_order_id_owner_link_id",
                table: "order_billing_award_witnesses",
                columns: new[] { "order_id", "owner_link_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_unit_award_suppressions_amendment_id",
                table: "order_billing_unit_award_suppressions",
                column: "amendment_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_unit_award_suppressions_order_id_amendment_id",
                table: "order_billing_unit_award_suppressions",
                columns: new[] { "order_id", "amendment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_unit_award_suppressions_order_id_snapshot_uni~",
                table: "order_billing_unit_award_suppressions",
                columns: new[] { "order_id", "snapshot_unit_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_unit_award_suppressions_snapshot_unit_id",
                table: "order_billing_unit_award_suppressions",
                column: "snapshot_unit_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_fidelity_points_transactions_asp_net_users_user_id",
                table: "fidelity_points_transactions",
                column: "user_id",
                principalTable: "Users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            ProtectOrderBillingAwardJournal(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM order_billing_award_witnesses LIMIT 1)
                        OR EXISTS (SELECT 1 FROM order_billing_unit_award_suppressions LIMIT 1)
                        OR EXISTS (SELECT 1 FROM order_billing_award_unit_coverages LIMIT 1)
                        OR EXISTS (SELECT 1 FROM order_amendment_loyalty_compensations LIMIT 1)
                        OR EXISTS (SELECT 1 FROM order_amendment_loyalty_compensation_units LIMIT 1)
                        OR EXISTS (SELECT 1 FROM order_amendment_loyalty_compensation_postings LIMIT 1)
                        OR EXISTS (SELECT 1 FROM order_amendment_loyalty_reservations LIMIT 1)
                        OR EXISTS (SELECT 1 FROM order_amendment_loyalty_owner_holds LIMIT 1)
                        OR EXISTS (SELECT 1 FROM fidelity_points_transactions
                            WHERE user_id IS NULL OR original_transaction_id IS NOT NULL LIMIT 1)
                    THEN
                        RAISE EXCEPTION USING ERRCODE = '23514',
                            MESSAGE = 'Native order loyalty history must be retained';
                    END IF;
                END $$;
                """);

            RemoveOrderBillingAwardJournalProtection(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_fidelity_points_transactions_asp_net_users_user_id",
                table: "fidelity_points_transactions");

            migrationBuilder.DropTable(
                name: "order_amendment_loyalty_compensation_postings");

            migrationBuilder.DropTable(
                name: "order_amendment_loyalty_compensation_units");

            migrationBuilder.DropTable(
                name: "order_amendment_loyalty_owner_holds");

            migrationBuilder.DropTable(
                name: "order_amendment_loyalty_reservations");

            migrationBuilder.DropTable(
                name: "order_billing_award_unit_coverages");

            migrationBuilder.DropTable(
                name: "order_billing_unit_award_suppressions");

            migrationBuilder.DropTable(
                name: "order_amendment_loyalty_compensations");

            migrationBuilder.DropTable(
                name: "order_billing_award_witnesses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_order_billing_snapshots_order_id_id",
                table: "order_billing_snapshots");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_order_billing_snapshot_units_order_id_id",
                table: "order_billing_snapshot_units");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_order_billing_snapshot_owner_links_order_id_id",
                table: "order_billing_snapshot_owner_links");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_order_amendments_source_order_id_id",
                table: "order_amendments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_order_amendment_resolution_operations_source_order_id_id",
                table: "order_amendment_resolution_operations");

            migrationBuilder.DropIndex(
                name: "ix_fidelity_points_transactions_original_transaction_id",
                table: "fidelity_points_transactions");

            migrationBuilder.DropColumn(
                name: "original_transaction_id",
                table: "fidelity_points_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "fidelity_points_transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_operations_source_order_id",
                table: "order_amendment_resolution_operations",
                column: "source_order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_fidelity_points_transactions_asp_net_users_user_id",
                table: "fidelity_points_transactions",
                column: "user_id",
                principalTable: "Users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
