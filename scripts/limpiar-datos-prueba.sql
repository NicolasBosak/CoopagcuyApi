-- ============================================================
-- Limpieza de datos de prueba — Sistema Cuy Azuayito
-- ============================================================
-- Borra TODOS los datos operativos (lotes, cuyes, faenamientos,
-- despachos, devoluciones, retornos, movilizaciones, pagos, QR,
-- novedades e historial) y reinicia los contadores de Id.
--
-- CONSERVA: Usuarios, Productoras y Comunidades.
--
-- ADVERTENCIA: irreversible. Ejecutar solo cuando se quiera
-- empezar de cero con el sistema de entregas por jaula.
--
-- Cómo ejecutarlo en Neon:
--   1. Entra a https://console.neon.tech y abre tu proyecto.
--   2. Ve a "SQL Editor".
--   3. Pega este script completo y presiona "Run".
-- ============================================================

BEGIN;

TRUNCATE TABLE
    "CuyFaenamientos",
    "RetornosProductora",
    "Devoluciones",
    "Despachos",
    "CodigosQR",
    "Faenamientos",
    "Movilizaciones",
    "Novedades",
    "CuyRegistros",
    "Pagos",
    "ProductoraCambios",
    "Lotes"
RESTART IDENTITY CASCADE;

COMMIT;

-- Verificación rápida: todas deben devolver 0
SELECT 'Lotes' AS tabla, COUNT(*) FROM "Lotes"
UNION ALL SELECT 'CuyRegistros', COUNT(*) FROM "CuyRegistros"
UNION ALL SELECT 'Faenamientos', COUNT(*) FROM "Faenamientos"
UNION ALL SELECT 'Movilizaciones', COUNT(*) FROM "Movilizaciones"
UNION ALL SELECT 'Pagos', COUNT(*) FROM "Pagos";
