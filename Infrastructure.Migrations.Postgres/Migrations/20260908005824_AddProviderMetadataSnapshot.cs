using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations.Postgres.Migrations
{
    public partial class AddProviderMetadataSnapshot : Migration
    {
        // Retain the withdrawn draft migration ID for local history compatibility.
        // Fresh installations skip its cache; existing draft data stays quarantined.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
