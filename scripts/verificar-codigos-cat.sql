-- Filas cuyo código de CAT NO es uno de los cinco códigos vigentes
-- ('PAT','NIE','HUE','NAB','PEL').
--
-- CORRER ANTES DE DESPLEGAR la migración CatalogoCentrosAcopio. Esa migración
-- agrega una clave foránea desde cada una de las cinco columnas de abajo
-- hacia CentrosAcopio.Codigo, y es segura SOLO porque el enum viejo (antes de
-- VerificacionCatString) garantizaba que todo valor almacenado ya era uno de
-- estos cinco códigos. Ese razonamiento nunca se comprobó contra los datos
-- reales: si esta consulta devuelve filas, la migración se va a detener (el
-- ALTER TABLE ... ADD CONSTRAINT falla con el primer valor que no cruce), y
-- hay que corregir esos valores a mano antes de subirla.
--
-- Usuarios.CatAsignado es la única de las cinco columnas donde NULL es
-- válido por diseño (los roles que no son OperadorCAT no tienen CAT
-- asignado): ahí solo se marca un valor no nulo que no sea uno de los cinco
-- códigos. En las otras cuatro, el modelo las declara NOT NULL desde la
-- migración VerificacionCatString, así que un NULL ahí sería en sí mismo un
-- dato corrupto — se marca explícitamente en vez de asumir que no puede
-- pasar.
WITH codigos_validos AS (
    SELECT unnest(ARRAY['PAT','NIE','HUE','NAB','PEL']) AS codigo
)
SELECT 'Usuarios.CatAsignado' AS columna, u."Id"::text AS id_fila, u."CatAsignado" AS valor
FROM "Usuarios" u
WHERE u."CatAsignado" IS NOT NULL
  AND u."CatAsignado" NOT IN (SELECT codigo FROM codigos_validos)

UNION ALL

SELECT 'Productoras.CatAsignado', p."Id"::text, p."CatAsignado"
FROM "Productoras" p
WHERE p."CatAsignado" IS NULL
   OR p."CatAsignado" NOT IN (SELECT codigo FROM codigos_validos)

UNION ALL

SELECT 'Lotes.CentroAcopio', l."Id"::text, l."CentroAcopio"
FROM "Lotes" l
WHERE l."CentroAcopio" IS NULL
   OR l."CentroAcopio" NOT IN (SELECT codigo FROM codigos_validos)

UNION ALL

SELECT 'EntregasPendientesVinculacion.CentroAcopio', v."Id"::text, v."CentroAcopio"
FROM "EntregasPendientesVinculacion" v
WHERE v."CentroAcopio" IS NULL
   OR v."CentroAcopio" NOT IN (SELECT codigo FROM codigos_validos)

UNION ALL

SELECT 'Comunidades.CatReferencia', c."Id"::text, c."CatReferencia"
FROM "Comunidades" c
WHERE c."CatReferencia" IS NULL
   OR c."CatReferencia" NOT IN (SELECT codigo FROM codigos_validos)

ORDER BY columna, id_fila;
