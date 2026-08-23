# Trazabilidad del transporte — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que las condiciones de transporte **no verificadas** dejen rastro en la guía, que la pregunta de llegada sea obligatoria cuando el checklist salió incompleto, y que una llegada en mal estado abra un cuestionario de catálogo cerrado.

**Architecture:** El checklist guarda hoy una frase ya compuesta con lo que sí se marcó, perdiendo las claves. Se añade una columna con las claves y lo que faltó se deriva del catálogo por diferencia. Las condiciones de llegada pasan de texto libre a claves de un catálogo cerrado, con la observación libre conservada en la columna que ya existe. Todo texto que va al papel se compone en funciones puras.

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql, QuestPDF 2024.3.1, xUnit + Shouldly + Respawn, React 19 + TypeScript + Vite + Tailwind.

**Spec:** `docs/superpowers/specs/2026-08-23-trazabilidad-del-transporte-design.md`

## Global Constraints

- **Rama del API y del front:** crear `feat/trazabilidad-transporte` desde `origin/main`. Este proyecto es **independiente** de A, B y C: no depende de ninguna de sus ramas.
- **Nada de `dotnet test` ni `dotnet ef` directamente en Windows.** Smart App Control bloquea la carga del DLL desde OneDrive (`0x800711C7`). Todo pasa por Docker.
  - Batería completa: `docker compose -f docker-compose.tests.yml run --rm tests`
  - Una clase: `docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~NombreDeLaClase"`
  - Puede tardar varios minutos; usa un timeout amplio.
