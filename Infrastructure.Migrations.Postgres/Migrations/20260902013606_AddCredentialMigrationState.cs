using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialMigrationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CredentialMigrationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderSubjectDirectoryBindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialMigrationStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CredentialMigrationStates_ProviderSubjectDirectoryBindings_~",
                        column: x => x.ProviderSubjectDirectoryBindingId,
                        principalTable: "ProviderSubjectDirectoryBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CredentialMigrationContinuations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialMigrationStateRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CsrfHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialMigrationContinuations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CredentialMigrationContinuations_CredentialMigrationStates_~",
                        column: x => x.CredentialMigrationStateRecordId,
                        principalTable: "CredentialMigrationStates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CredentialMigrationContinuations_CredentialMigrationStateRe~",
                table: "CredentialMigrationContinuations",
                columns: new[] { "CredentialMigrationStateRecordId", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CredentialMigrationContinuations_TokenHash",
                table: "CredentialMigrationContinuations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialMigrationStates_LocalAccountId",
                table: "CredentialMigrationStates",
                column: "LocalAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialMigrationStates_ProviderSubjectDirectoryBindingId",
                table: "CredentialMigrationStates",
                column: "ProviderSubjectDirectoryBindingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CredentialMigrationContinuations");

            migrationBuilder.DropTable(
                name: "CredentialMigrationStates");
        }
    }
}
