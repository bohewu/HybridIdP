using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAbnormalLoginDetectionToSecurityPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AbnormalLoginHistoryCount",
                table: "SecurityPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "BlockAbnormalLogin",
                table: "SecurityPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbnormalLoginHistoryCount",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "BlockAbnormalLogin",
                table: "SecurityPolicies");
        }
    }
}
