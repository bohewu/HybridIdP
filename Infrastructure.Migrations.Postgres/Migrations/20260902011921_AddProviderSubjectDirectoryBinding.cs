using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderSubjectDirectoryBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderSubjectDirectoryBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderNamespace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StableSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DirectoryObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderSubjectDirectoryBindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSubjectDirectoryBindings_DirectoryObjectId",
                table: "ProviderSubjectDirectoryBindings",
                column: "DirectoryObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSubjectDirectoryBindings_LocalAccountId",
                table: "ProviderSubjectDirectoryBindings",
                column: "LocalAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSubjectDirectoryBindings_ProviderNamespace_StableSu~",
                table: "ProviderSubjectDirectoryBindings",
                columns: new[] { "ProviderNamespace", "StableSubject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderSubjectDirectoryBindings");
        }
    }
}
