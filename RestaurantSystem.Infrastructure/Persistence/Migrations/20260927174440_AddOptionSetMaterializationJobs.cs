using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionSetMaterializationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OptionSetMaterializationJobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    option_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_version = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_set_materialization_jobs", x => x.id);
                    table.CheckConstraint("ck_option_set_materialization_jobs_status", "status IN ('queued', 'processing', 'completed', 'partial', 'blocked')");
                    table.ForeignKey(
                        name: "FK_OptionSetMaterializationJobs_OptionSets_option_set_id",
                        column: x => x.option_set_id,
                        principalTable: "OptionSets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OptionSetMaterializationJobTargets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    target_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    target_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_set_materialization_job_targets", x => x.id);
                    table.CheckConstraint("ck_option_set_materialization_job_targets_status", "status IN ('pending', 'applied', 'unchanged', 'conflict', 'failed')");
                    table.ForeignKey(
                        name: "fk_option_set_materialization_job_targets_option_set_materiali~",
                        column: x => x.job_id,
                        principalTable: "OptionSetMaterializationJobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetMaterializationJobs_option_set_id_idempotency_key",
                table: "OptionSetMaterializationJobs",
                columns: new[] { "option_set_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetMaterializationJobs_status_lease_expires_at_create~",
                table: "OptionSetMaterializationJobs",
                columns: new[] { "status", "lease_expires_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetMaterializationJobTargets_job_id_sequence",
                table: "OptionSetMaterializationJobTargets",
                columns: new[] { "job_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetMaterializationJobTargets_job_id_status_sequence",
                table: "OptionSetMaterializationJobTargets",
                columns: new[] { "job_id", "status", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetMaterializationJobTargets_job_id_target_key",
                table: "OptionSetMaterializationJobTargets",
                columns: new[] { "job_id", "target_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OptionSetMaterializationJobTargets");

            migrationBuilder.DropTable(
                name: "OptionSetMaterializationJobs");
        }
    }
}
