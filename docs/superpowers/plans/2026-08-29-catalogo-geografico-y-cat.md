# Catálogo geográfico y CAT gestionable — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que la cooperativa pueda dar de alta una provincia entera —con sus cantones, sus centros de acopio y sus comunidades— desde Administración, sin recompilar.

**Arquitectura:** Nacen tres catálogos gestionables (`Provincia` → `Canton` → `CentroAcopio`) y `Comunidad` cuelga de un cantón en vez de guardar su nombre como texto. El `enum CentroAcopio` desaparece y su código de tres letras pasa a ser la clave de una tabla, aprovechando que las cinco columnas que lo guardan ya se persisten como `string`. El trabajo va en cuatro fases sobre una rama: geografía, CAT, front, y provincia real en QR y PDFs.

**Stack:** .NET 8 · EF Core sobre PostgreSQL (Npgsql) · xUnit + Shouldly + Respawn contra Postgres real en Docker · React 19 + TypeScript + TanStack Query + Tailwind · QuestPDF.

**Spec:** [`docs/superpowers/specs/2026-08-29-catalogo-geografico-y-cat-design.md`](../specs/2026-08-29-catalogo-geografico-y-cat-design.md)

**Rama:** `feat/catalogo-geografico-y-cat` (ya creada, con el spec commiteado).

## Global Constraints

- **Las pruebas NO se corren con `dotnet test` en Windows.** Smart App Control bloquea el DLL desde OneDrive (`0x800711C7`). Siempre: `docker compose -f docker-compose.tests.yml run --rm tests`. Requiere Docker Desktop en marcha.
- **Repositorio de API:** `C:\Users\nicol\OneDrive\Documents\CoopagcuyApi`. **Repositorio de front:** `C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`. Son dos repos git distintos y cada uno lleva sus propios commits.
- **El código del CAT es `^[A-Z]{3}$`, único e inmutable.** Prefija el identificador de jaula (`PAT-20260615-001`).
- **Nada se borra, todo se desactiva.** Todas las entidades de catálogo llevan `Activa`/`Activo`. Las bajas con dependencias responden `409 Conflict` con `{ mensaje }`.
- **Una comunidad puede referenciar cualquier CAT activo**, de cualquier cantón y cualquier provincia. No hay validación de coherencia geográfica.
- **La procedencia del cuy sale de la comunidad de la productora, nunca del CAT.**
- **Escritura de catálogos:** `[Authorize(Roles = "AdminCooperativa,AdminTecnico")]`. Lectura: `[Authorize]` a secas.
- **Fechas:** el proyecto fija `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)`. No tocar.
- **Toda tabla de catálogo sembrada por migración debe entrar en `TablesToIgnore` de `BaseDatosFixture`**, o Respawn la vacía entre pruebas y la batería revienta en cascada.
- **Mensajes de usuario en español**, con tildes correctas. Los comentarios de código también: el repo lo hace así en todas partes.

---

# FASE 1 — Geografía

### Task 1: Entidades `Provincia` y `Canton`, y la semilla del Ecuador

**Files:**
- Create: `Features/Catalogos/Models/Provincia.cs`
- Create: `Features/Catalogos/Models/Canton.cs`
- Create: `Infrastructure/Data/Seed/GeografiaEcuador.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs` (añadir `DbSet`s y bloques `modelBuilder.Entity<>`)
- Modify: `tests/CoopagcuyApi.Tests/Infra/BaseDatosFixture.cs:44-56` (`TablesToIgnore`)
- Test: `tests/CoopagcuyApi.Tests/Integracion/CatalogoGeograficoTests.cs`

**Interfaces:**
- Consumes: nada (primera tarea).
- Produces: `Provincia { int Id, string Nombre, bool Activa, ICollection<Canton> Cantones }`; `Canton { int Id, string Nombre, int ProvinciaId, Provincia Provincia, bool Activo, ICollection<Comunidad> Comunidades }`; `GeografiaEcuador.Provincias` → `Provincia[]`; `GeografiaEcuador.Cantones` → `Canton[]`. `AppDbContext.Provincias`, `AppDbContext.Cantones`.
- **Ids fijos y estables:** provincias 1–24 en orden alfabético; cantones 1–221. Las tareas siguientes y las pruebas dependen de que Azuay sea la provincia 1 y de que Nabón, Pucará y Santa Isabel existan entre sus cantones.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/CatalogoGeograficoTests.cs`:

```csharp
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El catálogo geográfico llega sembrado por migración, no se da de alta a
/// mano. Estas pruebas verifican la semilla, no reglas de negocio: si se
/// caen, la migración no dejó la base como el resto del sistema espera.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CatalogoGeograficoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LaSemilla_traeLasVeinticuatroProvincias()
    {
        await using var db = api.NuevoDbContext();

        (await db.Provincias.CountAsync()).ShouldBe(24);
    }

    [Fact]
    public async Task LaSemilla_traeLosDoscientosVeintiunCantones()
    {
        await using var db = api.NuevoDbContext();

        (await db.Cantones.CountAsync()).ShouldBe(221);
    }

    [Fact]
    public async Task Azuay_traeLosCantonesDelPiloto()
    {
        await using var db = api.NuevoDbContext();

        var cantones = await db.Cantones
            .Where(c => c.Provincia.Nombre == "Azuay")
            .Select(c => c.Nombre)
            .ToListAsync();

        cantones.ShouldContain("Nabón");
        cantones.ShouldContain("Pucará");
        cantones.ShouldContain("Santa Isabel");
    }

    // Respawn trunca todo lo que no esté en TablesToIgnore. Si esta prueba
    // se cae, el catálogo se está vaciando entre pruebas y media batería va
    // a fallar por claves foráneas, no por lo que cada prueba verifica.
    [Fact]
    public async Task ElCatalogo_sobreviveALaLimpiezaEntrePruebas()
    {
        await api.LimpiarAsync();

        await using var db = api.NuevoDbContext();

        (await db.Provincias.AnyAsync()).ShouldBeTrue();
        (await db.Cantones.AnyAsync()).ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Correr la prueba y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CatalogoGeograficoTests"
```

Esperado: FALLA al compilar — `'AppDbContext' no contiene una definición para 'Provincias'`.

- [ ] **Step 3: Crear las entidades**

`Features/Catalogos/Models/Provincia.cs`:

```csharp
namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// División política de primer nivel. Existe para que la organización pueda
/// crecer fuera de Azuay: antes la provincia estaba escrita a mano en la
/// página pública del QR y en la guía de movilización.
/// </summary>
public class Provincia
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;

    public ICollection<Canton> Cantones { get; set; } = [];
}
```

`Features/Catalogos/Models/Canton.cs`:

```csharp
namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// Cantón al que pertenece una comunidad. Antes era un string libre dentro
/// de Comunidad, y con texto libre "Nabón" y "Nabon" eran dos cantones
/// distintos — la misma cicatriz que ya obligó a sacar el cantón de
/// Productora y llevarlo al catálogo de comunidades.
/// </summary>
public class Canton
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    public int ProvinciaId { get; set; }
    public Provincia Provincia { get; set; } = null!;

    public bool Activo { get; set; } = true;
}
```

- [ ] **Step 4: Escribir la semilla**

Crear `Infrastructure/Data/Seed/GeografiaEcuador.cs`. Vive en su propio archivo y no dentro de `AppDbContext` porque son 245 filas: metidas en el `OnModelCreating` lo volverían ilegible.

Los Ids son **fijos y ordenados**: provincias 1–24 alfabéticamente, cantones 1–221 agrupados por provincia. Que sean estables importa porque `HasData` los usa como clave y cambiarlos después generaría una migración que borra y reinserta todo el catálogo.

```csharp
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
            // 8 · Esmeraldas (7). La Concordia NO está aquí: se creó como
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
            // 21 · Santo Domingo de los Tsáchilas (2)
            [21] = ["Santo Domingo", "La Concordia"],   // ver nota en Esmeraldas
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
```

> **Nota para quien implemente:** el total debe dar **221**. La prueba del paso 1 lo comprueba. Si sale otro número, contar por provincia contra la lista de arriba antes de tocar la prueba — la prueba tiene razón, la lista no.

- [ ] **Step 5: Registrar en `AppDbContext`**

Añadir los `DbSet` junto a los que ya existen:

```csharp
public DbSet<Provincia> Provincias => Set<Provincia>();
public DbSet<Canton> Cantones => Set<Canton>();
```

Y dentro de `OnModelCreating`, **antes** del bloque de `Comunidad` (el orden importa para que EF ordene bien las migraciones):

```csharp
// Provincia — catálogo geográfico de primer nivel
modelBuilder.Entity<Provincia>(e =>
{
    e.HasKey(p => p.Id);
    e.HasIndex(p => p.Nombre).IsUnique();
    e.Property(p => p.Nombre).HasMaxLength(80).IsRequired();

    e.HasData(GeografiaEcuador.Provincias);
});

// Cantón — cuelga de una provincia. El nombre solo es único DENTRO de su
// provincia: hay cantones homónimos en el Ecuador ("Bolívar" está en Carchi
// y en Manabí; "Olmedo" en Loja y en Manabí).
modelBuilder.Entity<Canton>(e =>
{
    e.HasKey(c => c.Id);
    e.HasIndex(c => new { c.ProvinciaId, c.Nombre }).IsUnique();
    e.Property(c => c.Nombre).HasMaxLength(80).IsRequired();

    e.HasOne(c => c.Provincia)
        .WithMany(p => p.Cantones)
        .HasForeignKey(c => c.ProvinciaId)
        .OnDelete(DeleteBehavior.Restrict);

    e.HasData(GeografiaEcuador.Cantones);
});
```

Y el `using` correspondiente arriba del archivo:

```csharp
using CoopagcuyApi.Infrastructure.Data.Seed;
```

- [ ] **Step 6: Añadir las tablas a `TablesToIgnore` de Respawn**

En `tests/CoopagcuyApi.Tests/Infra/BaseDatosFixture.cs`, dentro de `TablesToIgnore`, junto a `Comunidades`:

```csharp
                new Table("public", "Comunidades"),
                // Misma razón que Comunidades: Provincias y Cantones son
                // catálogo sembrado por migración. Truncarlos dejaría a
                // Comunidad sin el cantón al que apunta y toda la batería
                // caería por clave foránea, no por lo que cada prueba mira.
                new Table("public", "Provincias"),
                new Table("public", "Cantones")
```

- [ ] **Step 7: Generar la migración**

La migración necesita una cadena de conexión: `AppDbContextFactory` la toma de user-secrets o de `ConnectionStrings__NeonDb`. Basta con que apunte a **cualquier** Postgres alcanzable — el generador solo lee el modelo, no escribe.

```bash
dotnet ef migrations add CatalogoGeografico --project CoopagcuyApi.csproj
```

Revisar el archivo generado en `Infrastructure/Data/Migrations/`: debe crear `Provincias` y `Cantones` con sus `InsertData`, y **no debe tocar ninguna otra tabla**. Si aparecen `AlterColumn` sobre columnas de fecha, falta el switch de `EnableLegacyTimestampBehavior` — no editar la migración, revisar `AppDbContextFactory`.

- [ ] **Step 8: Correr la prueba y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CatalogoGeograficoTests"
```

Esperado: 4 pruebas en verde.

- [ ] **Step 9: Correr la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: todo verde. Esta tarea no cambia comportamiento existente, así que cualquier rojo es una regresión de la semilla o de Respawn.

- [ ] **Step 10: Commit**

```bash
git add Features/Catalogos/Models/Provincia.cs Features/Catalogos/Models/Canton.cs Infrastructure/Data/Seed/GeografiaEcuador.cs Infrastructure/Data/AppDbContext.cs Infrastructure/Data/Migrations tests/CoopagcuyApi.Tests/Infra/BaseDatosFixture.cs tests/CoopagcuyApi.Tests/Integracion/CatalogoGeograficoTests.cs
git commit -m "feat: catálogo de provincias y cantones del Ecuador"
```

---

### Task 2: `Comunidad` cuelga de un cantón

**Files:**
- Modify: `Features/Catalogos/Models/Comunidad.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs` (bloque `Comunidad`, `~:457-474`)
- Create: `scripts/verificar-cantones.sql`
- Modify: la migración generada en este paso (se le añade SQL de backfill a mano)
- Test: `tests/CoopagcuyApi.Tests/Integracion/CatalogoGeograficoTests.cs` (añadir casos)

**Interfaces:**
- Consumes: `Provincia`, `Canton`, `GeografiaEcuador` de la Task 1.
- Produces: `Comunidad { int Id, string Nombre, int CantonId, Canton Canton, CentroAcopio CatReferencia, bool Activa, decimal? Latitud, decimal? Longitud, int? AltitudMinM, int? AltitudMaxM }`. **`Comunidad.Canton` deja de ser `string`**: cualquier código que lo leyera como texto ahora escribe `comunidad.Canton.Nombre`.
- Ids de cantón que usan las comunidades sembradas: Nabón = 4, Pucará = 6, Santa Isabel = 8 (posiciones 4, 6 y 8 de la lista de Azuay en `GeografiaEcuador`).

- [ ] **Step 1: Escribir las pruebas que fallan**

Añadir a `CatalogoGeograficoTests.cs`:

```csharp
    [Fact]
    public async Task LasComunidadesSembradas_apuntanASuCanton()
    {
        await using var db = api.NuevoDbContext();

        var comunidades = await db.Comunidades
            .Include(c => c.Canton)
            .ThenInclude(c => c.Provincia)
            .OrderBy(c => c.Id)
            .ToListAsync();

        comunidades.Select(c => c.Canton.Nombre).ShouldBe(
        [
            "Pucará",        // 1 Patococha
            "Nabón",         // 2 Las Nieves
            "Santa Isabel",  // 3 Huertas
            "Nabón",         // 4 Nabón / El Progreso
            "Pucará",        // 5 Pelincay
        ]);

        comunidades.ShouldAllBe(c => c.Canton.Provincia.Nombre == "Azuay");
    }

    // El cruce del backfill ignora tildes y mayúsculas. No es un detalle: en
    // la base real hay una comunidad cuyo cantón se escribió "Nabon" desde
    // Administración, y con comparación cruda se habría quedado sin cantón.
    //
    // La columna "Canton" ya no existe después de migrar, así que la prueba
    // ejecuta el MISMO SQL del backfill sobre un valor de entrada suelto. Es
    // la única forma de ejercitar esa lógica una vez aplicada la migración;
    // si el SQL de aquí y el de la migración divergen, esta prueba deja de
    // proteger nada — mantenerlos idénticos.
    [Theory]
    [InlineData("Nabon", "Nabón")]     // el caso real de la base
    [InlineData("NABÓN", "Nabón")]     // mayúsculas
    [InlineData("  Pucara  ", "Pucará")] // espacios y tilde
    public async Task ElCruceDeCantones_ignoraTildesYMayusculas(
        string escritoAMano, string esperado)
    {
        await using var db = api.NuevoDbContext();

        var id = await db.Database
            .SqlQuery<int>($"""
                SELECT ct."Id" AS "Value"
                FROM "Cantones" ct
                WHERE translate(lower(trim({escritoAMano})),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                    = translate(lower(trim(ct."Nombre")),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                  AND ct."ProvinciaId" = 1
                """)
            .SingleAsync();

        var canton = await db.Cantones.FindAsync(id);

        canton!.Nombre.ShouldBe(esperado);
    }

    // Dos provincias distintas pueden tener una comunidad con el mismo
    // nombre. Antes el índice único era global y eso habría bloqueado el alta.
    [Fact]
    public async Task DosComunidadesHomonimas_puedenCoexistirEnCantonesDistintos()
    {
        await using var db = api.NuevoDbContext();

        db.Comunidades.Add(new Comunidad
        {
            Nombre = "San José", CantonId = 1, CatReferencia = CentroAcopio.PAT,
        });
        db.Comunidades.Add(new Comunidad
        {
            Nombre = "San José", CantonId = 2, CatReferencia = CentroAcopio.PAT,
        });

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }
```

