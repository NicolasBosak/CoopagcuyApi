using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CicloPagoPorTicket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ComprobanteExpiraEn",
                table: "Pagos",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComprobanteUrl",
                table: "Pagos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Estado",
                table: "Pagos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaPagoEfectivo",
                table: "Pagos",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaVerificacion",
                table: "Pagos",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoPagadoUsd",
                table: "Pagos",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PagadoPor",
                table: "Pagos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificadoPor",
                table: "Pagos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Descuentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PagoId = table.Column<int>(type: "integer", nullable: false),
                    NovedadCatId = table.Column<int>(type: "integer", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    MontoUsd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    RegistradoPor = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Descuentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Descuentos_Novedades_NovedadCatId",
                        column: x => x.NovedadCatId,
                        principalTable: "Novedades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Descuentos_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Descuentos_NovedadCatId",
                table: "Descuentos",
                column: "NovedadCatId");

            migrationBuilder.CreateIndex(
                name: "IX_Descuentos_PagoId_NovedadCatId",
                table: "Descuentos",
                columns: new[] { "PagoId", "NovedadCatId" },
                unique: true);

            // Los pagos anteriores a este ciclo son transacciones cerradas del flujo en
            // efectivo: se pagó lo que se reconoció y nadie tiene que verificar nada.
            // Dejarlos en Pendiente los haría aparecer en la bandeja de la planta como
            // deuda viva.
            migrationBuilder.Sql(@"
                UPDATE ""Pagos""
                SET ""Estado"" = 2, ""MontoPagadoUsd"" = ""MontoUsd""
                WHERE ""FechaRegistro"" < NOW();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Descuentos");

            migrationBuilder.DropColumn(
                name: "ComprobanteExpiraEn",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "ComprobanteUrl",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "FechaPagoEfectivo",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "FechaVerificacion",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "MontoPagadoUsd",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "PagadoPor",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "VerificadoPor",
                table: "Pagos");
        }
    }
}
