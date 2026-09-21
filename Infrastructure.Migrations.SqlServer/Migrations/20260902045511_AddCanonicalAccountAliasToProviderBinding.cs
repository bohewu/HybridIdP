using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
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
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSubjectDirectoryBindings_NormalizedCanonicalAccountAlias",
                table: "ProviderSubjectDirectoryBindings",
                column: "NormalizedCanonicalAccountAlias",
                unique: true,
                filter: "[NormalizedCanonicalAccountAlias] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProviderSubjectDirectoryBindings_NormalizedCanonicalAccountAlias",
                table: "ProviderSubjectDirectoryBindings");

            migrationBuilder.DropColumn(
                name: "NormalizedCanonicalAccountAlias",
                table: "ProviderSubjectDirectoryBindings");
        }
    }
}