Añadir los `using` que la clase necesita:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Catalogos.Models;
```

- [ ] **Step 2: Correr y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CatalogoGeograficoTests"
```

Esperado: FALLA al compilar — `Comunidad.Canton` es `string` y no tiene `.Nombre`.

- [ ] **Step 3: Cambiar la entidad `Comunidad`**

Reemplazar `Features/Catalogos/Models/Comunidad.cs` entero:

```csharp
using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// Catálogo gestionable de comunidades — RF-102 / RF-506.
///
/// La comunidad cuelga de un cantón del catálogo, y el cantón de una
/// provincia: antes el cantón era texto libre aquí dentro y "Nabón" y
/// "Nabon" acababan siendo dos cantones distintos.
///
/// El CAT de referencia NO está restringido por geografía: una comunidad
/// entrega en el centro que le queda más cerca, aunque esté en otra
/// provincia. La procedencia del cuy sale de la comunidad, no del CAT.
/// </summary>
public class Comunidad
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    public int CantonId { get; set; }
    public Canton Canton { get; set; } = null!;

    public CentroAcopio CatReferencia { get; set; }
    public bool Activa { get; set; } = true;

    // Ubicación en el mapa público. Nullable porque una comunidad dada de
    // alta desde Administración nace sin coordenadas y la ficha del QR
    // tiene que seguir funcionando sin ellas.
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }

    // La altitud es un rango, no un punto: una comunidad ocupa una ladera.
    // Cuando la cooperativa da una sola cifra, mínimo y máximo coinciden.
    public int? AltitudMinM { get; set; }
    public int? AltitudMaxM { get; set; }
}
```

- [ ] **Step 4: Actualizar el mapeo y la semilla en `AppDbContext`**

Reemplazar el bloque `modelBuilder.Entity<Comunidad>`:

```csharp
// Comunidad — catálogo gestionable RF-102 / RF-506
modelBuilder.Entity<Comunidad>(e =>
{
    e.HasKey(c => c.Id);
    // Único POR CANTÓN, no global: "San José" existe en varias provincias
    // del Ecuador y un índice global bloquearía el alta de la segunda.
    e.HasIndex(c => new { c.CantonId, c.Nombre }).IsUnique();
    e.Property(c => c.Nombre).HasMaxLength(100).IsRequired();
    e.Property(c => c.CatReferencia).HasConversion<string>();
    e.Property(c => c.Latitud).HasPrecision(9, 6);
    e.Property(c => c.Longitud).HasPrecision(9, 6);

    e.HasOne(c => c.Canton)
        .WithMany()
        .HasForeignKey(c => c.CantonId)
        .OnDelete(DeleteBehavior.Restrict);

    // Comunidades relevadas en el diagnóstico PRODUCTO1. Los CantonId son
    // los de GeografiaEcuador: 4 Nabón, 6 Pucará, 8 Santa Isabel (Azuay).
    // Las coordenadas venían del front (coordenadas.ts) y suben aquí.
    e.HasData(
        new Comunidad { Id = 1, Nombre = "Patococha", CantonId = 6, CatReferencia = CentroAcopio.PAT, Latitud = -3.284722m, Longitud = -79.400833m, AltitudMinM = 3100, AltitudMaxM = 3300 },
        new Comunidad { Id = 2, Nombre = "Las Nieves", CantonId = 4, CatReferencia = CentroAcopio.NIE, Latitud = -3.417500m, Longitud = -79.166944m, AltitudMinM = 3200, AltitudMaxM = 3400 },
        new Comunidad { Id = 3, Nombre = "Huertas", CantonId = 8, CatReferencia = CentroAcopio.HUE, Latitud = -3.276111m, Longitud = -79.243889m, AltitudMinM = 2600, AltitudMaxM = 2900 },
        new Comunidad { Id = 4, Nombre = "Nabón / El Progreso", CantonId = 4, CatReferencia = CentroAcopio.NAB, Latitud = -3.339722m, Longitud = -79.060556m, AltitudMinM = 2700, AltitudMaxM = 2900 },
        new Comunidad { Id = 5, Nombre = "Pelincay", CantonId = 6, CatReferencia = CentroAcopio.PEL, Latitud = -3.243611m, Longitud = -79.386944m, AltitudMinM = 2900, AltitudMaxM = 3100 }
    );
});
```

> **Los valores de latitud, longitud y altitud se copian de `src/domain/comunidades/coordenadas.ts`** del repo del front, que es hoy la única fuente. Los de arriba son un punto de partida: **abrir ese archivo y transcribir los reales antes de correr nada**, porque son el dato que dibuja el mapa público y una cifra inventada movería un pin sin que ninguna prueba lo note.

- [ ] **Step 5: Generar la migración**

```bash
dotnet ef migrations add ComunidadCuelgaDeCanton --project CoopagcuyApi.csproj
```

- [ ] **Step 6: Añadir el backfill a mano en la migración**

EF genera `AddColumn CantonId` con default 0 y luego `DropColumn Canton`. **Entre esas dos operaciones hay que meter el backfill**, o las comunidades dadas de alta desde Administración pierden su cantón.

Abrir la migración recién creada y, en `Up()`, insertar justo después del `AddColumn` de `CantonId` y **antes** del `DropColumn` de `Canton`:

```csharp
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
    WHERE translate(lower(trim(c."Canton")),
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
```

En `Down()`, el `DropColumn`/`AddColumn` que EF genera no puede recuperar el texto original. Añadir al final de `Down()`:

```csharp
// La vuelta atrás repuebla el texto desde el catálogo. No es idéntico a lo
// que había —"Nabon" vuelve como "Nabón"—, y eso es deliberado: devolver el
// error de digitación sería devolver el problema.
migrationBuilder.Sql("""
    UPDATE "Comunidades" c
    SET "Canton" = ct."Nombre"
    FROM "Cantones" ct
    WHERE ct."Id" = c."CantonId";
    """);
```

- [ ] **Step 7: Escribir el script de verificación previa**

Crear `scripts/verificar-cantones.sql`. Se corre **contra la base real antes de desplegar**, para saber si la migración va a detenerse antes de que lo haga en producción:

```sql
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
```

- [ ] **Step 8: Arreglar lo que dejó de compilar**

Quitar `Comunidad.Canton` como `string` rompe todo lo que lo leía. Compilar y arreglar cada sitio cambiando `.Comunidad.Canton` por `.Comunidad.Canton.Nombre`, y añadiendo el `ThenInclude` donde haga falta:

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet build tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj
```

Sitios conocidos (verificar que no haya más siguiendo los errores del compilador):

| Archivo | Qué cambia |
|---|---|
| `Features/Productoras/Services/ProductoraService.cs` | proyección del DTO: `p.Comunidad.Canton` → `p.Comunidad.Canton.Nombre` |
| `Features/Catalogos/Services/CatalogosService.cs` | `c.Canton` → `c.Canton.Nombre` en la proyección, y `dto.Canton` deja de asignarse (se hace en la Task 3) |
| `Features/QR/Services/QRService.cs:~251` | `s.Lote.Productora?.Comunidad.Canton` → `.Canton.Nombre` |
| `Features/Recepcion/Services/GuiaMovilizacionService.cs:129` | `productora.Comunidad.Canton` → `.Canton.Nombre`; añadir `.ThenInclude(c => c.Canton)` al `Include` de la línea 54 |
| `Features/Reportes/Services/ReportesService.cs` | los sitios que el compilador señale |

En las consultas EF que ya hacen `.Include(p => p.Comunidad)`, encadenar `.ThenInclude(c => c.Canton)`. Sin eso, `Canton` llega `null` y revienta en ejecución, no en compilación.

- [ ] **Step 9: Correr las pruebas del catálogo**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CatalogoGeograficoTests"
```

Esperado: 9 pruebas en verde (la `[Theory]` cuenta tres).

- [ ] **Step 10: Correr la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: todo verde. Prestar atención a `AlcanceProductorasTests` y `GuiaMovilizacionTests`, que leen el cantón.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: la comunidad cuelga de un cantón del catálogo"
```

---

### Task 3: API de provincias y cantones

**Files:**
- Create: `Features/Catalogos/Services/GeografiaService.cs`
- Modify: `Features/Catalogos/DTOs/CatalogosDtos.cs`
- Modify: `Features/Catalogos/Controllers/CatalogosController.cs`
- Modify: `Program.cs` (registro del servicio en DI)
- Test: `tests/CoopagcuyApi.Tests/Integracion/ApiGeografiaTests.cs`

**Interfaces:**
- Consumes: `Provincia`, `Canton` (Task 1), `Comunidad.CantonId` (Task 2).
- Produces:
  - `IGeografiaService` con `ListarProvinciasAsync(bool incluirInactivas)`, `CrearProvinciaAsync(GuardarProvinciaDto)`, `ActualizarProvinciaAsync(int, GuardarProvinciaDto)`, `CambiarEstadoProvinciaAsync(int, bool)`, y los cuatro equivalentes de cantón con `ListarCantonesAsync(int? provinciaId, bool incluirInactivos)`.
  - `record ProvinciaDto(int Id, string Nombre, bool Activa, int TotalCantones)`
  - `record CantonDto(int Id, string Nombre, int ProvinciaId, string Provincia, bool Activo, int TotalComunidades)`
  - `class GuardarProvinciaDto { string Nombre }`
  - `class GuardarCantonDto { string Nombre; int ProvinciaId }`
- Las bajas con dependencias lanzan `InvalidOperationException`; el controlador la traduce a `409 Conflict` con `{ mensaje }`, igual que ya hace `CrearComunidad`.

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/ApiGeografiaTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Alta y baja de provincias y cantones. La regla que gobierna todo: nada se
/// borra, se desactiva — y no se desactiva lo que todavía sostiene a otros.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ApiGeografiaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record ProvinciaLeida(int Id, string Nombre, bool Activa, int TotalCantones);
    private sealed record CantonLeido(
        int Id, string Nombre, int ProvinciaId, string Provincia,
        bool Activo, int TotalComunidades);

    [Fact]
    public async Task CualquierAutenticado_listaLasProvincias()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetFromJsonAsync<List<ProvinciaLeida>>("/api/catalogos/provincias");

        respuesta!.ShouldContain(p => p.Nombre == "Azuay");
    }

    [Fact]
    public async Task Admin_creaUnaProvincia()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/provincias", new { nombre = "Nueva Provincia" });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OperadorCat_noPuedeCrearProvincias()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/catalogos/provincias", new { nombre = "Prohibida" });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Provincia_conNombreRepetido_esRechazada()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/provincias", new { nombre = "Azuay" });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Provincia_conCantonesActivos_noSeDesactiva()
    {
        // Azuay es la provincia 1 y trae 15 cantones sembrados
        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/provincias/1/estado", new { activa = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Canton_conComunidadesActivas_noSeDesactiva()
    {
        // Cantón 6 = Pucará, sostiene a Patococha y Pelincay
        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/cantones/6/estado", new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Canton_sinComunidades_seDesactiva()
    {
        // Cantón 1 = Cuenca, sembrado pero sin comunidades del piloto
        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/cantones/1/estado", new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LosCantones_seFiltranPorProvincia()
    {
        var cantones = await api.ComoAdmin()
            .GetFromJsonAsync<List<CantonLeido>>("/api/catalogos/cantones?provinciaId=1");

        cantones!.Count.ShouldBe(15);
        cantones.ShouldAllBe(c => c.Provincia == "Azuay");
    }

    [Fact]
    public async Task DosCantonesHomonimos_enProvinciasDistintas_seAceptan()
    {
        // "Bolívar" ya existe en Carchi (4) y en Manabí (14) desde la semilla
        var cantones = await api.ComoAdmin()
            .GetFromJsonAsync<List<CantonLeido>>("/api/catalogos/cantones");

        cantones!.Count(c => c.Nombre == "Bolívar").ShouldBe(2);
    }

    [Fact]
    public async Task Canton_repetidoDentroDeSuProvincia_esRechazado()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/cantones",
                new { nombre = "Nabón", provinciaId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ApiGeografiaTests"
```

Esperado: FALLA — todas devuelven `404 Not Found`.

- [ ] **Step 3: Añadir los DTOs**

Añadir a `Features/Catalogos/DTOs/CatalogosDtos.cs`:

```csharp
// ── Geografía ────────────────────────────────────────────────────────

public class GuardarProvinciaDto
{
    public string Nombre { get; set; } = string.Empty;
}

// TotalCantones acompaña a la provincia para que Administración pueda
// explicar por qué una baja fue rechazada sin pedir otra consulta.
public record ProvinciaDto(int Id, string Nombre, bool Activa, int TotalCantones);

public class GuardarCantonDto
{
    public string Nombre { get; set; } = string.Empty;
    public int ProvinciaId { get; set; }
}

public record CantonDto(
    int Id, string Nombre, int ProvinciaId, string Provincia,
    bool Activo, int TotalComunidades);
```

- [ ] **Step 4: Escribir el servicio**

Crear `Features/Catalogos/Services/GeografiaService.cs`:

```csharp
using CoopagcuyApi.Features.Catalogos.DTOs;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Catalogos.Services;

public interface IGeografiaService
{
    Task<IEnumerable<ProvinciaDto>> ListarProvinciasAsync(bool incluirInactivas);
    Task<ProvinciaDto> CrearProvinciaAsync(GuardarProvinciaDto dto);
    Task<bool> ActualizarProvinciaAsync(int id, GuardarProvinciaDto dto);
    Task<bool> CambiarEstadoProvinciaAsync(int id, bool activa);

    Task<IEnumerable<CantonDto>> ListarCantonesAsync(int? provinciaId, bool incluirInactivos);
    Task<CantonDto> CrearCantonAsync(GuardarCantonDto dto);
    Task<bool> ActualizarCantonAsync(int id, GuardarCantonDto dto);
    Task<bool> CambiarEstadoCantonAsync(int id, bool activo);
}

/// <summary>
/// Alta y baja del catálogo geográfico. Nada se borra: se desactiva, y no se
/// desactiva lo que todavía sostiene a otros. Un cantón dado de baja con
/// comunidades vivas dejaría fichas públicas sin poder decir de dónde es el cuy.
/// </summary>
public class GeografiaService(AppDbContext db) : IGeografiaService
{
    // ── Provincias ───────────────────────────────────────────────────

    public async Task<IEnumerable<ProvinciaDto>> ListarProvinciasAsync(bool incluirInactivas)
    {
        var query = db.Provincias.AsQueryable();
        if (!incluirInactivas) query = query.Where(p => p.Activa);

        return await query
            .OrderBy(p => p.Nombre)
            .Select(p => new ProvinciaDto(
                p.Id, p.Nombre, p.Activa,
                p.Cantones.Count(c => c.Activo)))
            .ToListAsync();
    }

    public async Task<ProvinciaDto> CrearProvinciaAsync(GuardarProvinciaDto dto)
    {
        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre de la provincia es obligatorio.");

        if (await db.Provincias.AnyAsync(p => p.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException($"Ya existe la provincia '{nombre}'.");

        var provincia = new Provincia { Nombre = nombre };
        db.Provincias.Add(provincia);
        await db.SaveChangesAsync();

        return new ProvinciaDto(provincia.Id, provincia.Nombre, provincia.Activa, 0);
    }

    public async Task<bool> ActualizarProvinciaAsync(int id, GuardarProvinciaDto dto)
    {
        var provincia = await db.Provincias.FindAsync(id);
        if (provincia is null) return false;

        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre de la provincia es obligatorio.");

        if (await db.Provincias.AnyAsync(p =>
                p.Id != id && p.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException($"Ya existe la provincia '{nombre}'.");

        provincia.Nombre = nombre;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoProvinciaAsync(int id, bool activa)
    {
        var provincia = await db.Provincias.FindAsync(id);
        if (provincia is null) return false;

        if (!activa)
        {
            var cantonesVivos = await db.Cantones
                .CountAsync(c => c.ProvinciaId == id && c.Activo);

            if (cantonesVivos > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{provincia.Nombre}': todavía tiene " +
                    $"{cantonesVivos} cantón(es) activo(s). Desactívalos primero.");
        }

        provincia.Activa = activa;
        await db.SaveChangesAsync();
        return true;
    }

    // ── Cantones ─────────────────────────────────────────────────────

    public async Task<IEnumerable<CantonDto>> ListarCantonesAsync(
        int? provinciaId, bool incluirInactivos)
    {
        var query = db.Cantones.AsQueryable();
        if (provinciaId is int id) query = query.Where(c => c.ProvinciaId == id);
        if (!incluirInactivos) query = query.Where(c => c.Activo);

        return await query
            .OrderBy(c => c.Provincia.Nombre).ThenBy(c => c.Nombre)
            .Select(c => new CantonDto(
                c.Id, c.Nombre, c.ProvinciaId, c.Provincia.Nombre, c.Activo,
                db.Comunidades.Count(x => x.CantonId == c.Id && x.Activa)))
            .ToListAsync();
    }

    public async Task<CantonDto> CrearCantonAsync(GuardarCantonDto dto)
    {
        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del cantón es obligatorio.");

        var provincia = await db.Provincias.FindAsync(dto.ProvinciaId)
            ?? throw new InvalidOperationException("La provincia indicada no existe.");

        // Único DENTRO de la provincia: hay cantones homónimos en el Ecuador
        // ("Bolívar" está en Carchi y en Manabí).
        if (await db.Cantones.AnyAsync(c =>
                c.ProvinciaId == dto.ProvinciaId && c.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException(
                $"Ya existe el cantón '{nombre}' en {provincia.Nombre}.");

        var canton = new Canton { Nombre = nombre, ProvinciaId = dto.ProvinciaId };
        db.Cantones.Add(canton);
        await db.SaveChangesAsync();

        return new CantonDto(canton.Id, canton.Nombre, provincia.Id, provincia.Nombre,
            canton.Activo, 0);
    }

    public async Task<bool> ActualizarCantonAsync(int id, GuardarCantonDto dto)
    {
        var canton = await db.Cantones.FindAsync(id);
        if (canton is null) return false;

        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del cantón es obligatorio.");

        if (!await db.Provincias.AnyAsync(p => p.Id == dto.ProvinciaId))
            throw new InvalidOperationException("La provincia indicada no existe.");

        if (await db.Cantones.AnyAsync(c =>
                c.Id != id && c.ProvinciaId == dto.ProvinciaId
                && c.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException(
                $"Ya existe otro cantón '{nombre}' en esa provincia.");

        canton.Nombre = nombre;
        canton.ProvinciaId = dto.ProvinciaId;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoCantonAsync(int id, bool activo)
    {
        var canton = await db.Cantones.FindAsync(id);
        if (canton is null) return false;

        if (!activo)
        {
            var comunidadesVivas = await db.Comunidades
                .CountAsync(c => c.CantonId == id && c.Activa);

            if (comunidadesVivas > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{canton.Nombre}': todavía tiene " +
                    $"{comunidadesVivas} comunidad(es) activa(s). Desactívalas primero.");
        }

        canton.Activo = activo;
        await db.SaveChangesAsync();
        return true;
    }
}
```

