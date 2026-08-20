using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NovedadPorCuy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CuyRegistroId",
                table: "Novedades",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Novedades_CuyRegistroId",
                table: "Novedades",
                column: "CuyRegistroId");

            migrationBuilder.AddForeignKey(
                name: "FK_Novedades_CuyRegistros_CuyRegistroId",
                table: "Novedades",
                column: "CuyRegistroId",
                principalTable: "CuyRegistros",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Novedades_CuyRegistros_CuyRegistroId",
                table: "Novedades");

            migrationBuilder.DropIndex(
                name: "IX_Novedades_CuyRegistroId",
                table: "Novedades");

            migrationBuilder.DropColumn(
                name: "CuyRegistroId",
                table: "Novedades");
        }
    }
}
