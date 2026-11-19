using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CENS15_V2.Migrations
{
    /// <inheritdoc />
    public partial class AddPreceptores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreceptorId",
                table: "Cursos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Preceptores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombres = table.Column<string>(type: "text", nullable: false),
                    Apellidos = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Preceptores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Preceptores_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cursos_PreceptorId",
                table: "Cursos",
                column: "PreceptorId");

            migrationBuilder.CreateIndex(
                name: "IX_Preceptores_UserId",
                table: "Preceptores",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Cursos_Preceptores_PreceptorId",
                table: "Cursos",
                column: "PreceptorId",
                principalTable: "Preceptores",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cursos_Preceptores_PreceptorId",
                table: "Cursos");

            migrationBuilder.DropTable(
                name: "Preceptores");

            migrationBuilder.DropIndex(
                name: "IX_Cursos_PreceptorId",
                table: "Cursos");

            migrationBuilder.DropColumn(
                name: "PreceptorId",
                table: "Cursos");

        }
    }
}
