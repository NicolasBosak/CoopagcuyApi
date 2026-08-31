-- Comprobación previa de los cantones, ANTES de desplegar la migración
-- ComunidadCuelgaDeCanton.
--
-- Esa migración cruza el cantón que hoy está escrito a mano en Comunidades
-- contra el catálogo, y SE DETIENE si alguna comunidad no cruza contra
-- ninguno, o cruza contra más de uno. Este guion adelanta ese veredicto.
--
-- IMPORTANTE: el catálogo va EMBEBIDO aquí abajo, no se lee de la tabla
-- "Cantones". Tiene que ser así: esa tabla la crea la propia migración, de
-- modo que un guion que la consultara no podría ejecutarse antes de
-- desplegar, que es justo cuando hace falta. Las 221 filas se generaron
-- desde Infrastructure/Data/Seed/GeografiaEcuador.cs, que es la misma
-- fuente que siembra la migración.
--
-- El cruce ignora tildes y mayúsculas, igual que el backfill: "Nabon" cruza
-- con "Nabón" y no aparece como problema.
--
-- Son dos consultas. Córrelas las dos.


-- ── 1 · Qué hay hoy ─────────────────────────────────────────────────
-- Orientación: los cantones escritos a mano que existen ahora mismo, y
-- cuántas comunidades cuelgan de cada uno. Con seis comunidades esto se
-- revisa de un vistazo.

SELECT c."Canton" AS canton_escrito_a_mano,
       count(*)   AS comunidades
FROM "Comunidades" c
GROUP BY c."Canton"
ORDER BY c."Canton";


-- ── 2 · Qué va a detener la migración ───────────────────────────────
-- Si devuelve CERO filas, la migración pasa. Si devuelve alguna, hay que
-- corregir ese cantón (o asignarlo a mano) ANTES de subirla; la columna
-- `motivo` dice cuál de los dos casos es, y `productoras_afectadas` cuánta
-- gente cuelga de esa comunidad.

