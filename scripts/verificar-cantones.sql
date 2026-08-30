-- Comunidades cuyo cantón escrito a mano NO cruza contra el catálogo.
--
-- CORRER ANTES DE DESPLEGAR la migración ComunidadCuelgaDeCanton. Si devuelve
-- filas, la migración se va a detener: hay que corregir esos cantones (o dar
-- de alta el cantón que falte) antes de subirla.
--
-- El cruce ignora tildes y mayúsculas, igual que el backfill de la migración:
-- "Nabon" cruza con "Nabón" y no aparece aquí.
SELECT c."Id",
       c."Nombre"   AS comunidad,
       c."Canton"   AS canton_escrito_a_mano,
       (SELECT count(*) FROM "Productoras" p WHERE p."ComunidadId" = c."Id")
           AS productoras_afectadas
FROM "Comunidades" c
WHERE NOT EXISTS (
    SELECT 1 FROM "Cantones" ct
    WHERE translate(lower(trim(c."Canton")), 'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
        = translate(lower(trim(ct."Nombre")), 'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
)
ORDER BY c."Nombre";
