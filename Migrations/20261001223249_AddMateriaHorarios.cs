using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CENS15_V2.Migrations
{
    /// <inheritdoc />
    public partial class AddMateriaHorarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CargaHoraria",
                table: "Materias",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "HorariosMateria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MateriaId = table.Column<int>(type: "integer", nullable: false),
                    CursoId = table.Column<int>(type: "integer", nullable: false),
                    HorarioAnexoId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorariosMateria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HorariosMateria_Cursos_CursoId",
                        column: x => x.CursoId,
                        principalTable: "Cursos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HorariosMateria_HorariosAnexo_HorarioAnexoId",
                        column: x => x.HorarioAnexoId,
                        principalTable: "HorariosAnexo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HorariosMateria_Materias_MateriaId",
                        column: x => x.MateriaId,
                        principalTable: "Materias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HorariosMateria_CursoId_HorarioAnexoId",
                table: "HorariosMateria",
                columns: new[] { "CursoId", "HorarioAnexoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HorariosMateria_HorarioAnexoId",
                table: "HorariosMateria",
                column: "HorarioAnexoId");

            migrationBuilder.CreateIndex(
                name: "IX_HorariosMateria_MateriaId",
                table: "HorariosMateria",
                column: "MateriaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HorariosMateria");

            migrationBuilder.DropColumn(
                name: "CargaHoraria",
                table: "Materias");

        }
    }
}
