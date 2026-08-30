using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComunidadCuelgaDeCanton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF generó por su cuenta el DropColumn "Canton" ANTES del
            // AddColumn "CantonId" (agrupa los drops al principio). Aquí se
            // reordena a mano: el backfill de abajo necesita leer la columna
            // "Canton" todavía viva, así que su DropColumn se mueve al final,
            // después del backfill y de la validación.
            migrationBuilder.DropIndex(
                name: "IX_Comunidades_Nombre",
                table: "Comunidades");

            migrationBuilder.AddColumn<int>(
                name: "AltitudMaxM",
                table: "Comunidades",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AltitudMinM",
                table: "Comunidades",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CantonId",
                table: "Comunidades",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitud",
                table: "Comunidades",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitud",
                table: "Comunidades",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            // Backfill: cruza el cantón que estaba escrito a mano contra el catálogo,
            // ignorando tildes y mayúsculas. Existe al menos una comunidad con "Nabon"
            // sin tilde dada de alta desde Administración; con comparación cruda se
            // quedaría sin cantón.
            //
            // Se usa translate() y no la extensión unaccent: unaccent hay que instalarla
            // en la base (CREATE EXTENSION) y en Neon eso es un permiso que la migración
            // no tiene por qué necesitar. translate() es SQL estándar y basta para las
            // vocales acentuadas y la eñe, que es todo lo que aparece en un topónimo
            // ecuatoriano.
            migrationBuilder.Sql("""
                UPDATE "Comunidades" c
                SET "CantonId" = ct."Id"
                FROM "Cantones" ct
                WHERE c."CantonId" = 0
                  AND translate(lower(trim(c."Canton")),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                    = translate(lower(trim(ct."Nombre")),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN');
                """);

            // Si alguna comunidad no cruzó, la migración SE DETIENE. No inventa un
            // cantón ni borra la fila: alguien tiene que mirar ese dato. El mensaje
            // nombra las comunidades para que sea accionable sin abrir la base.
            migrationBuilder.Sql("""
                DO $$
                DECLARE sueltas text;
                BEGIN
                    SELECT string_agg(format('%s (cantón "%s")', "Nombre", "Canton"), ', ')
                    INTO sueltas
                    FROM "Comunidades" WHERE "CantonId" = 0;

                    IF sueltas IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Hay comunidades cuyo cantón no existe en el catálogo: %. '
                            'Corrígelas antes de migrar (ver scripts/verificar-cantones.sql).',
                            sueltas;
                    END IF;
                END $$;
                """);

            migrationBuilder.UpdateData(
                table: "Comunidades",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AltitudMaxM", "AltitudMinM", "CantonId", "Latitud", "Longitud" },
                values: new object[] { 3190, 3190, 6, -3.225944m, -79.504472m });

            migrationBuilder.UpdateData(
                table: "Comunidades",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AltitudMaxM", "AltitudMinM", "CantonId", "Latitud", "Longitud" },
                values: new object[] { 3370, 3200, 6, -3.083667m, -79.451222m });

            migrationBuilder.UpdateData(
                table: "Comunidades",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AltitudMaxM", "AltitudMinM", "CantonId", "Latitud", "Longitud" },
                values: new object[] { 2900, 2600, 8, -3.135528m, -79.395972m });

            migrationBuilder.UpdateData(
                table: "Comunidades",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "AltitudMaxM", "AltitudMinM", "CantonId", "Latitud", "Longitud" },
                values: new object[] { 2800, 2600, 4, -3.340833m, -79.204806m });

            migrationBuilder.UpdateData(
                table: "Comunidades",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "AltitudMaxM", "AltitudMinM", "CantonId", "Latitud", "Longitud" },
                values: new object[] { null, null, 6, null, null });

            migrationBuilder.DropColumn(
                name: "Canton",
                table: "Comunidades");

            migrationBuilder.CreateIndex(
                name: "IX_Comunidades_CantonId_Nombre",
                table: "Comunidades",
                columns: new[] { "CantonId", "Nombre" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Comunidades_Cantones_CantonId",
                table: "Comunidades",
                column: "CantonId",
                principalTable: "Cantones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comunidades_Cantones_CantonId",
                table: "Comunidades");

            migrationBuilder.DropIndex(
                name: "IX_Comunidades_CantonId_Nombre",
                table: "Comunidades");

            migrationBuilder.AddColumn<string>(
                name: "Canton",
                table: "Comunidades",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // La vuelta atrás repuebla el texto desde el catálogo. No es idéntico a lo
            // que había —"Nabon" vuelve como "Nabón"—, y eso es deliberado: devolver el
            // error de digitación sería devolver el problema. Corre antes de tirar
            // "CantonId": lo necesita para el cruce.
            migrationBuilder.Sql("""
                UPDATE "Comunidades" c
                SET "Canton" = ct."Nombre"
                FROM "Cantones" ct
                WHERE ct."Id" = c."CantonId";
                """);

            migrationBuilder.DropColumn(
                name: "AltitudMaxM",
                table: "Comunidades");

            migrationBuilder.DropColumn(
                name: "AltitudMinM",
                table: "Comunidades");

            migrationBuilder.DropColumn(
                name: "CantonId",
                table: "Comunidades");

            migrationBuilder.DropColumn(
                name: "Latitud",
                table: "Comunidades");

            migrationBuilder.DropColumn(
                name: "Longitud",
                table: "Comunidades");

            migrationBuilder.CreateIndex(
                name: "IX_Comunidades_Nombre",
                table: "Comunidades",
                column: "Nombre",
                unique: true);
        }
    }
}