WITH catalogo(nombre, provincia) AS (
    VALUES
        ('Cuenca', 'Azuay'),
        ('Girón', 'Azuay'),
        ('Gualaceo', 'Azuay'),
        ('Nabón', 'Azuay'),
        ('Paute', 'Azuay'),
        ('Pucará', 'Azuay'),
        ('San Fernando', 'Azuay'),
        ('Santa Isabel', 'Azuay'),
        ('Sígsig', 'Azuay'),
        ('Oña', 'Azuay'),
        ('Chordeleg', 'Azuay'),
        ('El Pan', 'Azuay'),
        ('Sevilla de Oro', 'Azuay'),
        ('Guachapala', 'Azuay'),
        ('Camilo Ponce Enríquez', 'Azuay'),
        ('Guaranda', 'Bolívar'),
        ('Chillanes', 'Bolívar'),
        ('Chimbo', 'Bolívar'),
        ('Echeandía', 'Bolívar'),
        ('San Miguel', 'Bolívar'),
        ('Caluma', 'Bolívar'),
        ('Las Naves', 'Bolívar'),
        ('Azogues', 'Cañar'),
        ('Biblián', 'Cañar'),
        ('Cañar', 'Cañar'),
        ('La Troncal', 'Cañar'),
        ('El Tambo', 'Cañar'),
        ('Déleg', 'Cañar'),
        ('Suscal', 'Cañar'),
        ('Tulcán', 'Carchi'),
        ('Bolívar', 'Carchi'),
        ('Espejo', 'Carchi'),
        ('Mira', 'Carchi'),
        ('Montúfar', 'Carchi'),
        ('San Pedro de Huaca', 'Carchi'),
        ('Riobamba', 'Chimborazo'),
        ('Alausí', 'Chimborazo'),
        ('Colta', 'Chimborazo'),
        ('Chambo', 'Chimborazo'),
        ('Chunchi', 'Chimborazo'),
        ('Guamote', 'Chimborazo'),
        ('Guano', 'Chimborazo'),
        ('Pallatanga', 'Chimborazo'),
        ('Penipe', 'Chimborazo'),
        ('Cumandá', 'Chimborazo'),
        ('Latacunga', 'Cotopaxi'),
        ('La Maná', 'Cotopaxi'),
        ('Pangua', 'Cotopaxi'),
        ('Pujilí', 'Cotopaxi'),
        ('Salcedo', 'Cotopaxi'),
        ('Saquisilí', 'Cotopaxi'),
        ('Sigchos', 'Cotopaxi'),
        ('Machala', 'El Oro'),
        ('Arenillas', 'El Oro'),
        ('Atahualpa', 'El Oro'),
        ('Balsas', 'El Oro'),
        ('Chilla', 'El Oro'),
        ('El Guabo', 'El Oro'),
        ('Huaquillas', 'El Oro'),
        ('Marcabelí', 'El Oro'),
        ('Pasaje', 'El Oro'),
        ('Piñas', 'El Oro'),
        ('Portovelo', 'El Oro'),
        ('Santa Rosa', 'El Oro'),
        ('Zaruma', 'El Oro'),
        ('Las Lajas', 'El Oro'),
        ('Esmeraldas', 'Esmeraldas'),
        ('Eloy Alfaro', 'Esmeraldas'),
        ('Muisne', 'Esmeraldas'),
        ('Quinindé', 'Esmeraldas'),
        ('San Lorenzo', 'Esmeraldas'),
        ('Atacames', 'Esmeraldas'),
        ('Rioverde', 'Esmeraldas'),
        ('San Cristóbal', 'Galápagos'),
        ('Isabela', 'Galápagos'),
        ('Santa Cruz', 'Galápagos'),
        ('Guayaquil', 'Guayas'),
        ('Alfredo Baquerizo Moreno', 'Guayas'),
        ('Balao', 'Guayas'),
        ('Balzar', 'Guayas'),
        ('Colimes', 'Guayas'),
        ('Daule', 'Guayas'),
        ('Durán', 'Guayas'),
        ('El Empalme', 'Guayas'),
        ('El Triunfo', 'Guayas'),
        ('Milagro', 'Guayas'),
        ('Naranjal', 'Guayas'),
        ('Naranjito', 'Guayas'),
        ('Palestina', 'Guayas'),
        ('Pedro Carbo', 'Guayas'),
        ('Samborondón', 'Guayas'),
        ('Santa Lucía', 'Guayas'),
        ('Salitre', 'Guayas'),
        ('San Jacinto de Yaguachi', 'Guayas'),
        ('Playas', 'Guayas'),
        ('Simón Bolívar', 'Guayas'),
        ('Coronel Marcelino Maridueña', 'Guayas'),
        ('Lomas de Sargentillo', 'Guayas'),
        ('Nobol', 'Guayas'),
        ('General Antonio Elizalde', 'Guayas'),
        ('Isidro Ayora', 'Guayas'),
        ('Ibarra', 'Imbabura'),
        ('Antonio Ante', 'Imbabura'),
        ('Cotacachi', 'Imbabura'),
        ('Otavalo', 'Imbabura'),
        ('Pimampiro', 'Imbabura'),
        ('San Miguel de Urcuquí', 'Imbabura'),
        ('Loja', 'Loja'),
        ('Calvas', 'Loja'),
        ('Catamayo', 'Loja'),
        ('Celica', 'Loja'),
        ('Chaguarpamba', 'Loja'),
        ('Espíndola', 'Loja'),
        ('Gonzanamá', 'Loja'),
        ('Macará', 'Loja'),
        ('Paltas', 'Loja'),
        ('Puyango', 'Loja'),
        ('Saraguro', 'Loja'),
        ('Sozoranga', 'Loja'),
        ('Zapotillo', 'Loja'),
        ('Pindal', 'Loja'),
        ('Quilanga', 'Loja'),
        ('Olmedo', 'Loja'),
        ('Babahoyo', 'Los Ríos'),
        ('Baba', 'Los Ríos'),
        ('Montalvo', 'Los Ríos'),
        ('Puebloviejo', 'Los Ríos'),
        ('Quevedo', 'Los Ríos'),
        ('Urdaneta', 'Los Ríos'),
        ('Ventanas', 'Los Ríos'),
        ('Vínces', 'Los Ríos'),
        ('Palenque', 'Los Ríos'),
        ('Buena Fe', 'Los Ríos'),
        ('Valencia', 'Los Ríos'),
        ('Mocache', 'Los Ríos'),
        ('Quinsaloma', 'Los Ríos'),
        ('Portoviejo', 'Manabí'),
        ('Bolívar', 'Manabí'),
        ('Chone', 'Manabí'),
        ('El Carmen', 'Manabí'),
        ('Flavio Alfaro', 'Manabí'),
        ('Jipijapa', 'Manabí'),
        ('Junín', 'Manabí'),
        ('Manta', 'Manabí'),
        ('Montecristi', 'Manabí'),
        ('Paján', 'Manabí'),
        ('Pichincha', 'Manabí'),
        ('Rocafuerte', 'Manabí'),
        ('Santa Ana', 'Manabí'),
        ('Sucre', 'Manabí'),
        ('Tosagua', 'Manabí'),
        ('24 de Mayo', 'Manabí'),
        ('Pedernales', 'Manabí'),
        ('Olmedo', 'Manabí'),
        ('Puerto López', 'Manabí'),
        ('Jama', 'Manabí'),
        ('Jaramijó', 'Manabí'),
        ('San Vicente', 'Manabí'),
        ('Morona', 'Morona Santiago'),
        ('Gualaquiza', 'Morona Santiago'),
        ('Limón Indanza', 'Morona Santiago'),
        ('Palora', 'Morona Santiago'),
        ('Santiago', 'Morona Santiago'),
        ('Sucúa', 'Morona Santiago'),
        ('Huamboya', 'Morona Santiago'),
        ('San Juan Bosco', 'Morona Santiago'),
        ('Taisha', 'Morona Santiago'),
        ('Logroño', 'Morona Santiago'),
        ('Pablo Sexto', 'Morona Santiago'),
        ('Tiwintza', 'Morona Santiago'),
        ('Tena', 'Napo'),
        ('Archidona', 'Napo'),
        ('El Chaco', 'Napo'),
        ('Quijos', 'Napo'),
        ('Carlos Julio Arosemena Tola', 'Napo'),
        ('Orellana', 'Orellana'),
        ('Aguarico', 'Orellana'),
        ('La Joya de los Sachas', 'Orellana'),
        ('Loreto', 'Orellana'),
        ('Pastaza', 'Pastaza'),
        ('Mera', 'Pastaza'),
        ('Santa Clara', 'Pastaza'),
        ('Arajuno', 'Pastaza'),
        ('Quito', 'Pichincha'),
        ('Cayambe', 'Pichincha'),
        ('Mejía', 'Pichincha'),
        ('Pedro Moncayo', 'Pichincha'),
        ('Rumiñahui', 'Pichincha'),
        ('San Miguel de los Bancos', 'Pichincha'),
        ('Pedro Vicente Maldonado', 'Pichincha'),
        ('Puerto Quito', 'Pichincha'),
        ('Santa Elena', 'Santa Elena'),
        ('La Libertad', 'Santa Elena'),
        ('Salinas', 'Santa Elena'),
        ('Santo Domingo', 'Santo Domingo de los Tsáchilas'),
        ('La Concordia', 'Santo Domingo de los Tsáchilas'),
        ('Lago Agrio', 'Sucumbíos'),
        ('Gonzalo Pizarro', 'Sucumbíos'),
        ('Putumayo', 'Sucumbíos'),
        ('Shushufindi', 'Sucumbíos'),
        ('Sucumbíos', 'Sucumbíos'),
        ('Cascales', 'Sucumbíos'),
        ('Cuyabeno', 'Sucumbíos'),
        ('Ambato', 'Tungurahua'),
        ('Baños de Agua Santa', 'Tungurahua'),
        ('Cevallos', 'Tungurahua'),
        ('Mocha', 'Tungurahua'),
        ('Patate', 'Tungurahua'),
        ('Quero', 'Tungurahua'),
        ('San Pedro de Pelileo', 'Tungurahua'),
        ('Santiago de Píllaro', 'Tungurahua'),
        ('Tisaleo', 'Tungurahua'),
        ('Zamora', 'Zamora Chinchipe'),
        ('Chinchipe', 'Zamora Chinchipe'),
        ('Nangaritza', 'Zamora Chinchipe'),
        ('Yacuambi', 'Zamora Chinchipe'),
        ('Yantzaza', 'Zamora Chinchipe'),
        ('El Pangui', 'Zamora Chinchipe'),
        ('Centinela del Cóndor', 'Zamora Chinchipe'),
        ('Palanda', 'Zamora Chinchipe'),
        ('Paquisha', 'Zamora Chinchipe')
),
cruces AS (
    SELECT c."Id",
           c."Nombre" AS comunidad,
           c."Canton" AS canton_escrito_a_mano,
           count(ct.nombre) AS candidatos,
           string_agg(DISTINCT ct.provincia, ', ') AS provincias_candidatas
    FROM "Comunidades" c
    LEFT JOIN catalogo ct
      ON translate(lower(trim(c."Canton")), 'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
       = translate(lower(trim(ct.nombre)),  'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
    GROUP BY c."Id", c."Nombre", c."Canton"
)
SELECT x."Id",
       x.comunidad,
       x.canton_escrito_a_mano,
       CASE WHEN x.candidatos = 0
            THEN 'NO cruza contra ningun canton del catalogo'
            ELSE 'AMBIGUO: cruza contra ' || x.candidatos
                 || ' cantones, en ' || x.provincias_candidatas
       END AS motivo,
       (SELECT count(*) FROM "Productoras" p WHERE p."ComunidadId" = x."Id")
           AS productoras_afectadas
FROM cruces x
WHERE x.candidatos <> 1
ORDER BY x.comunidad;
