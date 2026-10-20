using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeFieldAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NativeFieldStateJson",
                table: "Persons",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NativeFieldStateJson",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NativeFieldSourcePolicies",
                columns: table => new
                {
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Paused = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeFieldSourcePolicies", x => x.Source);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NativeFieldSourcePolicies");

            migrationBuilder.DropColumn(
                name: "NativeFieldStateJson",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "NativeFieldStateJson",
                table: "AspNetUsers");
        }
    }
}
