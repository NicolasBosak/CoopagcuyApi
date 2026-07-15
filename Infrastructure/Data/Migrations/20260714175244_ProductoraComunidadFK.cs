using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Productora.Comunidad/Canton (texto libre) → ComunidadId contra el
    /// catálogo. Los valores existentes se mapean por nombre normalizado
    /// (sin tildes, sin mayúsculas, sin espacios sobrantes). Si alguno no
    /// tiene correspondencia la migración aborta y lo reporta: preferimos
    /// no desplegar antes que inventar o perder el origen de una productora.
    /// </summary>
    public partial class ProductoraComunidadFK : Migration
    {
        // Normaliza para comparar: quita tildes, pasa a minúsculas y colapsa
        // los espacios internos. "  PATOCOCHA " y "Patococha" son la misma.
        private const string Norm =
            @"lower(regexp_replace(trim(translate({0}, " +
            @"'áàäâãéèëêíìïîóòöôõúùüûñÁÀÄÂÃÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑ', " +
            @"'aaaaaeeeeiiiiooooouuuunAAAAAEEEEIIIIOOOOOUUUUN')), '\s+', ' ', 'g'))";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Nullable primero: aún no sabemos a qué comunidad va cada fila
            migrationBuilder.AddColumn<int>(
                name: "ComunidadId",
                table: "Productoras",
                type: "integer",
                nullable: true);

            // 2. Mapeo por nombre normalizado contra el catálogo
            migrationBuilder.Sql($@"
                UPDATE ""Productoras"" p
                SET ""ComunidadId"" = c.""Id""
                FROM ""Comunidades"" c
                WHERE {string.Format(Norm, @"p.""Comunidad""")}
                    = {string.Format(Norm, @"c.""Nombre""")};");

            // 3. Cortafuegos: si algo no calzó, abortar con el detalle exacto.
            //    La transacción de la migración revierte el paso 1.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    total int;
                    detalle text;
                BEGIN
                    SELECT count(*),
                           string_agg(
                               format('  - %s (cédula %s) → ""%s""',
                                      ""NombreCompleto"", ""Cedula"", ""Comunidad""),
                               chr(10))
                    INTO total, detalle
                    FROM ""Productoras""
                    WHERE ""ComunidadId"" IS NULL;

                    IF total > 0 THEN
                        RAISE EXCEPTION E'Migración abortada: % productora(s) tienen una comunidad que no existe en el catálogo.\n%\nCorrige el nombre en Productoras o añade la comunidad al catálogo, y vuelve a migrar.',
                            total, detalle;
                    END IF;
                END $$;");

            // 4. Ya sabemos que todas calzaron: la FK pasa a obligatoria
            migrationBuilder.Sql(
                @"ALTER TABLE ""Productoras"" ALTER COLUMN ""ComunidadId"" SET NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Productoras_ComunidadId",
                table: "Productoras",
                column: "ComunidadId");

            migrationBuilder.AddForeignKey(
                name: "FK_Productoras_Comunidades_ComunidadId",
                table: "Productoras",
                column: "ComunidadId",
                principalTable: "Comunidades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 5. Solo ahora, con el origen ya preservado en la FK
            migrationBuilder.DropColumn(name: "Comunidad", table: "Productoras");
            migrationBuilder.DropColumn(name: "Canton", table: "Productoras");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comunidad",
                table: "Productoras",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Canton",
                table: "Productoras",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Rehidrata el texto libre desde el catálogo antes de soltar la FK
            migrationBuilder.Sql(@"
                UPDATE ""Productoras"" p
                SET ""Comunidad"" = c.""Nombre"", ""Canton"" = c.""Canton""
                FROM ""Comunidades"" c
                WHERE p.""ComunidadId"" = c.""Id"";");

            migrationBuilder.DropForeignKey(
                name: "FK_Productoras_Comunidades_ComunidadId",
                table: "Productoras");

            migrationBuilder.DropIndex(
                name: "IX_Productoras_ComunidadId",
                table: "Productoras");

            migrationBuilder.DropColumn(
                name: "ComunidadId",
                table: "Productoras");
        }
    }
}
