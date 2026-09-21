using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeRecoveryAssistance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NativeContextHash",
                table: "RecoveryProofChallenges",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NativeCsrfHash",
                table: "RecoveryProofChallenges",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NativeDirectoryAuthority",
                table: "RecoveryProofChallenges",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NativeDirectoryObjectId",
                table: "RecoveryProofChallenges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NativeRecoveryChallengeId",
                table: "RecoveryProofChallenges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NativeRecoveryEmailVersion",
                table: "RecoveryProofChallenges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NativeSecurityStamp",
                table: "RecoveryProofChallenges",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NativeRecoveryResetApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryProofChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryEmailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryEmailVersion = table.Column<long>(type: "bigint", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CsrfHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DirectoryAuthority = table.Column<bool>(type: "bit", nullable: false),
                    DirectoryObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IdentityCheckEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeRecoveryResetApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NativeRecoveryResetApprovals_RecoveryProofChallenges_RecoveryProofChallengeId",
                        column: x => x.RecoveryProofChallengeId,
                        principalTable: "RecoveryProofChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NativeRecoveryResetApprovals_RecoveryProofChallengeId",
                table: "NativeRecoveryResetApprovals",
                column: "RecoveryProofChallengeId",
                unique: true,
                filter: "[RevokedAtUtc] IS NULL AND [ConsumedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NativeRecoveryResetApprovals_RecoveryProofChallengeId_RevokedAtUtc_ConsumedAtUtc",
                table: "NativeRecoveryResetApprovals",
                columns: new[] { "RecoveryProofChallengeId", "RevokedAtUtc", "ConsumedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NativeRecoveryResetApprovals");

            migrationBuilder.DropColumn(
                name: "NativeContextHash",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "NativeCsrfHash",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "NativeDirectoryAuthority",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "NativeDirectoryObjectId",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "NativeRecoveryChallengeId",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "NativeRecoveryEmailVersion",
                table: "RecoveryProofChallenges");

            migrationBuilder.DropColumn(
                name: "NativeSecurityStamp",
                table: "RecoveryProofChallenges");
        }
    }
}
