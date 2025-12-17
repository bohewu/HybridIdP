using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWebAuthnCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecoveryCodes",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SignatureCounter = table.Column<long>(type: "bigint", nullable: false),
                    CredType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AaGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCredentials_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCredentials_UserId",
                table: "UserCredentials",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCredentials");

            migrationBuilder.DropColumn(
                name: "RecoveryCodes",
                table: "AspNetUsers");
        }
    }
}
