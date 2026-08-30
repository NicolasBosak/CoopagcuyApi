using CoopagcuyApi.Features.Catalogos.Models;

namespace CoopagcuyApi.Infrastructure.Data.Seed;

/// <summary>
/// División política del Ecuador: 24 provincias y sus 221 cantones.
///
/// Se siembra completa aunque la cooperativa opere hoy en una sola provincia.
/// El motivo es que el administrador ELIJA en vez de digitar: con texto libre,
/// "Nabón" y "Nabon" acababan siendo dos cantones distintos, y ese fue
/// exactamente el problema que este catálogo viene a cerrar.
///
/// Los Id son fijos y contiguos —provincias 1–24 en orden alfabético,
/// cantones 1–221 agrupados por provincia—. HasData los usa como clave: si
/// alguien los reordena, EF genera una migración que borra y reinserta el
/// catálogo entero y arrastra las claves foráneas de Comunidad.
/// </summary>
public static class GeografiaEcuador
{
    public static readonly Provincia[] Provincias =
    [
        new() { Id = 1,  Nombre = "Azuay" },
        new() { Id = 2,  Nombre = "Bolívar" },
        new() { Id = 3,  Nombre = "Cañar" },
        new() { Id = 4,  Nombre = "Carchi" },
        new() { Id = 5,  Nombre = "Chimborazo" },
        new() { Id = 6,  Nombre = "Cotopaxi" },
        new() { Id = 7,  Nombre = "El Oro" },
        new() { Id = 8,  Nombre = "Esmeraldas" },
        new() { Id = 9,  Nombre = "Galápagos" },
        new() { Id = 10, Nombre = "Guayas" },
        new() { Id = 11, Nombre = "Imbabura" },
        new() { Id = 12, Nombre = "Loja" },
        new() { Id = 13, Nombre = "Los Ríos" },
        new() { Id = 14, Nombre = "Manabí" },
        new() { Id = 15, Nombre = "Morona Santiago" },
        new() { Id = 16, Nombre = "Napo" },
        new() { Id = 17, Nombre = "Orellana" },
        new() { Id = 18, Nombre = "Pastaza" },
        new() { Id = 19, Nombre = "Pichincha" },
        new() { Id = 20, Nombre = "Santa Elena" },
        new() { Id = 21, Nombre = "Santo Domingo de los Tsáchilas" },
        new() { Id = 22, Nombre = "Sucumbíos" },
        new() { Id = 23, Nombre = "Tungurahua" },
        new() { Id = 24, Nombre = "Zamora Chinchipe" },
    ];

    public static readonly Canton[] Cantones = Construir();

