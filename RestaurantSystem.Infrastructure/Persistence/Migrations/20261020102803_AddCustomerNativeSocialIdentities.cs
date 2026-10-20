using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerNativeSocialIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_social_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    flow = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    proof_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nonce = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    security_stamp_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_social_challenges", x => x.id);
                    table.CheckConstraint("ck_customer_social_challenge_flow", "(flow = 'login' AND user_id IS NULL AND security_stamp_hash IS NULL) OR (flow = 'link' AND user_id IS NOT NULL AND security_stamp_hash IS NOT NULL)");
                    table.CheckConstraint("ck_customer_social_challenge_lifetime", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_customer_social_challenges_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_social_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    issuer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_social_identities", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_social_identities_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_social_challenges_expires_at",
                table: "customer_social_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_customer_social_challenges_user_id",
                table: "customer_social_challenges",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_social_identities_tenant_id_provider_issuer_subject",
                table: "customer_social_identities",
                columns: new[] { "tenant_id", "provider", "issuer", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_social_identities_tenant_id_user_id_provider",
                table: "customer_social_identities",
                columns: new[] { "tenant_id", "user_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_social_identities_user_id",
                table: "customer_social_identities",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE customer_social_challenges, customer_social_identities IN ACCESS EXCLUSIVE MODE NOWAIT;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM customer_social_identities)
                        OR EXISTS (SELECT 1 FROM customer_social_challenges WHERE expires_at > CURRENT_TIMESTAMP)
                    THEN
                        RAISE EXCEPTION 'Native social account bindings and live challenges must be preserved before rollback'
                            USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "customer_social_challenges");

            migrationBuilder.DropTable(
                name: "customer_social_identities");
        }
    }
}
