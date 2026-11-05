using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CENS15_V2.Migrations
{
    /// <inheritdoc />
    public partial class AddCalificacionCondiciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "C1Condicion",
                table: "Calificaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "C2Condicion",
                table: "Calificaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DiciembreCondicion",
                table: "Calificaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MarzoCondicion",
                table: "Calificaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecuperacionExcepcional",
                table: "Calificaciones",
                type: "integer",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "C1Condicion",
                table: "Calificaciones");

            migrationBuilder.DropColumn(
                name: "C2Condicion",
                table: "Calificaciones");

            migrationBuilder.DropColumn(
                name: "DiciembreCondicion",
                table: "Calificaciones");

            migrationBuilder.DropColumn(
                name: "MarzoCondicion",
                table: "Calificaciones");

            migrationBuilder.DropColumn(
                name: "RecuperacionExcepcional",
                table: "Calificaciones");

        }
    }
}
