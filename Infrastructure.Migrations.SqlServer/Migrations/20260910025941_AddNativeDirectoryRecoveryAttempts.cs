using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeDirectoryRecoveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NativeDirectoryRecoveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryProofChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DirectoryObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeDirectoryRecoveryAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NativeDirectoryRecoveryAttempts_RecoveryProofChallenges_RecoveryProofChallengeId",
                        column: x => x.RecoveryProofChallengeId,
                        principalTable: "RecoveryProofChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NativeDirectoryRecoveryAttempts_LocalAccountId",
                table: "NativeDirectoryRecoveryAttempts",
                column: "LocalAccountId",
                unique: true,
                filter: "[Status] IN (N'Reserved', N'ReconciliationRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_NativeDirectoryRecoveryAttempts_RecoveryProofChallengeId",
                table: "NativeDirectoryRecoveryAttempts",
                column: "RecoveryProofChallengeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NativeDirectoryRecoveryAttempts");
        }
    }
}
