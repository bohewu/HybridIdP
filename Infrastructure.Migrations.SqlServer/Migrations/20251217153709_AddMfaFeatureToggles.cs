using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaFeatureToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "UserCredentials",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableEmailMfa",
                table: "SecurityPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnablePasskey",
                table: "SecurityPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableTotpMfa",
                table: "SecurityPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxPasskeysPerUser",
                table: "SecurityPolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "UserCredentials");

            migrationBuilder.DropColumn(
                name: "EnableEmailMfa",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "EnablePasskey",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "EnableTotpMfa",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "MaxPasskeysPerUser",
                table: "SecurityPolicies");
        }
    }
}
