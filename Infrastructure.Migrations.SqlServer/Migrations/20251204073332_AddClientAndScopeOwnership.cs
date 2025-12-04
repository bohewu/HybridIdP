using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddClientAndScopeOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientOwnerships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedByPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientOwnerships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientOwnerships_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientOwnerships_Persons_CreatedByPersonId",
                        column: x => x.CreatedByPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScopeOwnerships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedByPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeOwnerships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScopeOwnerships_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScopeOwnerships_Persons_CreatedByPersonId",
                        column: x => x.CreatedByPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientOwnerships_ClientId",
                table: "ClientOwnerships",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientOwnerships_CreatedByPersonId",
                table: "ClientOwnerships",
                column: "CreatedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientOwnerships_CreatedByUserId",
                table: "ClientOwnerships",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeOwnerships_CreatedByPersonId",
                table: "ScopeOwnerships",
                column: "CreatedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeOwnerships_CreatedByUserId",
                table: "ScopeOwnerships",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeOwnerships_ScopeId",
                table: "ScopeOwnerships",
                column: "ScopeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientOwnerships");

            migrationBuilder.DropTable(
                name: "ScopeOwnerships");
        }
    }
}
