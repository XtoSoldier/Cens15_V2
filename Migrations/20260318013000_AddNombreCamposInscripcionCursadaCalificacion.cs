using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CENS15_V2.Migrations
{
    public partial class AddNombreCamposInscripcionCursadaCalificacion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MateriaNombre",
                table: "CursadasMaterias",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CursoNombre",
                table: "Inscripciones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Division",
                table: "Inscripciones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MateriaNombre",
                table: "Calificaciones",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MateriaNombre",
                table: "CursadasMaterias");

            migrationBuilder.DropColumn(
                name: "CursoNombre",
                table: "Inscripciones");

            migrationBuilder.DropColumn(
                name: "Division",
                table: "Inscripciones");

            migrationBuilder.DropColumn(
                name: "MateriaNombre",
                table: "Calificaciones");
        }
    }
}
