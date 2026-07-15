using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <summary>
    /// "Letras" pasa a llamarse "días" en los pagos a crédito. Es solo el
    /// nombre: el cálculo sigue siendo monto ÷ N, así que los pagos ya
    /// registrados conservan su valor y basta con renombrar las columnas —
    /// no hay que recalcular ni convertir nada.
    /// </summary>
    public partial class PagoLetrasADias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ValorPorLetra",
                table: "Pagos",
                newName: "ValorPorDia");

            migrationBuilder.RenameColumn(
                name: "NumeroLetras",
                table: "Pagos",
                newName: "NumeroDias");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ValorPorDia",
                table: "Pagos",
                newName: "ValorPorLetra");

            migrationBuilder.RenameColumn(
                name: "NumeroDias",
                table: "Pagos",
                newName: "NumeroLetras");
        }
    }
}
