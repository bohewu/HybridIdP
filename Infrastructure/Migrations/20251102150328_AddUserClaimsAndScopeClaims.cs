using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserClaimsAndScopeClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Core.Application.IApplicationDbContext.UserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ClaimType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserPropertyPath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsStandard = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Core.Application.IApplicationDbContext.UserClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScopeClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScopeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScopeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserClaimId = table.Column<int>(type: "integer", nullable: false),
                    AlwaysInclude = table.Column<bool>(type: "boolean", nullable: false),
                    CustomMappingLogic = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScopeClaims_Core.Application.IApplicationDbContext.UserClai~",
                        column: x => x.UserClaimId,
                        principalTable: "Core.Application.IApplicationDbContext.UserClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Core.Application.IApplicationDbContext.UserClaims_ClaimType",
                table: "Core.Application.IApplicationDbContext.UserClaims",
                column: "ClaimType");

            migrationBuilder.CreateIndex(
                name: "IX_Core.Application.IApplicationDbContext.UserClaims_Name",
                table: "Core.Application.IApplicationDbContext.UserClaims",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScopeClaims_ScopeId_UserClaimId",
                table: "ScopeClaims",
                columns: new[] { "ScopeId", "UserClaimId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScopeClaims_UserClaimId",
                table: "ScopeClaims",
                column: "UserClaimId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScopeClaims");

            migrationBuilder.DropTable(
                name: "Core.Application.IApplicationDbContext.UserClaims");
        }
    }
}
