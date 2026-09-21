using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderEmailSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderEmailSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderSubjectDirectoryBindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    EmailTrustOrigin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefreshedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEmailSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderEmailSnapshots_ProviderSubjectDirectoryBindings_Pro~",
                        column: x => x.ProviderSubjectDirectoryBindingId,
                        principalTable: "ProviderSubjectDirectoryBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEmailSnapshots_ProviderSubjectDirectoryBindingId",
                table: "ProviderEmailSnapshots",
                column: "ProviderSubjectDirectoryBindingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderEmailSnapshots");
        }
    }
}
