using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddClientRequiredScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "UserSessions",
                schema: "dbo",
                newName: "UserSessions");

            migrationBuilder.RenameTable(
                name: "Settings",
                schema: "dbo",
                newName: "Settings");

            migrationBuilder.RenameTable(
                name: "SecurityPolicies",
                schema: "dbo",
                newName: "SecurityPolicies");

            migrationBuilder.RenameTable(
                name: "ScopeExtensions",
                schema: "dbo",
                newName: "ScopeExtensions");

            migrationBuilder.RenameTable(
                name: "ScopeClaims",
                schema: "dbo",
                newName: "ScopeClaims");

            migrationBuilder.RenameTable(
                name: "Resources",
                schema: "dbo",
                newName: "Resources");

            migrationBuilder.RenameTable(
                name: "OpenIddictTokens",
                schema: "dbo",
                newName: "OpenIddictTokens");

            migrationBuilder.RenameTable(
                name: "OpenIddictScopes",
                schema: "dbo",
                newName: "OpenIddictScopes");

            migrationBuilder.RenameTable(
                name: "OpenIddictAuthorizations",
                schema: "dbo",
                newName: "OpenIddictAuthorizations");

            migrationBuilder.RenameTable(
                name: "OpenIddictApplications",
                schema: "dbo",
                newName: "OpenIddictApplications");

            migrationBuilder.RenameTable(
                name: "LoginHistories",
                schema: "dbo",
                newName: "LoginHistories");

            migrationBuilder.RenameTable(
                name: "Core.Application.IApplicationDbContext.UserClaims",
                schema: "dbo",
                newName: "Core.Application.IApplicationDbContext.UserClaims");

            migrationBuilder.RenameTable(
                name: "AuditEvents",
                schema: "dbo",
                newName: "AuditEvents");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                schema: "dbo",
                newName: "AspNetUserTokens");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                schema: "dbo",
                newName: "AspNetUsers");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                schema: "dbo",
                newName: "AspNetUserRoles");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                schema: "dbo",
                newName: "AspNetUserLogins");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                schema: "dbo",
                newName: "AspNetUserClaims");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                schema: "dbo",
                newName: "AspNetRoles");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                schema: "dbo",
                newName: "AspNetRoleClaims");

            migrationBuilder.RenameTable(
                name: "ApiResourceScopes",
                schema: "dbo",
                newName: "ApiResourceScopes");

            migrationBuilder.RenameTable(
                name: "ApiResources",
                schema: "dbo",
                newName: "ApiResources");

            migrationBuilder.CreateTable(
                name: "ClientRequiredScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRequiredScopes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRequiredScopes_ClientId_ScopeId",
                table: "ClientRequiredScopes",
                columns: new[] { "ClientId", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientRequiredScopes");

            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.RenameTable(
                name: "UserSessions",
                newName: "UserSessions",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "Settings",
                newName: "Settings",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "SecurityPolicies",
                newName: "SecurityPolicies",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "ScopeExtensions",
                newName: "ScopeExtensions",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "ScopeClaims",
                newName: "ScopeClaims",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "Resources",
                newName: "Resources",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "OpenIddictTokens",
                newName: "OpenIddictTokens",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "OpenIddictScopes",
                newName: "OpenIddictScopes",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "OpenIddictAuthorizations",
                newName: "OpenIddictAuthorizations",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "OpenIddictApplications",
                newName: "OpenIddictApplications",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "LoginHistories",
                newName: "LoginHistories",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "Core.Application.IApplicationDbContext.UserClaims",
                newName: "Core.Application.IApplicationDbContext.UserClaims",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AuditEvents",
                newName: "AuditEvents",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                newName: "AspNetUserTokens",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "AspNetUsers",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                newName: "AspNetUserRoles",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                newName: "AspNetUserLogins",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                newName: "AspNetUserClaims",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                newName: "AspNetRoles",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                newName: "AspNetRoleClaims",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "ApiResourceScopes",
                newName: "ApiResourceScopes",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "ApiResources",
                newName: "ApiResources",
                newSchema: "dbo");
        }
    }
}
