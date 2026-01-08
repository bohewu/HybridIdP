using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RenameUserClaimsToClaimDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScopeClaims_Core.Application.IApplicationDbContext.UserClai~",
                table: "ScopeClaims");

            migrationBuilder.RenameTable(
                name: "Core.Application.IApplicationDbContext.UserClaims",
                newName: "Core.Application.IApplicationDbContext.ClaimDefinitions");

            migrationBuilder.RenameIndex(
                name: "IX_Core.Application.IApplicationDbContext.UserClaims_ClaimType",
                table: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                newName: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_Cla~");

            migrationBuilder.RenameIndex(
                name: "IX_Core.Application.IApplicationDbContext.UserClaims_Name",
                table: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                newName: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_Name");

            migrationBuilder.RenameColumn(
                name: "UserClaimId",
                table: "ScopeClaims",
                newName: "ClaimDefinitionId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopeClaims_UserClaimId",
                table: "ScopeClaims",
                newName: "IX_ScopeClaims_ClaimDefinitionId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopeClaims_ScopeId_UserClaimId",
                table: "ScopeClaims",
                newName: "IX_ScopeClaims_ScopeId_ClaimDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScopeClaims_Core.Application.IApplicationDbContext.ClaimDef~",
                table: "ScopeClaims",
                column: "ClaimDefinitionId",
                principalTable: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScopeClaims_Core.Application.IApplicationDbContext.ClaimDef~",
                table: "ScopeClaims");

            migrationBuilder.RenameTable(
                name: "Core.Application.IApplicationDbContext.ClaimDefinitions",
                newName: "Core.Application.IApplicationDbContext.UserClaims");

            migrationBuilder.RenameIndex(
                name: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_Cla~",
                table: "Core.Application.IApplicationDbContext.UserClaims",
                newName: "IX_Core.Application.IApplicationDbContext.UserClaims_ClaimType");

            migrationBuilder.RenameIndex(
                name: "IX_Core.Application.IApplicationDbContext.ClaimDefinitions_Name",
                table: "Core.Application.IApplicationDbContext.UserClaims",
                newName: "IX_Core.Application.IApplicationDbContext.UserClaims_Name");

            migrationBuilder.RenameColumn(
                name: "ClaimDefinitionId",
                table: "ScopeClaims",
                newName: "UserClaimId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopeClaims_ScopeId_ClaimDefinitionId",
                table: "ScopeClaims",
                newName: "IX_ScopeClaims_ScopeId_UserClaimId");

            migrationBuilder.RenameIndex(
                name: "IX_ScopeClaims_ClaimDefinitionId",
                table: "ScopeClaims",
                newName: "IX_ScopeClaims_UserClaimId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScopeClaims_Core.Application.IApplicationDbContext.UserClai~",
                table: "ScopeClaims",
                column: "UserClaimId",
                principalTable: "Core.Application.IApplicationDbContext.UserClaims",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
