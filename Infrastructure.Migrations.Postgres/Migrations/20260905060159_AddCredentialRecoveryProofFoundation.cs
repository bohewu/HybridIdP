using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialRecoveryProofFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecoveryEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextSendAllowedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAdministrativeActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastAdministrativeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastIdentityCheckEvidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryEmails_AspNetUsers_LocalAccountId",
                        column: x => x.LocalAccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryResetApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialMigrationContinuationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IdentityCheckEvidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryResetApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryResetApprovals_AspNetUsers_LocalAccountId",
                        column: x => x.LocalAccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecoveryResetApprovals_CredentialMigrationContinuations_Cre~",
                        column: x => x.CredentialMigrationContinuationId,
                        principalTable: "CredentialMigrationContinuations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryProofChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecoveryEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialMigrationContinuationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ProofTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerificationAttempts = table.Column<int>(type: "integer", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryProofChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryProofChallenges_CredentialMigrationContinuations_Cr~",
                        column: x => x.CredentialMigrationContinuationId,
                        principalTable: "CredentialMigrationContinuations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecoveryProofChallenges_RecoveryEmails_RecoveryEmailId",
                        column: x => x.RecoveryEmailId,
                        principalTable: "RecoveryEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryEmails_LocalAccountId",
                table: "RecoveryEmails",
                column: "LocalAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryProofChallenges_CredentialMigrationContinuationId",
                table: "RecoveryProofChallenges",
                column: "CredentialMigrationContinuationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryProofChallenges_LocalAccountId_Purpose_CredentialMi~",
                table: "RecoveryProofChallenges",
                columns: new[] { "LocalAccountId", "Purpose", "CredentialMigrationContinuationId", "RevokedAtUtc", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryProofChallenges_ProofTokenHash",
                table: "RecoveryProofChallenges",
                column: "ProofTokenHash",
                unique: true,
                filter: "\"ProofTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryProofChallenges_RecoveryEmailId",
                table: "RecoveryProofChallenges",
                column: "RecoveryEmailId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryResetApprovals_CredentialMigrationContinuationId_Re~",
                table: "RecoveryResetApprovals",
                columns: new[] { "CredentialMigrationContinuationId", "RevokedAtUtc", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryResetApprovals_LocalAccountId",
                table: "RecoveryResetApprovals",
                column: "LocalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryResetApprovals_TokenHash",
                table: "RecoveryResetApprovals",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecoveryProofChallenges");

            migrationBuilder.DropTable(
                name: "RecoveryResetApprovals");

            migrationBuilder.DropTable(
                name: "RecoveryEmails");
        }
    }
}
