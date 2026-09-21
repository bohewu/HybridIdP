using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoverySourceBootstrapRevocationAndForgotPasswordMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ForgotPasswordMode",
                table: "SecurityPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecoverySourceBootstrapRevokedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForgotPasswordMode",
                table: "SecurityPolicies");

            migrationBuilder.DropColumn(
                name: "RecoverySourceBootstrapRevokedAtUtc",
                table: "AspNetUsers");
        }
    }
}
