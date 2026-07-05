using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncIdempotenciaEntregas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncEntregasProcesadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DispositivoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdCliente = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FechaProcesado = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncEntregasProcesadas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncEntregasProcesadas_DispositivoId_IdCliente",
                table: "SyncEntregasProcesadas",
                columns: new[] { "DispositivoId", "IdCliente" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncEntregasProcesadas");
        }
    }
}
