using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoCentrosAcopio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CentrosAcopio",
                columns: table => new
                {
                    Codigo = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CantonId = table.Column<int>(type: "integer", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentrosAcopio", x => x.Codigo);
                    table.ForeignKey(
                        name: "FK_CentrosAcopio_Cantones_CantonId",
                        column: x => x.CantonId,
                        principalTable: "Cantones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "CentrosAcopio",
                columns: new[] { "Codigo", "Activo", "CantonId", "Nombre" },
                values: new object[,]
                {
                    { "HUE", true, 8, "Huertas" },
                    { "NAB", true, 4, "Nabón / El Progreso" },
                    { "NIE", true, 4, "Las Nieves" },
                    { "PAT", true, 6, "Patococha" },
                    { "PEL", true, 6, "Pelincay" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_CatAsignado",
                table: "Usuarios",
                column: "CatAsignado");

            migrationBuilder.CreateIndex(
                name: "IX_Productoras_CatAsignado",
                table: "Productoras",
                column: "CatAsignado");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasPendientesVinculacion_CentroAcopio",
                table: "EntregasPendientesVinculacion",
                column: "CentroAcopio");

            migrationBuilder.CreateIndex(
                name: "IX_Comunidades_CatReferencia",
                table: "Comunidades",
                column: "CatReferencia");

            migrationBuilder.CreateIndex(
                name: "IX_CentrosAcopio_CantonId",
                table: "CentrosAcopio",
                column: "CantonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Comunidades_CentrosAcopio_CatReferencia",
                table: "Comunidades",
                column: "CatReferencia",
                principalTable: "CentrosAcopio",
                principalColumn: "Codigo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EntregasPendientesVinculacion_CentrosAcopio_CentroAcopio",
                table: "EntregasPendientesVinculacion",
                column: "CentroAcopio",
                principalTable: "CentrosAcopio",
                principalColumn: "Codigo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lotes_CentrosAcopio_CentroAcopio",
                table: "Lotes",
                column: "CentroAcopio",
                principalTable: "CentrosAcopio",
                principalColumn: "Codigo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Productoras_CentrosAcopio_CatAsignado",
                table: "Productoras",
                column: "CatAsignado",
                principalTable: "CentrosAcopio",
                principalColumn: "Codigo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_CentrosAcopio_CatAsignado",
                table: "Usuarios",
                column: "CatAsignado",
                principalTable: "CentrosAcopio",
                principalColumn: "Codigo",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comunidades_CentrosAcopio_CatReferencia",
                table: "Comunidades");

            migrationBuilder.DropForeignKey(
                name: "FK_EntregasPendientesVinculacion_CentrosAcopio_CentroAcopio",
                table: "EntregasPendientesVinculacion");

            migrationBuilder.DropForeignKey(
                name: "FK_Lotes_CentrosAcopio_CentroAcopio",
                table: "Lotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Productoras_CentrosAcopio_CatAsignado",
                table: "Productoras");

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_CentrosAcopio_CatAsignado",
                table: "Usuarios");

            migrationBuilder.DropTable(
                name: "CentrosAcopio");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_CatAsignado",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Productoras_CatAsignado",
                table: "Productoras");

            migrationBuilder.DropIndex(
                name: "IX_EntregasPendientesVinculacion_CentroAcopio",
                table: "EntregasPendientesVinculacion");

            migrationBuilder.DropIndex(
                name: "IX_Comunidades_CatReferencia",
                table: "Comunidades");
        }
    }
}
