using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CENS15_V2.Migrations
{
    /// <inheritdoc />
    public partial class AddCalificacionOrigen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DesdePase",
                table: "Calificaciones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DisposicionEquivalencia",
                table: "Calificaciones",
                type: "text",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesdePase",
                table: "Calificaciones");

            migrationBuilder.DropColumn(
                name: "DisposicionEquivalencia",
                table: "Calificaciones");

        }
    }
}
