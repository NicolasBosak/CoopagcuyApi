using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PagoCreditoYDespachoTransporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumeroLetras",
                table: "Pagos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorPorLetra",
                table: "Pagos",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Chofer",
                table: "Despachos",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ruta",
                table: "Despachos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumeroLetras",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "ValorPorLetra",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "Chofer",
                table: "Despachos");

            migrationBuilder.DropColumn(
                name: "Ruta",
                table: "Despachos");
        }
    }
}
