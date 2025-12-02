using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Phase11_1_AddActiveRoleToSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveRoleId",
                table: "UserSessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRoleSwitchUtc",
                table: "UserSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_ActiveRoleId",
                table: "UserSessions",
                column: "ActiveRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserSessions_AspNetRoles_ActiveRoleId",
                table: "UserSessions",
                column: "ActiveRoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserSessions_AspNetRoles_ActiveRoleId",
                table: "UserSessions");

            migrationBuilder.DropIndex(
                name: "IX_UserSessions_ActiveRoleId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "ActiveRoleId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "LastRoleSwitchUtc",
                table: "UserSessions");
        }
    }
}
