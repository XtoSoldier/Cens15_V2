using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CENS15_V2.Migrations
{
    public partial class AddDocenteMateriaDocente : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Docentes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombres = table.Column<string>(type: "text", nullable: false),
                    Apellidos = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Docentes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Docentes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MateriaDocentes",
                columns: table => new
                {
                    MateriaId = table.Column<int>(type: "integer", nullable: false),
                    DocenteId = table.Column<int>(type: "integer", nullable: false),
                    Rol = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MateriaDocentes", x => new { x.MateriaId, x.DocenteId });
                    table.ForeignKey(
                        name: "FK_MateriaDocentes_Docentes_DocenteId",
                        column: x => x.DocenteId,
                        principalTable: "Docentes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MateriaDocentes_Materias_MateriaId",
                        column: x => x.MateriaId,
                        principalTable: "Materias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Docentes_Email",
                table: "Docentes",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Docentes_UserId",
                table: "Docentes",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MateriaDocentes_DocenteId",
                table: "MateriaDocentes",
                column: "DocenteId");

            migrationBuilder.CreateIndex(
                name: "IX_MateriaDocentes_MateriaId_Rol",
                table: "MateriaDocentes",
                columns: new[] { "MateriaId", "Rol" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MateriaDocentes");

            migrationBuilder.DropTable(
                name: "Docentes");
        }
    }
}