    // Se arma desde un diccionario de "provincia -> nombres de cantón" y los
    // Id se asignan por posición. Escribir 221 literales con su Id a mano
    // sería una fuente de duplicados silenciosos.
    private static Canton[] Construir()
    {
        var porProvincia = new Dictionary<int, string[]>
        {
            // 1 · Azuay (15)
            [1] = ["Cuenca", "Girón", "Gualaceo", "Nabón", "Paute", "Pucará",
                   "San Fernando", "Santa Isabel", "Sígsig", "Oña", "Chordeleg",
                   "El Pan", "Sevilla de Oro", "Guachapala", "Camilo Ponce Enríquez"],
            // 2 · Bolívar (7)
            [2] = ["Guaranda", "Chillanes", "Chimbo", "Echeandía", "San Miguel",
                   "Caluma", "Las Naves"],
            // 3 · Cañar (7)
            [3] = ["Azogues", "Biblián", "Cañar", "La Troncal", "El Tambo",
                   "Déleg", "Suscal"],
            // 4 · Carchi (6)
            [4] = ["Tulcán", "Bolívar", "Espejo", "Mira", "Montúfar",
                   "San Pedro de Huaca"],
            // 5 · Chimborazo (10)
            [5] = ["Riobamba", "Alausí", "Colta", "Chambo", "Chunchi", "Guamote",
                   "Guano", "Pallatanga", "Penipe", "Cumandá"],
            // 6 · Cotopaxi (7)
            [6] = ["Latacunga", "La Maná", "Pangua", "Pujilí", "Salcedo",
                   "Saquisilí", "Sigchos"],
            // 7 · El Oro (14)
            [7] = ["Machala", "Arenillas", "Atahualpa", "Balsas", "Chilla",
                   "El Guabo", "Huaquillas", "Marcabelí", "Pasaje", "Piñas",
                   "Portovelo", "Santa Rosa", "Zaruma", "Las Lajas"],
            // 8 · Esmeraldas (7). La Concordia no está aquí: se creó como
            // cantón de Esmeraldas en 2007, pero la consulta popular la pasó
            // a Santo Domingo de los Tsáchilas y ahí pertenece hoy.
            [8] = ["Esmeraldas", "Eloy Alfaro", "Muisne", "Quinindé",
                   "San Lorenzo", "Atacames", "Rioverde"],
            // 9 · Galápagos (3)
            [9] = ["San Cristóbal", "Isabela", "Santa Cruz"],
            // 10 · Guayas (25)
            [10] = ["Guayaquil", "Alfredo Baquerizo Moreno", "Balao", "Balzar",
                    "Colimes", "Daule", "Durán", "El Empalme", "El Triunfo",
                    "Milagro", "Naranjal", "Naranjito", "Palestina", "Pedro Carbo",
                    "Samborondón", "Santa Lucía", "Salitre", "San Jacinto de Yaguachi",
                    "Playas", "Simón Bolívar", "Coronel Marcelino Maridueña",
                    "Lomas de Sargentillo", "Nobol", "General Antonio Elizalde",
                    "Isidro Ayora"],
            // 11 · Imbabura (6)
            [11] = ["Ibarra", "Antonio Ante", "Cotacachi", "Otavalo", "Pimampiro",
                    "San Miguel de Urcuquí"],
            // 12 · Loja (16)
            [12] = ["Loja", "Calvas", "Catamayo", "Celica", "Chaguarpamba",
                    "Espíndola", "Gonzanamá", "Macará", "Paltas", "Puyango",
                    "Saraguro", "Sozoranga", "Zapotillo", "Pindal", "Quilanga",
                    "Olmedo"],
            // 13 · Los Ríos (13)
            [13] = ["Babahoyo", "Baba", "Montalvo", "Puebloviejo", "Quevedo",
                    "Urdaneta", "Ventanas", "Vínces", "Palenque", "Buena Fe",
                    "Valencia", "Mocache", "Quinsaloma"],
            // 14 · Manabí (22)
            [14] = ["Portoviejo", "Bolívar", "Chone", "El Carmen", "Flavio Alfaro",
                    "Jipijapa", "Junín", "Manta", "Montecristi", "Paján", "Pichincha",
                    "Rocafuerte", "Santa Ana", "Sucre", "Tosagua", "24 de Mayo",
                    "Pedernales", "Olmedo", "Puerto López", "Jama", "Jaramijó",
                    "San Vicente"],
            // 15 · Morona Santiago (12)
            [15] = ["Morona", "Gualaquiza", "Limón Indanza", "Palora", "Santiago",
                    "Sucúa", "Huamboya", "San Juan Bosco", "Taisha", "Logroño",
                    "Pablo Sexto", "Tiwintza"],
            // 16 · Napo (5)
            [16] = ["Tena", "Archidona", "El Chaco", "Quijos", "Carlos Julio Arosemena Tola"],
            // 17 · Orellana (4)
            [17] = ["Orellana", "Aguarico", "La Joya de los Sachas", "Loreto"],
            // 18 · Pastaza (4)
            [18] = ["Pastaza", "Mera", "Santa Clara", "Arajuno"],
            // 19 · Pichincha (8)
            [19] = ["Quito", "Cayambe", "Mejía", "Pedro Moncayo", "Rumiñahui",
                    "San Miguel de los Bancos", "Pedro Vicente Maldonado",
                    "Puerto Quito"],
            // 20 · Santa Elena (3)
            [20] = ["Santa Elena", "La Libertad", "Salinas"],
            // 21 · Santo Domingo de los Tsáchilas (2). El brief original
            // repetía "La Concordia" aquí y en Esmeraldas (8): el mismo
            // cantón contado dos veces era el error que inflaba la semilla
            // a 222. Se queda en Santo Domingo porque es la provincia a la
            // que pertenece hoy: nació como cantón de Esmeraldas en 2007,
            // pero la consulta popular la trasladó aquí.
            [21] = ["Santo Domingo", "La Concordia"],
            // 22 · Sucumbíos (7)
            [22] = ["Lago Agrio", "Gonzalo Pizarro", "Putumayo", "Shushufindi",
                    "Sucumbíos", "Cascales", "Cuyabeno"],
            // 23 · Tungurahua (9)
            [23] = ["Ambato", "Baños de Agua Santa", "Cevallos", "Mocha", "Patate",
                    "Quero", "San Pedro de Pelileo", "Santiago de Píllaro", "Tisaleo"],
            // 24 · Zamora Chinchipe (9)
            [24] = ["Zamora", "Chinchipe", "Nangaritza", "Yacuambi", "Yantzaza",
                    "El Pangui", "Centinela del Cóndor", "Palanda", "Paquisha"],
        };

        var id = 1;
        return porProvincia
            .OrderBy(p => p.Key)
            .SelectMany(p => p.Value.Select(nombre => new Canton
            {
                Id = id++,
                Nombre = nombre,
                ProvinciaId = p.Key,
            }))
            .ToArray();
    }
}