- **Punto de partida: la batería de `origin/main` en verde.** Ejecútala antes de tocar nada y anota el número: es tu línea base, y este plan no puede dejar ninguna roja.
- **Toda columna nueva NO anulable sobre una tabla con datos necesita `HasDefaultValue` en el `modelBuilder`**, no solo el inicializador de C#. En este plan **todas las columnas nuevas son anulables**, así que no aplica — pero si acabas añadiendo una que no lo sea, esta es la regla.
- **Respawn limpia la base antes de cada prueba** pero trunca **SIN RESTART IDENTITY**: nunca asumas que la primera fila sembrada tenga `Id` 1.
- **Azurite no se limpia entre pruebas**, solo Postgres.
- **Del binario del PDF no se puede afirmar nada** (QuestPDF comprime los flujos de texto). Todo texto que va al papel se compone en una función pura que se fija por unidad. `TextosGuia` es la clase que ya sigue ese patrón.
- **El tamaño de un PDF no es proporcional al texto añadido:** el subconjunto de fuentes embebido cambia con los glifos usados, y en un proyecto anterior quitar una línea corta hizo el documento *más pequeño*. **Mide antes de fijar cualquier umbral** y deja el valor medido en el comentario.
- **Verificación del front:** `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`, los tres con salida 0. No hay Vitest ni Playwright.
- **Objetivos táctiles de 44 px** en el front: tablets de 7 pulgadas usadas en campo y con guantes. La convención del repo es `min-h-[44px]`; **`min-h-12` no existe en este Tailwind y no aplicaría nada.**
- **Mutación obligatoria:** cada guarda nueva se comprueba quitándola, viendo la prueba en rojo y restaurándola. En los proyectos A y B ese paso encontró trece problemas, siete de ellos suposiciones falsas del propio plan. **Si una mutación no pone roja su prueba, para y avisa** en vez de ajustar la prueba.
- **Mensajes de commit en castellano**, terminados en `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## File Structure

**API — se crean**

| Archivo | Responsabilidad |
|---|---|
| `Features/Recepcion/Models/CondicionLlegada.cs` | Catálogo cerrado de condiciones de llegada |
| `Infrastructure/Data/Migrations/*_TrazabilidadTransporte.cs` | La migración |
| `tests/.../Unitarias/CondicionesNoVerificadasTests.cs` | Derivación por diferencia y textos |
| `tests/.../Integracion/LlegadaEnMalEstadoTests.cs` | Obligatoriedad, catálogo cerrado y cuestionario |

**API — se modifican**

| Archivo | Cambio |
|---|---|
| `Features/Recepcion/Models/CondicionTransporte.cs` | + `NoVerificadas(...)`, + `SEPARADOR` |
| `Features/Recepcion/Models/Movilizacion.cs` | + `CondicionesClaves`, `LlegaronEnBuenEstado`, `CondicionesLlegadaClaves` |
| `Infrastructure/Data/AppDbContext.cs` | Longitudes de las columnas nuevas |
| `Features/Recepcion/DTOs/RecepcionDtos.cs` | `ConfirmarRecepcionPlantaDto` y `MovilizacionResponseDto` |
| `Features/Recepcion/Services/MovilizacionService.cs` | Persistir claves; validar la llegada |
| `Features/Recepcion/Services/TextosGuia.cs` | + `LineaNoVerificadas(...)` |
| `Features/Recepcion/Services/GuiaMovilizacionService.cs` | El bloque de lo no verificado |
| `tests/.../Integracion/GuiaMovilizacionTests.cs` | Dos pruebas nuevas |

**Front — se modifican**

| Archivo | Cambio |
|---|---|
| `src/types/recepcion.ts` | Los campos nuevos de la movilización |
| `src/api/recepcion.ts` | El cuerpo de confirmación de llegada |
| `src/components/recepcion/FormMovilizacion.tsx` | Aviso de checklist incompleto |
| `src/pages/Faenamiento.tsx` | Pregunta obligatoria y cuestionario |

---

## Fase 1 · El modelo y los catálogos

### Task 1: Las columnas y el catálogo de llegada

**Files:**
- Create: `Features/Recepcion/Models/CondicionLlegada.cs`
- Modify: `Features/Recepcion/Models/Movilizacion.cs`
- Modify: `Features/Recepcion/Models/CondicionTransporte.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs`
- Create: la migración

**Interfaces:**
- Produces:
  - `Movilizacion.CondicionesClaves` (`string?`) — claves marcadas, separadas por `;`
  - `Movilizacion.LlegaronEnBuenEstado` (`bool?`)
  - `Movilizacion.CondicionesLlegadaClaves` (`string?`)
  - `CondicionTransporte.Separador` (`const char`)
  - `CondicionLlegada.Catalogo` (`IReadOnlyDictionary<string,string>`), `.EsValida(string)`, `.Describir(IEnumerable<string>)`

- [ ] **Step 1: Crear la rama y anotar la línea base**

```bash
git checkout -b feat/trazabilidad-transporte origin/main
```

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Anota el número de pruebas: es tu línea base para todo el plan.

- [ ] **Step 2: El separador en `CondicionTransporte`**

Añadir al principio de la clase `CondicionTransporte`:

```csharp
    /// <summary>
    /// Separador con el que las claves marcadas se guardan en una sola
    /// columna. Punto y coma y no coma: las etiquetas del catálogo llevan
    /// comas dentro ("Jaulas aseguradas, sin apilar") y algún día alguien
    /// intentará partir por el separador equivocado.
    /// </summary>
    public const char Separador = ';';
```

- [ ] **Step 3: La derivación por diferencia**

Añadir a `CondicionTransporte`:

```csharp
    /// <summary>
    /// Etiquetas de las condiciones que NO se marcaron, en el orden del
    /// catálogo para que dos guías sean comparables.
    ///
    /// Las claves desconocidas se ignoran: una movilización guardada con una
    /// clave que después se retiró del catálogo no puede hacer que aquí
    /// aparezca una condición inventada.
    /// </summary>
    public static IReadOnlyList<string> NoVerificadas(IEnumerable<string> clavesMarcadas)
    {
        var marcadas = clavesMarcadas.ToHashSet();
        return Catalogo
            .Where(kv => !marcadas.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();
    }
```

- [ ] **Step 4: Las tres columnas de `Movilizacion`**

Añadir a `Features/Recepcion/Models/Movilizacion.cs`:

```csharp
    // Claves del checklist que SÍ se marcaron, separadas por punto y coma.
    //
    // CondicionesTransporte guarda una frase ya compuesta y pierde las
    // claves, así que con ella sola es imposible saber qué faltó: habría que
    // parsear texto, y el propio catálogo advierte de que las etiquetas
    // cambian mientras las claves no ("Maximo20" sigue llamándose así aunque
    // el tope sea 15).
    //
    // NULO significa "movilización anterior a este cambio, no se registró",
    // que NO es lo mismo que "no se verificó ninguna". La guía distingue los
    // dos casos.
    public string? CondicionesClaves { get; set; }

    // Respuesta a "¿llegaron en buen estado?". Nula en las movilizaciones
    // anteriores, en las que nunca se preguntó. Obligatoria en el servicio
    // cuando el checklist de transporte salió incompleto.
    public bool? LlegaronEnBuenEstado { get; set; }

    // Claves del cuestionario de llegada, separadas por punto y coma. Solo
    // se llenan cuando LlegaronEnBuenEstado es false.
    //
    // La observación libre sigue viviendo en CondicionLlegada, que ya era
    // texto libre: reutilizarla mantiene legibles las recepciones antiguas
    // en vez de dejar una columna con dos significados.
    public string? CondicionesLlegadaClaves { get; set; }
```

- [ ] **Step 5: El catálogo de llegada**

Crear `Features/Recepcion/Models/CondicionLlegada.cs`:

```csharp
namespace CoopagcuyApi.Features.Recepcion.Models;

/// <summary>
/// Catálogo cerrado de lo que un operador de planta puede constatar al abrir
/// la jaula cuando los animales NO llegaron en buen estado.
///
/// Cerrado por el mismo motivo que CondicionTransporte: un campo abierto hace
/// que cada planta escriba lo suyo y que después no se pueda contar ni
/// comparar nada. El servidor solo acepta estas claves; el texto que se
/// guarda y se imprime lo pone él, no el operador.
/// </summary>
public static class CondicionLlegada
{
    public static readonly IReadOnlyDictionary<string, string> Catalogo =
        new Dictionary<string, string>
        {
            ["AnimalesGolpeados"] = "Animales con golpes o heridas",
            ["AnimalesDeshidratados"] = "Animales deshidratados o decaídos",
            ["AnimalesMuertos"] = "Animales muertos",
            ["JaulasSucias"] = "Jaulas sucias o con excretas",
            ["Hacinamiento"] = "Hacinamiento en la jaula",
            ["JaulasDanadas"] = "Jaulas rotas o mal aseguradas",
            ["Otro"] = "Otra condición (ver observación)",
        };

    public static bool EsValida(string clave) => Catalogo.ContainsKey(clave);

    /// <summary>
    /// Texto canónico que se guarda y se imprime, en el orden del catálogo
    /// para que dos recepciones sean comparables.
    /// </summary>
    public static string Describir(IEnumerable<string> claves)
    {
        var marcadas = claves.ToHashSet();
        return string.Join(", ", Catalogo
            .Where(kv => marcadas.Contains(kv.Key))
            .Select(kv => kv.Value));
    }
}
```

- [ ] **Step 6: Configurar las columnas**

En `Infrastructure/Data/AppDbContext.cs`, dentro del bloque `modelBuilder.Entity<Movilizacion>(e => { … })`, junto a las demás propiedades:

```csharp
            // Siete claves del catálogo separadas por punto y coma caben de
            // sobra en 300; el mismo tamaño que ya tiene la frase compuesta.
            e.Property(m => m.CondicionesClaves).HasMaxLength(300);
            e.Property(m => m.CondicionesLlegadaClaves).HasMaxLength(300);
```

`LlegaronEnBuenEstado` es un `bool?` y no necesita configuración.

- [ ] **Step 7: Generar la migración**

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/Users/nicol/OneDrive/Documents/CoopagcuyApi:/src" -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=placeholder;Username=postgres;Password=postgres" mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet tool install --global dotnet-ef --version 8.* >/dev/null 2>&1; export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add TrazabilidadTransporte --project CoopagcuyApi.csproj"
```

Esperado: `Done. To undo this action, use 'ef migrations remove'`.

- [ ] **Step 8: Leer la migración antes de seguir**

Abrir el `.cs` generado y comprobar que las **tres** columnas son `nullable: true` y que no toca nada más.

**Si algo está mal, no la edites a mano.** Borra los dos archivos generados, restaura el snapshot con `git checkout -- Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`, corrige el `modelBuilder` y repite el paso 7. (`dotnet ef migrations remove` **no sirve**: intenta conectarse a la base y falla con la cadena de marcador.)

- [ ] **Step 9: La batería sigue en verde y commit**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: el mismo número de la línea base, 0 fallos.

```bash
git add Features/ Infrastructure/
git commit -m "feat: la base guarda que condiciones se verificaron y como llego el lote

CondicionesTransporte guarda una frase ya compuesta y pierde las claves,
asi que hoy es imposible saber cual falto. La columna nueva guarda las
claves y lo que falta se deriva del catalogo por diferencia.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Los textos, como funciones puras

**Files:**
- Modify: `Features/Recepcion/Services/TextosGuia.cs`
- Create: `tests/CoopagcuyApi.Tests/Unitarias/CondicionesNoVerificadasTests.cs`

**Interfaces:**
- Consumes: `CondicionTransporte.NoVerificadas`, `CondicionTransporte.Separador` (Tarea 1).
- Produces:
  - `TextosGuia.ClavesDe(string? csv)` → `IReadOnlyList<string>`
  - `TextosGuia.LineaNoVerificadas(string? clavesCsv)` → `string?`

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Unitarias/CondicionesNoVerificadasTests.cs`:

```csharp
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Features.Recepcion.Services;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El operador del CAT marca un checklist antes de enviar la jaula, y hasta
/// ahora lo que dejaba sin marcar no se reflejaba en ningún sitio: la guía
/// imprimía lo verificado y lo que faltó desaparecía.
///
/// Funciones puras porque del PDF no se puede afirmar nada: QuestPDF comprime
/// los flujos de texto del documento.
/// </summary>
public class CondicionesNoVerificadasTests
{
    [Fact]
    public void NoVerificadas_devuelveLasQueFaltan_enOrdenDelCatalogo()
    {
        var marcadas = new[] { "JaulasLimpias", "Ventilacion" };

        var faltan = CondicionTransporte.NoVerificadas(marcadas);

        faltan.Count.ShouldBe(CondicionTransporte.Catalogo.Count - 2);
        faltan.ShouldContain("Vehículo limpio");
        faltan.ShouldNotContain("Ventilación adecuada");
    }

    [Fact]
    public void NoVerificadas_conTodasMarcadas_devuelveVacio()
    {
        var todas = CondicionTransporte.Catalogo.Keys.ToArray();

        CondicionTransporte.NoVerificadas(todas).ShouldBeEmpty();
    }

    [Fact]
    public void NoVerificadas_ignoraUnaClaveDesconocida()
    {
        // Una movilización guardada con una clave que después se retiró del
        // catálogo no puede hacer aparecer una condición inventada.
        var faltan = CondicionTransporte.NoVerificadas(new[] { "ClaveQueYaNoExiste" });

        faltan.Count.ShouldBe(CondicionTransporte.Catalogo.Count);
    }

    [Fact]
    public void ClavesDe_partePorElSeparador()
    {
        TextosGuia.ClavesDe("JaulasLimpias;Ventilacion")
            .ShouldBe(new[] { "JaulasLimpias", "Ventilacion" });
    }

    [Fact]
    public void ClavesDe_conNuloDevuelveVacio()
    {
        TextosGuia.ClavesDe(null).ShouldBeEmpty();
    }

    [Fact]
    public void LineaNoVerificadas_nombraLasQueFaltan()
    {
        var linea = TextosGuia.LineaNoVerificadas("JaulasLimpias;Ventilacion");

        linea.ShouldNotBeNull();
        linea.ShouldContain("Vehículo limpio");
    }

    [Fact]
    public void LineaNoVerificadas_conTodasMarcadas_devuelveNulo()
    {
        // Nada que imprimir: la guía de un lote con el checklist completo
        // debe salir idéntica a las de antes de esta feature.
        var todas = string.Join(CondicionTransporte.Separador,
            CondicionTransporte.Catalogo.Keys);

        TextosGuia.LineaNoVerificadas(todas).ShouldBeNull();
    }

    [Fact]
    public void LineaNoVerificadas_sinRegistro_noAfirmaQueNoSeVerificoNada()
    {
        // Una movilización anterior a esta feature no tiene claves guardadas.
        // "No se registró" NO es lo mismo que "no se verificó ninguna", y la
        // guía no puede decir lo segundo cuando lo cierto es lo primero.
        TextosGuia.LineaNoVerificadas(null).ShouldBeNull();
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CondicionesNoVerificadasTests"
```

Esperado: error de compilación — `ClavesDe` y `LineaNoVerificadas` no existen.

- [ ] **Step 3: Implementar**

Añadir a `Features/Recepcion/Services/TextosGuia.cs`:

```csharp
    /// <summary>
    /// Claves guardadas en una columna, partidas por su separador. Nulo o
    /// vacío devuelve una lista vacía, no una lista con una cadena vacía
    /// dentro — eso último haría que NoVerificadas creyera que hay una clave
    /// marcada que no existe.
    /// </summary>
    public static IReadOnlyList<string> ClavesDe(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(CondicionTransporte.Separador,
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                 .ToList();

    /// <summary>
    /// "No se verificó: Ventilación adecuada, Vehículo limpio", o NULO cuando
    /// no hay nada que decir.
    ///
    /// Devuelve nulo en DOS casos distintos que la guía trata igual —no
    /// imprimir nada— pero que no son lo mismo: con todas las condiciones
    /// marcadas no falta ninguna, y en una movilización anterior a esta
    /// feature no se registró cuáles se marcaron. Afirmar en ese segundo caso
    /// que "no se verificó ninguna" sería inventar un dato que nadie guardó.
    /// </summary>
    public static string? LineaNoVerificadas(string? clavesCsv)
    {
        if (string.IsNullOrWhiteSpace(clavesCsv)) return null;

        var faltan = CondicionTransporte.NoVerificadas(ClavesDe(clavesCsv));
        return faltan.Count == 0
            ? null
            : $"No se verificó: {string.Join(", ", faltan)}";
    }
```

Añadir `using CoopagcuyApi.Features.Recepcion.Models;` si no está (ya lo está, por `CuyRegistro`).

- [ ] **Step 4: Ejecutar y ver que pasan**

Esperado: `Passed: 8, Failed: 0`.

- [ ] **Step 5: Comprobar por mutación**

Cambiar el `!marcadas.Contains(kv.Key)` de `NoVerificadas` por `marcadas.Contains(kv.Key)`. Esperado: fallan `NoVerificadas_devuelveLasQueFaltan_enOrdenDelCatalogo`, `NoVerificadas_conTodasMarcadas_devuelveVacio` y `LineaNoVerificadas_conTodasMarcadas_devuelveNulo`. **Restaurar.**

Quitar el `if (string.IsNullOrWhiteSpace(clavesCsv)) return null;` de `LineaNoVerificadas`. Esperado: falla `LineaNoVerificadas_sinRegistro_noAfirmaQueNoSeVerificoNada`. **Restaurar.**

- [ ] **Step 6: Commit**

```bash
git add Features/Recepcion/Services/TextosGuia.cs Features/Recepcion/Models/CondicionTransporte.cs tests/
git commit -m "feat: los textos de lo no verificado, como funciones puras

Del PDF no se puede afirmar nada, asi que la composicion vive fuera del
armado del documento. Distingue "no falto ninguna" de "no se registro
cuales se marcaron": no son lo mismo y la guia no puede confundirlas.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 2 · El servicio

### Task 3: Persistir las claves y validar la llegada

**Files:**
- Modify: `Features/Recepcion/DTOs/RecepcionDtos.cs`
- Modify: `Features/Recepcion/Services/MovilizacionService.cs`
- Create: `tests/CoopagcuyApi.Tests/Integracion/LlegadaEnMalEstadoTests.cs`

**Interfaces:**
- Consumes: las columnas de la Tarea 1, `CondicionLlegada.EsValida`, `TextosGuia.ClavesDe`.
- Produces:
  - `ConfirmarRecepcionPlantaDto` con `LlegaronEnBuenEstado` (`bool?`) y `CondicionesLlegada` (`List<string>`)
  - `MovilizacionResponseDto` con `CondicionesClaves`, `LlegaronEnBuenEstado`, `CondicionesLlegadaClaves`

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/LlegadaEnMalEstadoTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El checklist de transporte no bloquea el envío, pero deja constancia. Y si
/// salió incompleto, el operador de planta no puede confirmar la llegada sin
/// decir si los animales llegaron bien: es el único momento en que alguien
/// puede contrastar lo que se prometió con lo que llegó.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class LlegadaEnMalEstadoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task LasClavesMarcadasSeGuardan()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias", "Ventilacion" });

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);

        mov.CondicionesClaves.ShouldNotBeNull();
        mov.CondicionesClaves.ShouldContain("JaulasLimpias");
        mov.CondicionesClaves.ShouldContain("Ventilacion");
        // Y la frase compuesta de siempre sigue ahí: reimprimir una guía
        // antigua no puede perder ese dato.
        mov.CondicionesTransporte.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConChecklistIncompletoLaPreguntaEsObligatoria()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta"
                // sin llegaronEnBuenEstado
            });

        // 409 y no 400: decidir que la respuesta es obligatoria exige mirar
        // el checklist GUARDADO, no el cuerpo de la peticion. Es el criterio
        // que ya sigue todo el modulo de pagos.
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);
        mov.FechaRecepcionPlanta.ShouldBeNull();
    }

    [Fact]
    public async Task ConChecklistCompletoLaPreguntaSigueSiendoOpcional()
    {
        // Control: la obligatoriedad es consecuencia del checklist incompleto,
        // no una molestia nueva para todo el mundo.
        var todas = CoopagcuyApi.Features.Recepcion.Models
            .CondicionTransporte.Catalogo.Keys.ToArray();
        var (_, movId) = await MovilizarAsync(todas);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnNoSinNingunaCondicionSeRechaza()
    {
        // Decir que llegaron mal y no decir en qué no informa de nada.
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = false,
                condicionesLlegada = Array.Empty<string>()
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnaClaveDeLlegadaDesconocidaSeRechaza()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = false,
                condicionesLlegada = new[] { "SeLosComioElPerro" }
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnNoConSusCondicionesSeGuarda()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = false,
                condicionesLlegada = new[] { "AnimalesGolpeados", "JaulasSucias" },
                condicionLlegada = "tres con heridas en el lomo"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);

        mov.LlegaronEnBuenEstado.ShouldBe(false);
        mov.CondicionesLlegadaClaves.ShouldNotBeNull();
        mov.CondicionesLlegadaClaves.ShouldContain("AnimalesGolpeados");
        // La observación libre se conserva aparte: el catálogo dice QUÉ pasó
        // y el texto dice qué vio.
        mov.CondicionLlegada.ShouldBe("tres con heridas en el lomo");
    }

    [Fact]
    public async Task UnSiNoArrastraCondiciones()
    {
        // Si llegaron bien, no puede quedar un cuestionario guardado.
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = true,
                condicionesLlegada = new[] { "AnimalesGolpeados" }
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);
        mov.CondicionesLlegadaClaves.ShouldBeNullOrEmpty();
    }

    /// Entrega de 3 cuyes en PAT, lote cerrado y movilizado con las
    /// condiciones indicadas. Devuelve el código del lote y el Id de la
    /// movilización.
    private async Task<(string Codigo, int MovilizacionId)> MovilizarAsync(
        string[] condiciones)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, 3).Select(_ => new
        {
            pesoGramos = 1300m,
            colorPelaje = "Blanco",
            estadoOreja = "Blanda",
            tamanoAnimal = "Normal"
        }).ToArray();

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        string codigo;
        await using (var db = api.NuevoDbContext())
        {
            var loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId).FirstAsync();
            var lote = await db.Lotes.FirstAsync(l => l.Id == loteId);
            lote.Cerrado = true;
            await db.SaveChangesAsync();
            codigo = lote.CodigoLote;
        }

        var mov = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 3,
                condicionesTransporte = condiciones,
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });
        mov.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var movId = await db2.Movilizaciones.Select(m => m.Id).FirstAsync();
        return (codigo, movId);
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~LlegadaEnMalEstadoTests"
```

Esperado: fallan todas menos `ConChecklistCompletoLaPreguntaSigueSiendoOpcional`, que ya pasa porque hoy no hay ninguna validación.

**Antes de implementar, confirma la ruta real de confirmación de recepción** en `Features/Recepcion/Controllers/RecepcionController.cs` (busca `ConfirmarRecepcionPlanta`). Si no es `POST /api/recepcion/movilizaciones/{id}/recepcion`, corrige **la URL de las pruebas**, no el controlador.

- [ ] **Step 3: Ampliar los DTOs**

En `Features/Recepcion/DTOs/RecepcionDtos.cs`:

```csharp
public class ConfirmarRecepcionPlantaDto
{
    public string RecibidoPor { get; set; } = string.Empty;

    // Observación libre de siempre. El catálogo dice QUÉ pasó; esto dice qué
    // vio el operador con sus palabras.
    public string? CondicionLlegada { get; set; }

    // Obligatoria cuando el checklist de transporte salió incompleto.
    public bool? LlegaronEnBuenEstado { get; set; }

    // Solo se leen cuando LlegaronEnBuenEstado es false.
    public List<string> CondicionesLlegada { get; set; } = [];
}
```

Y añadir al final de `MovilizacionResponseDto`, después de `SinAntibioticos7Dias`:

```csharp
    string? CondicionesClaves,
    bool? LlegaronEnBuenEstado,
    string? CondicionesLlegadaClaves
```

Actualizar el `Mapear(...)` de `MovilizacionService` con los tres valores.

- [ ] **Step 4: Persistir las claves al registrar**

En `MovilizacionService.RegistrarAsync`, en el objeto `new Movilizacion { … }`, junto a `CondicionesTransporte`:

```csharp
            // Las claves, además de la frase. La frase se conserva porque es
            // lo único que tienen las movilizaciones anteriores a este
            // cambio, y reimprimir una guía antigua no puede perder ese dato.
            CondicionesClaves = string.Join(
                CondicionTransporte.Separador, dto.CondicionesTransporte),
```

- [ ] **Step 5: Validar la llegada**

En `MovilizacionService.ConfirmarRecepcionAsync`, después de la comprobación de que no esté ya confirmada y **antes** de escribir nada:

```csharp
        // Si el checklist salió incompleto, la pregunta deja de ser opcional:
        // es el único momento en que alguien puede contrastar lo que se
        // prometió al cargar con lo que de verdad llegó.
        //
        // Nulo en CondicionesClaves es una movilización anterior a esta
        // feature: ahí no se sabe qué se verificó, así que no se puede exigir
        // nada y la pregunta sigue siendo opcional.
        var faltaron = movilizacion.CondicionesClaves is not null
            && CondicionTransporte.NoVerificadas(
                   TextosGuia.ClavesDe(movilizacion.CondicionesClaves)).Count > 0;

        // TransicionInvalidaException y no CuerpoInvalidoException: esto
        // depende del estado guardado, no del cuerpo. El controlador ya
        // traduce InvalidOperationException —de la que hereda— a 409.
        if (faltaron && dto.LlegaronEnBuenEstado is null)
            throw new TransicionInvalidaException(
                "El checklist de transporte quedó incompleto: hay que indicar " +
                "si los animales llegaron en buen estado.");

        var claves = dto.CondicionesLlegada
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        var desconocidas = claves.Where(c => !CondicionLlegada.EsValida(c)).ToList();
        if (desconocidas.Count > 0)
            throw new CuerpoInvalidoException(
                $"Condición de llegada no reconocida: {string.Join(", ", desconocidas)}.");

        if (dto.LlegaronEnBuenEstado == false && claves.Count == 0)
            throw new CuerpoInvalidoException(
                "Si los animales no llegaron en buen estado, hay que indicar " +
                "al menos una condición.");
```

Y al escribir:

```csharp
        movilizacion.LlegaronEnBuenEstado = dto.LlegaronEnBuenEstado;
        // Solo se guardan si la respuesta fue "no": un "sí" con casillas
        // marcadas de un intento anterior dejaría un cuestionario que
        // contradice su propia respuesta.
        movilizacion.CondicionesLlegadaClaves = dto.LlegaronEnBuenEstado == false
            ? string.Join(CondicionTransporte.Separador, claves)
            : null;
```

Añadir los `using` que falten (`CoopagcuyApi.Common.Exceptions`, y `CondicionLlegada` ya está en el mismo namespace de modelos).

**El endpoint es `HttpPatch("movilizaciones/{id:int}/recepcion")`**, no POST — las pruebas usan `PatchAsJsonAsync`. Y hoy **solo** captura `InvalidOperationException` y la traduce a **409**; no tiene `catch` de 400. Añádele uno:

```csharp
        catch (CuerpoInvalidoException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
```

**Colócalo ANTES del `catch (InvalidOperationException)`.** Si `CuerpoInvalidoException` hereda de `InvalidOperationException` —compruébalo— el orden decide cual gana, y al reves los 400 saldrian como 409.

- [ ] **Step 6: Ejecutar y comprobar por mutación**

Esperado: `Passed: 7, Failed: 0`.

Mutaciones, restaurando después de cada una:
1. Quitar el `if (faltaron && dto.LlegaronEnBuenEstado is null)` → falla `ConChecklistIncompletoLaPreguntaEsObligatoria`.
2. Quitar la comprobación de claves desconocidas → falla `UnaClaveDeLlegadaDesconocidaSeRechaza`.
3. Quitar el `if (dto.LlegaronEnBuenEstado == false && claves.Count == 0)` → falla `UnNoSinNingunaCondicionSeRechaza`.
4. Cambiar el ternario del guardado por `string.Join(...)` sin condición → falla `UnSiNoArrastraCondiciones`.

- [ ] **Step 7: Batería completa y commit**

Esperado: línea base + 15 (8 de la Tarea 2 y 7 de esta), 0 fallos.

```bash
git add Features/ tests/
git commit -m "feat: la llegada se responde y se justifica cuando el checklist fallo

Con el checklist incompleto, confirmar la recepcion exige decir si los
animales llegaron bien; un "no" exige al menos una condicion de un
catalogo cerrado. La observacion libre se conserva aparte: el catalogo
dice que paso y el texto dice que vio.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 3 · El papel

### Task 4: La guía imprime lo que no se verificó

**Files:**
- Modify: `Features/Recepcion/Services/GuiaMovilizacionService.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs`

**Interfaces:**
- Consumes: `TextosGuia.LineaNoVerificadas` (Tarea 2), `Movilizacion.CondicionesClaves` (Tarea 1).
- Produces: nada nuevo.

- [ ] **Step 1: Añadir las pruebas**

Añadir a `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs` dos pruebas, reutilizando su sembrador `SembrarLoteAsync()`:

- Una guía de un lote movilizado con **todas** las condiciones marcadas y otra con **una sola** marcada. Afirmar que la segunda es **sensiblemente más larga** que la primera.
- Una guía con todas marcadas: afirmar que **no crece** respecto de la misma guía generada antes de esta feature — como eso no se puede comparar contra el pasado, la forma honesta es sembrar dos lotes gemelos, movilizar los dos con el checklist completo, y afirmar que sus PDF miden **prácticamente lo mismo** (diferencia menor que unas decenas de bytes, que es el ruido del código de lote).

**Mide el crecimiento real antes de fijar el umbral** y deja el valor medido en el comentario. El tamaño de un PDF no es proporcional al texto añadido: en un proyecto anterior una línea corta lo hizo *más pequeño*. Aquí el bloque son varias condiciones, así que debería crecer — **pero compruébalo**, y si no crece de forma fiable, **para y avisa** en vez de inventar un número.

- [ ] **Step 2: Ejecutar y ver que falla la primera**

Esperado: la de crecimiento falla (todavía no se imprime nada); la de gemelos pasa.

- [ ] **Step 3: El bloque en la guía**

En `GuiaMovilizacionService`, junto a la línea que hoy imprime las condiciones:

```csharp
                                r.RelativeItem().Text(
                                    $"Condiciones: {movilizacion.CondicionesTransporte ?? "-"}");
```

añadir debajo, dentro del mismo bloque de datos del transporte:

```csharp
                            // Lo que NO se verificó. Va aquí y no en una nota
                            // al pie porque es el mismo dato que la línea de
                            // arriba, leído del otro lado: sin esto, una
                            // jaula que salió con tres casillas sin marcar
                            // produce una guía indistinguible de una completa.
                            var noVerificadas = TextosGuia.LineaNoVerificadas(
                                movilizacion.CondicionesClaves);

                            if (noVerificadas is not null)
                                c.Item().PaddingTop(2).Text(noVerificadas)
                                    .FontSize(9).Bold();
```

**Ojo con el nombre del contenedor:** en ese punto del documento el elemento se llama `c` o `r` según el bloque. Usa el que corresponda al sitio donde lo coloques y comprueba que compila.

- [ ] **Step 4: Ejecutar y comprobar por mutación**

Esperado: las dos en verde.

Mutación: envolver el bloque en `if (false)`. Esperado: falla la prueba de crecimiento. **Restaurar.**

- [ ] **Step 5: Batería completa y commit**

```bash
git add Features/ tests/
git commit -m "feat: la guia dice que condiciones no se verificaron

Hasta ahora imprimia solo lo que si se marco, asi que una jaula que salio
con tres casillas sin marcar producia una guia indistinguible de una
completa. Con el checklist entero, la guia sale igual que siempre.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 4 · Las dos pantallas

### Task 5: El aviso en el formulario de envío

**Files:**
- Modify: `src/types/recepcion.ts` (front)
- Modify: `src/components/recepcion/FormMovilizacion.tsx` (front)

**Interfaces:**
- Consumes: nada del API que no exista ya.
- Produces: nada.

- [ ] **Step 1: Crear la rama del front**

```bash
git checkout -b feat/trazabilidad-transporte origin/main
```

- [ ] **Step 2: Los campos nuevos en el tipo**

Añadir a la interfaz de movilización en `src/types/recepcion.ts`:

```ts
    condicionesClaves: string | null;
    llegaronEnBuenEstado: boolean | null;
    condicionesLlegadaClaves: string | null;
```

- [ ] **Step 3: El aviso**

`FormMovilizacion.tsx` ya muestra «{n} de {total} verificadas» (~línea 185). Añadir, cuando `n < total`, un aviso visible bajo el contador:

```tsx
                            {form.condicionesTransporte.length < condiciones.length && (
                                <p className="mt-2 text-xs font-semibold text-bayo-700">
                                    Las condiciones sin marcar quedarán registradas en
                                    la guía, y la planta tendrá que responder cómo
                                    llegaron los animales.
                                </p>
                            )}
```

No bloquea el envío: bloquearlo empujaría a marcar casillas sin mirar, que es el problema que este checklist vino a resolver cuando sustituyó al texto libre.

- [ ] **Step 4: Verificar**

```bash
pnpm lint
```
```bash
pnpm exec tsc -b
```
```bash
pnpm build
```

Los tres con salida 0.

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "feat: el formulario avisa de que lo no verificado queda registrado

Para que dejar una casilla sin marcar sea una decision y no un descuido.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: La pregunta y el cuestionario de llegada

**Files:**
- Modify: `src/api/recepcion.ts` (front)
- Modify: `src/pages/Faenamiento.tsx` (front)

**Interfaces:**
- Consumes: `POST /api/recepcion/movilizaciones/{id}/recepcion` con `llegaronEnBuenEstado` y `condicionesLlegada` (Tarea 3).
- Produces: nada.

- [ ] **Step 1: El catálogo en el front**

El catálogo vive en el servidor y **no hay endpoint que lo exponga**. Duplicarlo en el front es la opción pragmática —son siete pares clave/etiqueta— pero **queda anotado en un comentario** que la fuente de verdad es `CondicionLlegada.cs` y que el servidor rechaza cualquier clave que no reconozca, así que una desincronización se manifiesta como un 400 y no como un dato malo.

En `src/pages/Faenamiento.tsx`, junto a los demás literales del archivo:

```tsx
// Espejo de Features/Recepcion/Models/CondicionLlegada.cs. El servidor es la
// fuente de verdad y rechaza con 400 cualquier clave que no reconozca, así
// que si esto se desincroniza se ve como un error al confirmar, no como un
// dato incorrecto guardado.
const CONDICIONES_LLEGADA: { clave: string; etiqueta: string }[] = [
    { clave: "AnimalesGolpeados", etiqueta: "Animales con golpes o heridas" },
    { clave: "AnimalesDeshidratados", etiqueta: "Animales deshidratados o decaídos" },
    { clave: "AnimalesMuertos", etiqueta: "Animales muertos" },
    { clave: "JaulasSucias", etiqueta: "Jaulas sucias o con excretas" },
    { clave: "Hacinamiento", etiqueta: "Hacinamiento en la jaula" },
    { clave: "JaulasDanadas", etiqueta: "Jaulas rotas o mal aseguradas" },
    { clave: "Otro", etiqueta: "Otra condición (ver observación)" },
];
```

- [ ] **Step 2: El estado y el cuerpo de la petición**

El modal de «Confirmar llegada» (~líneas 565-600 de `Faenamiento.tsx`) gana dos estados: la respuesta (`boolean | null`) y el conjunto de claves marcadas. El cuerpo de la petición pasa a incluirlos, y `src/api/recepcion.ts` amplía la firma de confirmación.

**Al cerrar el modal se limpian los tres estados** —respuesta, claves y observación—, o el siguiente lote heredaría lo que se respondió del anterior.

- [ ] **Step 3: La pregunta, obligatoria cuando toca**

La movilización ya trae `condicionesClaves`. El modal calcula si el checklist salió incompleto comparando esas claves contra el catálogo de transporte —que el front ya carga para el formulario de envío— y, cuando faltó alguna:

- muestra un aviso de qué condiciones no se verificaron al cargar;
- exige la respuesta: el botón de confirmar se deshabilita mientras sea `null`.

Con el checklist completo, la pregunta se muestra igual pero es opcional.

- [ ] **Step 4: El cuestionario**

Con la respuesta en «no», se despliegan las casillas del catálogo y el campo de observación. **El botón de confirmar se deshabilita si no hay ninguna casilla marcada**, porque el servidor lo rechazaría con 400 y es un error evitable.

**Objetivos táctiles de 44 px**: `min-h-[44px]` en cada casilla. **`min-h-12` no existe en este Tailwind.**

- [ ] **Step 5: Verificar**

```bash
pnpm lint
```
```bash
pnpm exec tsc -b
```
```bash
pnpm build
```

- [ ] **Step 6: Comprobación manual**

Con el API corriendo:
1. Enviar un lote con **todas** las casillas marcadas → al confirmar la llegada, la pregunta aparece pero se puede confirmar sin responderla.
2. Enviar otro con **una sola** marcada → el aviso nombra las que faltaron y no se puede confirmar sin responder.
3. Responder «no» → aparecen las casillas; sin marcar ninguna no deja confirmar; marcando una, sí.
4. Confirmar y reabrir el modal de otro lote → no debe arrastrar nada del anterior.

- [ ] **Step 7: Commit**

```bash
git add src/
git commit -m "feat: la planta responde como llegaron los animales

Obligatorio cuando el checklist de transporte salio incompleto, con un
cuestionario de catalogo cerrado si la respuesta es que no. La
observacion libre se conserva junto al catalogo.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Cierre

- [ ] **Batería completa del API en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: línea base + 15, con 0 fallos.

- [ ] **Front verificado**

```bash
pnpm lint && pnpm exec tsc -b && pnpm build
```

- [ ] **Verificación manual obligatoria**

De la maquetación de un PDF no se puede afirmar nada desde código:

1. **Imprimir la guía de un lote con el checklist completo.** Debe salir
   **idéntica** a las de antes: es la garantía de no regresión.
2. **Imprimir la de un lote con tres casillas sin marcar.** El renglón de lo no
   verificado debe nombrarlas, y leerse a la primera.
3. **Imprimir la guía de una movilización anterior a esta feature** (una que
   tenga `CondicionesClaves` nulo). **No debe aparecer ningún renglón de no
   verificadas**: no se sabe qué se marcó, y afirmar que no se verificó nada
   sería inventar el dato.

- [ ] **Abrir los dos PR**, el del API primero: el front consume campos del DTO que no existen todavía en producción.

## Lo que este plan deja fuera a propósito

- **Contar cuántos animales afectó cada condición de llegada.** Más preciso para reclamar al transportista, más trabajo en una tablet en campo.
- **Bloquear el envío por condiciones críticas.** Requiere decidir cuáles lo son, y esa es una decisión sanitaria que el diseño no puede inventar.
- **Un endpoint que exponga el catálogo de llegada.** Se duplica en el front con su comentario; si aparece un tercer consumidor, se convierte en endpoint.
- **Modelar la reclamación al transportista.** El sistema deja constancia; el proceso no está modelado.
