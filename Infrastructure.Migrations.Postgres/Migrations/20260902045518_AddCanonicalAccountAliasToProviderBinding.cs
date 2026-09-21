using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalAccountAliasToProviderBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedCanonicalAccountAlias",
                table: "ProviderSubjectDirectoryBindings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSubjectDirectoryBindings_NormalizedCanonicalAccount~",
                table: "ProviderSubjectDirectoryBindings",
                column: "NormalizedCanonicalAccountAlias",
                unique: true,
                filter: "\"NormalizedCanonicalAccountAlias\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProviderSubjectDirectoryBindings_NormalizedCanonicalAccount~",
                table: "ProviderSubjectDirectoryBindings");

            migrationBuilder.DropColumn(
                name: "NormalizedCanonicalAccountAlias",
                table: "ProviderSubjectDirectoryBindings");
        }
    }
}
