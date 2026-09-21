using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Hidp11PendingSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DirectorySettlementPreparations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedAttemptVersion = table.Column<long>(type: "bigint", nullable: false),
                    OperationKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpectedAttemptStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectoryObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationVersion = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecoveryEmailVersion = table.Column<long>(type: "bigint", nullable: false),
                    AccountSecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OperatorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorSecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AuthorizationExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MfaAuthorized = table.Column<bool>(type: "boolean", nullable: false),
                    UsersUpdateAuthorized = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalWritersDrained = table.Column<bool>(type: "boolean", nullable: false),
                    Disposition = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    EvidenceCategory = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContinuationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnershipChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContextHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CsrfHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectorySettlementPreparations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectorySettlementPreparations_NativeDirectoryRecoveryAtte~",
                        column: x => x.AttemptId,
                        principalTable: "NativeDirectoryRecoveryAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DirectorySettlementPreparations_RecoveryProofChallenges_Own~",
                        column: x => x.OwnershipChallengeId,
                        principalTable: "RecoveryProofChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectorySettlementPreparations_AttemptId",
                table: "DirectorySettlementPreparations",
                column: "AttemptId",
                unique: true,
                filter: "\"Status\" IN ('Prepared', 'OwnershipPending', 'Verifying')");

            migrationBuilder.CreateIndex(
                name: "IX_DirectorySettlementPreparations_ContinuationHash",
                table: "DirectorySettlementPreparations",
                column: "ContinuationHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectorySettlementPreparations_OwnershipChallengeId",
                table: "DirectorySettlementPreparations",
                column: "OwnershipChallengeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectorySettlementPreparations");
        }
    }
}
