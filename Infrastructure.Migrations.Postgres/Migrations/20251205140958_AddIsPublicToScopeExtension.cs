using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddIsPublicToScopeExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "ScopeExtensions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            
            // Set OIDC scopes (openid, profile, email, roles) as public
            // These scopes are user-centric and should be available to all clients
            // Note: CAST is required because ScopeId is VARCHAR but OpenIddictScopes.Id is UUID
            migrationBuilder.Sql(@"
                UPDATE ""ScopeExtensions"" 
                SET ""IsPublic"" = true 
                WHERE ""ScopeId"" IN (
                    SELECT CAST(""Id"" AS TEXT)
                    FROM ""OpenIddictScopes"" 
                    WHERE ""Name"" IN ('openid', 'profile', 'email', 'roles')
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "ScopeExtensions");
        }
    }
}