- [ ] **Step 5: Añadir los endpoints al controlador**

Añadir a `Features/Catalogos/Controllers/CatalogosController.cs`. El constructor primario pasa a recibir los dos servicios:

```csharp
public class CatalogosController(
    ICatalogosService service,
    IGeografiaService geografia) : ControllerBase
```

Y los endpoints, antes del bloque de comunidades:

```csharp
    // ── Provincias ───────────────────────────────────────────────────

    [HttpGet("provincias")]
    public async Task<IActionResult> ListarProvincias(
        [FromQuery] bool incluirInactivas = false) =>
        Ok(await geografia.ListarProvinciasAsync(incluirInactivas));

    [HttpPost("provincias")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CrearProvincia([FromBody] GuardarProvinciaDto dto)
    {
        try
        {
            var result = await geografia.CrearProvinciaAsync(dto);
            return CreatedAtAction(nameof(ListarProvincias), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("provincias/{id:int}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ActualizarProvincia(
        int id, [FromBody] GuardarProvinciaDto dto)
    {
        try
        {
            return await geografia.ActualizarProvinciaAsync(id, dto)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("provincias/{id:int}/estado")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CambiarEstadoProvincia(
        int id, [FromBody] CambiarEstadoProvinciaDto dto)
    {
        try
        {
            return await geografia.CambiarEstadoProvinciaAsync(id, dto.Activa)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    // ── Cantones ─────────────────────────────────────────────────────

    [HttpGet("cantones")]
    public async Task<IActionResult> ListarCantones(
        [FromQuery] int? provinciaId = null,
        [FromQuery] bool incluirInactivos = false) =>
        Ok(await geografia.ListarCantonesAsync(provinciaId, incluirInactivos));

    [HttpPost("cantones")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CrearCanton([FromBody] GuardarCantonDto dto)
    {
        try
        {
            var result = await geografia.CrearCantonAsync(dto);
            return CreatedAtAction(nameof(ListarCantones), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("cantones/{id:int}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ActualizarCanton(
        int id, [FromBody] GuardarCantonDto dto)
    {
        try
        {
            return await geografia.ActualizarCantonAsync(id, dto)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("cantones/{id:int}/estado")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CambiarEstadoCanton(
        int id, [FromBody] CambiarEstadoCantonDto dto)
    {
        try
        {
            return await geografia.CambiarEstadoCantonAsync(id, dto.Activo)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
```

Y al final del archivo, junto a `CambiarEstadoComunidadDto`:

```csharp
public record CambiarEstadoProvinciaDto(bool Activa);
public record CambiarEstadoCantonDto(bool Activo);
```

- [ ] **Step 6: Registrar el servicio en DI**

En `Program.cs`, junto al registro de `ICatalogosService` (buscar `AddScoped<ICatalogosService`):

```csharp
builder.Services.AddScoped<IGeografiaService, GeografiaService>();
```

- [ ] **Step 7: Correr las pruebas y verificar que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ApiGeografiaTests"
```

Esperado: 10 pruebas en verde.

- [ ] **Step 8: Correr la batería completa y commitear**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

```bash
git add -A
git commit -m "feat: alta y baja de provincias y cantones desde Administración"
```

---

# FASE 2 — CAT gestionable

### Task 4: El `enum CentroAcopio` pasa a ser `string`

Esta tarea **no cambia comportamiento**: al terminar, el sistema hace exactamente lo mismo que antes. Es el refactor mecánico que habilita la Task 5, y va solo en su commit para que se pueda revisar sin ruido.

**Files:**
- Modify: `Common/Enums.cs` (borrar el enum)
- Modify: `Common/Auth/Usuario.cs:22`, `Common/Auth/UsuarioDtos.cs:14,23`, `Common/Auth/UsuarioService.cs`
- Modify: `Features/Catalogos/Models/Comunidad.cs`, `Features/Catalogos/DTOs/CatalogosDtos.cs`, `Features/Catalogos/Services/CatalogosService.cs`
- Modify: `Features/Productoras/Models/Productora.cs:19`, `Features/Productoras/Models/Lote.cs:27`, y sus DTOs, servicios y controlador
- Modify: `Features/Recepcion/Models/EntregaPendienteVinculacion.cs` y todo `Features/Recepcion/`
- Modify: `Features/Pagos/`, `Features/Faenamiento/`, `Features/QR/`, `Features/Reportes/`
- Modify: `Infrastructure/Data/AppDbContext.cs` (5 mapeos)
- Modify: `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs` y los 25 archivos de `tests/CoopagcuyApi.Tests/Integracion/`
- Test: no se añaden pruebas nuevas. Las ~200 existentes son la red.

**Interfaces:**
- Consumes: nada nuevo.
- Produces: el código del CAT viaja como `string` en todo el sistema. `Usuario.CatAsignado: string?`, `Productora.CatAsignado: string`, `Lote.CentroAcopio: string`, `EntregaPendienteVinculacion.CentroAcopio: string`, `Comunidad.CatReferencia: string`. Todas las firmas `CentroAcopio? filtroCat` pasan a `string? filtroCat`.
- `CatalogosService.NombresCat` sigue siendo el diccionario en memoria hasta la Task 5; su clave pasa de `CentroAcopio` a `string`.

- [ ] **Step 1: Correr la batería y guardar el resultado base**

Antes de tocar nada, saber de qué verde se parte:

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: todo verde. Anotar el número de pruebas — al final de la tarea debe ser **el mismo**.

- [ ] **Step 2: Borrar el enum**

En `Common/Enums.cs`, borrar el bloque completo:

```csharp
public enum CentroAcopio
{
    PAT, // Patococha
    NIE, // Las Nieves
    HUE, // Huertas
    NAB, // Nabón/El Progreso
    PEL  // Pelincay
}
```

y dejar en su lugar el comentario que explica adónde se fue:

```csharp
// El centro de acopio ERA un enum de cinco valores. Dejó de serlo cuando la
// organización necesitó crear centros nuevos sin recompilar: ahora es un
// catálogo (Features/Catalogos/Models/CentroAcopio.cs) y su código de tres
// letras viaja como string. Las columnas de base no cambiaron: ya se
// persistían con HasConversion<string>().
```

- [ ] **Step 3: Cambiar el tipo en las cinco entidades**

| Archivo | Antes | Después |
|---|---|---|
| `Common/Auth/Usuario.cs:22` | `public CentroAcopio? CatAsignado` | `public string? CatAsignado` |
| `Features/Productoras/Models/Productora.cs:19` | `public CentroAcopio CatAsignado` | `public string CatAsignado = string.Empty` |
| `Features/Productoras/Models/Lote.cs:27` | `public CentroAcopio CentroAcopio` | `public string CentroAcopio = string.Empty` |
| `Features/Recepcion/Models/EntregaPendienteVinculacion.cs` | `public CentroAcopio CentroAcopio` | `public string CentroAcopio = string.Empty` |
| `Features/Catalogos/Models/Comunidad.cs` | `public CentroAcopio CatReferencia` | `public string CatReferencia = string.Empty` |

- [ ] **Step 4: Cambiar los cinco mapeos de `AppDbContext`**

Reemplazar cada `HasConversion<string>()` de esas columnas por el largo máximo. La columna en base **no cambia de tipo** (sigue siendo texto), así que esto no genera migración de datos:

```csharp
e.Property(p => p.CatAsignado).HasMaxLength(3);      // Productora, línea ~52
e.Property(l => l.CentroAcopio).HasMaxLength(3);     // Lote, línea ~79
e.Property(u => u.CatAsignado).HasMaxLength(3);      // Usuario, línea ~192
e.Property(v => v.CentroAcopio).HasMaxLength(3);     // EntregaPendiente, ~412
e.Property(c => c.CatReferencia).HasMaxLength(3);    // Comunidad, ~464
```

Y en el `HasData` de `Comunidad`, los cinco `CentroAcopio.XXX` pasan a `"XXX"`:

```csharp
new Comunidad { Id = 1, Nombre = "Patococha", CantonId = 6, CatReferencia = "PAT", ... },
new Comunidad { Id = 2, Nombre = "Las Nieves", CantonId = 4, CatReferencia = "NIE", ... },
new Comunidad { Id = 3, Nombre = "Huertas", CantonId = 8, CatReferencia = "HUE", ... },
new Comunidad { Id = 4, Nombre = "Nabón / El Progreso", CantonId = 4, CatReferencia = "NAB", ... },
new Comunidad { Id = 5, Nombre = "Pelincay", CantonId = 6, CatReferencia = "PEL", ... }
```

- [ ] **Step 5: Cambiar las firmas de servicio y arreglar lo que rompa**

Compilar y seguir los errores. Los patrones son cuatro y siempre los mismos:

```csharp
// 1 · Firmas: CentroAcopio? filtroCat  →  string? filtroCat
Task<PagoResponseDto> RegistrarAsync(RegistrarPagoDto dto, string? filtroCat);

// 2 · Pattern matching: el tipo cambia
if (filtroCat is CentroAcopio cat)   →   if (filtroCat is string cat)

// 3 · .ToString() sobre el enum: sobra, ya es string
l.CentroAcopio.ToString()            →   l.CentroAcopio
u.CatAsignado?.ToString()            →   u.CatAsignado

// 4 · Comparaciones: siguen escribiéndose igual, ya son string
query.Where(l => l.CentroAcopio == cat)
```

**Simplificación que hay que aprovechar** — en `Features/Pagos/Controllers/PagosController.cs:25-26`:

```csharp
// Antes
private CentroAcopio? FiltroCat() =>
    Enum.TryParse<CentroAcopio>(User.CatRestringido(), out var c) ? c : null;

// Después: AlcanceUsuario.CatRestringido() ya devuelve el string del claim.
// El parseo existía solo para volver al enum y ahora no hay adónde volver.
private string? FiltroCat() => User.CatRestringido();
```

Y en `Common/Auth/JwtTokenService.cs:35`, el `.ToString()!` sobra:

```csharp
claims.Add(new Claim("cat", usuario.CatAsignado));
```

- [ ] **Step 6: Normalizar a mayúsculas en el borde**

Postgres compara strings con distinción de mayúsculas. Donde antes el enum garantizaba la forma, ahora hay que garantizarla al entrar. En `Common/Auth/UsuarioService.cs`, dentro de `ValidarCatOperador`, cambiar la firma y añadir la normalización:

```csharp
    // El código del CAT se normaliza AQUÍ, en el borde, y no en cada consulta:
    // Postgres distingue mayúsculas y un "pat" minúsculo dejaría al operador
    // viendo una bandeja vacía sin ningún error que lo explique.
    private static string? NormalizarCat(string? cat) =>
        string.IsNullOrWhiteSpace(cat) ? null : cat.Trim().ToUpperInvariant();
```

y usarla en `CrearAsync` y `ActualizarAsync`:

```csharp
usuario.CatAsignado = dto.Rol == RolUsuario.OperadorCAT
    ? NormalizarCat(dto.CatAsignado) : null;
```

Aplicar lo mismo en `ProductoraService` (al crear y actualizar la productora) y en `RecepcionService` (al abrir una jaula y al registrar una entrega pendiente de vinculación).

- [ ] **Step 7: Actualizar `CatalogosService`**

El diccionario en memoria sigue existiendo hasta la Task 5, pero su clave pasa a `string`:

```csharp
    private static readonly Dictionary<string, string> NombresCat = new()
    {
        ["PAT"] = "Patococha",
        ["NIE"] = "Las Nieves",
        ["HUE"] = "Huertas",
        ["NAB"] = "Nabón / El Progreso",
        ["PEL"] = "Pelincay"
    };
```

y en la proyección de comunidades, `c.CatReferencia.ToString()` pasa a `c.CatReferencia`.

- [ ] **Step 8: Actualizar el sembrador de pruebas**

En `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`:

```csharp
    public static async Task<Usuario> UsuarioAsync(
        ApiFactory api,
        string cedula,
        RolUsuario rol = RolUsuario.OperadorCAT,
        string? cat = "PAT",
        bool activo = true,
        string password = PasswordPorDefecto)
```

```csharp
    public static async Task<Productora> ProductoraAsync(
        ApiFactory api,
        string cedula,
        string cat = "PAT",
        int comunidadId = 1,
        bool activa = true)
```

- [ ] **Step 9: Actualizar los 25 archivos de prueba**

Todos siguen el mismo patrón. Compilar y seguir los errores:

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet build tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj
```

`CentroAcopio.PAT` → `"PAT"`, `CentroAcopio.NIE` → `"NIE"`, y así con los cinco. Borrar el `using CoopagcuyApi.Common;` de los archivos donde solo servía para el enum (el compilador avisa si sigue haciendo falta por `RolUsuario` u otro enum).

- [ ] **Step 10: Comprobar que EF no quiere generar migración**

El cambio es solo de tipo en C#: la columna ya era texto. Verificarlo:

```bash
dotnet ef migrations add VerificacionCatString --project CoopagcuyApi.csproj
```

Abrir la migración generada. **Debe estar vacía** (`Up` y `Down` sin operaciones), salvo quizá un `AlterColumn` que solo fije `maxLength: 3`. Si aparece cualquier `DropColumn`, `AddColumn` o cambio de tipo, **algo se hizo mal**: revisar el paso 4 antes de seguir.

