using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgregaLoteFaenadoYCatOperador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CodigosQR_Lotes_LoteId",
                table: "CodigosQR");

            migrationBuilder.AddColumn<string>(
                name: "CatAsignado",
                table: "Usuarios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoteFaenadoId",
                table: "Faenamientos",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LoteId",
                table: "CodigosQR",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "LoteFaenadoId",
                table: "CodigosQR",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LotesFaenados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FechaFaenamiento = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    OperarioResponsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TemperaturaAlmacenamiento = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesFaenados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Faenamientos_LoteFaenadoId",
                table: "Faenamientos",
                column: "LoteFaenadoId");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosQR_LoteFaenadoId",
                table: "CodigosQR",
                column: "LoteFaenadoId");

            migrationBuilder.CreateIndex(
                name: "IX_LotesFaenados_Codigo",
                table: "LotesFaenados",
                column: "Codigo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CodigosQR_LotesFaenados_LoteFaenadoId",
                table: "CodigosQR",
                column: "LoteFaenadoId",
                principalTable: "LotesFaenados",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CodigosQR_Lotes_LoteId",
                table: "CodigosQR",
                column: "LoteId",
                principalTable: "Lotes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Faenamientos_LotesFaenados_LoteFaenadoId",
                table: "Faenamientos",
                column: "LoteFaenadoId",
                principalTable: "LotesFaenados",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CodigosQR_LotesFaenados_LoteFaenadoId",
                table: "CodigosQR");

            migrationBuilder.DropForeignKey(
                name: "FK_CodigosQR_Lotes_LoteId",
                table: "CodigosQR");

            migrationBuilder.DropForeignKey(
                name: "FK_Faenamientos_LotesFaenados_LoteFaenadoId",
                table: "Faenamientos");

            migrationBuilder.DropTable(
                name: "LotesFaenados");

            migrationBuilder.DropIndex(
                name: "IX_Faenamientos_LoteFaenadoId",
                table: "Faenamientos");

            migrationBuilder.DropIndex(
                name: "IX_CodigosQR_LoteFaenadoId",
                table: "CodigosQR");

            migrationBuilder.DropColumn(
                name: "CatAsignado",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "LoteFaenadoId",
                table: "Faenamientos");

            migrationBuilder.DropColumn(
                name: "LoteFaenadoId",
                table: "CodigosQR");

            migrationBuilder.AlterColumn<int>(
                name: "LoteId",
                table: "CodigosQR",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CodigosQR_Lotes_LoteId",
                table: "CodigosQR",
                column: "LoteId",
                principalTable: "Lotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
