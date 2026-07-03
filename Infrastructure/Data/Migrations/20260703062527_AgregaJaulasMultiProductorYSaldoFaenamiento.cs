using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgregaJaulasMultiProductorYSaldoFaenamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lotes_Productoras_ProductoraId",
                table: "Lotes");

            migrationBuilder.DropIndex(
                name: "IX_Faenamientos_LoteId",
                table: "Faenamientos");

            migrationBuilder.AddColumn<int>(
                name: "ProductoraId",
                table: "RetornosProductora",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "ProductoraId",
                table: "Lotes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "Cerrado",
                table: "Lotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCierre",
                table: "Lotes",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductoraId",
                table: "CuyRegistros",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetornosProductora_ProductoraId",
                table: "RetornosProductora",
                column: "ProductoraId");

            migrationBuilder.CreateIndex(
                name: "IX_Lotes_CentroAcopio_Cerrado",
                table: "Lotes",
                columns: new[] { "CentroAcopio", "Cerrado" });

            migrationBuilder.CreateIndex(
                name: "IX_Faenamientos_LoteId",
                table: "Faenamientos",
                column: "LoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CuyRegistros_ProductoraId",
                table: "CuyRegistros",
                column: "ProductoraId");

            migrationBuilder.AddForeignKey(
                name: "FK_CuyRegistros_Productoras_ProductoraId",
                table: "CuyRegistros",
                column: "ProductoraId",
                principalTable: "Productoras",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Lotes_Productoras_ProductoraId",
                table: "Lotes",
                column: "ProductoraId",
                principalTable: "Productoras",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RetornosProductora_Productoras_ProductoraId",
                table: "RetornosProductora",
                column: "ProductoraId",
                principalTable: "Productoras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CuyRegistros_Productoras_ProductoraId",
                table: "CuyRegistros");

            migrationBuilder.DropForeignKey(
                name: "FK_Lotes_Productoras_ProductoraId",
                table: "Lotes");

            migrationBuilder.DropForeignKey(
                name: "FK_RetornosProductora_Productoras_ProductoraId",
                table: "RetornosProductora");

            migrationBuilder.DropIndex(
                name: "IX_RetornosProductora_ProductoraId",
                table: "RetornosProductora");

            migrationBuilder.DropIndex(
                name: "IX_Lotes_CentroAcopio_Cerrado",
                table: "Lotes");

            migrationBuilder.DropIndex(
                name: "IX_Faenamientos_LoteId",
                table: "Faenamientos");

            migrationBuilder.DropIndex(
                name: "IX_CuyRegistros_ProductoraId",
                table: "CuyRegistros");

            migrationBuilder.DropColumn(
                name: "ProductoraId",
                table: "RetornosProductora");

            migrationBuilder.DropColumn(
                name: "Cerrado",
                table: "Lotes");

            migrationBuilder.DropColumn(
                name: "FechaCierre",
                table: "Lotes");

            migrationBuilder.DropColumn(
                name: "ProductoraId",
                table: "CuyRegistros");

            migrationBuilder.AlterColumn<int>(
                name: "ProductoraId",
                table: "Lotes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Faenamientos_LoteId",
                table: "Faenamientos",
                column: "LoteId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Lotes_Productoras_ProductoraId",
                table: "Lotes",
                column: "ProductoraId",
                principalTable: "Productoras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
