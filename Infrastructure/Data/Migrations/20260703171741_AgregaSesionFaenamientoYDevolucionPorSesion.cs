using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgregaSesionFaenamientoYDevolucionPorSesion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumeroSesion",
                table: "Faenamientos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RegistroFaenamientoId",
                table: "Devoluciones",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_RegistroFaenamientoId",
                table: "Devoluciones",
                column: "RegistroFaenamientoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Devoluciones_Faenamientos_RegistroFaenamientoId",
                table: "Devoluciones",
                column: "RegistroFaenamientoId",
                principalTable: "Faenamientos",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devoluciones_Faenamientos_RegistroFaenamientoId",
                table: "Devoluciones");

            migrationBuilder.DropIndex(
                name: "IX_Devoluciones_RegistroFaenamientoId",
                table: "Devoluciones");

            migrationBuilder.DropColumn(
                name: "NumeroSesion",
                table: "Faenamientos");

            migrationBuilder.DropColumn(
                name: "RegistroFaenamientoId",
                table: "Devoluciones");
        }
    }
}
