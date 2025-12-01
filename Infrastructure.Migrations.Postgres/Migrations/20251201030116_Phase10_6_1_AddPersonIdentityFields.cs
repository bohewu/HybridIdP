using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_6_1_AddPersonIdentityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Persons_EmployeeId",
                table: "Persons");

            migrationBuilder.AddColumn<string>(
                name: "IdentityDocumentType",
                table: "Persons",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IdentityVerifiedAt",
                table: "Persons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdentityVerifiedBy",
                table: "Persons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumber",
                table: "Persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResidentCertificateNumber",
                table: "Persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_EmployeeId",
                table: "Persons",
                column: "EmployeeId",
                unique: true,
                filter: "\"EmployeeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_NationalId",
                table: "Persons",
                column: "NationalId",
                unique: true,
                filter: "\"NationalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PassportNumber",
                table: "Persons",
                column: "PassportNumber",
                unique: true,
                filter: "\"PassportNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_ResidentCertificateNumber",
                table: "Persons",
                column: "ResidentCertificateNumber",
                unique: true,
                filter: "\"ResidentCertificateNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Persons_EmployeeId",
                table: "Persons");

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

            migrationBuilder.CreateIndex(
                name: "IX_Persons_EmployeeId",
                table: "Persons",
                column: "EmployeeId",
                unique: true,
                filter: "\"EmployeeId\" IS NOT NULL");
        }
    }
}
