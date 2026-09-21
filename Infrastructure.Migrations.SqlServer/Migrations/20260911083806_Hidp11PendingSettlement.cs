using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedAttemptVersion = table.Column<long>(type: "bigint", nullable: false),
                    OperationKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExpectedAttemptStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DirectoryObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MigrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MigrationVersion = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryEmailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryEmailVersion = table.Column<long>(type: "bigint", nullable: false),
                    AccountSecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OperatorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperatorSecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AuthorizationExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MfaAuthorized = table.Column<bool>(type: "bit", nullable: false),
                    UsersUpdateAuthorized = table.Column<bool>(type: "bit", nullable: false),
                    OriginalWritersDrained = table.Column<bool>(type: "bit", nullable: false),
                    Disposition = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    EvidenceCategory = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContinuationHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnershipChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContextHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CsrfHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectorySettlementPreparations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectorySettlementPreparations_NativeDirectoryRecoveryAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "NativeDirectoryRecoveryAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DirectorySettlementPreparations_RecoveryProofChallenges_OwnershipChallengeId",
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
                filter: "[Status] IN (N'Prepared', N'OwnershipPending', N'Verifying')");

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
