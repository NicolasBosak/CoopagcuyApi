using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DespachoPorLoteFaenado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Despachos_Lotes_LoteId",
                table: "Despachos");

            migrationBuilder.AlterColumn<int>(
                name: "LoteId",
                table: "Despachos",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "LoteFaenadoId",
                table: "Despachos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DespachoCuys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DespachoId = table.Column<int>(type: "integer", nullable: false),
                    CuyFaenamientoId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DespachoCuys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DespachoCuys_CuyFaenamientos_CuyFaenamientoId",
                        column: x => x.CuyFaenamientoId,
                        principalTable: "CuyFaenamientos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DespachoCuys_Despachos_DespachoId",
                        column: x => x.DespachoId,
                        principalTable: "Despachos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Despachos_LoteFaenadoId",
                table: "Despachos",
                column: "LoteFaenadoId");

            migrationBuilder.CreateIndex(
                name: "IX_DespachoCuys_CuyFaenamientoId",
                table: "DespachoCuys",
                column: "CuyFaenamientoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DespachoCuys_DespachoId",
                table: "DespachoCuys",
                column: "DespachoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Despachos_LotesFaenados_LoteFaenadoId",
                table: "Despachos",
                column: "LoteFaenadoId",
                principalTable: "LotesFaenados",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Despachos_Lotes_LoteId",
                table: "Despachos",
                column: "LoteId",
                principalTable: "Lotes",
                principalColumn: "Id");

            // Mapeo best-effort de despachos previos al detalle por animal:
            // se les asigna el lote faenado de la primera sesión de su
            // jaula. Su detalle por cuy queda vacío (eran solo cantidades),
            // por lo que no descuentan animales del nuevo saldo despachable.
            migrationBuilder.Sql("""
                UPDATE "Despachos" d
                SET "LoteFaenadoId" = (
                    SELECT f."LoteFaenadoId"
                    FROM "Faenamientos" f
                    WHERE f."LoteId" = d."LoteId"
                      AND f."LoteFaenadoId" IS NOT NULL
                    ORDER BY f."Id"
                    LIMIT 1)
                WHERE d."LoteFaenadoId" IS NULL AND d."LoteId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Despachos_LotesFaenados_LoteFaenadoId",
                table: "Despachos");

            migrationBuilder.DropForeignKey(
                name: "FK_Despachos_Lotes_LoteId",
                table: "Despachos");

            migrationBuilder.DropTable(
                name: "DespachoCuys");

            migrationBuilder.DropIndex(
                name: "IX_Despachos_LoteFaenadoId",
                table: "Despachos");

            migrationBuilder.DropColumn(
                name: "LoteFaenadoId",
                table: "Despachos");

            migrationBuilder.AlterColumn<int>(
                name: "LoteId",
                table: "Despachos",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Despachos_Lotes_LoteId",
                table: "Despachos",
                column: "LoteId",
                principalTable: "Lotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