Si está vacía, borrarla:

```bash
dotnet ef migrations remove --project CoopagcuyApi.csproj
```

Si trae solo el `maxLength`, conservarla y renombrarla mentalmente como parte de la Task 5.

- [ ] **Step 11: Correr la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: **el mismo número de pruebas en verde que en el paso 1**. Ni una más ni una menos: esta tarea no añade ni quita comportamiento. Si alguna falla, casi siempre es un `.ToUpperInvariant()` que falta en un borde.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "refactor: el código del CAT viaja como string en vez de enum"
```

---

### Task 5: La tabla `CentroAcopio` y sus claves foráneas

**Files:**
- Create: `Features/Catalogos/Models/CentroAcopio.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs`
- Modify: `tests/CoopagcuyApi.Tests/Infra/BaseDatosFixture.cs` (`TablesToIgnore`)
- Test: `tests/CoopagcuyApi.Tests/Integracion/CatalogoCatTests.cs`

**Interfaces:**
- Consumes: `Canton` (Task 1); el código como `string` en las cinco columnas (Task 4).
- Produces: `CentroAcopio { string Codigo, string Nombre, int CantonId, Canton Canton, bool Activo }`. `AppDbContext.CentrosAcopio`. **La clave primaria es `Codigo`, no un `Id`.**
- Semilla: `PAT`/Patococha/cantón 6, `NIE`/Las Nieves/cantón 4, `HUE`/Huertas/cantón 8, `NAB`/Nabón - El Progreso/cantón 4, `PEL`/Pelincay/cantón 6.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/CatalogoCatTests.cs`:

```csharp
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El centro de acopio dejó de ser un enum compilado y es una tabla. Su clave
/// es el código de tres letras, no un Id: ese código prefija el identificador
/// de cada jaula (PAT-20260615-001) y ya estaba guardado como texto en las
/// cinco columnas que lo referencian.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CatalogoCatTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LaSemilla_traeLosCincoCentrosDelPiloto()
    {
        await using var db = api.NuevoDbContext();

        var codigos = await db.CentrosAcopio
            .OrderBy(c => c.Codigo).Select(c => c.Codigo).ToListAsync();

        codigos.ShouldBe(["HUE", "NAB", "NIE", "PAT", "PEL"]);
    }

    [Fact]
    public async Task CadaCentro_conoceSuCantonYSuProvincia()
    {
        await using var db = api.NuevoDbContext();

        var pat = await db.CentrosAcopio
            .Include(c => c.Canton).ThenInclude(c => c.Provincia)
            .SingleAsync(c => c.Codigo == "PAT");

        pat.Nombre.ShouldBe("Patococha");
        pat.Canton.Nombre.ShouldBe("Pucará");
        pat.Canton.Provincia.Nombre.ShouldBe("Azuay");
    }

    // La clave foránea es lo que impide que una jaula nazca apuntando a un
    // centro que no existe. Antes lo garantizaba el enum; ahora, la base.
    [Fact]
    public async Task UnaJaula_conCentroInexistente_esRechazadaPorLaBase()
    {
        await using var db = api.NuevoDbContext();

        db.Lotes.Add(new CoopagcuyApi.Features.Productoras.Models.Lote
        {
            CodigoLote = "ZZZ-20260101-001",
            CentroAcopio = "ZZZ",
            FechaRecepcion = new DateTime(2026, 1, 1),
            CantidadAnimales = 0,
            PesoTotalGramos = 0,
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ElCatalogoDeCat_sobreviveALaLimpiezaEntrePruebas()
    {
        await api.LimpiarAsync();

        await using var db = api.NuevoDbContext();

        (await db.CentrosAcopio.CountAsync()).ShouldBe(5);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CatalogoCatTests"
```

Esperado: FALLA al compilar — `'AppDbContext' no contiene una definición para 'CentrosAcopio'`.

- [ ] **Step 3: Crear la entidad**

`Features/Catalogos/Models/CentroAcopio.cs`:

```csharp
namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// Centro de acopio y transformación. Era un enum de cinco valores compilado
/// en el binario; crear uno nuevo exigía recompilar y desplegar.
///
/// La clave primaria es el CÓDIGO de tres letras y no un Id entero. No es
/// pereza: ese código ya era la clave real en base —las cinco columnas que lo
/// referencian se persistían con HasConversion&lt;string&gt;()— y además
/// prefija el identificador de cada jaula (PAT-20260615-001). Clavando la
/// tabla encima del código, la migración es un ADD CONSTRAINT en vez de un
/// backfill de cinco columnas.
///
/// Por eso mismo el código es INMUTABLE una vez creado: cambiarlo dejaría
/// jaulas históricas con un prefijo que ya no corresponde a ningún centro, y
/// códigos ya impresos que nadie podría resolver.
///
/// El cantón dice dónde está el centro, no a quién atiende: una comunidad
/// entrega en el que le queda más cerca, aunque esté en otra provincia.
/// </summary>
public class CentroAcopio
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;

    public int CantonId { get; set; }
    public Canton Canton { get; set; } = null!;

    public bool Activo { get; set; } = true;
}
```

- [ ] **Step 4: Mapear y sembrar en `AppDbContext`**

Añadir el `DbSet`:

```csharp
public DbSet<CentroAcopio> CentrosAcopio => Set<CentroAcopio>();
```

Y el bloque, **después** de `Canton` y **antes** de `Comunidad`:

```csharp
// Centro de acopio — catálogo gestionable. La clave es el código de tres
// letras: ver el comentario de la entidad para el porqué.
modelBuilder.Entity<CentroAcopio>(e =>
{
    e.HasKey(c => c.Codigo);
    e.Property(c => c.Codigo).HasMaxLength(3).IsRequired();
    e.Property(c => c.Nombre).HasMaxLength(100).IsRequired();

    e.HasOne(c => c.Canton)
        .WithMany()
        .HasForeignKey(c => c.CantonId)
        .OnDelete(DeleteBehavior.Restrict);

    // Los cinco del piloto, con su código intacto y el cantón donde están
    // físicamente. Los CantonId salen de GeografiaEcuador (Azuay):
    // 4 Nabón, 6 Pucará, 8 Santa Isabel.
    e.HasData(
        new CentroAcopio { Codigo = "PAT", Nombre = "Patococha", CantonId = 6 },
        new CentroAcopio { Codigo = "NIE", Nombre = "Las Nieves", CantonId = 4 },
        new CentroAcopio { Codigo = "HUE", Nombre = "Huertas", CantonId = 8 },
        new CentroAcopio { Codigo = "NAB", Nombre = "Nabón / El Progreso", CantonId = 4 },
        new CentroAcopio { Codigo = "PEL", Nombre = "Pelincay", CantonId = 6 }
    );
});
```

- [ ] **Step 5: Declarar las cinco claves foráneas**

En cada bloque de entidad, junto al `HasMaxLength(3)` que puso la Task 4. Se declaran **sin propiedad de navegación**: el código ya viaja solo por todo el sistema y añadir cinco navegaciones obligaría a un `Include` en cada consulta a cambio de nada.

```csharp
// Productora (~línea 52)
e.HasOne<CentroAcopio>().WithMany()
    .HasForeignKey(p => p.CatAsignado)
    .OnDelete(DeleteBehavior.Restrict);

// Lote (~línea 79)
e.HasOne<CentroAcopio>().WithMany()
    .HasForeignKey(l => l.CentroAcopio)
    .OnDelete(DeleteBehavior.Restrict);

// Usuario (~línea 192) — nullable: solo el OperadorCAT tiene centro
e.HasOne<CentroAcopio>().WithMany()
    .HasForeignKey(u => u.CatAsignado)
    .OnDelete(DeleteBehavior.Restrict);

// EntregaPendienteVinculacion (~línea 412)
e.HasOne<CentroAcopio>().WithMany()
    .HasForeignKey(v => v.CentroAcopio)
    .OnDelete(DeleteBehavior.Restrict);

// Comunidad (~línea 464)
e.HasOne<CentroAcopio>().WithMany()
    .HasForeignKey(c => c.CatReferencia)
    .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 6: Añadir la tabla a `TablesToIgnore`**

En `BaseDatosFixture.cs`, junto a las otras:

```csharp
                new Table("public", "Cantones"),
                new Table("public", "CentrosAcopio")
```

- [ ] **Step 7: Generar la migración**

```bash
dotnet ef migrations add CatalogoCentrosAcopio --project CoopagcuyApi.csproj
```

Revisar: debe crear la tabla `CentrosAcopio` con sus cinco `InsertData` y **luego** los `CreateIndex` + `AddForeignKey` sobre las cinco columnas. El orden importa — si EF pusiera las FK antes de los `InsertData`, la migración fallaría contra una base con datos. Si aparecen en mal orden, moverlas a mano.

**No debe haber ningún `UpdateData` ni `AlterColumn` de tipo sobre esas columnas.** Si lo hay, la Task 4 dejó algo a medias.

- [ ] **Step 8: Correr las pruebas nuevas**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CatalogoCatTests"
```

Esperado: 4 pruebas en verde.

- [ ] **Step 9: Correr la batería completa y commitear**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Vigilar las pruebas que insertan lotes o productoras con códigos inventados: ahora la FK las rechaza. Si alguna prueba usaba un CAT que no existe, **la prueba estaba mal** — corregirla para que use uno de los cinco.

```bash
git add -A
git commit -m "feat: el centro de acopio es una tabla con el código como clave"
```

---

### Task 6: Alta y baja de CATs desde la API

**Files:**
- Modify: `Features/Catalogos/Services/CatalogosService.cs`
- Modify: `Features/Catalogos/DTOs/CatalogosDtos.cs`
- Modify: `Features/Catalogos/Controllers/CatalogosController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/ApiCentrosAcopioTests.cs`

**Interfaces:**
- Consumes: la tabla `CentroAcopio` (Task 5), `Canton` (Task 1).
- Produces:
  - `CentroAcopioDto(string Codigo, string Nombre, int CantonId, string Canton, string Provincia, bool Activo)` — **reemplaza** al `CentroAcopioDto(string Codigo, string Nombre)` de hoy. El cambio es aditivo en JSON: el front actual sigue leyendo `codigo` y `nombre`.
  - `class CrearCentroAcopioDto { string Codigo; string Nombre; int CantonId }`
  - `class ActualizarCentroAcopioDto { string Nombre; int CantonId }` — **sin `Codigo`**: es inmutable, y no ofrecerlo en el contrato es más claro que aceptarlo y rechazarlo.
  - `ICatalogosService` gana `ListarCentrosAcopioAsync(bool incluirInactivos)` (reemplaza al `ListarCentrosAcopio()` síncrono), `CrearCentroAcopioAsync`, `ActualizarCentroAcopioAsync`, `CambiarEstadoCentroAcopioAsync`.

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/ApiCentrosAcopioTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Alta y baja de centros de acopio. El código de tres letras es la clave del
/// sistema —prefija cada jaula— así que se valida al entrar y no se toca nunca
/// más.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ApiCentrosAcopioTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record CatLeido(
        string Codigo, string Nombre, int CantonId, string Canton,
        string Provincia, bool Activo);

    [Fact]
    public async Task Admin_creaUnCentroDeAcopio()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "CUE", nombre = "Cuenca Centro", cantonId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ElCodigo_seNormalizaAMayusculas()
    {
        await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
            new { codigo = "gir", nombre = "Girón", cantonId = 2 });

        var lista = await api.ComoAdmin()
            .GetFromJsonAsync<List<CatLeido>>("/api/catalogos/centros-acopio");

        lista!.ShouldContain(c => c.Codigo == "GIR");
    }

    [Theory]
    [InlineData("PA")]      // dos letras
    [InlineData("PATO")]    // cuatro
    [InlineData("P4T")]     // un dígito
    [InlineData("")]        // vacío
    public async Task UnCodigoQueNoEsTresLetras_esRechazado(string codigo)
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo, nombre = "Da igual", cantonId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnCodigoRepetido_esRechazado()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "PAT", nombre = "Otro Patococha", cantonId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // El contrato de actualización NO tiene campo Codigo. Mandarlo no hace
    // nada: el centro sigue llamándose igual que siempre.
    [Fact]
    public async Task ElCodigo_noSePuedeCambiar()
    {
        await api.ComoAdmin().PutAsJsonAsync("/api/catalogos/centros-acopio/PAT",
            new { codigo = "XXX", nombre = "Patococha renombrada", cantonId = 6 });

        var lista = await api.ComoAdmin()
            .GetFromJsonAsync<List<CatLeido>>("/api/catalogos/centros-acopio");

        lista!.ShouldContain(c => c.Codigo == "PAT");
        lista.ShouldNotContain(c => c.Codigo == "XXX");
    }

    [Fact]
    public async Task UnCentro_conJaulaAbierta_noSeDesactiva()
    {
        await Sembrador.LoteAbiertoAsync(api, "PAT");

        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/centros-acopio/PAT/estado",
                new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnCentro_conProductorasActivas_noSeDesactiva()
    {
        await Sembrador.ProductoraAsync(api, "0104576277", cat: "NIE", comunidadId: 2);

        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/centros-acopio/NIE/estado",
                new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnCentroReciénCreado_seDesactivaSinProblema()
    {
        await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
            new { codigo = "SIG", nombre = "Sígsig", cantonId = 9 });

        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/centros-acopio/SIG/estado",
                new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OperadorCat_noPuedeCrearCentros()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "ABC", nombre = "Prohibido", cantonId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Una comunidad puede entregar en el CAT que le quede más cerca, aunque
    // esté en otra provincia. No hay validación geográfica y no debe haberla.
    [Fact]
    public async Task UnaComunidad_puedeReferenciarUnCatDeOtraProvincia()
    {
        // Cantón 108 = Loja (Loja). Es el primero de la provincia 12: las
        // once anteriores suman 107 cantones en GeografiaEcuador.
        await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
            new { codigo = "LOJ", nombre = "Loja Centro", cantonId = 108 });

        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/comunidades",
                new { nombre = "Comunidad Fronteriza", cantonId = 4, catReferencia = "LOJ" });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // Un centro creado en caliente acota igual que los cinco del piloto: el
    // alcance sale del claim "cat" del token, y ese claim ya era un string
    // antes de que el enum desapareciera. Si esto se cayera, un operador de
    // un centro nuevo vería las jaulas de todos los demás.
    [Fact]
    public async Task UnOperador_deUnCentroNuevo_soloVeLoSuyo()
    {
        await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
            new { codigo = "GUA", nombre = "Gualaceo", cantonId = 3 });

        await Sembrador.LoteAbiertoAsync(api, "PAT");
        await Sembrador.LoteAbiertoAsync(api, "GUA");

        var jaulas = await api.ComoOperadorCat("GUA")
            .GetFromJsonAsync<List<JaulaLeida>>("/api/recepcion/lotes");

        jaulas!.ShouldAllBe(j => j.CentroAcopio == "GUA");
        jaulas.Count.ShouldBe(1);
    }

    private sealed record JaulaLeida(int Id, string CodigoLote, string CentroAcopio);
}
```

> La ruta es la de `RecepcionController.cs:171` (`[HttpGet("lotes")]` sobre `[Route("api/[controller]")]`); el enrutado de ASP.NET no distingue mayúsculas, así que `/api/recepcion/lotes` resuelve. `LoteAbiertoAsync` genera el mismo `CodigoLote` si se la llama dos veces con el mismo CAT; aquí se la llama con centros distintos, así que no chocan. `JaulaLeida` solo declara los tres campos que la prueba mira: `System.Text.Json` ignora el resto del JSON.

- [ ] **Step 2: Añadir el sembrador de jaula abierta**

La prueba de jaula abierta necesita un helper. Añadir a `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`:

```csharp
    /// <summary>
    /// Jaula abierta en un centro, sin animales. Es lo mínimo que impide
    /// desactivar ese centro: hay cuyes físicamente esperando en él.
    /// </summary>
    public static async Task<Lote> LoteAbiertoAsync(ApiFactory api, string cat)
    {
        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = $"{cat}-20260101-001",
            CentroAcopio = cat,
            FechaRecepcion = new DateTime(2026, 1, 1),
            CantidadAnimales = 0,
            PesoTotalGramos = 0,
            Cerrado = false,
        };

        db.Lotes.Add(lote);
        await db.SaveChangesAsync();
        return lote;
    }
