using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgregaMovilizacionPagosControlesProceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignosClinicos",
                table: "Lotes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaIngresoFrio",
                table: "Faenamientos",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaSalidaFrio",
                table: "Faenamientos",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoDecomiso",
                table: "Faenamientos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentacionEmpaque",
                table: "Faenamientos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TiempoLavadoMinutos",
                table: "Faenamientos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnidadesDecomisadas",
                table: "Faenamientos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Movilizaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LoteId = table.Column<int>(type: "integer", nullable: false),
                    FechaDespacho = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Conductor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CantidadMovilizada = table.Column<int>(type: "integer", nullable: false),
                    CondicionesTransporte = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TipoForraje = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DiasRetiroMedicamentos = table.Column<int>(type: "integer", nullable: true),
                    ResponsableDespacho = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FechaRecepcionPlanta = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RecibidoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CondicionLlegada = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movilizaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movilizaciones_Lotes_LoteId",
                        column: x => x.LoteId,
                        principalTable: "Lotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pagos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductoraId = table.Column<int>(type: "integer", nullable: false),
                    LoteId = table.Column<int>(type: "integer", nullable: true),
                    MontoUsd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    FechaPago = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MetodoPago = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Responsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pagos_Lotes_LoteId",
                        column: x => x.LoteId,
                        principalTable: "Lotes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Pagos_Productoras_ProductoraId",
                        column: x => x.ProductoraId,
                        principalTable: "Productoras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movilizaciones_LoteId",
                table: "Movilizaciones",
                column: "LoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_LoteId",
                table: "Pagos",
                column: "LoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_ProductoraId",
                table: "Pagos",
                column: "ProductoraId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Movilizaciones");

            migrationBuilder.DropTable(
                name: "Pagos");

            migrationBuilder.DropColumn(
                name: "SignosClinicos",
                table: "Lotes");

            migrationBuilder.DropColumn(
                name: "FechaIngresoFrio",
                table: "Faenamientos");

            migrationBuilder.DropColumn(
                name: "FechaSalidaFrio",
                table: "Faenamientos");

            migrationBuilder.DropColumn(
                name: "MotivoDecomiso",
                table: "Faenamientos");

            migrationBuilder.DropColumn(
                name: "PresentacionEmpaque",
                table: "Faenamientos");

            migrationBuilder.DropColumn(
                name: "TiempoLavadoMinutos",
                table: "Faenamientos");

            migrationBuilder.DropColumn(
                name: "UnidadesDecomisadas",
                table: "Faenamientos");
        }
    }
}
