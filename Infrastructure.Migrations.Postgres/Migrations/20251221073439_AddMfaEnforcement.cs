using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
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
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MfaEnforcementGracePeriodDays",
                table: "SecurityPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaRequirementNotifiedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
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
