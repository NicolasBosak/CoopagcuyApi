using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DevolucionPorDespacho : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devoluciones_Lotes_LoteId",
                table: "Devoluciones");

            migrationBuilder.AlterColumn<int>(
                name: "LoteId",
                table: "Devoluciones",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "DespachoId",
                table: "Devoluciones",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_DespachoId",
                table: "Devoluciones",
                column: "DespachoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Devoluciones_Despachos_DespachoId",
                table: "Devoluciones",
                column: "DespachoId",
                principalTable: "Despachos",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Devoluciones_Lotes_LoteId",
                table: "Devoluciones",
                column: "LoteId",
                principalTable: "Lotes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devoluciones_Despachos_DespachoId",
                table: "Devoluciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Devoluciones_Lotes_LoteId",
                table: "Devoluciones");

            migrationBuilder.DropIndex(
                name: "IX_Devoluciones_DespachoId",
                table: "Devoluciones");

            migrationBuilder.DropColumn(
                name: "DespachoId",
                table: "Devoluciones");

            migrationBuilder.AlterColumn<int>(
                name: "LoteId",
                table: "Devoluciones",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Devoluciones_Lotes_LoteId",
                table: "Devoluciones",
                column: "LoteId",
                principalTable: "Lotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
