using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Hidp13LegacyPasswordSyncAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyPasswordSyncAttempts",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAttemptVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceCompletion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceCreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderSubjectDirectoryBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountIdentity = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SanitizedOutcome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyPasswordSyncAttempts", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_LegacyPasswordSyncAttempts_ProviderSubjectDirectoryBindings_ProviderSubjectDirectoryBindingId",
                        column: x => x.ProviderSubjectDirectoryBindingId,
                        principalTable: "ProviderSubjectDirectoryBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPasswordSyncAttempts_LocalAccountId_AccountIdentity",
                table: "LegacyPasswordSyncAttempts",
                columns: new[] { "LocalAccountId", "AccountIdentity" },
                unique: true,
                filter: "[Status] IN (N'Claimed', N'Unknown')");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPasswordSyncAttempts_LocalAccountId_AccountIdentity_Generation",
                table: "LegacyPasswordSyncAttempts",
                columns: new[] { "LocalAccountId", "AccountIdentity", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPasswordSyncAttempts_ProviderSubjectDirectoryBindingId",
                table: "LegacyPasswordSyncAttempts",
                column: "ProviderSubjectDirectoryBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPasswordSyncAttempts_SourceKind_SourceAttemptId",
                table: "LegacyPasswordSyncAttempts",
                columns: new[] { "SourceKind", "SourceAttemptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyPasswordSyncAttempts");
        }
    }
}