```

- [ ] **Step 3: Correr y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ApiCentrosAcopioTests"
```

Esperado: FALLA — los POST/PUT/PATCH devuelven `404` y el GET devuelve el DTO viejo.

- [ ] **Step 4: Reemplazar los DTOs del CAT**

En `Features/Catalogos/DTOs/CatalogosDtos.cs`, sustituir la línea del `CentroAcopioDto` actual:

```csharp
// Centro de acopio del catálogo. Antes era `(Codigo, Nombre)` derivado del
// enum; los campos nuevos son aditivos, así que un cliente viejo sigue
// leyendo codigo y nombre sin enterarse.
public record CentroAcopioDto(
    string Codigo, string Nombre, int CantonId, string Canton,
    string Provincia, bool Activo);

public class CrearCentroAcopioDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int CantonId { get; set; }
}

// Sin Codigo a propósito: es inmutable. No ofrecerlo en el contrato dice más
// que aceptarlo para después rechazarlo.
public class ActualizarCentroAcopioDto
{
    public string Nombre { get; set; } = string.Empty;
    public int CantonId { get; set; }
}
```

Y en `GuardarComunidadDto`, `Canton` (string) pasa a `CantonId`:

```csharp
public class GuardarComunidadDto
{
    public string Nombre { get; set; } = string.Empty;
    public int CantonId { get; set; }
    public string CatReferencia { get; set; } = string.Empty;
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public int? AltitudMinM { get; set; }
    public int? AltitudMaxM { get; set; }
}

public record ComunidadResponseDto(
    int Id,
    string Nombre,
    int CantonId,
    string Canton,
    string Provincia,
    string CatReferencia,
    bool Activa,
    decimal? Latitud,
    decimal? Longitud,
    int? AltitudMinM,
    int? AltitudMaxM
);
```

- [ ] **Step 5: Reescribir `CatalogosService`**

Reemplazar `Features/Catalogos/Services/CatalogosService.cs` entero:

```csharp
using System.Text.RegularExpressions;
using CoopagcuyApi.Features.Catalogos.DTOs;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Catalogos.Services;

public interface ICatalogosService
{
    Task<IEnumerable<ComunidadResponseDto>> ListarComunidadesAsync(bool incluirInactivas);
    Task<ComunidadResponseDto> CrearComunidadAsync(GuardarComunidadDto dto);
    Task<bool> ActualizarComunidadAsync(int id, GuardarComunidadDto dto);
    Task<bool> CambiarEstadoComunidadAsync(int id, bool activa);

    Task<IEnumerable<CentroAcopioDto>> ListarCentrosAcopioAsync(bool incluirInactivos);
    Task<CentroAcopioDto> CrearCentroAcopioAsync(CrearCentroAcopioDto dto);
    Task<bool> ActualizarCentroAcopioAsync(string codigo, ActualizarCentroAcopioDto dto);
    Task<bool> CambiarEstadoCentroAcopioAsync(string codigo, bool activo);
}

public partial class CatalogosService(AppDbContext db) : ICatalogosService
{
    // Tres letras A–Z, ni una más. El código prefija el identificador de cada
    // jaula (PAT-20260615-001): con ancho variable se romperían las etiquetas
    // ink-jet y cualquier lectura por posición.
    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CodigoCat();

    // ── Comunidades ──────────────────────────────────────────────────

    public async Task<IEnumerable<ComunidadResponseDto>> ListarComunidadesAsync(
        bool incluirInactivas)
    {
        var query = db.Comunidades.AsQueryable();
        if (!incluirInactivas) query = query.Where(c => c.Activa);

        return await query
            .OrderBy(c => c.Nombre)
            .Select(c => new ComunidadResponseDto(
                c.Id, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.CatReferencia, c.Activa,
                c.Latitud, c.Longitud, c.AltitudMinM, c.AltitudMaxM))
            .ToListAsync();
    }

    public async Task<ComunidadResponseDto> CrearComunidadAsync(GuardarComunidadDto dto)
    {
        var nombre = dto.Nombre.Trim();
        var cat = dto.CatReferencia.Trim().ToUpperInvariant();

        await ValidarComunidadAsync(nombre, dto.CantonId, cat, idExcluido: null);

        var comunidad = new Comunidad
        {
            Nombre = nombre,
            CantonId = dto.CantonId,
            CatReferencia = cat,
            Latitud = dto.Latitud,
            Longitud = dto.Longitud,
            AltitudMinM = dto.AltitudMinM,
            AltitudMaxM = dto.AltitudMaxM,
        };

        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();

        return await LeerComunidadAsync(comunidad.Id);
    }

    public async Task<bool> ActualizarComunidadAsync(int id, GuardarComunidadDto dto)
    {
        var comunidad = await db.Comunidades.FindAsync(id);
        if (comunidad is null) return false;

        var nombre = dto.Nombre.Trim();
        var cat = dto.CatReferencia.Trim().ToUpperInvariant();

        await ValidarComunidadAsync(nombre, dto.CantonId, cat, idExcluido: id);

        comunidad.Nombre = nombre;
        comunidad.CantonId = dto.CantonId;
        comunidad.CatReferencia = cat;
        comunidad.Latitud = dto.Latitud;
        comunidad.Longitud = dto.Longitud;
        comunidad.AltitudMinM = dto.AltitudMinM;
        comunidad.AltitudMaxM = dto.AltitudMaxM;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoComunidadAsync(int id, bool activa)
    {
        var comunidad = await db.Comunidades.FindAsync(id);
        if (comunidad is null) return false;

        comunidad.Activa = activa;
        await db.SaveChangesAsync();
        return true;
    }

    // El nombre es único DENTRO del cantón, no en todo el sistema: "San José"
    // existe en varias provincias del Ecuador.
    //
    // NO se valida que el CAT sea del mismo cantón ni de la misma provincia:
    // una comunidad entrega donde le queda más cerca, y hay comunidades a las
    // que les queda más cerca un centro de la provincia de al lado.
    private async Task ValidarComunidadAsync(
        string nombre, int cantonId, string cat, int? idExcluido)
    {
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre de la comunidad es obligatorio.");

        if (!await db.Cantones.AnyAsync(c => c.Id == cantonId && c.Activo))
            throw new InvalidOperationException("El cantón indicado no existe o está inactivo.");

        if (!await db.CentrosAcopio.AnyAsync(c => c.Codigo == cat && c.Activo))
            throw new InvalidOperationException(
                $"El centro de acopio '{cat}' no existe o está inactivo.");

        var repetida = await db.Comunidades.AnyAsync(c =>
            c.CantonId == cantonId
            && c.Nombre.ToLower() == nombre.ToLower()
            && (idExcluido == null || c.Id != idExcluido));

        if (repetida)
            throw new InvalidOperationException(
                $"Ya existe la comunidad '{nombre}' en ese cantón.");
    }

    private Task<ComunidadResponseDto> LeerComunidadAsync(int id) =>
        db.Comunidades
            .Where(c => c.Id == id)
            .Select(c => new ComunidadResponseDto(
                c.Id, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.CatReferencia, c.Activa,
                c.Latitud, c.Longitud, c.AltitudMinM, c.AltitudMaxM))
            .SingleAsync();

    // ── Centros de acopio ────────────────────────────────────────────

    public async Task<IEnumerable<CentroAcopioDto>> ListarCentrosAcopioAsync(
        bool incluirInactivos)
    {
        var query = db.CentrosAcopio.AsQueryable();
        if (!incluirInactivos) query = query.Where(c => c.Activo);

        return await query
            .OrderBy(c => c.Nombre)
            .Select(c => new CentroAcopioDto(
                c.Codigo, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.Activo))
            .ToListAsync();
    }

    public async Task<CentroAcopioDto> CrearCentroAcopioAsync(CrearCentroAcopioDto dto)
    {
        var codigo = dto.Codigo.Trim().ToUpperInvariant();
        var nombre = dto.Nombre.Trim();

        if (!CodigoCat().IsMatch(codigo))
            throw new InvalidOperationException(
                "El código del centro debe ser exactamente tres letras (por ejemplo, PAT).");

        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del centro es obligatorio.");

        if (await db.CentrosAcopio.AnyAsync(c => c.Codigo == codigo))
            throw new InvalidOperationException(
                $"Ya existe un centro de acopio con el código '{codigo}'.");

        if (!await db.Cantones.AnyAsync(c => c.Id == dto.CantonId && c.Activo))
            throw new InvalidOperationException("El cantón indicado no existe o está inactivo.");

        db.CentrosAcopio.Add(new CentroAcopio
        {
            Codigo = codigo,
            Nombre = nombre,
            CantonId = dto.CantonId,
        });
        await db.SaveChangesAsync();

        return await LeerCentroAsync(codigo);
    }

    // El código no se toca: no está en el DTO y aquí tampoco se lee.
    public async Task<bool> ActualizarCentroAcopioAsync(
        string codigo, ActualizarCentroAcopioDto dto)
    {
        var centro = await db.CentrosAcopio.FindAsync(codigo.Trim().ToUpperInvariant());
        if (centro is null) return false;

        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del centro es obligatorio.");

        if (!await db.Cantones.AnyAsync(c => c.Id == dto.CantonId && c.Activo))
            throw new InvalidOperationException("El cantón indicado no existe o está inactivo.");

        centro.Nombre = nombre;
        centro.CantonId = dto.CantonId;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoCentroAcopioAsync(string codigo, bool activo)
    {
        var clave = codigo.Trim().ToUpperInvariant();
        var centro = await db.CentrosAcopio.FindAsync(clave);
        if (centro is null) return false;

        if (!activo)
        {
            // Una jaula abierta son cuyes esperando físicamente en ese centro.
            var jaulasAbiertas = await db.Lotes
                .CountAsync(l => l.CentroAcopio == clave && !l.Cerrado);

            if (jaulasAbiertas > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{centro.Nombre}': tiene {jaulasAbiertas} " +
                    "jaula(s) abierta(s). Ciérralas primero.");

            var productorasVivas = await db.Productoras
                .CountAsync(p => p.CatAsignado == clave && p.Activa);

            if (productorasVivas > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{centro.Nombre}': todavía entregan " +
                    $"{productorasVivas} productora(s) activa(s). Reasígnalas primero.");
        }

        centro.Activo = activo;
        await db.SaveChangesAsync();
        return true;
    }

    private Task<CentroAcopioDto> LeerCentroAsync(string codigo) =>
        db.CentrosAcopio
            .Where(c => c.Codigo == codigo)
            .Select(c => new CentroAcopioDto(
                c.Codigo, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.Activo))
            .SingleAsync();
}
```

- [ ] **Step 6: Actualizar el controlador**

Reemplazar el endpoint `ListarCentrosAcopio` y añadir los tres nuevos:

```csharp
    /// <summary>
    /// Catálogo de centros de acopio. Dejó de derivarse de un enum: ahora se
    /// da de alta desde aquí, porque la organización puede sumar provincias.
    /// </summary>
    [HttpGet("centros-acopio")]
    public async Task<IActionResult> ListarCentrosAcopio(
        [FromQuery] bool incluirInactivos = false) =>
        Ok(await service.ListarCentrosAcopioAsync(incluirInactivos));

    [HttpPost("centros-acopio")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CrearCentroAcopio(
        [FromBody] CrearCentroAcopioDto dto)
    {
        try
        {
            var result = await service.CrearCentroAcopioAsync(dto);
            return CreatedAtAction(nameof(ListarCentrosAcopio), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("centros-acopio/{codigo}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ActualizarCentroAcopio(
        string codigo, [FromBody] ActualizarCentroAcopioDto dto)
    {
        try
        {
            return await service.ActualizarCentroAcopioAsync(codigo, dto)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("centros-acopio/{codigo}/estado")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CambiarEstadoCentroAcopio(
        string codigo, [FromBody] CambiarEstadoCentroAcopioDto dto)
    {
        try
        {
            return await service.CambiarEstadoCentroAcopioAsync(codigo, dto.Activo)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
```

Y el record al final del archivo:

```csharp
public record CambiarEstadoCentroAcopioDto(bool Activo);
```

También hay que envolver `CrearComunidad`, `ActualizarComunidad` — que ahora pueden lanzar por cantón o CAT inválidos — en el mismo `try/catch` que devuelve `409`. `ActualizarComunidad` hoy no lo tiene.

- [ ] **Step 7: Correr las pruebas nuevas**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ApiCentrosAcopioTests"
```

Esperado: 14 pruebas en verde (la `[Theory]` cuenta cuatro).

- [ ] **Step 8: Correr la batería completa y commitear**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

```bash
git add -A
git commit -m "feat: alta y baja de centros de acopio desde Administración"
```

---

# FASE 3 — Front

> Las tareas de esta fase van en el repo **`CoopagcuyFront/coopagcuy-frontend`**. No hay framework de pruebas: la verificación es `pnpm build` (que corre `tsc -b`) más `pnpm lint`, y la comprobación en el navegador contra el API corriendo.

### Task 7: El front lee los catálogos del API

**Files:**
- Modify: `src/types/admin.ts`
- Modify: `src/types/productora.ts` (borrar `CENTROS_ACOPIO` y el union `CentroAcopio`)
- Modify: `src/api/admin.ts`
- Create: `src/api/catalogos.ts`
- Create: `src/hooks/useCatalogos.ts`
- Modify los ocho consumidores: `src/components/admin/FormComunidad.tsx`, `FormUsuario.tsx`, `TablaComunidades.tsx`, `TablaUsuarios.tsx`, `src/components/productoras/FormProductora.tsx`, `src/components/recepcion/FormLote.tsx`, `JaulaEnArmado.tsx`, `src/components/reportes/FiltrosPeriodo.tsx`

**Interfaces:**
- Consumes: los endpoints de las Tasks 3 y 6.
- Produces:
  - `src/types/admin.ts`: `Provincia { id, nombre, activa, totalCantones }`, `Canton { id, nombre, provinciaId, provincia, activo, totalComunidades }`, `CentroAcopio { codigo, nombre, cantonId, canton, provincia, activo }`, `Comunidad { id, nombre, cantonId, canton, provincia, catReferencia, activa, latitud, longitud, altitudMinM, altitudMaxM }`.
  - `src/hooks/useCatalogos.ts`: `useProvincias(incluirInactivas?)`, `useCantones(provinciaId?, incluirInactivos?)`, `useCentrosAcopio(incluirInactivos?)`, y `useNombreCat()` → `(codigo: string) => string`.
  - **`CentroAcopio` deja de ser un union de cinco literales y pasa a ser `string`** en todo el front.

- [ ] **Step 1: Reescribir los tipos**

En `src/types/admin.ts`, reemplazar la interfaz `Comunidad` y `GuardarComunidadRequest`, y añadir las tres nuevas:

```typescript
export interface Provincia {
    id: number;
    nombre: string;
    activa: boolean;
    // Cantones activos que cuelgan de ella: explica por qué una baja falló
    totalCantones: number;
}

export interface GuardarProvinciaRequest {
    nombre: string;
}

export interface Canton {
    id: number;
    nombre: string;
    provinciaId: number;
    // Nombre resuelto de la provincia (solo lectura)
    provincia: string;
    activo: boolean;
    totalComunidades: number;
}

export interface GuardarCantonRequest {
    nombre: string;
    provinciaId: number;
}

// El código de tres letras es la clave, no un id numérico: prefija el
// identificador de cada jaula (PAT-20260615-001) y por eso es inmutable.
export interface CentroAcopio {
    codigo: string;
    nombre: string;
    cantonId: number;
    canton: string;
    provincia: string;
    activo: boolean;
}

export interface CrearCentroAcopioRequest {
    codigo: string;
    nombre: string;
    cantonId: number;
}

// Sin código: es inmutable, y el contrato del API tampoco lo acepta.
export interface ActualizarCentroAcopioRequest {
    nombre: string;
    cantonId: number;
}

