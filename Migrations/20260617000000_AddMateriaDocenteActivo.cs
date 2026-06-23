using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CENS15_V2.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260617000000_AddMateriaDocenteActivo")]
    public partial class AddMateriaDocenteActivo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "MateriaDocentes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
                UPDATE ""MateriaDocentes"" md
                SET ""Activo"" = TRUE
                WHERE md.""DocenteId"" = (
                    SELECT md2.""DocenteId""
                    FROM ""MateriaDocentes"" md2
                    WHERE md2.""MateriaId"" = md.""MateriaId""
                    ORDER BY CASE WHEN LOWER(md2.""Rol"") = 'titular' THEN 0 ELSE 1 END, md2.""DocenteId""
                    LIMIT 1
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activo",
                table: "MateriaDocentes");
        }
    }
}
