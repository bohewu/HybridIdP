using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeNativeDirectoryCredentialAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NativeDirectoryRecoveryAttempts_RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "OperationKind",
                table: "NativeDirectoryRecoveryAttempts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NativeReset");

            migrationBuilder.CreateIndex(
                name: "IX_NativeDirectoryRecoveryAttempts_RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts",
                column: "RecoveryProofChallengeId",
                unique: true,
                filter: "[RecoveryProofChallengeId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NativeDirectoryRecoveryAttempts_OperationProof",
                table: "NativeDirectoryRecoveryAttempts",
                sql: "([OperationKind] = N'NativeReset' AND [RecoveryProofChallengeId] IS NOT NULL) OR ([OperationKind] IN (N'AdminTemporaryIssue', N'RequiredChange') AND [RecoveryProofChallengeId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [NativeDirectoryRecoveryAttempts]
                    WHERE [OperationKind] <> N'NativeReset' OR [RecoveryProofChallengeId] IS NULL)
                    THROW 51000, 'Cannot downgrade while non-native directory credential attempts exist.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_NativeDirectoryRecoveryAttempts_RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NativeDirectoryRecoveryAttempts_OperationProof",
                table: "NativeDirectoryRecoveryAttempts");

            migrationBuilder.DropColumn(
                name: "OperationKind",
                table: "NativeDirectoryRecoveryAttempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NativeDirectoryRecoveryAttempts_RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts",
                column: "RecoveryProofChallengeId",
                unique: true);
        }
    }
}
