using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CENS15_V2.Migrations
{
    public partial class AddAlumnoSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Eliminado",
                table: "Alumnos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEliminacion",
                table: "Alumnos",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Eliminado",
                table: "Alumnos");

            migrationBuilder.DropColumn(
                name: "FechaEliminacion",
                table: "Alumnos");
        }
    }
}