export interface Comunidad {
    id: number;
    nombre: string;
    cantonId: number;
    // Cantón y provincia resueltos desde el catálogo (solo lectura)
    canton: string;
    provincia: string;
    catReferencia: string;
    activa: boolean;
    // Ubicación en el mapa público; null en comunidades dadas de alta
    // desde Administración a las que nadie les puso coordenadas todavía
    latitud: number | null;
    longitud: number | null;
    altitudMinM: number | null;
    altitudMaxM: number | null;
}

export interface GuardarComunidadRequest {
    nombre: string;
    cantonId: number;
    catReferencia: string;
    latitud?: number | null;
    longitud?: number | null;
    altitudMinM?: number | null;
    altitudMaxM?: number | null;
}
```

- [ ] **Step 2: Borrar la lista hardcodeada**

En `src/types/productora.ts`, borrar:

```typescript
export type CentroAcopio = "PAT" | "NIE" | "HUE" | "NAB" | "PEL";
```

y la constante `CENTROS_ACOPIO` (esté donde esté en el archivo). Dejar en su lugar:

```typescript
// El catálogo de centros de acopio ERA una constante aquí. Dejó de serlo
// cuando se pudieron crear centros nuevos desde Administración: ahora llega
// del API (useCentrosAcopio en src/hooks/useCatalogos.ts). Una lista quemada
// aquí volvería a quedarse corta en cuanto se sume una provincia.
```

- [ ] **Step 3: Escribir el cliente de API**

Crear `src/api/catalogos.ts`:

```typescript
import client from "./client";
import type {
    Provincia, GuardarProvinciaRequest,
    Canton, GuardarCantonRequest,
    CentroAcopio, CrearCentroAcopioRequest, ActualizarCentroAcopioRequest,
} from "../types/admin";

export const geografiaApi = {
    listarProvincias: async (incluirInactivas = false) => {
        const { data } = await client.get<Provincia[]>("/api/catalogos/provincias", {
            params: { incluirInactivas },
        });
        return data;
    },

    crearProvincia: async (body: GuardarProvinciaRequest) => {
        const { data } = await client.post<Provincia>("/api/catalogos/provincias", body);
        return data;
    },

    actualizarProvincia: async (id: number, body: GuardarProvinciaRequest) => {
        await client.put(`/api/catalogos/provincias/${id}`, body);
    },

    cambiarEstadoProvincia: async (id: number, activa: boolean) => {
        await client.patch(`/api/catalogos/provincias/${id}/estado`, { activa });
    },

    listarCantones: async (provinciaId?: number, incluirInactivos = false) => {
        const { data } = await client.get<Canton[]>("/api/catalogos/cantones", {
            params: { provinciaId, incluirInactivos },
        });
        return data;
    },

    crearCanton: async (body: GuardarCantonRequest) => {
        const { data } = await client.post<Canton>("/api/catalogos/cantones", body);
        return data;
    },

    actualizarCanton: async (id: number, body: GuardarCantonRequest) => {
        await client.put(`/api/catalogos/cantones/${id}`, body);
    },

    cambiarEstadoCanton: async (id: number, activo: boolean) => {
        await client.patch(`/api/catalogos/cantones/${id}/estado`, { activo });
    },
};

export const centrosAcopioApi = {
    listar: async (incluirInactivos = false) => {
        const { data } = await client.get<CentroAcopio[]>(
            "/api/catalogos/centros-acopio", { params: { incluirInactivos } });
        return data;
    },

    crear: async (body: CrearCentroAcopioRequest) => {
        const { data } = await client.post<CentroAcopio>(
            "/api/catalogos/centros-acopio", body);
        return data;
    },

    // La ruta lleva el código porque el código ES la clave del recurso
    actualizar: async (codigo: string, body: ActualizarCentroAcopioRequest) => {
        await client.put(`/api/catalogos/centros-acopio/${codigo}`, body);
    },

    cambiarEstado: async (codigo: string, activo: boolean) => {
        await client.patch(`/api/catalogos/centros-acopio/${codigo}/estado`, { activo });
    },
};
```

- [ ] **Step 4: Escribir los hooks**

Crear `src/hooks/useCatalogos.ts`:

```typescript
import { useQuery } from "@tanstack/react-query";
import { geografiaApi, centrosAcopioApi } from "../api/catalogos";

// El catálogo cambia muy de tanto en tanto —una provincia nueva es un evento
// del año— así que se cachea largo. Sin esto, cada pantalla que pinta un
// selector de CAT dispararía su propia petición al montarse.
const CACHE_LARGO = { staleTime: 10 * 60 * 1000 };

export function useProvincias(incluirInactivas = false) {
    return useQuery({
        queryKey: ["provincias", incluirInactivas],
        queryFn: () => geografiaApi.listarProvincias(incluirInactivas),
        ...CACHE_LARGO,
    });
}

// provinciaId undefined = todos los cantones. El selector dependiente de
// FormComunidad lo usa con la provincia elegida.
export function useCantones(provinciaId?: number, incluirInactivos = false) {
    return useQuery({
        queryKey: ["cantones", provinciaId ?? null, incluirInactivos],
        queryFn: () => geografiaApi.listarCantones(provinciaId, incluirInactivos),
        ...CACHE_LARGO,
    });
}

export function useCentrosAcopio(incluirInactivos = false) {
    return useQuery({
        queryKey: ["centros-acopio", incluirInactivos],
        queryFn: () => centrosAcopioApi.listar(incluirInactivos),
        ...CACHE_LARGO,
    });
}

/**
 * Código de CAT -> nombre legible, para las tablas que solo tienen el código.
 *
 * Devuelve el código tal cual mientras el catálogo carga o si el centro fue
 * desactivado: una celda que dice "PAT" es peor que una que dice "Patococha",
 * pero muchísimo mejor que una vacía en un histórico.
 */
export function useNombreCat() {
    const { data: centros = [] } = useCentrosAcopio(true);
    return (codigo: string) =>
        centros.find((c) => c.codigo === codigo)?.nombre ?? codigo;
}

/**
 * Etiqueta larga para los selectores: "Patococha (Pucará, Azuay)".
 *
 * El sufijo no es adorno. Una comunidad entrega en el CAT que le queda más
 * cerca, aunque sea de otra provincia; en cuanto haya dos provincias, sin
 * el cantón y la provincia el operador no sabe cuál está eligiendo.
 */
export function etiquetaCat(c: { nombre: string; canton: string; provincia: string }) {
    return `${c.nombre} (${c.canton}, ${c.provincia})`;
}
```

- [ ] **Step 5: Actualizar los ocho consumidores**

El patrón es siempre el mismo. Donde antes había:

```typescript
import { CENTROS_ACOPIO } from "../../types/productora";
// ...
{CENTROS_ACOPIO.map(({ value, label }) => (
    <option key={value} value={value}>{label}</option>
))}
```

ahora va:

```typescript
import { useCentrosAcopio, etiquetaCat } from "../../hooks/useCatalogos";
// ...
const { data: centros = [] } = useCentrosAcopio();
// ...
{centros.map((c) => (
    <option key={c.codigo} value={c.codigo}>{etiquetaCat(c)}</option>
))}
```

Y donde había una búsqueda de etiqueta:

```typescript
const nombreCat = (cat: string) =>
    CENTROS_ACOPIO.find((c) => c.value === cat)?.label ?? cat;
```

ahora:

```typescript
const nombreCat = useNombreCat();
```

Casos que no siguen el patrón exacto:

- **`FormLote.tsx:172`** — `CENTROS_ACOPIO.some((c) => c.value === cat)` valida el CAT guardado en `localStorage`. Pasa a `centros.some((c) => c.codigo === cat)`. **Ojo:** mientras el catálogo carga, `centros` está vacío y la validación diría que el CAT guardado no vale. Añadir la guarda: `if (!isLoading && !centros.some(...))`.
- **`FormLote.tsx:887` y `JaulaEnArmado.tsx:55`, `FormProductora.tsx:212`** — muestran el nombre del CAT fijo del operador. Usan `useNombreCat()`.
- **`FiltrosPeriodo.tsx`** — importa `type CentroAcopio` como union. Cambiar el tipo del estado a `string`.

- [ ] **Step 6: Compilar y pasar el linter**

```bash
pnpm build
```

Esperado: sin errores de TypeScript. Los que salgan van a ser todos del union `CentroAcopio` que ya no existe: cambiar esos tipos a `string`.

```bash
pnpm lint
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: el front lee los centros de acopio del catálogo, no de una constante"
```

---

### Task 8: Pantallas de Provincias, Cantones y CATs

**Files:**
- Modify: `src/pages/Administracion.tsx`
- Create: `src/components/admin/SelectorCanton.tsx`
- Create: `src/components/admin/PanelCatalogos.tsx`
- Create: `src/components/admin/TablaProvincias.tsx`, `FormProvincia.tsx`
- Create: `src/components/admin/TablaCantones.tsx`, `FormCanton.tsx`
- Create: `src/components/admin/TablaCentrosAcopio.tsx`, `FormCentroAcopio.tsx`

**Interfaces:**
- Consumes: `useProvincias`, `useCantones`, `useCentrosAcopio`, `etiquetaCat` (Task 7); `geografiaApi`, `centrosAcopioApi` (Task 7); el componente `Segmentado` de `src/components/ui/Segmentado.tsx` y `ModalShell` de `src/components/ui/ModalShell.tsx`.
- Produces:
  - `PanelCatalogos` — componente sin props que agrupa las cuatro sub-pestañas.
  - `SelectorCanton` — `{ cantonId: number | undefined; onCambio: (id: number | undefined) => void; requerido?: boolean }`. Lo consumen `FormCentroAcopio` (esta tarea) y `FormComunidad` (Task 9).

- [ ] **Step 0: Escribir el selector de cantón compartido**

Los dos formularios que vienen —el del CAT y el de la comunidad— necesitan la misma pieza: dos `select` encadenados, el reseteo del cantón al cambiar de provincia, y la deducción de la provincia cuando se abre en modo edición y solo se conoce el cantón. Es la lógica más sutil de esta fase; teniéndola dos veces, una de las copias se desviaría.

Crear `src/components/admin/SelectorCanton.tsx`:

```tsx
import { useEffect, useState } from "react";
import { useProvincias, useCantones } from "../../hooks/useCatalogos";

interface Props {
    cantonId: number | undefined;
    onCambio: (id: number | undefined) => void;
    requerido?: boolean;
}

/**
 * Elegir un cantón, en dos pasos: provincia y después cantón.
 *
 * En un paso solo serían 221 opciones en una lista plana, con cantones
 * homónimos ("Bolívar" está en Carchi y en Manabí) que el usuario no podría
 * distinguir.
 *
 * Vive suelto y no dentro de un formulario porque lo usan dos —el del centro
 * de acopio y el de la comunidad— y las tres reglas de abajo son fáciles de
 * implementar mal por separado.
 */
export function SelectorCanton({ cantonId, onCambio, requerido = true }: Props) {
    const [provinciaId, setProvinciaId] = useState<number | undefined>();

    const { data: provincias = [] } = useProvincias();
    const { data: cantones = [] } = useCantones(provinciaId);
    const { data: todosLosCantones = [] } = useCantones();

    // Al abrir en modo edición llega un cantón pero no su provincia: hay que
    // deducirla o el primer selector arrancaría vacío y el segundo, deshabilitado,
    // dejando al usuario sin ver lo que ya está guardado.
    useEffect(() => {
        if (cantonId === undefined || provinciaId !== undefined) return;
        const suyo = todosLosCantones.find((c) => c.id === cantonId);
        if (suyo) setProvinciaId(suyo.provinciaId);
    }, [cantonId, provinciaId, todosLosCantones]);

    return (
        <>
            <div>
                <label className="block text-xs font-bold uppercase tracking-wide
                    text-gray-500 mb-1">
                    Provincia
                </label>
                <select
                    required={requerido}
                    value={provinciaId ?? ""}
                    onChange={(e) => {
                        setProvinciaId(e.target.value ? Number(e.target.value) : undefined);
                        // El cantón elegido pertenecía a la provincia anterior:
                        // sin limpiarlo, el formulario enviaría un cantón que no
                        // está en la lista que el usuario tiene delante.
                        onCambio(undefined);
                    }}
                    className="w-full h-12 px-3 rounded-xl border-2 border-gray-200
                        text-base focus:border-primary-500 focus:outline-none"
                >
                    <option value="">Elige una provincia…</option>
                    {provincias.map((p) => (
                        <option key={p.id} value={p.id}>{p.nombre}</option>
                    ))}
                </select>
            </div>

            <div>
                <label className="block text-xs font-bold uppercase tracking-wide
                    text-gray-500 mb-1">
                    Cantón
                </label>
                <select
                    required={requerido}
                    value={cantonId ?? ""}
                    disabled={!provinciaId}
                    onChange={(e) =>
                        onCambio(e.target.value ? Number(e.target.value) : undefined)}
                    className="w-full h-12 px-3 rounded-xl border-2 border-gray-200
                        text-base disabled:bg-gray-100
                        focus:border-primary-500 focus:outline-none"
                >
                    <option value="">
                        {provinciaId ? "Elige un cantón…" : "Elige antes la provincia"}
                    </option>
                    {cantones.map((c) => (
                        <option key={c.id} value={c.id}>{c.nombre}</option>
                    ))}
                </select>
            </div>
        </>
    );
}
```

- [ ] **Step 1: Cambiar la pestaña de Administración**

En `src/pages/Administracion.tsx`, la pestaña `comunidades` pasa a `catalogos`:

```typescript
import { PanelCatalogos } from "../components/admin/PanelCatalogos";

type Tab = "usuarios" | "catalogos" | "contrasenas";
```

```typescript
                    Usuarios, catálogos geográficos y solicitudes de contraseña
```

```typescript
                        { id: "usuarios", label: "Usuarios" },
                        { id: "catalogos", label: "Catálogos" },
                        { id: "contrasenas", label: "Contraseñas" },
```

```typescript
            {tab === "catalogos" && <PanelCatalogos />}
```

Borrar el import de `TablaComunidades` (pasa a importarlo `PanelCatalogos`).

- [ ] **Step 2: Escribir el panel de sub-pestañas**

Crear `src/components/admin/PanelCatalogos.tsx`:

```tsx
import { useState } from "react";
import { Segmentado } from "../ui/Segmentado";
import { TablaProvincias } from "./TablaProvincias";
import { TablaCantones } from "./TablaCantones";
import { TablaCentrosAcopio } from "./TablaCentrosAcopio";
import { TablaComunidades } from "./TablaComunidades";

type SubTab = "provincias" | "cantones" | "cat" | "comunidades";

/**
 * Los cuatro catálogos geográficos, en el orden en que se dan de alta.
 *
 * El orden no es alfabético a propósito: es la cadena de dependencias. Para
 * crear una comunidad hace falta un cantón, y para un cantón una provincia.
 * Puestas al revés, el administrador encuentra primero el formulario que
 * todavía no puede llenar.
 */
export function PanelCatalogos() {
    const [sub, setSub] = useState<SubTab>("comunidades");

    return (
        <>
            <div className="mb-5">
                <Segmentado
                    activo={sub}
                    onCambio={setSub}
                    opciones={[
                        { id: "provincias", label: "Provincias" },
                        { id: "cantones", label: "Cantones" },
                        { id: "cat", label: "Centros de acopio" },
                        { id: "comunidades", label: "Comunidades" },
                    ]}
                />
            </div>

            {sub === "provincias" && <TablaProvincias />}
            {sub === "cantones" && <TablaCantones />}
            {sub === "cat" && <TablaCentrosAcopio />}
            {sub === "comunidades" && <TablaComunidades />}
        </>
    );
}
```

> Arranca en `comunidades` porque es lo que el administrador usa a diario; provincias y cantones se tocan una vez al año.

- [ ] **Step 3: Escribir `TablaProvincias` y `FormProvincia`**

`TablaProvincias.tsx` sigue **exactamente** la estructura de `TablaComunidades.tsx`: botón de alta arriba a la derecha, tabla en `bg-white rounded-2xl border border-gray-200 overflow-x-auto`, cabecera `bg-gray-50`, `Badge` para el estado, y el modal de formulario controlado por `useState`. Columnas: **Provincia · Cantones activos · Estado · (acciones)**.

La única diferencia de fondo con `TablaComunidades` es que el cambio de estado puede fallar con `409`, así que la mutación necesita `onError`:

```tsx
    const toggle = useMutation({
        mutationFn: ({ id, activa }: { id: number; activa: boolean }) =>
            geografiaApi.cambiarEstadoProvincia(id, activa),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["provincias"] });
            setError(null);
        },
        // El API rechaza desactivar una provincia con cantones vivos. El
        // mensaje que manda ya explica cuántos son: mostrarlo tal cual es
        // más útil que un "no se pudo" genérico.
        onError: (e: unknown) => {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setError(err.response?.data?.mensaje
                ?? "No se pudo cambiar el estado de la provincia.");
        },
    });
