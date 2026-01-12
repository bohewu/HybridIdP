using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddApiResourceOwnerPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerPersonId",
                table: "ApiResources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiResources_OwnerPersonId",
                table: "ApiResources",
                column: "OwnerPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiResources_Persons_OwnerPersonId",
                table: "ApiResources",
                column: "OwnerPersonId",
                principalTable: "Persons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiResources_Persons_OwnerPersonId",
                table: "ApiResources");

            migrationBuilder.DropIndex(
                name: "IX_ApiResources_OwnerPersonId",
                table: "ApiResources");

            migrationBuilder.DropColumn(
                name: "OwnerPersonId",
                table: "ApiResources");
        }
    }
}
