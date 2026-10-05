using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class BindClientOwnershipToApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientOwnerships_ClientId",
                table: "ClientOwnerships");

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationId",
                table: "ClientOwnerships",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientOwnerships_ApplicationId",
                table: "ClientOwnerships",
                column: "ApplicationId",
                unique: true,
                filter: "[ApplicationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClientOwnerships_ClientId",
                table: "ClientOwnerships",
                column: "ClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientOwnerships_ApplicationId",
                table: "ClientOwnerships");

            migrationBuilder.DropIndex(
                name: "IX_ClientOwnerships_ClientId",
                table: "ClientOwnerships");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "ClientOwnerships");

            migrationBuilder.CreateIndex(
                name: "IX_ClientOwnerships_ClientId",
                table: "ClientOwnerships",
                column: "ClientId",
                unique: true);
        }
    }
}
