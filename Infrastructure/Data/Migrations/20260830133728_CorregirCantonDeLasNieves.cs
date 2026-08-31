using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorregirCantonDeLasNieves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CentrosAcopio",
                keyColumn: "Codigo",
                keyValue: "NIE",
                column: "CantonId",
                value: 6);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CentrosAcopio",
                keyColumn: "Codigo",
                keyValue: "NIE",
                column: "CantonId",
                value: 4);
        }
    }
}
