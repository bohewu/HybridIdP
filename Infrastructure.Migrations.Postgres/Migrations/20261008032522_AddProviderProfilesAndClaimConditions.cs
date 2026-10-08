using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderProfilesAndClaimConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderProfilesJson",
                table: "Persons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConditionJson",
                table: "ClaimDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderProfileSource",
                table: "ClaimDefinitions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderProfilesJson",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "ConditionJson",
                table: "ClaimDefinitions");

            migrationBuilder.DropColumn(
                name: "ProviderProfileSource",
                table: "ClaimDefinitions");
        }
    }
}
