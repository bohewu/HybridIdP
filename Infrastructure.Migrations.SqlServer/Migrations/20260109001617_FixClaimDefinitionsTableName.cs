using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class FixClaimDefinitionsTableName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScopeClaims_Core.Application.IApplicationDbContext.ClaimDefinitions_ClaimDefinitionId",
                table: "ScopeClaims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Core.Application.IApplicationDbContext.UserClaims",
                table: "Core.Application.IApplicationDbContext.ClaimDefinitions");

            migrationBuilder.RenameTable(
                name: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                newName: "ClaimDefinitions");

            migrationBuilder.RenameIndex(
                name: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_Name",
                table: "ClaimDefinitions",
                newName: "IX_ClaimDefinitions_Name");

            migrationBuilder.RenameIndex(
                name: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_ClaimType",
                table: "ClaimDefinitions",
                newName: "IX_ClaimDefinitions_ClaimType");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ClaimDefinitions",
                table: "ClaimDefinitions",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScopeClaims_ClaimDefinitions_ClaimDefinitionId",
                table: "ScopeClaims",
                column: "ClaimDefinitionId",
                principalTable: "ClaimDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScopeClaims_ClaimDefinitions_ClaimDefinitionId",
                table: "ScopeClaims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ClaimDefinitions",
                table: "ClaimDefinitions");

            migrationBuilder.RenameTable(
                name: "ClaimDefinitions",
                newName: "Core.Application.IApplicationDbContext.ClaimDefinitions");

            migrationBuilder.RenameIndex(
                name: "IX_ClaimDefinitions_Name",
                table: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                newName: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_Name");

            migrationBuilder.RenameIndex(
                name: "IX_ClaimDefinitions_ClaimType",
                table: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                newName: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_ClaimType");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Core.Application.IApplicationDbContext.UserClaims",
                table: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScopeClaims_Core.Application.IApplicationDbContext.ClaimDefinitions_ClaimDefinitionId",
                table: "ScopeClaims",
                column: "ClaimDefinitionId",
                principalTable: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
