using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class VentaLocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsVentaLocal",
                table: "Pagos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "VentaLocalPagoId",
                table: "CuyRegistros",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CuyRegistros_VentaLocalPagoId",
                table: "CuyRegistros",
                column: "VentaLocalPagoId");

            migrationBuilder.AddForeignKey(
                name: "FK_CuyRegistros_Pagos_VentaLocalPagoId",
                table: "CuyRegistros",
                column: "VentaLocalPagoId",
                principalTable: "Pagos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CuyRegistros_Pagos_VentaLocalPagoId",
                table: "CuyRegistros");

            migrationBuilder.DropIndex(
                name: "IX_CuyRegistros_VentaLocalPagoId",
                table: "CuyRegistros");

            migrationBuilder.DropColumn(
                name: "EsVentaLocal",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "VentaLocalPagoId",
                table: "CuyRegistros");
        }
    }
}
