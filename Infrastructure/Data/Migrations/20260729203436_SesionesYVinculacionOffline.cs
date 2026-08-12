using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SesionesYVinculacionOffline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntregasPendientesVinculacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Cedula = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CentroAcopio = table.Column<string>(type: "text", nullable: false),
                    EnAyunas = table.Column<bool>(type: "boolean", nullable: false),
                    ResponsableRecepcion = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FechaCaptura = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DispositivoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdCliente = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CuyesJson = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FechaResolucion = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResueltaPor = table.Column<string>(type: "text", nullable: true),
                    ProductoraVinculadaId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntregasPendientesVinculacion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DispositivoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IpCreacion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FechaExpiracion = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FechaUltimoUso = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Revocado = table.Column<bool>(type: "boolean", nullable: false),
                    FechaRevocacion = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReemplazadoPorHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntregasPendientesVinculacion_DispositivoId_IdCliente",
                table: "EntregasPendientesVinculacion",
                columns: new[] { "DispositivoId", "IdCliente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UsuarioId",
                table: "RefreshTokens",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntregasPendientesVinculacion");

            migrationBuilder.DropTable(
                name: "RefreshTokens");
        }
    }
}
