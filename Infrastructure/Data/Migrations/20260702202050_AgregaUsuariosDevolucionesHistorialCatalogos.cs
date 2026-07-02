using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgregaUsuariosDevolucionesHistorialCatalogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comunidades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Canton = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CatReferencia = table.Column<string>(type: "text", nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comunidades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devoluciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LoteId = table.Column<int>(type: "integer", nullable: false),
                    ClienteDevuelve = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FechaDevolucion = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CantidadUnidades = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Responsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devoluciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devoluciones_Lotes_LoteId",
                        column: x => x.LoteId,
                        principalTable: "Lotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductoraCambios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductoraId = table.Column<int>(type: "integer", nullable: false),
                    CampoModificado = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValorAnterior = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ValorNuevo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ModificadoPor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FechaCambio = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoraCambios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductoraCambios_Productoras_ProductoraId",
                        column: x => x.ProductoraId,
                        principalTable: "Productoras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Comunidades",
                columns: new[] { "Id", "Activa", "Canton", "CatReferencia", "Nombre" },
                values: new object[,]
                {
                    { 1, true, "Pucará", "PAT", "Patococha" },
                    { 2, true, "Nabón", "NIE", "Las Nieves" },
                    { 3, true, "Santa Isabel", "HUE", "Huertas" },
                    { 4, true, "Nabón", "NAB", "Nabón / El Progreso" },
                    { 5, true, "Pucará", "PEL", "Pelincay" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comunidades_Nombre",
                table: "Comunidades",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_LoteId",
                table: "Devoluciones",
                column: "LoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoraCambios_ProductoraId",
                table: "ProductoraCambios",
                column: "ProductoraId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comunidades");

            migrationBuilder.DropTable(
                name: "Devoluciones");

            migrationBuilder.DropTable(
                name: "ProductoraCambios");
        }
    }
}