```

y el banner de error, igual que el de `FormComunidad`:

```tsx
            {error && (
                <div className="mb-4 bg-teja-50 border border-teja-100 rounded-xl
                    px-3 py-2 text-sm text-teja-700">
                    {error}
                </div>
            )}
```

`FormProvincia.tsx` copia la estructura de `FormComunidad.tsx` (`ModalShell` + `form` con `id`, botón de guardar en el `footer`) con un solo campo: **Nombre de la provincia**.

- [ ] **Step 4: Escribir `TablaCantones` y `FormCanton`**

`TablaCantones.tsx`, misma estructura. Columnas: **Cantón · Provincia · Comunidades activas · Estado · (acciones)**.

Añade un filtro por provincia encima de la tabla, porque son 221 filas:

```tsx
    const [filtroProvincia, setFiltroProvincia] = useState<number | undefined>();
    const { data: provincias = [] } = useProvincias(true);
    const { data: cantones = [], isLoading } = useCantones(filtroProvincia, true);
```

```tsx
            <div className="flex flex-wrap gap-3 items-end justify-between mb-4">
                <div>
                    <label className="block text-xs font-bold uppercase tracking-wide
                        text-gray-500 mb-1">
                        Provincia
                    </label>
                    <select
                        value={filtroProvincia ?? ""}
                        onChange={(e) => setFiltroProvincia(
                            e.target.value ? Number(e.target.value) : undefined)}
                        className="h-11 px-3 rounded-xl border-2 border-gray-200
                            text-base focus:border-primary-500 focus:outline-none"
                    >
                        <option value="">Todas</option>
                        {provincias.map((p) => (
                            <option key={p.id} value={p.id}>{p.nombre}</option>
                        ))}
                    </select>
                </div>

                <button
                    onClick={() => { setCantonEditar(null); setShowForm(true); }}
                    className="h-11 px-5 bg-primary-600 hover:bg-primary-700
                        text-white text-sm font-semibold rounded-xl transition
                        active:scale-[0.98]"
                >
                    + Nuevo cantón
                </button>
            </div>
```

`FormCanton.tsx`: dos campos, **Nombre del cantón** y **Provincia** (un `select` alimentado por `useProvincias()`).

- [ ] **Step 5: Escribir `TablaCentrosAcopio` y `FormCentroAcopio`**

`TablaCentrosAcopio.tsx`, misma estructura. Columnas: **Código · Nombre · Cantón · Provincia · Estado · (acciones)**. El código va en `font-mono font-bold` — es un identificador, no prosa.

`FormCentroAcopio.tsx` tiene la particularidad del código inmutable:

```tsx
export function FormCentroAcopio({ centro, onClose }: Props) {
    const qc = useQueryClient();
    const editando = centro !== null;

    const [codigo, setCodigo] = useState(centro?.codigo ?? "");
    const [nombre, setNombre] = useState(centro?.nombre ?? "");
    const [provinciaId, setProvinciaId] = useState<number | undefined>();
    const [cantonId, setCantonId] = useState<number | undefined>(centro?.cantonId);
    const [error, setError] = useState<string | null>(null);

    const { data: provincias = [] } = useProvincias();
    const { data: cantones = [] } = useCantones(provinciaId);

    const mutation = useMutation({
        mutationFn: async () => {
            if (editando) {
                await centrosAcopioApi.actualizar(centro.codigo,
                    { nombre, cantonId: cantonId! });
            } else {
                await centrosAcopioApi.crear(
                    { codigo, nombre, cantonId: cantonId! });
            }
        },
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["centros-acopio"] });
            onClose();
        },
        onError: (e: unknown) => {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setError(err.response?.data?.mensaje
                ?? "No se pudo guardar el centro de acopio.");
        },
    });
```

El campo del código, **deshabilitado al editar**, con la explicación a la vista para que nadie crea que es un fallo:

```tsx
                <div>
                    <label className="block text-xs font-bold uppercase tracking-wide
                        text-gray-500 mb-1">
                        Código
                    </label>
                    <input
                        type="text" required maxLength={3}
                        value={codigo}
                        disabled={editando}
                        onChange={(e) => setCodigo(e.target.value.toUpperCase())}
                        placeholder="PAT"
                        className="w-full h-12 px-3 rounded-xl border-2 border-gray-200
                            text-base font-mono uppercase tracking-widest
                            disabled:bg-gray-100 disabled:text-gray-500
                            focus:border-primary-500 focus:outline-none"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                        {editando
                            ? "El código no se puede cambiar: encabeza el identificador de cada jaula ya registrada."
                            : "Tres letras. Encabeza el identificador de cada jaula del centro (por ejemplo, PAT-20260615-001)."}
                    </p>
                </div>
```

Y el cantón, con el componente del paso 0 — que ya trae dentro el encadenamiento, el reseteo y la deducción de la provincia al editar:

```tsx
                <SelectorCanton cantonId={cantonId} onCambio={setCantonId} />
```

Con eso, el estado del formulario no necesita `provinciaId` ni las consultas de provincias y cantones:

```tsx
    const [codigo, setCodigo] = useState(centro?.codigo ?? "");
    const [nombre, setNombre] = useState(centro?.nombre ?? "");
    const [cantonId, setCantonId] = useState<number | undefined>(centro?.cantonId);
    const [error, setError] = useState<string | null>(null);
```

- [ ] **Step 6: Compilar, pasar el linter y verificar en el navegador**

```bash
pnpm build
```

```bash
pnpm lint
```

Levantar el API y el front, entrar como `AdminCooperativa` a Administración → Catálogos y comprobar la cadena completa: crear provincia → crear cantón en ella → crear CAT en ese cantón → verificar que el CAT nuevo aparece en el selector de `FormComunidad`. Después, intentar desactivar Azuay y comprobar que sale el mensaje del `409`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: pantallas de provincias, cantones y centros de acopio"
```

---

### Task 9: `FormComunidad` con selectores encadenados

**Files:**
- Modify: `src/components/admin/FormComunidad.tsx`
- Modify: `src/components/admin/TablaComunidades.tsx`

**Interfaces:**
- Consumes: `Comunidad` y `GuardarComunidadRequest` (Task 7), `useCentrosAcopio`, `etiquetaCat`, `useNombreCat` (Task 7), `SelectorCanton` (Task 8).
- Produces: nada que consuman otras tareas.

- [ ] **Step 1: Cambiar el estado y las consultas de `FormComunidad`**

```tsx
    const [nombre, setNombre] = useState(comunidad?.nombre ?? "");
    const [cantonId, setCantonId] = useState<number | undefined>(comunidad?.cantonId);
    const [cat, setCat] = useState(comunidad?.catReferencia ?? "");
    const [error, setError] = useState<string | null>(null);

    const { data: centros = [] } = useCentrosAcopio();
```

Y el cuerpo de la mutación:

```tsx
            const body = { nombre, cantonId: cantonId!, catReferencia: cat };
```

- [ ] **Step 2: Sustituir el input de cantón por el selector compartido**

Reemplazar el bloque completo del `<input>` de cantón por el componente de la Task 8, que ya trae los dos `select` encadenados, el reseteo al cambiar de provincia y la deducción de la provincia al editar:

```tsx
                <SelectorCanton cantonId={cantonId} onCambio={setCantonId} />
```

- [ ] **Step 3: Cambiar el selector de CAT**

```tsx
                <div>
                    <label className="block text-xs font-bold uppercase tracking-wide
                        text-gray-500 mb-1">
                        Centro de acopio de referencia
                    </label>
                    <select
                        required
                        value={cat}
                        onChange={(e) => setCat(e.target.value)}
                        className="w-full h-12 px-3 rounded-xl border-2 border-gray-200
                            text-base focus:border-primary-500 focus:outline-none"
                    >
                        <option value="">Elige un centro…</option>
                        {centros.map((c) => (
                            <option key={c.codigo} value={c.codigo}>
                                {etiquetaCat(c)}
                            </option>
                        ))}
                    </select>
                    <p className="text-xs text-gray-500 mt-1">
                        Puede estar en otro cantón o en otra provincia: la comunidad
                        entrega donde le queda más cerca.
                    </p>
                </div>
```

> **La lista NO se filtra por la provincia elegida.** El texto de ayuda está ahí para que nadie lo "arregle" después creyendo que es un descuido.

- [ ] **Step 4: Añadir la provincia a `TablaComunidades`**

La cabecera pasa a **Comunidad · Cantón · Provincia · CAT de referencia · Estado · (acciones)**, y `nombreCat` pasa a venir de `useNombreCat()`:

```tsx
    const nombreCat = useNombreCat();
```

- [ ] **Step 5: Compilar, linter y verificación en el navegador**

```bash
pnpm build
```

```bash
pnpm lint
```

En el navegador: crear una comunidad eligiendo provincia y cantón, y asignarle un CAT de otra provincia. Debe guardarse sin error — es el caso que la Task 6 verifica en el API y esta pantalla no debe estorbarlo.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: la comunidad elige provincia y cantón del catálogo"
```

---

# FASE 4 — La provincia real

### Task 10: La provincia deja de estar escrita a mano

**Files:**
- Modify: `Features/QR/Services/QRService.cs:~180,191,214,261`
- Modify: `Features/QR/DTOs/QRDtos.cs` (`PaginaPublicaDto`)
- Modify: `Features/Faenamiento/Services/FaenamientoService.cs:814,854`
- Modify: `Features/Recepcion/Services/GuiaMovilizacionService.cs:129`
- Modify: `src/types/publico.ts` y `src/pages/QRPublico.tsx` (repo del front)
- Test: `tests/CoopagcuyApi.Tests/Integracion/PaginaPublicaTests.cs`

**Interfaces:**
- Consumes: `Comunidad.Canton.Provincia.Nombre` (Task 2).
- Produces: `PaginaPublicaDto` gana `string Provincia` **después** de `Canton`. El front lo lee como `provincia: string`.

- [ ] **Step 1: Escribir la prueba que falla**

`PaginaPublicaTests.cs` ya tiene un `private async Task SembrarPaginaAsync()` que arma la cadena completa (productora → jaula → lote faenado → QR) con `comunidadId: 1` quemado, y lee la respuesta con `JsonDocument`. Se le añade el parámetro y se sigue su estilo.

Cambiar la firma del sembrador existente:

```csharp
    /// Lote faenado con QR activo y dos animales, uno de ellos con novedad.
    /// La comunidad es parámetro desde 2026-08: la ficha pública dice de qué
    /// provincia viene el cuy, y eso solo se puede verificar con una
    /// comunidad que no sea de Azuay.
    private async Task SembrarPaginaAsync(int comunidadId = 1)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, "PAT", comunidadId: comunidadId);
```

(el resto del método no cambia) y añadir la prueba:

```csharp
    // "Azuay" estuvo escrita a mano en cuatro sitios de QRService. Con una
    // sola provincia nunca se notó; en cuanto entre otra, el QR le mentiría
    // al consumidor sobre de dónde viene el cuy que tiene en la mano.
    [Fact]
    public async Task LaFichaPublica_diceLaProvinciaDeLaComunidad_noUnaFija()
    {
        var comunidadId = await ComunidadLojanaAsync();

        await SembrarPaginaAsync(comunidadId);

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{CodigoFaenado}");
        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("provincia").GetString().ShouldBe("Loja");

        var parametros = doc.RootElement.GetProperty("parametrosAprobados")
            .EnumerateArray().Select(p => p.GetString()!).ToList();
        parametros.ShouldContain(p => p.Contains("Loja, Ecuador"));
    }

    /// Comunidad de Loja que entrega en un CAT de Azuay. Es el caso que
    /// obliga a derivar la provincia de la COMUNIDAD y no del centro: una
    /// comunidad entrega donde le queda más cerca, aunque sea otra provincia.
    private async Task<int> ComunidadLojanaAsync()
    {
        await using var db = api.NuevoDbContext();

        // Cantón 108 = Loja (Loja), el primero de la provincia 12 en
        // GeografiaEcuador: las once anteriores suman 107 cantones.
        var comunidad = new Comunidad
        {
            Nombre = "Comunidad Lojana",
            CantonId = 108,
            CatReferencia = "PAT",
        };

        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();
        return comunidad.Id;
    }
```

Comprobar que la clase ya tiene `using System.Text.Json;` (lo usa en la prueba de `estadoCalidad`) y añadir `using CoopagcuyApi.Features.Catalogos.Models;`.

> **Respawn no trunca `Comunidades`** (está en `TablesToIgnore`), así que esta comunidad sobrevive entre pruebas. Como su nombre es único dentro del cantón 108 y ninguna otra prueba lo usa, no molesta; pero si `ComunidadLojanaAsync` se llamara dos veces en la misma corrida chocaría con el índice único. Por eso devuelve el Id de la que ya exista:
>
> ```csharp
>         var existente = await db.Comunidades
>             .FirstOrDefaultAsync(c => c.CantonId == 108 && c.Nombre == "Comunidad Lojana");
>         if (existente is not null) return existente.Id;
> ```
>
> justo antes del `db.Comunidades.Add(...)`.

- [ ] **Step 2: Correr y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~PaginaPublicaTests"
```

Esperado: FALLA — `PaginaPublicaDto` no tiene `Provincia`.

- [ ] **Step 3: Añadir el campo al DTO**

En `Features/QR/DTOs/QRDtos.cs`, dentro de `PaginaPublicaDto`, justo después de `Canton`:

```csharp
    string Provincia,
```

- [ ] **Step 4: Derivar la provincia en `QRService`**

En `ConstruirPaginaAsync`, junto al bloque que ya calcula `cantones` (~línea 250), añadir el equivalente para provincias, y asegurar el `ThenInclude` en la consulta que trae las sesiones:

```csharp
        var provincias = sesiones
            .Select(s => s.Lote.Productora?.Comunidad.Canton.Provincia.Nombre)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct()
            .ToList();

        // Una sesión de planta puede reunir jaulas de varias provincias. Se
        // nombran todas: recortar a la primera diría media verdad.
        var provinciaOrigen = provincias.Count > 0
            ? string.Join(" y ", provincias)
            : "Ecuador";
```

Sustituir los cuatro literales:

| Línea | Antes | Después |
|---|---|---|
| ~180 | `?? "Azuay")))` | `?? "origen no registrado")))` |
| ~191 | `: "Azuay";` | `: "origen no registrado";` |
| ~214 | `parametros.Add("✓ Crianza familiar — Azuay, Ecuador");` | `parametros.Add($"✓ Crianza familiar — {provinciaOrigen}, Ecuador");` |
| ~261 | `Canton: cantones.Count > 0 ? string.Join(" y ", cantones) : "Azuay",` | `Canton: cantones.Count > 0 ? string.Join(" y ", cantones) : "—",` |

Y añadir el campo nuevo a la construcción del `PaginaPublicaDto`, después de `Canton`:

```csharp
            Provincia: provinciaOrigen,
```

> Los fallbacks de las líneas 180 y 191 eran `"Azuay"` porque no había otra cosa que decir. Ahora que la provincia es real, seguir escribiendo un topónimo cuando **no se sabe** el origen sería peor: se dice que no se sabe.

