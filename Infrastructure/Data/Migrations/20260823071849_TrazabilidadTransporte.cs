using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrazabilidadTransporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CondicionesClaves",
                table: "Movilizaciones",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CondicionesLlegadaClaves",
                table: "Movilizaciones",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LlegaronEnBuenEstado",
                table: "Movilizaciones",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CondicionesClaves",
                table: "Movilizaciones");

            migrationBuilder.DropColumn(
                name: "CondicionesLlegadaClaves",
                table: "Movilizaciones");

            migrationBuilder.DropColumn(
                name: "LlegaronEnBuenEstado",
                table: "Movilizaciones");
        }
    }
}
