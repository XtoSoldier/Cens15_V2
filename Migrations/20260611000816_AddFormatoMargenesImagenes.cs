using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CENS15_V2.Migrations
{
    /// <inheritdoc />
    public partial class AddFormatoMargenesImagenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Formato",
                table: "CertificadoTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImagenesJson",
                table: "CertificadoTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MargenDerecho",
                table: "CertificadoTemplates",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MargenInferior",
                table: "CertificadoTemplates",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MargenIzquierdo",
                table: "CertificadoTemplates",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MargenSuperior",
                table: "CertificadoTemplates",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Formato",
                table: "CertificadoTemplates");

            migrationBuilder.DropColumn(
                name: "ImagenesJson",
                table: "CertificadoTemplates");

            migrationBuilder.DropColumn(
                name: "MargenDerecho",
                table: "CertificadoTemplates");

            migrationBuilder.DropColumn(
                name: "MargenInferior",
                table: "CertificadoTemplates");

            migrationBuilder.DropColumn(
                name: "MargenIzquierdo",
                table: "CertificadoTemplates");

            migrationBuilder.DropColumn(
                name: "MargenSuperior",
                table: "CertificadoTemplates");
        }
    }
}
