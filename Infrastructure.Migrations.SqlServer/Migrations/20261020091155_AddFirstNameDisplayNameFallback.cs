using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstNameDisplayNameFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FirstNameDisplayNameFallback",
                table: "NativeFieldSourcePolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FirstNameFallbackProviderNamespace",
                table: "NativeFieldSourcePolicies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstNameDisplayNameFallback",
                table: "NativeFieldSourcePolicies");

            migrationBuilder.DropColumn(
                name: "FirstNameFallbackProviderNamespace",
                table: "NativeFieldSourcePolicies");
        }
    }
}
