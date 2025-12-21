using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnforceMandatoryMfaEnrollment",
                table: "SecurityPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MfaEnforcementGracePeriodDays",
                table: "SecurityPolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaRequirementNotifiedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforceMandatoryMfaEnrollment",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "MfaEnforcementGracePeriodDays",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "MfaRequirementNotifiedAt",
                table: "AspNetUsers");
        }
    }
}
