using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgregaRegistroIndividualCuyesYRetornos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CuyFaenamientos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegistroFaenamientoId = table.Column<int>(type: "integer", nullable: false),
                    NumeroEnLote = table.Column<int>(type: "integer", nullable: false),
                    PesoCanalGramos = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RetornadoAProductora = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuyFaenamientos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CuyFaenamientos_Faenamientos_RegistroFaenamientoId",
                        column: x => x.RegistroFaenamientoId,
                        principalTable: "Faenamientos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CuyRegistros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LoteId = table.Column<int>(type: "integer", nullable: false),
                    NumeroEnLote = table.Column<int>(type: "integer", nullable: false),
                    PesoGramos = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ColorPelaje = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EstadoOreja = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TamanoAnimal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SignosClinicos = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    MotivoNovedad = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuyRegistros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CuyRegistros_Lotes_LoteId",
                        column: x => x.LoteId,
                        principalTable: "Lotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RetornosProductora",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LoteId = table.Column<int>(type: "integer", nullable: false),
                    NumeroEnLote = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FechaRetorno = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Responsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetornosProductora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetornosProductora_Lotes_LoteId",
                        column: x => x.LoteId,
                        principalTable: "Lotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CuyFaenamientos_RegistroFaenamientoId_NumeroEnLote",
                table: "CuyFaenamientos",
                columns: new[] { "RegistroFaenamientoId", "NumeroEnLote" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CuyRegistros_LoteId_NumeroEnLote",
                table: "CuyRegistros",
                columns: new[] { "LoteId", "NumeroEnLote" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetornosProductora_LoteId",
                table: "RetornosProductora",
                column: "LoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CuyFaenamientos");

            migrationBuilder.DropTable(
                name: "CuyRegistros");

            migrationBuilder.DropTable(
                name: "RetornosProductora");
        }
    }
}
