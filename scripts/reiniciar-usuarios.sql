-- ============================================================
-- Reinicio de usuarios · Login por cédula — Sistema Cuy Azuayito
-- ============================================================
-- Borra TODOS los usuarios y crea el usuario maestro inicial
-- (rol AdminTecnico), que inicia sesión con número de cédula.
-- Desde este usuario se crean los demás en /administracion.
--
-- ANTES DE EJECUTAR:
--   1. Aplica la migración del API (borra usuarios y cambia el
--      esquema de la tabla):
--        dotnet ef database update
--   2. Reemplaza los 3 valores <<...>> del INSERT de abajo:
--        · <<CEDULA>>: cédula ecuatoriana REAL y VÁLIDA de 10
--          dígitos (provincia 01–24 y dígito verificador). El
--          API valida esto al crear usuarios; usa la cédula
--          verdadera del administrador.
--        · <<NOMBRE COMPLETO>>: nombre de la persona.
--        · <<CONTRASENA>>: mínimo 8 caracteres con al menos una
--          letra y un número (misma política del sistema).
--      No dejes este archivo guardado con la contraseña real.
--
-- Cómo ejecutarlo en Neon:
--   1. Entra a https://console.neon.tech y abre tu proyecto.
--   2. Ve a "SQL Editor".
--   3. Pega este script completo (ya editado) y presiona "Run".
--
-- Nota técnica: pgcrypto con gen_salt('bf', 11) genera hashes
-- Blowfish ($2a$11$...) totalmente compatibles con BCrypt.Net,
-- el verificador que usa el API.
-- ============================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

BEGIN;

-- Ningún dato operativo referencia a Usuarios: borrado directo
TRUNCATE TABLE "Usuarios" RESTART IDENTITY;

INSERT INTO "Usuarios"
    ("Cedula", "NombreCompleto", "Email", "PasswordHash",
     "Rol", "CatAsignado", "Activo", "FechaCreacion")
VALUES (
    '<<CEDULA>>',
    '<<NOMBRE COMPLETO>>',
    NULL,                                        -- correo opcional
    crypt('<<CONTRASENA>>', gen_salt('bf', 11)), -- hash BCrypt
    'AdminTecnico',
    NULL,
    TRUE,
    NOW() AT TIME ZONE 'utc'
);

COMMIT;

-- Verificación: debe devolver una sola fila con tu cédula
SELECT "Id", "Cedula", "NombreCompleto", "Rol", "Activo"
FROM "Usuarios";
