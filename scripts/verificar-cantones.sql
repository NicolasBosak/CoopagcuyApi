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

-- Comunidades cuyo cantón escrito a mano cruza contra MÁS DE UN cantón del
-- catálogo. El catálogo es nacional y hay nombres de cantón repetidos entre
-- provincias ("Bolívar" en Carchi y Manabí, "Olmedo" en Loja y Manabí): con
-- texto libre no hay forma de saber cuál era la comunidad, y un UPDATE que
-- cruce por nombre elegiría uno en silencio. Si esta consulta devuelve filas,
-- la migración se va a detener igual que con las que no cruzan; hay que
-- asignar el cantón correcto a mano antes de subirla.
SELECT c."Id",
       c."Nombre"   AS comunidad,
       c."Canton"   AS canton_escrito_a_mano,
       count(*)     AS cantones_candidatos
FROM "Comunidades" c
JOIN "Cantones" ct
  ON translate(lower(trim(c."Canton")), 'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
   = translate(lower(trim(ct."Nombre")), 'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
GROUP BY c."Id", c."Nombre", c."Canton"
HAVING count(*) > 1
ORDER BY c."Nombre";
