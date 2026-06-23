using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace CENS15_V2.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260617001000_AddInitialAccessFields")]
    public partial class AddInitialAccessFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Auths",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialAccessSentAt",
                table: "Auths",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Auths");

            migrationBuilder.DropColumn(
                name: "InitialAccessSentAt",
                table: "Auths");
        }
    }
}
