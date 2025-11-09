using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLockoutAndMinAgeToSecurityPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LockoutDurationMinutes",
                table: "SecurityPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxFailedAccessAttempts",
                table: "SecurityPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinPasswordAgeDays",
                table: "SecurityPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockoutDurationMinutes",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "MaxFailedAccessAttempts",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "MinPasswordAgeDays",
                table: "SecurityPolicies");
        }
    }
}
