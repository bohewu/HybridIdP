using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeNativeDirectoryCredentialAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "OperationKind",
                table: "NativeDirectoryRecoveryAttempts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NativeReset");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NativeDirectoryRecoveryAttempts_OperationProof",
                table: "NativeDirectoryRecoveryAttempts",
                sql: "(\"OperationKind\" = 'NativeReset' AND \"RecoveryProofChallengeId\" IS NOT NULL) OR (\"OperationKind\" IN ('AdminTemporaryIssue', 'RequiredChange') AND \"RecoveryProofChallengeId\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "NativeDirectoryRecoveryAttempts"
                        WHERE "OperationKind" <> 'NativeReset' OR "RecoveryProofChallengeId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot downgrade while non-native directory credential attempts exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_NativeDirectoryRecoveryAttempts_OperationProof",
                table: "NativeDirectoryRecoveryAttempts");

            migrationBuilder.DropColumn(
                name: "OperationKind",
                table: "NativeDirectoryRecoveryAttempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
