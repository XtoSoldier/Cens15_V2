using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CENS15_V2.Migrations
{
    public partial class AddCursadaMateriaCalificacion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CursadasMaterias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InscripcionId = table.Column<int>(type: "integer", nullable: false),
                    MateriaId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CursadasMaterias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CursadasMaterias_Inscripciones_InscripcionId",
                        column: x => x.InscripcionId,
                        principalTable: "Inscripciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CursadasMaterias_Materias_MateriaId",
                        column: x => x.MateriaId,
                        principalTable: "Materias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Calificaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CursadaMateriaId = table.Column<int>(type: "integer", nullable: false),
                    C1Nota1 = table.Column<decimal>(type: "numeric", nullable: true),
                    C1Nota2 = table.Column<decimal>(type: "numeric", nullable: true),
                    C1Nota3 = table.Column<decimal>(type: "numeric", nullable: true),
                    C1Promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    C2Nota1 = table.Column<decimal>(type: "numeric", nullable: true),
                    C2Nota2 = table.Column<decimal>(type: "numeric", nullable: true),
                    C2Nota3 = table.Column<decimal>(type: "numeric", nullable: true),
                    C2Promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    PromedioAnual = table.Column<decimal>(type: "numeric", nullable: true),
                    RecuperacionDiciembre = table.Column<decimal>(type: "numeric", nullable: true),
                    RecuperacionMarzo = table.Column<decimal>(type: "numeric", nullable: true),
                    CalificacionFinal = table.Column<decimal>(type: "numeric", nullable: true),
                    Estado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calificaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calificaciones_CursadasMaterias_CursadaMateriaId",
                        column: x => x.CursadaMateriaId,
                        principalTable: "CursadasMaterias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Calificaciones_CursadaMateriaId",
                table: "Calificaciones",
                column: "CursadaMateriaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CursadasMaterias_InscripcionId_MateriaId",
                table: "CursadasMaterias",
                columns: new[] { "InscripcionId", "MateriaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CursadasMaterias_MateriaId",
                table: "CursadasMaterias",
                column: "MateriaId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Calificaciones");

            migrationBuilder.DropTable(
                name: "CursadasMaterias");
        }
    }
}
