using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class Phase10_6_1_AddPersonIdentityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityDocumentType",
                table: "Persons",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IdentityVerifiedAt",
                table: "Persons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdentityVerifiedBy",
                table: "Persons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Persons",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumber",
                table: "Persons",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResidentCertificateNumber",
                table: "Persons",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_NationalId",
                table: "Persons",
                column: "NationalId",
                unique: true,
                filter: "[NationalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PassportNumber",
                table: "Persons",
                column: "PassportNumber",
                unique: true,
                filter: "[PassportNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_ResidentCertificateNumber",
                table: "Persons",
                column: "ResidentCertificateNumber",
                unique: true,
                filter: "[ResidentCertificateNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Persons_NationalId",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Persons_PassportNumber",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Persons_ResidentCertificateNumber",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "IdentityDocumentType",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "IdentityVerifiedAt",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "IdentityVerifiedBy",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "PassportNumber",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "ResidentCertificateNumber",
                table: "Persons");
        }
    }
}
