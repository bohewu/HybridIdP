using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserOwnershipColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientOwnerships_AspNetUsers_CreatedByUserId",
                table: "ClientOwnerships");

            migrationBuilder.DropForeignKey(
                name: "FK_ScopeOwnerships_AspNetUsers_CreatedByUserId",
                table: "ScopeOwnerships");

            migrationBuilder.DropIndex(
                name: "IX_ScopeOwnerships_CreatedByUserId",
                table: "ScopeOwnerships");

            migrationBuilder.DropIndex(
                name: "IX_ClientOwnerships_CreatedByUserId",
                table: "ClientOwnerships");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ScopeOwnerships");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ClientOwnerships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "ScopeOwnerships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "ClientOwnerships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ScopeOwnerships_CreatedByUserId",
                table: "ScopeOwnerships",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientOwnerships_CreatedByUserId",
                table: "ClientOwnerships",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientOwnerships_AspNetUsers_CreatedByUserId",
                table: "ClientOwnerships",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScopeOwnerships_AspNetUsers_CreatedByUserId",
                table: "ScopeOwnerships",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