Verificar que la consulta que carga `sesiones` encadena `.ThenInclude(c => c.Canton).ThenInclude(c => c.Provincia)` tras el `Include` de `Comunidad`. Sin eso, `Provincia` llega `null` y revienta en ejecución.

- [ ] **Step 5: Hacer lo mismo en `FaenamientoService`**

Líneas 814 y 854, mismo criterio: el fallback `"Azuay"` pasa a derivarse de la comunidad, y cuando no hay comunidad se dice que no se sabe. Añadir los `ThenInclude` que hagan falta.

- [ ] **Step 6: Añadir la provincia a la guía de movilización**

`Features/Recepcion/Services/GuiaMovilizacionService.cs:129`:

```csharp
                                    $"({productora.Comunidad.Nombre}, " +
                                    $"{productora.Comunidad.Canton.Nombre}, " +
                                    $"{productora.Comunidad.Canton.Provincia.Nombre})");
```

Y en el `Include` de la línea ~54, encadenar:

```csharp
                .ThenInclude(p => p!.Comunidad)
                    .ThenInclude(c => c.Canton)
                        .ThenInclude(c => c.Provincia)
```

- [ ] **Step 7: Correr las pruebas del API**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: todo verde, incluida la nueva. Atención a `GuiaMovilizacionTests` y `TextosGuiaTests`, que verifican texto del PDF: si alguna comprueba la línea de origen, hay que actualizar el texto esperado.

- [ ] **Step 8: Commitear el API**

```bash
git add -A
git commit -m "feat: la provincia del cuy sale del catálogo, no de un literal"
```

- [ ] **Step 9: Mostrar la provincia en la ficha pública**

En el repo del front, añadir `provincia: string;` a la interfaz de la página pública (`src/types/publico.ts` o donde esté declarada — buscar `canton` en `src/types/`), y pintarla en `src/pages/QRPublico.tsx` junto al cantón. Donde hoy diga `{ficha.canton}`, pasa a `{ficha.canton}, {ficha.provincia}`.

```bash
pnpm build
```

```bash
git add -A
git commit -m "feat: la ficha pública muestra la provincia de origen"
```

---

### Task 11: Las coordenadas suben al catálogo

**Files:**
- Modify: `src/domain/comunidades/coordenadas.ts`
- Modify: `src/components/publico/MapaOrigen.tsx`
- Modify: `src/pages/QRPublico.tsx`
- Modify: `src/components/admin/FormComunidad.tsx`
- Modify: `Features/QR/DTOs/QRDtos.cs` y `Features/QR/Services/QRService.cs` (el DTO de aporte por comunidad)

**Interfaces:**
- Consumes: `Comunidad.Latitud/Longitud/AltitudMinM/AltitudMaxM` (Task 2), `ComunidadResponseDto` con esos campos (Task 6).
- Produces: `ComunidadAporteDto(string Comunidad, int Cantidad, decimal? Latitud, decimal? Longitud, int? AltitudMinM, int? AltitudMaxM)` — **reemplaza** al `ComunidadAporteDto(string Comunidad, int Cantidad)` actual. Aditivo en JSON.
- `coordenadas.ts` conserva `PLANTA`, `LIENZO`, `proyectar`, `enlaceMapa`, `distanciaKm`, `clave`, `altitudTexto`, `msnmMedio`, `kmAPlanta`, `desnivelAPlanta` y `CURVAS`. **Pierde** `UBICACIONES`, `COMUNIDADES_CONOCIDAS`, `POR_CLAVE` y `ubicacionDe`.

- [ ] **Step 1: Añadir las coordenadas al DTO de aporte**

En `Features/QR/DTOs/QRDtos.cs`:

```csharp
// Cuántos animales puso cada comunidad, y dónde queda. Las coordenadas
// vienen del catálogo desde 2026-08: antes vivían en una tabla del front
// indexada por nombre, que dejaba sin pin a cualquier comunidad nueva.
public record ComunidadAporteDto(
    string Comunidad, int Cantidad,
    decimal? Latitud, decimal? Longitud,
    int? AltitudMinM, int? AltitudMaxM);
```

- [ ] **Step 2: Poblarlo en `QRService`**

En `ConstruirPaginaAsync`, la proyección de `animales` ya arrastra el nombre de la comunidad; ahora arrastra la comunidad entera:

```csharp
        var animales = sesiones
            .SelectMany(f => f.Cuyes
                .Where(cf => cf.Estado != EstadoCanal.Rechazado)
                .Select(cf => (
                    Faenado: cf,
                    Comunidad: f.Lote.Cuyes
                        .FirstOrDefault(c => c.NumeroEnLote == cf.NumeroEnLote)
                        ?.Productora?.Comunidad
                        ?? f.Lote.Productora?.Comunidad)))
            .ToList();

        var comunidadesAporte = animales
            .Where(a => a.Comunidad is not null)
            .GroupBy(a => a.Comunidad!.Id)
            .Select(g => new ComunidadAporteDto(
                g.First().Comunidad!.Nombre, g.Count(),
                g.First().Comunidad!.Latitud, g.First().Comunidad!.Longitud,
                g.First().Comunidad!.AltitudMinM, g.First().Comunidad!.AltitudMaxM))
            .OrderByDescending(c => c.Cantidad)
            .ToList();
```

> **Se agrupa por `Id` y no por nombre.** Con varias provincias puede haber dos comunidades homónimas en la misma sesión, y agruparlas juntas sumaría animales de sitios distintos bajo un solo pin.

Ajustar los usos posteriores de `comunidadesAporte` (el `comunidadOrigen` y la construcción de `NombreProductora`), que hoy leen `c.Comunidad` como clave del grupo — siguen funcionando porque el campo se llama igual.

- [ ] **Step 3: Correr las pruebas del API y commitear**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

```bash
git add -A
git commit -m "feat: la ficha pública sirve las coordenadas de cada comunidad"
```

- [ ] **Step 4: Vaciar la tabla de ubicaciones del front**

En `src/domain/comunidades/coordenadas.ts`, borrar el array `UBICACIONES`, el `Map` `POR_CLAVE`, la función `ubicacionDe` y la constante `COMUNIDADES_CONOCIDAS`, y sustituir la cabecera del archivo por:

```typescript
/**
 * Geometría del mapa de origen: proyección, distancias y la planta.
 *
 * Las COORDENADAS DE LAS COMUNIDADES ya no viven aquí. Subieron al catálogo
 * (Comunidad.Latitud/Longitud/AltitudMinM/AltitudMaxM) el día en que la
 * cooperativa pudo dar de alta comunidades desde Administración, que es
 * exactamente lo que la cabecera anterior de este archivo anticipaba. Llegan
 * dentro de cada aporte de la ficha pública.
 *
 * Lo que SÍ sigue aquí es lo que no es un dato de catálogo: la coordenada de
 * la planta —única y fija—, la proyección al lienzo y las fórmulas de
 * distancia y desnivel.
 *
 * ── El encuadre está CONGELADO ──────────────────────────────────────
 *
 * RANGO_LAT y RANGO_LON son literales y no se recalculan a partir de las
 * comunidades que llegan. El relieve del fondo es una malla SRTM horneada
 * (relieve.generado, producida por scripts/relieve) para este encuadre
 * exacto: si el marco se moviera al aparecer una comunidad lejana, el
 * terreno se quedaría donde estaba y los pines señalarían montañas que no
 * son. Un mapa desincronizado es peor que un pin de menos.
 *
 * Una comunidad cuya coordenada caiga fuera de este marco NO recibe pin. La
 * ficha sigue diciendo su nombre, su cantón, su provincia y su enlace a
 * Google Maps, que se arma desde la coordenada y no depende del encuadre.
 * Para dibujar otra provincia hay que regenerar el relieve con
 * scripts/relieve y actualizar estos dos rangos a la vez.
 */
```

Para congelar el encuadre **sin transcribir cifras a mano** —una cifra mal pegada movería los cinco pines a la vez y ninguna prueba lo notaría— se conserva el cálculo `rango(...)` tal cual y se le cambia la entrada: en vez de leer `COMUNIDADES_CONOCIDAS` (que deja de existir), lee un array literal con las seis coordenadas del piloto. El resultado es idéntico al de hoy por construcción.

Sustituir el bloque que hoy dice `const TODOS = [...COMUNIDADES_CONOCIDAS, PLANTA];` por:

```typescript
/**
 * Las seis coordenadas que definen el encuadre horneado: las cinco
 * comunidades del piloto más la planta.
 *
 * Se quedan aquí como literales, y NO se leen del catálogo, aunque el
 * catálogo ya las tenga. Son la geometría del PNG de relieve, no el dato de
 * dónde está una comunidad: si mañana alguien corrige por 200 metros la
 * coordenada de Huertas en Administración, el mapa de fondo no se
 * reproyecta, y este marco no debe moverse con ella.
 *
 * Solo se tocan a la vez que se regenera el relieve con scripts/relieve.
 */
const ENCUADRE_PILOTO: Coordenada[] = [
    { lat: -3.284722, lon: -79.400833 },  // Patococha
    { lat: -3.417500, lon: -79.166944 },  // Las Nieves
    { lat: -3.276111, lon: -79.243889 },  // Huertas
    { lat: -3.339722, lon: -79.060556 },  // Nabón / El Progreso
    { lat: -3.243611, lon: -79.386944 },  // Pelincay
    { lat: PLANTA.lat, lon: PLANTA.lon },
];

const RANGO_LAT = rango(ENCUADRE_PILOTO.map((u) => u.lat));
const RANGO_LON = rango(ENCUADRE_PILOTO.map((u) => u.lon));
```

> Las cinco coordenadas se copian **del array `UBICACIONES` de este mismo archivo justo antes de borrarlo**, no de este plan: las de arriba son las mismas que la Task 2 llevó al `HasData`, y ese es exactamente el punto — si no coinciden con las que había, algo se transcribió mal en la Task 2 y hay que arreglarlo allí, no aquí.

Añadir el predicado que decide si un punto entra en el marco:

```typescript
/** ¿La coordenada cae dentro del encuadre horneado? Ver cabecera. */
export function dentroDelEncuadre(c: Coordenada): boolean {
    return c.lat >= RANGO_LAT.min && c.lat <= RANGO_LAT.max
        && c.lon >= RANGO_LON.min && c.lon <= RANGO_LON.max;
}
```

- [ ] **Step 5: Adaptar `MapaOrigen`**

`MapaOrigen` recibe hoy `aportes: { comunidad, cantidad }[]` y resuelve la ubicación con `ubicacionDe(nombre)`. Ahora la ubicación viene dentro del aporte:

```tsx
interface Aporte {
    comunidad: string;
    cantidad: number;
    latitud: number | null;
    longitud: number | null;
    altitudMinM: number | null;
    altitudMaxM: number | null;
}
```

y la resolución pasa a ser:

```tsx
// Una comunidad sin coordenadas (dada de alta sin ponérselas) o fuera del
// encuadre horneado no lleva pin. Sigue nombrada en el texto de la ficha:
// lo que se pierde es el punto en el mapa, no la trazabilidad.
const ubicados = aportes
    .map((a) => {
        if (a.latitud === null || a.longitud === null) return null;
        const coordenada = { lat: a.latitud, lon: a.longitud };
        if (!dentroDelEncuadre(coordenada)) return null;
        return {
            ...a,
            ubicacion: {
                ...coordenada,
                nombre: a.comunidad,
                canton: "",
                msnm: {
                    min: a.altitudMinM ?? 0,
                    max: a.altitudMaxM ?? 0,
                },
            } satisfies Ubicacion,
        };
    })
    .filter((a) => a !== null);
```

El resto del componente —proyección, hilos hasta la planta, tamaño del pin— no cambia: opera sobre `ubicacion`, que sigue teniendo la misma forma. Los sitios que hoy iteran `COMUNIDADES_CONOCIDAS` para pintar los pines vacíos de las comunidades que **no** aportaron pasan a no pintar nada: sin tabla local, el front ya no sabe qué otras comunidades existen, y pedirlas al API solo para dibujar puntos grises no lo vale.

Si el filtro deja `ubicados` vacío, el componente devuelve `null` y `QRPublico` no monta el mapa.

- [ ] **Step 6: Pasar las coordenadas desde `QRPublico`**

Donde `QRPublico.tsx` construye los `aportes` para `MapaOrigen`, pasar el objeto completo del API en vez de solo nombre y cantidad. El tipo del front de `ComunidadAporte` gana los cuatro campos nullable.

- [ ] **Step 7: Añadir los campos de coordenadas a `FormComunidad`**

Cuatro inputs numéricos opcionales, en un bloque colapsable o al final del formulario, con la explicación de para qué son:

```tsx
                <div className="pt-2 border-t border-gray-100">
                    <p className="text-xs font-bold uppercase tracking-wide
                        text-gray-500 mb-1">
                        Ubicación (opcional)
                    </p>
                    <p className="text-xs text-gray-500 mb-3">
                        Sitúa la comunidad en el mapa de la ficha pública del QR.
                        Sin coordenadas la comunidad funciona igual: solo no
                        aparece dibujada.
                    </p>

                    <div className="grid grid-cols-2 gap-3">
                        <input type="number" step="0.000001" value={latitud ?? ""}
                            onChange={(e) => setLatitud(
                                e.target.value === "" ? null : Number(e.target.value))}
                            placeholder="Latitud (-3.284722)"
                            className="h-12 px-3 rounded-xl border-2 border-gray-200
                                text-base focus:border-primary-500 focus:outline-none" />
                        <input type="number" step="0.000001" value={longitud ?? ""}
                            onChange={(e) => setLongitud(
                                e.target.value === "" ? null : Number(e.target.value))}
                            placeholder="Longitud (-79.400833)"
                            className="h-12 px-3 rounded-xl border-2 border-gray-200
                                text-base focus:border-primary-500 focus:outline-none" />
                        <input type="number" value={altitudMin ?? ""}
                            onChange={(e) => setAltitudMin(
                                e.target.value === "" ? null : Number(e.target.value))}
                            placeholder="Altitud mínima (m)"
                            className="h-12 px-3 rounded-xl border-2 border-gray-200
                                text-base focus:border-primary-500 focus:outline-none" />
                        <input type="number" value={altitudMax ?? ""}
                            onChange={(e) => setAltitudMax(
                                e.target.value === "" ? null : Number(e.target.value))}
                            placeholder="Altitud máxima (m)"
                            className="h-12 px-3 rounded-xl border-2 border-gray-200
                                text-base focus:border-primary-500 focus:outline-none" />
                    </div>
                </div>
```

y sumarlos al `body` de la mutación.

- [ ] **Step 8: Compilar, linter y verificación visual**

```bash
pnpm build
```

```bash
pnpm lint
```

Abrir la ficha pública de un lote del piloto y **comparar el mapa con una captura anterior al cambio**: los cinco pines tienen que caer exactamente donde caían. Si alguno se movió, los rangos congelados del paso 4 se pegaron mal, o las coordenadas de la Task 2 no se transcribieron bien desde el archivo original.

Después, crear desde Administración una comunidad sin coordenadas, hacerle un lote y abrir su ficha: el nombre debe aparecer en el texto y el mapa no debe romperse.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: el mapa público lee las coordenadas del catálogo"
```

---

## Cierre

- [ ] **Correr la batería completa una última vez**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

- [ ] **Correr el script de verificación previa contra la base real**

Antes de desplegar, con la cadena de conexión de producción:

```bash
psql "$CONNECTION_STRING" -f scripts/verificar-cantones.sql
```

Si devuelve filas, **corregir esos cantones antes de migrar**: la migración de la Task 2 se detiene con esas comunidades. La comunidad con «Nabon» sin tilde **no** debe aparecer aquí — el cruce ignora tildes.

- [ ] **Actualizar la documentación formal**

El SRS documenta el catálogo de comunidades como RF-102 / RF-506 y describe el centro de acopio como un valor fijo. Ambas cosas cambian: hay que reflejar el catálogo geográfico y el CAT gestionable. Preguntar al usuario si esta actualización entra ahora o va en su propia entrega.

- [ ] **Cerrar la rama**

Usar la skill `superpowers:finishing-a-development-branch`.
