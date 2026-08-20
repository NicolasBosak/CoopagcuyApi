# Reglas de recepción y declaraciones de movilización — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usa `superpowers:subagent-driven-development` (recomendado) o `superpowers:executing-plans` para ejecutar tarea por tarea. Los pasos usan casillas (`- [ ]`) para seguimiento.

**Objetivo:** Aplicar seis cambios de reglas de negocio sobre recepción y movilización: jaula de 15 cuyes, rango de peso 1200–1500 g, nueva paleta de colores, evidencia fotográfica de novedad clínica con caducidad a 90 días, tipo de forraje adicional y declaración obligatoria de ausencia de antibióticos.

**Arquitectura:** Las reglas dispersas se centralizan en un módulo por repo (`Common/ReglasRecepcion.cs` y `src/domain/reglasRecepcion.ts`) antes de cambiar sus valores, para que cada número tenga un solo sitio. La evidencia fotográfica viaja como base64 dentro del `CuyRegistroDto` ya existente —sin tocar el protocolo de sync offline— y se sube a un contenedor de Blob privado y separado, cuya caducidad la gestiona una política de ciclo de vida de Azure.

**Stack:** ASP.NET Core 8 + EF Core + PostgreSQL (Neon) · React 19 + TypeScript + Vite + Tailwind · xUnit + Shouldly + Respawn · Azure Blob Storage / Azurite.

**Spec:** `docs/superpowers/specs/2026-08-19-reglas-recepcion-y-movilizacion-design.md`

## Restricciones globales

- **Las pruebas solo corren en Docker.** Smart App Control bloquea `dotnet test` en OneDrive. Comando único: `docker compose -f docker-compose.tests.yml run --rm tests`
- **`dotnet ef` tampoco corre en local** por el mismo motivo. Las migraciones se generan dentro de un contenedor Linux (comando exacto en la Tarea 4).
- **El SDK 8 no entiende `.slnx`.** Todo comando `dotnet` apunta a `tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj`, que arrastra al API por referencia de proyecto.
- **Respawn trunca sin `RESTART IDENTITY`:** ninguna prueba puede asumir `Id == 1`. Usa siempre la entidad que devuelve `Sembrador`.
- **`AppDbContextFactory` y `BaseDatosFixture` fijan `Npgsql.EnableLegacyTimestampBehavior=true`.** Si una migración nueva genera `AlterColumn` masivo de fechas, es que el switch no se aplicó: descártala y repite.
- **Valores nuevos, textuales:** capacidad de jaula `15`; peso mínimo `1200` g; peso máximo `1500` g; colores `Blanco`, `Amarillo`, `Rojo`, `Combinado`; retención de foto `90` días; ventana de antibióticos `7` días.
- **Nunca `GroupBy` ni igualdad por instancia de entidad** — siempre por `Id`. Con `AsNoTracking` no hay identity map y agrupar por referencia produce resultados falsos (regresión ya sufrida en este repo).
- **Fuera de alcance:** los umbrales de `880` g en `QRService.cs:221` y `FormFaenamiento.tsx:182,594` son **peso de canal** post-faenamiento. No se tocan.

---

## Estructura de archivos

**API — crear**
- `Common/ReglasRecepcion.cs` — las tres constantes de recepción, única fuente de verdad del backend
- `Features/Recepcion/Validators/RegistrarMovilizacionValidator.cs` — exige la declaración de antibióticos
- `Infrastructure/Data/Migrations/*_EvidenciaFotograficaNovedad.cs` — generada por EF
- `Infrastructure/Data/Migrations/*_DeclaracionAntibioticos.cs` — generada por EF
- `tests/CoopagcuyApi.Tests/Unitarias/ReglasRecepcionTests.cs`
- `tests/CoopagcuyApi.Tests/Integracion/CapacidadJaulaTests.cs`
- `tests/CoopagcuyApi.Tests/Integracion/BandasDePesoTests.cs`
- `tests/CoopagcuyApi.Tests/Integracion/EvidenciaClinicaTests.cs`
- `tests/CoopagcuyApi.Tests/Integracion/DeclaracionAntibioticosTests.cs`

**API — modificar**
- `Features/Recepcion/Services/RecepcionService.cs` — consume `ReglasRecepcion`, bandas nuevas, sube evidencia
- `Features/Recepcion/Models/Novedad.cs` — `FotoUrl`, `FotoExpiraEn`
- `Features/Recepcion/Models/Movilizacion.cs` — `SinAntibioticos7Dias`
- `Features/Recepcion/DTOs/RecepcionDtos.cs` — `FotoBase64`, `TieneFoto`, `SinAntibioticos7Dias`
- `Features/Recepcion/Controllers/RecepcionController.cs` — endpoint de foto + validador de movilización
- `Features/Recepcion/Services/MovilizacionService.cs` — persiste la declaración
- `Features/Recepcion/Services/GuiaMovilizacionService.cs` — línea sanitaria del PDF
- `Infrastructure/Storage/BlobStorageService.cs` — `SubirEvidenciaAsync`
- `Infrastructure/Data/AppDbContext.cs` — configuración de las columnas nuevas
- `docker-compose.tests.yml` + `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs` — Azurite
- `infra/bootstrap.azcli` — contenedor y política de ciclo de vida

**Front — crear**
- `src/domain/reglasRecepcion.ts` — espejo de las reglas + `evaluarCuy()` + `COLORES`
- `src/utils/comprimirImagen.ts` — compresión en canvas antes de guardar
- `src/components/recepcion/EvidenciaNovedad.tsx` — miniatura y visor de la foto

**Front — modificar**
- `src/types/recepcion.ts` — `ColorPelaje`, `fotoBase64`, `tieneFoto`, `sinAntibioticos7Dias`
- `src/components/recepcion/FormLote.tsx` — importa reglas, captura de foto
- `src/components/recepcion/JaulaEnArmado.tsx` — usa `CAPACIDAD_JAULA`
- `src/components/recepcion/FormMovilizacion.tsx` — forraje + casilla de antibióticos
- `src/components/reportes/graficos/AnilloNovedades.tsx` — etiqueta `>1500g`
- `src/pages/Faenamiento.tsx` — muestra la declaración en vez de los días
- `src/api/recepcion.ts` — descarga autenticada de la evidencia
- `src/pages/Recepcion.tsx` — enlaza el visor desde la columna de novedades

**Orden de dependencias:** T1 → T2 (reglas antes de sus valores) · T3 → T4 (Azurite antes de la evidencia) · T5 independiente · T6 → T7 → T8 (módulo de reglas, luego captura, luego visor) · T9 tras T5 (consume el DTO) · T10 al final.

---

### Tarea 1: Reglas centralizadas y capacidad de jaula 15

**Archivos:**
- Crear: `Common/ReglasRecepcion.cs`
- Modificar: `Features/Recepcion/Services/RecepcionService.cs:33-34,148-149,191`
- Test: `tests/CoopagcuyApi.Tests/Unitarias/ReglasRecepcionTests.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/CapacidadJaulaTests.cs`

**Interfaces:**
- Produce: `CoopagcuyApi.Common.ReglasRecepcion` con `const int CapacidadJaula`, `const decimal PesoMinimoGramos`, `const decimal PesoMaximoGramos`. La Tarea 2 consume las dos constantes de peso.

- [ ] **Paso 1: Escribir la prueba unitaria que falla**

Crear `tests/CoopagcuyApi.Tests/Unitarias/ReglasRecepcionTests.cs`:

```csharp
using CoopagcuyApi.Common;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Fija los tres parámetros de recepción. No comprueban lógica: existen para
/// que un cambio accidental de un número de negocio rompa CI en vez de
/// desplegarse en silencio. El front mantiene su propio espejo en
/// src/domain/reglasRecepcion.ts y estos valores deben coincidir.
/// </summary>
public class ReglasRecepcionTests
{
    [Fact]
    public void LaJaulaAdmiteQuinceCuyes() =>
        ReglasRecepcion.CapacidadJaula.ShouldBe(15);

    [Fact]
    public void ElPesoMinimoEsMilDoscientosGramos() =>
        ReglasRecepcion.PesoMinimoGramos.ShouldBe(1200m);

    [Fact]
    public void ElPesoMaximoEsMilQuinientosGramos() =>
        ReglasRecepcion.PesoMaximoGramos.ShouldBe(1500m);
}
```

- [ ] **Paso 2: Ejecutar y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: fallo de compilación — `El nombre del tipo o del espacio de nombres 'ReglasRecepcion' no existe`.

- [ ] **Paso 3: Crear el módulo de reglas**

Crear `Common/ReglasRecepcion.cs`:

```csharp
namespace CoopagcuyApi.Common;

/// <summary>
/// Parámetros de negocio de la recepción en el CAT. Están aquí y no dispersos
/// por el servicio porque cambian por decisión de la cooperativa, no por
/// refactor: quien los ajuste debe encontrarlos en un solo sitio.
///
/// El front mantiene un espejo en src/domain/reglasRecepcion.ts. No se sirven
/// por endpoint a propósito: el wizard de campo evalúa animales SIN señal, y
/// un catálogo remoto lo dejaría sin reglas justo cuando más las necesita.
/// </summary>
public static class ReglasRecepcion
{
    /// Capacidad de la jaula de transporte del CAT — SRS RF-104.
    public const int CapacidadJaula = 15;

    /// Por debajo de este peso el animal se rechaza.
    public const decimal PesoMinimoGramos = 1200m;

    /// Por encima se acepta, pero queda fuera del rango comercial y se anota.
    public const decimal PesoMaximoGramos = 1500m;
}
```

- [ ] **Paso 4: Ejecutar y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: las 3 pruebas nuevas en verde; las 12 existentes siguen en verde.

- [ ] **Paso 5: Escribir la prueba de integración de la jaula**

Crear `tests/CoopagcuyApi.Tests/Integracion/CapacidadJaulaTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La jaula pasó de 20 a 15 cuyes. La segunda prueba cubre la transición: en
/// producción hay jaulas ABIERTAS con más de 15 animales, y el acumulador
/// tiene que cerrarlas sin perder la entrega que llega.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CapacidadJaulaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private static object Entrega(int productoraId, int cuantos) => new
    {
        centroAcopio = "PAT",
        productoraId,
        cuyes = Enumerable.Range(0, cuantos).Select(_ => new
        {
            pesoGramos = 1300m,
            colorPelaje = "Blanco",
            estadoOreja = "Blanda",
            tamanoAnimal = "Normal"
        }).ToArray(),
        enAyunas = true,
        responsableRecepcion = "Operadora de prueba"
    };

    [Fact]
    public async Task DieciseisCuyesLlenanUnaJaulaDeQuinceYAbrenOtra()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", Entrega(productora.Id, 16));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var lotes = await db.Lotes
            .Where(l => l.CentroAcopio == CentroAcopio.PAT)
            .OrderBy(l => l.Id)
            .AsNoTracking()
            .ToListAsync();

        lotes.Count.ShouldBe(2);
        lotes[0].CantidadAnimales.ShouldBe(15);
        lotes[0].Cerrado.ShouldBeTrue();
        lotes[1].CantidadAnimales.ShouldBe(1);
        lotes[1].Cerrado.ShouldBeFalse();
    }

    [Fact]
    public async Task UnaJaulaHeredadaDeDieciochoSeCierraYNoAparecerComoAfectada()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        // Jaula abierta con 18: el estado que dejó la capacidad de 20.
        await using (var db = api.NuevoDbContext())
        {
            db.Lotes.Add(new Lote
            {
                CodigoLote = "PAT-20260818-001",
                ProductoraId = productora.Id,
                CentroAcopio = CentroAcopio.PAT,
                CantidadAnimales = 18,
                PesoTotalGramos = 18 * 1300m,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado,
                Cerrado = false,
                ResponsableRecepcion = "Operadora de prueba"
            });
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", Entrega(productora.Id, 2));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var resultado = await respuesta.Content
            .ReadFromJsonAsync<EntregaResultadoParcial>();

        // La jaula vieja se cierra, pero NO recibió ningún animal: no debe
        // figurar como lote afectado por esta entrega.
        resultado!.LotesAfectados.Count.ShouldBe(1);
        resultado.LotesAfectados[0].CantidadAnimales.ShouldBe(2);

        await using var verificacion = api.NuevoDbContext();
        var vieja = await verificacion.Lotes.AsNoTracking()
            .FirstAsync(l => l.CodigoLote == "PAT-20260818-001");
        vieja.Cerrado.ShouldBeTrue();
        vieja.CantidadAnimales.ShouldBe(18);
    }

    // Proyección mínima: solo lo que esta prueba afirma.
    private record EntregaResultadoParcial(List<LoteParcial> LotesAfectados);
    private record LoteParcial(string CodigoLote, int CantidadAnimales);
}
```

- [ ] **Paso 6: Ejecutar y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `DieciseisCuyesLlenanUnaJaulaDeQuinceYAbrenOtra` falla con `lotes.Count` = 1 (con capacidad 20 los 16 caben en una). `UnaJaulaHeredadaDeDieciocho...` falla con `LotesAfectados.Count` = 2.

- [ ] **Paso 7: Consumir la constante y añadir la guarda**

En `Features/Recepcion/Services/RecepcionService.cs`, borrar las líneas 33-34:

```csharp
    // Capacidad máxima de la jaula de transporte — SRS RF-104
    private const int CapacidadJaula = 20;
```

Sustituir sus tres usos por `ReglasRecepcion.CapacidadJaula` (el `using CoopagcuyApi.Common;` ya está en el archivo).

En el bucle de reparto (línea ~148), reemplazar:

```csharp
                var lote = await ObtenerOCrearJaulaAbiertaAsync(dto, fechaUtc);
                if (!lotesAfectados.Contains(lote))
                    lotesAfectados.Add(lote);

                var espacio = CapacidadJaula - lote.CantidadAnimales;
                var aTomar = Math.Min(espacio, pendientes.Count);
```

por:

```csharp
                var lote = await ObtenerOCrearJaulaAbiertaAsync(dto, fechaUtc);

                // Math.Max(0, ...): una jaula heredada puede tener MÁS
                // animales que la capacidad actual (venía de cuando eran 20).
                // Sin esta guarda el espacio sale negativo; el bucle ya no
                // itera, pero el lote se anotaba como afectado sin haber
                // recibido nada. Se cierra más abajo y la vuelta siguiente
                // abre una jaula nueva.
                var espacio = Math.Max(0, ReglasRecepcion.CapacidadJaula - lote.CantidadAnimales);
                var aTomar = Math.Min(espacio, pendientes.Count);

                if (aTomar > 0 && !lotesAfectados.Contains(lote))
                    lotesAfectados.Add(lote);
```

En la línea ~191, reemplazar `if (lote.CantidadAnimales >= CapacidadJaula)` por `if (lote.CantidadAnimales >= ReglasRecepcion.CapacidadJaula)`.

- [ ] **Paso 8: Ejecutar y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: todo en verde.

- [ ] **Paso 9: Actualizar los comentarios que dicen 20**

En `Features/Recepcion/Controllers/RecepcionController.cs:42` y `:46-47`, y en `Features/Productoras/Models/Lote.cs:29`, cambiar las menciones de "20" por "15". Son comentarios de documentación; si se quedan, contradicen al código.

- [ ] **Paso 10: Commit**

```bash
git add Common/ReglasRecepcion.cs Features/Recepcion/Services/RecepcionService.cs Features/Recepcion/Controllers/RecepcionController.cs Features/Productoras/Models/Lote.cs tests/CoopagcuyApi.Tests/Unitarias/ReglasRecepcionTests.cs tests/CoopagcuyApi.Tests/Integracion/CapacidadJaulaTests.cs
git commit -m "feat: capacidad de jaula 15 y reglas de recepción centralizadas"
```

---

### Tarea 2: Bandas de peso 1200–1500 y retirada de la regla de color negro

**Archivos:**
- Modificar: `Features/Recepcion/Services/RecepcionService.cs:573-604`
- Modificar: `Common/Enums.cs:10-19` (solo comentarios)
- Test: `tests/CoopagcuyApi.Tests/Integracion/BandasDePesoTests.cs`

**Interfaces:**
- Consume: `ReglasRecepcion.PesoMinimoGramos`, `ReglasRecepcion.PesoMaximoGramos` (Tarea 1).
- Produce: ninguna firma nueva. Los valores `TipoNovedad.BajoPeso` y `SobrePeso` conservan su nombre.

- [ ] **Paso 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/BandasDePesoTests.cs`:

```csharp
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Rango operativo nuevo: por debajo de 1200 g se rechaza, entre 1200 y 1500
/// pasa limpio, por encima de 1500 se acepta y se anota. La evaluación corre
/// SIEMPRE en el servidor, también al sincronizar entregas capturadas offline
/// con un bundle antiguo: por eso se prueba por HTTP y no por unidad.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class BandasDePesoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private async Task<CuyGuardado> RegistrarUnCuyAsync(decimal pesoGramos)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuerpo = new
        {
            centroAcopio = "PAT",
            productoraId = productora.Id,
            cuyes = new[]
            {
                new
                {
                    pesoGramos,
                    colorPelaje = "Blanco",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Normal"
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var cuy = await db.CuyRegistros.AsNoTracking().SingleAsync();
        var novedades = await db.Novedades.AsNoTracking()
            .Where(n => n.Tipo != TipoNovedad.SinAyuno)
            .Select(n => n.Tipo)
            .ToListAsync();

        return new CuyGuardado(cuy.Estado, novedades);
    }

    [Fact]
    public async Task MilCientoNoventaYNueveGramosSeRechaza()
    {
        var cuy = await RegistrarUnCuyAsync(1199m);

        cuy.Estado.ShouldBe(EstadoLote.Rechazado);
        cuy.Novedades.ShouldContain(TipoNovedad.BajoPeso);
    }

    [Fact]
    public async Task MilDoscientosGramosSeAceptaSinNovedad()
    {
        var cuy = await RegistrarUnCuyAsync(1200m);

        cuy.Estado.ShouldBe(EstadoLote.Aceptado);
        cuy.Novedades.ShouldBeEmpty();
    }

    [Fact]
    public async Task MilQuinientosGramosSeAceptaSinNovedad()
    {
        // El límite superior es inclusivo: 1500 está DENTRO del rango.
        var cuy = await RegistrarUnCuyAsync(1500m);

        cuy.Estado.ShouldBe(EstadoLote.Aceptado);
        cuy.Novedades.ShouldBeEmpty();
    }

    [Fact]
    public async Task MilQuinientosUnGramosSeAceptaConSobrepeso()
    {
        var cuy = await RegistrarUnCuyAsync(1501m);

        // Sobrepeso NO rechaza: el animal está sano, solo fuera del rango
        // comercial. Es la distinción que pidió la cooperativa.
        cuy.Estado.ShouldBe(EstadoLote.ConNovedad);
        cuy.Novedades.ShouldContain(TipoNovedad.SobrePeso);
    }

    [Fact]
    public async Task ElColorNegroYaNoGeneraNovedad()
    {
        // "Negro" salió del catálogo de captura. Si llegara desde una tablet
        // con caché antigua, se guarda tal cual sin marcar el lote.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuerpo = new
        {
            centroAcopio = "PAT",
            productoraId = productora.Id,
            cuyes = new[]
            {
                new
                {
                    pesoGramos = 1300m,
                    colorPelaje = "Negro",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Normal"
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var cuy = await db.CuyRegistros.AsNoTracking().SingleAsync();

        cuy.Estado.ShouldBe(EstadoLote.Aceptado);
        cuy.ColorPelaje.ShouldBe("Negro");

        var hayColorNoConforme = await db.Novedades.AsNoTracking()
            .AnyAsync(n => n.Tipo == TipoNovedad.ColorNoConforme);
        hayColorNoConforme.ShouldBeFalse();
    }

    private record CuyGuardado(EstadoLote Estado, List<TipoNovedad> Novedades);
}
```

- [ ] **Paso 2: Ejecutar y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `MilCientoNoventaYNueveGramos...` falla (con el mínimo en 850 el animal se acepta), `MilQuinientosUnGramos...` falla (no genera `SobrePeso` con el tope en 1300), `ElColorNegroYaNoGeneraNovedad` falla (sí la genera).

- [ ] **Paso 3: Reescribir la evaluación**

En `Features/Recepcion/Services/RecepcionService.cs`, sustituir el bloque de peso y el de color negro (líneas ~573-604) por:

```csharp
        if (c.PesoGramos < ReglasRecepcion.PesoMinimoGramos)
        {
            rechazado = true;
            motivos.Add($"peso {c.PesoGramos:F0}g bajo el mínimo " +
                        $"({ReglasRecepcion.PesoMinimoGramos:F0}g)");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.BajoPeso,
                $"Peso {c.PesoGramos:F0}g por debajo del mínimo " +
                $"({ReglasRecepcion.PesoMinimoGramos:F0}g). Animal rechazado.",
                responsable, c.PesoGramos));
        }
        else if (c.PesoGramos > ReglasRecepcion.PesoMaximoGramos)
        {
            // No rechaza: el animal está sano, solo queda fuera del rango
            // comercial. Mezclarlo con el bajo peso borraría esa diferencia.
            motivos.Add($"sobre el rango operativo ({c.PesoGramos:F0}g)");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.SobrePeso,
                $"Peso {c.PesoGramos:F0}g sobre el rango operativo " +
                $"(máx. {ReglasRecepcion.PesoMaximoGramos:F0}g).",
                responsable, c.PesoGramos));
        }
```

Borrar por completo el bloque:

```csharp
        if (c.ColorPelaje.Equals("Negro", StringComparison.OrdinalIgnoreCase))
        {
            motivos.Add("piel negra");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.ColorNoConforme,
                "Piel completamente negra. No conforme para mercado formal.",
                responsable, null));
        }
```

Los bloques de `EstadoOreja == "Dura"` y `SignosClinicos` se quedan intactos.

- [ ] **Paso 4: Actualizar los comentarios del enum**

En `Common/Enums.cs`, líneas 10-19:

```csharp
public enum TipoNovedad
{
    BajoPeso,        // < 1200g: animal rechazado
    OrejaDura,       // animal viejo
    // Ya no se genera: "Negro" salió del catálogo de colores en 2026-08.
    // El valor permanece por las filas históricas y por AnilloNovedades.
    ColorNoConforme,
    SinAyuno,
    SobrePeso,       // > 1500g: fuera del rango comercial, se acepta
    SignosClinicos,  // condición sanitaria visual con observación
    Otro
}
```

- [ ] **Paso 5: Ejecutar y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: las 5 pruebas nuevas en verde, todas las anteriores también.

- [ ] **Paso 6: Commit**

```bash
git add Features/Recepcion/Services/RecepcionService.cs Common/Enums.cs tests/CoopagcuyApi.Tests/Integracion/BandasDePesoTests.cs
git commit -m "feat: rango de peso 1200-1500 g y retirada de la regla de color negro"
```

---

### Tarea 3: Azurite en las pruebas y subida de evidencias al Blob

**Archivos:**
- Modificar: `docker-compose.tests.yml`
- Modificar: `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs:60-70`
- Modificar: `Infrastructure/Storage/BlobStorageService.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/EvidenciaClinicaTests.cs` (primera prueba)

**Interfaces:**
- Produce: `IBlobStorageService.SubirEvidenciaAsync(string nombre, byte[] jpeg) → Task<string>` (devuelve la URI absoluta del blob) y `IBlobStorageService.DescargarEvidenciaAsync(string nombre) → Task<byte[]?>` (null si el blob ya no existe). La Tarea 4 consume ambas.

> **Por qué esta tarea existe.** `ApiFactory` fija hoy `AzureBlob__ConnectionString` a cadena vacía y `BlobStorageService` **lanza en su constructor** si está vacía. En cuanto `RecepcionService` dependa del blob, el grafo de DI reventaría en TODAS las pruebas de recepción, no solo en las de foto. Se añade Azurite —el emulador oficial— en vez de un doble, para no romper la regla del `ApiFactory` de ejercitar el pipeline real.

- [ ] **Paso 1: Añadir Azurite al compose de pruebas**

En `docker-compose.tests.yml`, añadir el servicio antes de `tests:`:

```yaml
  azurite:
    # Emulador oficial de Azure Storage. Se añade porque BlobStorageService
    # lanza si no hay cadena de conexión: sin esto, cualquier prueba que
    # resuelva el servicio por DI falla al construirlo, aunque no suba nada.
    image: mcr.microsoft.com/azure-storage/azurite:latest
    command: azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck
    ports:
      - "10000:10000"
    healthcheck:
      test: ["CMD", "nc", "-z", "127.0.0.1", "10000"]
      interval: 3s
      timeout: 3s
      retries: 20
```

En el servicio `tests`, añadir la dependencia y la variable:

```yaml
    depends_on:
      postgres:
        condition: service_healthy
      azurite:
        condition: service_healthy
    environment:
      TEST_DB_CONNECTION: "Host=postgres;Port=5432;Database=coopagcuy_test;Username=postgres;Password=postgres"
      # Cuenta de desarrollo de Azurite: credencial pública y fija del
      # emulador, documentada por Microsoft. No es un secreto.
      TEST_BLOB_CONNECTION: "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;"
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
      NUGET_PACKAGES: /nuget
```

- [ ] **Paso 2: Apuntar `ApiFactory` a Azurite**

En `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs`, añadir junto a `Cadena`:

```csharp
    private const string CadenaBlobPorDefecto =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        "BlobEndpoint=http://localhost:10000/devstoreaccount1;";

    /// Dentro del compose llega por variable de entorno; fuera, apunta al
    /// Azurite publicado en 10000. Nunca a una cuenta real de Azure.
    public static string CadenaBlob =>
        Environment.GetEnvironmentVariable("TEST_BLOB_CONNECTION")
        ?? CadenaBlobPorDefecto;
```

En el constructor estático, sustituir la línea que fija la cadena vacía:

```csharp
        Environment.SetEnvironmentVariable("AzureBlob__ConnectionString", CadenaBlob);
        Environment.SetEnvironmentVariable("AzureBlob__ContainerName", "qr-test");
        Environment.SetEnvironmentVariable("AzureBlob__ContainerEvidencias", "evidencias-test");
```

- [ ] **Paso 3: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/EvidenciaClinicaTests.cs`:

```csharp
using System.Text;
using CoopagcuyApi.Infrastructure.Storage;
using CoopagcuyApi.Tests.Infra;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Evidencia fotográfica de novedad clínica: subida al contenedor privado,
/// lectura autorizada y caducidad a 90 días.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class EvidenciaClinicaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static IBlobStorageService ServicioBlob()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureBlob:ConnectionString"] = ApiFactory.CadenaBlob,
                ["AzureBlob:ContainerEvidencias"] = "evidencias-test"
            })
            .Build();

        return new BlobStorageService(configuracion);
    }

    [Fact]
    public async Task LaEvidenciaSubeYSeVuelveADescargarIgual()
    {
        var servicio = ServicioBlob();
        var contenido = Encoding.UTF8.GetBytes("bytes-de-prueba-de-evidencia");
        var nombre = $"prueba-{Guid.NewGuid():N}.jpg";

        var uri = await servicio.SubirEvidenciaAsync(nombre, contenido);

        uri.ShouldContain(nombre);

        var descargado = await servicio.DescargarEvidenciaAsync(nombre);
        descargado.ShouldNotBeNull();
        descargado.ShouldBe(contenido);
    }

    [Fact]
    public async Task DescargarUnaEvidenciaInexistenteDevuelveNulo()
    {
        // Es el caso real tras el borrado por política de ciclo de vida: la
        // fila sigue en la base y el blob ya no está.
        var servicio = ServicioBlob();

        var descargado = await servicio.DescargarEvidenciaAsync(
            $"no-existe-{Guid.NewGuid():N}.jpg");

        descargado.ShouldBeNull();
    }
}
```

- [ ] **Paso 4: Ejecutar y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: fallo de compilación — `IBlobStorageService` no contiene `SubirEvidenciaAsync`.

- [ ] **Paso 5: Ampliar el servicio de Blob**

Sustituir por completo `Infrastructure/Storage/BlobStorageService.cs`:

```csharp
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CoopagcuyApi.Infrastructure.Storage;

public interface IBlobStorageService
{
    Task<string> SubirQRAsync(string codigoLote, byte[] imagenPng);

    /// Sube una evidencia clínica al contenedor PRIVADO y devuelve su URI.
    Task<string> SubirEvidenciaAsync(string nombre, byte[] jpeg);

    /// Devuelve los bytes de la evidencia, o null si el blob ya no existe
    /// (caso normal tras el borrado por política de ciclo de vida).
    Task<byte[]?> DescargarEvidenciaAsync(string nombre);
}

public class BlobStorageService(IConfiguration configuration) : IBlobStorageService
{
    // IsNullOrWhiteSpace y no solo null: appsettings.json trae la clave
    // como cadena vacía y el valor real llega por user-secrets o entorno.
    // Sin esta guardia el error aparecería recién al generar el primer QR.
    private readonly string _connectionString =
        !string.IsNullOrWhiteSpace(configuration["AzureBlob:ConnectionString"])
            ? configuration["AzureBlob:ConnectionString"]!
            : throw new InvalidOperationException(
                "AzureBlob:ConnectionString no configurado.");

    private readonly string _containerName =
        configuration["AzureBlob:ContainerName"] ?? "qr-coopagcuy";

    // Contenedor SEPARADO del de QR, por dos motivos: el de QR es público a
    // propósito (tiene que escanearse desde fuera) y una foto de defectos de
    // un proveedor no debe serlo; y la política de caducidad se aplica por
    // contenedor, así que compartirlo borraría también los QR a los 90 días.
    private readonly string _containerEvidencias =
        configuration["AzureBlob:ContainerEvidencias"] ?? "evidencias-clinicas";

    public async Task<string> SubirQRAsync(string codigoLote, byte[] imagenPng)
    {
        var cliente = new BlobServiceClient(_connectionString);
        var contenedor = cliente.GetBlobContainerClient(_containerName);

        // Crear el contenedor si no existe, con acceso público de lectura
        await contenedor.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobNombre = $"qr/{codigoLote}.png";
        var blob = contenedor.GetBlobClient(blobNombre);

        using var stream = new MemoryStream(imagenPng);
        await blob.UploadAsync(stream, overwrite: true);

        return blob.Uri.ToString();
    }

    public async Task<string> SubirEvidenciaAsync(string nombre, byte[] jpeg)
    {
        var contenedor = await ContenedorEvidenciasAsync();
        var blob = contenedor.GetBlobClient(nombre);

        using var stream = new MemoryStream(jpeg);
        await blob.UploadAsync(stream, overwrite: true);

        return blob.Uri.ToString();
    }

    public async Task<byte[]?> DescargarEvidenciaAsync(string nombre)
    {
        var contenedor = await ContenedorEvidenciasAsync();
        var blob = contenedor.GetBlobClient(nombre);

        try
        {
            var respuesta = await blob.DownloadContentAsync();
            return respuesta.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // La política de ciclo de vida ya borró el blob. No es un error:
            // la fila de la novedad sobrevive al binario por diseño.
            return null;
        }
    }

    private async Task<BlobContainerClient> ContenedorEvidenciasAsync()
    {
        var cliente = new BlobServiceClient(_connectionString);
        var contenedor = cliente.GetBlobContainerClient(_containerEvidencias);

        // PublicAccessType.None, no Blob: la evidencia se sirve solo a través
        // del endpoint autenticado del API.
        await contenedor.CreateIfNotExistsAsync(PublicAccessType.None);
        return contenedor;
    }
}
```

- [ ] **Paso 6: Ejecutar y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: las 2 pruebas nuevas en verde. Si Azurite no arranca, el fallo dice `Connection refused` sobre el puerto 10000 — comprobar el healthcheck del compose.

- [ ] **Paso 7: Commit**

```bash
git add docker-compose.tests.yml tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs Infrastructure/Storage/BlobStorageService.cs tests/CoopagcuyApi.Tests/Integracion/EvidenciaClinicaTests.cs
git commit -m "feat: contenedor privado de evidencias y Azurite en las pruebas"
```

---

### Tarea 4: Evidencia fotográfica de novedad clínica

**Archivos:**
- Modificar: `Features/Recepcion/Models/Novedad.cs`
- Modificar: `Features/Recepcion/DTOs/RecepcionDtos.cs:6-11,101-108`
- Modificar: `Infrastructure/Data/AppDbContext.cs:83-93`
- Modificar: `Features/Recepcion/Services/RecepcionService.cs`
- Modificar: `Features/Recepcion/Controllers/RecepcionController.cs`
- Crear: `Infrastructure/Data/Migrations/*_EvidenciaFotograficaNovedad.cs` (generada)
- Test: `tests/CoopagcuyApi.Tests/Integracion/EvidenciaClinicaTests.cs` (ampliar)

**Interfaces:**
- Consume: `SubirEvidenciaAsync`, `DescargarEvidenciaAsync` (Tarea 3).
- Produce: `CuyRegistroDto.FotoBase64` (`string?`), `NovedadResponseDto.TieneFoto` (`bool`), endpoint `GET /api/recepcion/novedades/{id}/foto`. La Tarea 7 consume el primero; la Tarea 8 consume los otros dos.

- [ ] **Paso 1: Añadir las columnas al modelo**

En `Features/Recepcion/Models/Novedad.cs`, antes del cierre de la clase:

```csharp
    // Evidencia fotográfica para reclamar al proveedor. Solo la llevan las
    // novedades de tipo SignosClinicos. El binario vive en Blob, no en la
    // base: aquí queda la referencia y la fecha en que deja de ser válida.
    public string? FotoUrl { get; set; }

    // El blob lo borra una política de ciclo de vida de Azure a los 90 días.
    // Esta fecha permite que el API deje de servir la foto en el momento
    // exacto, sin depender de cuándo pase el barrido de Azure.
    public DateTime? FotoExpiraEn { get; set; }
```

En `Infrastructure/Data/AppDbContext.cs`, dentro de `modelBuilder.Entity<Novedad>`:

```csharp
            e.Property(n => n.FotoUrl).HasMaxLength(500);
```

- [ ] **Paso 2: Generar la migración**

`dotnet ef` no corre en el Windows del equipo (Smart App Control bloquea el DLL en OneDrive). Desde Git Bash, en la raíz del repo:

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd)":/src -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=x;Username=x;Password=x" mcr.microsoft.com/dotnet/sdk:8.0 sh -c "dotnet tool install --global dotnet-ef --version 8.* && export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add EvidenciaFotograficaNovedad --project CoopagcuyApi.csproj"
```

Abrir el archivo generado y **verificar que solo contiene dos `AddColumn`** sobre `Novedades`. Si aparece cualquier `AlterColumn` de fechas, el switch `EnableLegacyTimestampBehavior` no se aplicó: borrar la migración y repetir.

- [ ] **Paso 3: Escribir la prueba que falla**

Añadir a `tests/CoopagcuyApi.Tests/Integracion/EvidenciaClinicaTests.cs`:

```csharp
    private const string CedulaProductora = "0104576277";

    // JPEG mínimo válido: cabecera SOI + marcador APP0 + EOI. Basta para
    // comprobar el viaje de ida y vuelta sin incrustar una foto real.
    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    private async Task<int> RegistrarConFotoAsync(string? fotoBase64)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CoopagcuyApi.Common.CentroAcopio.PAT);

        var cuerpo = new
        {
            centroAcopio = "PAT",
            productoraId = productora.Id,
            cuyes = new[]
            {
                new
                {
                    pesoGramos = 1300m,
                    colorPelaje = "Blanco",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Normal",
                    signosClinicos = "Lesión en la oreja derecha",
                    fotoBase64
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var novedad = await db.Novedades.AsNoTracking()
            .SingleAsync(n => n.Tipo == CoopagcuyApi.Common.TipoNovedad.SignosClinicos);
        return novedad.Id;
    }

    [Fact]
    public async Task LaNovedadClinicaConFotoGuardaUrlYCaducidadA90Dias()
    {
        var id = await RegistrarConFotoAsync(Convert.ToBase64String(JpegMinimo));

        await using var db = api.NuevoDbContext();
        var novedad = await db.Novedades.AsNoTracking().SingleAsync(n => n.Id == id);

        novedad.FotoUrl.ShouldNotBeNullOrWhiteSpace();
        novedad.FotoExpiraEn.ShouldNotBeNull();

        var dias = (novedad.FotoExpiraEn.Value - DateTime.UtcNow).TotalDays;
        dias.ShouldBeInRange(89.9, 90.1);
    }

    [Fact]
    public async Task LaFotoSeDescargaPorElEndpointAutenticado()
    {
        var id = await RegistrarConFotoAsync(Convert.ToBase64String(JpegMinimo));

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/novedades/{id}/foto");

        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("image/jpeg");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.ShouldBe(JpegMinimo);
    }

    [Fact]
    public async Task ElEndpointDeFotoExigeAutenticacion()
    {
        var id = await RegistrarConFotoAsync(Convert.ToBase64String(JpegMinimo));

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/recepcion/novedades/{id}/foto");

        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnaFotoCaducadaDevuelve404AunqueElBlobSigaAhi()
    {
        var id = await RegistrarConFotoAsync(Convert.ToBase64String(JpegMinimo));

        await using (var db = api.NuevoDbContext())
        {
            var novedad = await db.Novedades.SingleAsync(n => n.Id == id);
            novedad.FotoExpiraEn = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/novedades/{id}/foto");

        // La fecha manda sobre el blob: el API deja de servirla en el momento
        // exacto, sin esperar al barrido de Azure.
        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnaFotoDeMasDeDosMegasSeRechaza()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CoopagcuyApi.Common.CentroAcopio.PAT);

        var demasiado = Convert.ToBase64String(new byte[2 * 1024 * 1024 + 1]);

        var cuerpo = new
        {
            centroAcopio = "PAT",
            productoraId = productora.Id,
            cuyes = new[]
            {
                new
                {
                    pesoGramos = 1300m,
                    colorPelaje = "Blanco",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Normal",
                    signosClinicos = "Lesión",
                    fotoBase64 = demasiado
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);

        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SinFotoLaNovedadClinicaSeRegistraIgual()
    {
        var id = await RegistrarConFotoAsync(fotoBase64: null);

        await using var db = api.NuevoDbContext();
        var novedad = await db.Novedades.AsNoTracking().SingleAsync(n => n.Id == id);

        novedad.FotoUrl.ShouldBeNull();
        novedad.FotoExpiraEn.ShouldBeNull();
    }
```

Añadir al principio del archivo los `using` que faltan: `System.Net.Http.Json`, `Microsoft.EntityFrameworkCore`.

- [ ] **Paso 4: Ejecutar y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: fallo de compilación en `novedad.FotoUrl` si el Paso 1 no se aplicó; si se aplicó, las pruebas fallan con `FotoUrl` nulo y `404` en el endpoint inexistente.

- [ ] **Paso 5: Añadir el campo al DTO de entrada y de salida**

En `Features/Recepcion/DTOs/RecepcionDtos.cs`, en `CuyRegistroDto`:

```csharp
public class CuyRegistroDto
{
    public decimal PesoGramos { get; set; }
    public string ColorPelaje { get; set; } = string.Empty;
    public string EstadoOreja { get; set; } = string.Empty;
    public string TamanoAnimal { get; set; } = string.Empty;
    public string? SignosClinicos { get; set; }

    // Evidencia del defecto, en base64 y sin prefijo data:. Viaja dentro del
    // cuy y no por multipart aparte: así el sync offline no cambia de forma
    // y la idempotencia por IdCliente cubre también la foto.
    public string? FotoBase64 { get; set; }
}
```

En `NovedadResponseDto`, añadir el último campo:

```csharp
public record NovedadResponseDto(
    int Id,
    string Tipo,
    string Descripcion,
    decimal? PesoRegistradoGramos,
    DateTime FechaRegistro,
    string RegistradoPor,
    // El front solo necesita saber si hay algo que pedir: el binario se
    // descarga aparte y solo si la ficha se abre.
    bool TieneFoto
);
```

Buscar dónde se construye `NovedadResponseDto` en `RecepcionService.cs` y añadir el argumento:

```csharp
                n.FotoUrl != null && n.FotoExpiraEn > DateTime.UtcNow
```

- [ ] **Paso 6: Subir la evidencia ANTES de la transacción**

En `Features/Recepcion/Services/RecepcionService.cs`, cambiar la firma del servicio:

```csharp
public class RecepcionService(AppDbContext db, IBlobStorageService blobService)
    : IRecepcionService
```

Añadir `using CoopagcuyApi.Infrastructure.Storage;` arriba y estas constantes junto a las demás:

```csharp
    // Retención de la evidencia fotográfica. Debe coincidir con la política
    // de ciclo de vida del contenedor evidencias-clinicas en Azure.
    private const int DiasRetencionEvidencia = 90;

    private const int MaxBytesEvidencia = 2 * 1024 * 1024;
```

Añadir el método privado:

```csharp
    /// <summary>
    /// Sube las fotos y devuelve la URL de cada una indexada por su posición
    /// en dto.Cuyes.
    ///
    /// Corre FUERA de la transacción a propósito. El registro de la entrega
    /// va dentro de CreateExecutionStrategy, que REINTENTA el delegado ante
    /// fallos transitorios de Neon: subir ahí dentro duplicaría blobs en cada
    /// reintento y mantendría el advisory lock del CAT abierto durante una
    /// subida de red. El coste es que una transacción fallida puede dejar un
    /// blob huérfano — lo recoge la política de ciclo de vida a los 90 días.
    /// Lo que NO puede quedar huérfana es una fila: la URL se escribe dentro
    /// de la transacción.
    /// </summary>
    private async Task<Dictionary<int, string>> SubirEvidenciasAsync(
        RegistrarEntregaDto dto)
    {
        var urls = new Dictionary<int, string>();

        for (var i = 0; i < dto.Cuyes.Count; i++)
        {
            var foto = dto.Cuyes[i].FotoBase64;
            if (string.IsNullOrWhiteSpace(foto)) continue;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(foto);
            }
            catch (FormatException)
            {
                throw new ArgumentException(
                    $"La foto del cuy #{i + 1} no es base64 válido.");
            }

            if (bytes.Length > MaxBytesEvidencia)
                throw new ArgumentException(
                    $"La foto del cuy #{i + 1} pesa {bytes.Length / 1024} KB y " +
                    $"el máximo es {MaxBytesEvidencia / 1024} KB.");

            var nombre = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}.jpg";
            urls[i] = await blobService.SubirEvidenciaAsync(nombre, bytes);
        }

        return urls;
    }
```

En `RegistrarEntregaAsync`, justo después de `await ResolverProductoraPorCedulaAsync(dto);` y **antes** de `var estrategia = ...`:

```csharp
        // Fuera de la transacción: ver el comentario de SubirEvidenciasAsync.
        var evidencias = await SubirEvidenciasAsync(dto);
```

`EvaluarCuyIndividual` necesita la URL. Cambiar su firma y el punto donde se marca la novedad clínica:

```csharp
    private static (CuyRegistro cuy, List<Novedad> novedades) EvaluarCuyIndividual(
        CuyRegistroDto c, int numero, string responsable,
        string? fotoUrl, int diasRetencion)
```

y dentro, en el bloque de signos clínicos:

```csharp
        if (!string.IsNullOrWhiteSpace(c.SignosClinicos))
        {
            motivos.Add($"signos clínicos: {c.SignosClinicos.Trim()}");

            var novedadClinica = NovedadDeCuy(numero, TipoNovedad.SignosClinicos,
                $"Condición sanitaria con observación: {c.SignosClinicos.Trim()}",
                responsable, null);

            // La evidencia se ancla a la novedad clínica, la única que se
            // reclama al proveedor.
            if (fotoUrl is not null)
            {
                novedadClinica.FotoUrl = fotoUrl;
                novedadClinica.FotoExpiraEn = DateTime.UtcNow.AddDays(diasRetencion);
            }

            novedades.Add(novedadClinica);
        }
```

En el bucle de reparto, la llamada necesita saber a qué índice del DTO corresponde el cuy que se está sacando de la cola. Sustituir el `Queue<CuyRegistroDto>` por una cola de pares:

```csharp
            var pendientes = new Queue<(int Indice, CuyRegistroDto Cuy)>(
                dto.Cuyes.Select((c, i) => (i, c)));
```

y dentro del `for`:

```csharp
                    var (indice, cuyDto) = pendientes.Dequeue();
                    var numero = lote.CantidadAnimales + 1;

                    var (cuy, novedades) = EvaluarCuyIndividual(
                        cuyDto, numero, dto.ResponsableRecepcion,
                        evidencias.GetValueOrDefault(indice), DiasRetencionEvidencia);
```

- [ ] **Paso 7: Traducir `ArgumentException` a 400 y añadir el endpoint de lectura**

En `Features/Recepcion/Controllers/RecepcionController.cs`, en `RegistrarEntrega`, añadir el `catch` **antes** del de `InvalidOperationException` (`ArgumentException` no hereda de él, pero el orden deja clara la intención):

```csharp
        catch (EvidenciaInvalidaException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
```

> **Corregido durante la ejecución.** El plan decía `catch (ArgumentException)`.
> Es demasiado ancho: `ArgumentException` es padre de `ArgumentNullException` y
> `ArgumentOutOfRangeException`, así que cualquier bug de esa familia en el árbol
> de `RegistrarEntregaAsync` dejaba de llegar al manejador global como 500 y se
> convertía en un 400 que además expone el mensaje interno de .NET al cliente.
> Se creó `Common/Exceptions/EvidenciaInvalidaException.cs` siguiendo el patrón
> de `EntregaDuplicadaException`. El mismo `catch` hace falta en
> `ResolverVinculacion`: sin él, una vinculación con foto inválida da 500 y
> queda irresoluble en la bandeja del administrador.

Añadir el endpoint al final de la clase:

```csharp
    /// <summary>
    /// Evidencia fotográfica de una novedad clínica. Se sirve a través del API
    /// y no por URL directa al Blob porque el contenedor es privado: es una
    /// foto de defectos atribuida a un proveedor, no un QR público.
    /// </summary>
    [HttpGet("novedades/{id:int}/foto")]
    [Authorize]
    public async Task<IActionResult> FotoDeNovedad(int id)
    {
        var bytes = await service.ObtenerFotoNovedadAsync(id);

        return bytes is null
            ? NotFound(new { mensaje = "La evidencia no existe o ya caducó." })
            : File(bytes, "image/jpeg");
    }
```

Añadir a la interfaz `IRecepcionService`:

```csharp
    /// Bytes de la evidencia, o null si la novedad no tiene foto, si ya
    /// caducó, o si el blob fue borrado por la política de ciclo de vida.
    Task<byte[]?> ObtenerFotoNovedadAsync(int novedadId);
```

e implementarlo en `RecepcionService`:

```csharp
    public async Task<byte[]?> ObtenerFotoNovedadAsync(int novedadId)
    {
        var novedad = await db.Novedades.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == novedadId);

        if (novedad?.FotoUrl is null) return null;

        // La fecha manda sobre el blob: en cuanto caduca dejamos de servirla,
        // sin esperar a que pase el barrido de Azure.
        if (novedad.FotoExpiraEn is null || novedad.FotoExpiraEn <= DateTime.UtcNow)
            return null;

        // El nombre del blob es lo que sigue al nombre del contenedor en la
        // URI; se guarda la URI completa para poder diagnosticar desde la base.
        var nombre = new Uri(novedad.FotoUrl).Segments[^3..]
            .Aggregate(string.Empty, (acumulado, s) => acumulado + s)
            .TrimStart('/');

        return await blobService.DescargarEvidenciaAsync(nombre);
    }
```

- [ ] **Paso 8: Ejecutar y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: las 6 pruebas de evidencia en verde. Si `LaFotoSeDescargaPorElEndpointAutenticado` falla con 404, el nombre del blob se está reconstruyendo mal desde la URI: imprimir `novedad.FotoUrl` y ajustar el recorte de segmentos (el nombre tiene forma `yyyy/MM/guid.jpg`, tres segmentos).

- [ ] **Paso 9: Commit**

```bash
git add Features/Recepcion Infrastructure/Data tests/CoopagcuyApi.Tests/Integracion/EvidenciaClinicaTests.cs
git commit -m "feat: evidencia fotográfica de novedad clínica con caducidad a 90 días"
```

---

### Tarea 5: Declaración de ausencia de antibióticos

**Archivos:**
- Modificar: `Features/Recepcion/Models/Movilizacion.cs`
- Modificar: `Features/Recepcion/DTOs/RecepcionDtos.cs` (`RegistrarMovilizacionDto`, `MovilizacionResponseDto`)
- Crear: `Features/Recepcion/Validators/RegistrarMovilizacionValidator.cs`
- Modificar: `Features/Recepcion/Controllers/RecepcionController.cs:224-240`
- Modificar: `Features/Recepcion/Services/MovilizacionService.cs:55-70,130-145`
- Modificar: `Features/Recepcion/Services/GuiaMovilizacionService.cs:236-241`
- Crear: migración `DeclaracionAntibioticos`
- Test: `tests/CoopagcuyApi.Tests/Integracion/DeclaracionAntibioticosTests.cs`
- Test: `tests/CoopagcuyApi.Tests/Unitarias/TextosGuiaTests.cs` (ampliar)

**Interfaces:**
- Produce: `RegistrarMovilizacionDto.SinAntibioticos7Dias` (`bool?`), `MovilizacionResponseDto.SinAntibioticos7Dias` (`bool?`), `GuiaMovilizacionService.TextoDeclaracionSanitaria(Movilizacion) → string` (público y estático, para poder fijarlo por unidad). La Tarea 9 del front consume el primero.

- [ ] **Paso 1: Añadir la columna al modelo**

En `Features/Recepcion/Models/Movilizacion.cs`, junto a los otros campos de declaración:

```csharp
    // Declaración de tratamientos básicos (guía de movilización)
    public string? TipoForraje { get; set; }

    // Legado: se dejó de capturar en 2026-08, sustituido por la declaración
    // de abajo. La columna se conserva para que reimprimir una guía antigua
    // no pierda el dato.
    public int? DiasRetiroMedicamentos { get; set; }

    // Nulo = movilización anterior al cambio, nunca se preguntó.
    // True = el responsable declaró que no recibieron antibióticos en 7 días.
    // El validador exige true en los registros nuevos, así que false no
    // debería aparecer nunca; se admite por no mentirle al tipo.
    public bool? SinAntibioticos7Dias { get; set; }
```

- [ ] **Paso 2: Generar la migración**

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd)":/src -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=x;Username=x;Password=x" mcr.microsoft.com/dotnet/sdk:8.0 sh -c "dotnet tool install --global dotnet-ef --version 8.* && export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add DeclaracionAntibioticos --project CoopagcuyApi.csproj"
```

Verificar que solo contiene un `AddColumn<bool>` **nullable** sobre `Movilizaciones`.

- [ ] **Paso 3: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/DeclaracionAntibioticosTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La pregunta por los días de retiro se sustituyó por una declaración
/// explícita de que los cuyes no recibieron antibióticos en los últimos 7
/// días. Es obligatoria: sin ella la guía de movilización no tendría
/// respaldo sanitario de nadie.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class DeclaracionAntibioticosTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";
    private const string CodigoLote = "PAT-20260819-001";

    private async Task SembrarLoteAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();
        db.Lotes.Add(new Lote
        {
            CodigoLote = CodigoLote,
            ProductoraId = productora.Id,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 5,
            PesoTotalGramos = 5 * 1300m,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            Cerrado = true,
            ResponsableRecepcion = "Operadora de prueba"
        });
        await db.SaveChangesAsync();
    }

    private static object Movilizacion(bool? declaracion) => new
    {
        conductor = "Juan Pérez",
        cantidadMovilizada = 5,
        condicionesTransporte = Array.Empty<string>(),
        tipoForraje = "Concentrado sin proteína animal",
        sinAntibioticos7Dias = declaracion,
        responsableDespacho = "Responsable de prueba"
    };

    [Fact]
    public async Task SinLaDeclaracionSeRechazaCon400()
    {
        await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: null));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        cuerpo!.Mensaje.ShouldContain("antibióticos");
    }

    [Fact]
    public async Task DeclararFalsoTambienSeRechaza()
    {
        await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: false));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConLaDeclaracionSeRegistraYSeGuarda()
    {
        await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: true));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var db = api.NuevoDbContext();
        var movilizacion = await db.Movilizaciones.AsNoTracking().SingleAsync();

        movilizacion.SinAntibioticos7Dias.ShouldBe(true);
        movilizacion.TipoForraje.ShouldBe("Concentrado sin proteína animal");
        movilizacion.DiasRetiroMedicamentos.ShouldBeNull();
    }

    [Fact]
    public async Task LaGuiaSeGeneraParaUnaMovilizacionDeclarada()
    {
        await SembrarLoteAsync();

        await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: true));

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{CodigoLote}/guia");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");
    }

    // No se llama "Mensaje": un record no puede tener un miembro con el mismo
    // nombre que su tipo contenedor (CS0542).
    private record RespuestaError(string Mensaje);
}
```

Ampliar `tests/CoopagcuyApi.Tests/Unitarias/TextosGuiaTests.cs` con:

```csharp
    [Fact]
    public void LaGuiaDeclaraLaAusenciaDeAntibioticosConElResponsable()
    {
        var movilizacion = new Movilizacion
        {
            ResponsableDespacho = "Nicolas Nieves",
            SinAntibioticos7Dias = true
        };

        var texto = GuiaMovilizacionService.TextoDeclaracionSanitaria(movilizacion);

        texto.ShouldContain("Sin antibióticos últimos 7 días");
        texto.ShouldContain("Nicolas Nieves");
    }

    [Fact]
    public void UnaMovilizacionHeredadaConservaLaLineaDeDiasDeRetiro()
    {
        // Reimprimir una guía anterior al cambio no puede perder el dato que
        // sí se capturó entonces.
        var movilizacion = new Movilizacion
        {
            ResponsableDespacho = "Nicolas Nieves",
            SinAntibioticos7Dias = null,
            DiasRetiroMedicamentos = 12
        };

        var texto = GuiaMovilizacionService.TextoDeclaracionSanitaria(movilizacion);

        texto.ShouldContain("12 días");
    }

    [Fact]
    public void UnaMovilizacionHeredadaSinDatoDiceSinDeclaracion()
    {
        var movilizacion = new Movilizacion
        {
            ResponsableDespacho = "Nicolas Nieves",
            SinAntibioticos7Dias = null,
            DiasRetiroMedicamentos = null
        };

        var texto = GuiaMovilizacionService.TextoDeclaracionSanitaria(movilizacion);

        texto.ShouldContain("sin declaración");
    }
```

- [ ] **Paso 4: Ejecutar y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: fallo de compilación — `TextoDeclaracionSanitaria` no existe; y las de integración fallan con `Created` en vez de `BadRequest`.

- [ ] **Paso 5: Cambiar los DTOs**

En `RegistrarMovilizacionDto`, quitar `DiasRetiroMedicamentos` y añadir:

```csharp
    public string? TipoForraje { get; set; }

    // Obligatoria y true: la exige RegistrarMovilizacionValidator. Es bool?
    // y no bool para poder distinguir "no vino en el cuerpo" de "vino false"
    // y dar un mensaje de error distinto en cada caso.
    public bool? SinAntibioticos7Dias { get; set; }
```

En `MovilizacionResponseDto`, **conservar** `DiasRetiroMedicamentos` y añadir al final:

```csharp
    int? DiasRetiroMedicamentos,
    bool? SinAntibioticos7Dias,
```

- [ ] **Paso 6: Crear el validador**

Crear `Features/Recepcion/Validators/RegistrarMovilizacionValidator.cs`:

```csharp
using CoopagcuyApi.Features.Recepcion.DTOs;
using FluentValidation;

namespace CoopagcuyApi.Features.Recepcion.Validators;

/// <summary>
/// La declaración sanitaria es lo que da valor probatorio a la guía de
/// movilización: sin ella el documento afirma un transporte sin que nadie
/// responda por el estado de los animales.
/// </summary>
public class RegistrarMovilizacionValidator
    : AbstractValidator<RegistrarMovilizacionDto>
{
    public RegistrarMovilizacionValidator()
    {
        RuleFor(m => m.SinAntibioticos7Dias)
            .NotNull()
            .WithMessage("Debes confirmar que los cuyes no recibieron " +
                         "antibióticos en los últimos 7 días.")
            .Must(v => v == true)
            .WithMessage("No se puede registrar el envío: los cuyes no deben " +
                         "haber recibido antibióticos en los últimos 7 días.");

        RuleFor(m => m.Conductor)
            .NotEmpty().WithMessage("El conductor es obligatorio.");

        RuleFor(m => m.ResponsableDespacho)
            .NotEmpty().WithMessage("El responsable del despacho es obligatorio.");

        RuleFor(m => m.CantidadMovilizada)
            .GreaterThan(0).WithMessage("La cantidad movilizada debe ser mayor que cero.");
    }
}
```

`Program.cs:69` ya llama a `AddValidatorsFromAssemblyContaining<CrearProductoraValidator>()`, que escanea el ensamblado entero: **no hace falta registrar nada**.

- [ ] **Paso 7: Conectar el validador en el controlador**

En `RecepcionController`, añadir el parámetro al constructor primario:

```csharp
public class RecepcionController(
    IRecepcionService service,
    IGuiaMovilizacionService guiaService,
    IMovilizacionService movilizacionService,
    IValidator<RegistrarMovilizacionDto> movilizacionValidator) : ControllerBase
```

con `using FluentValidation;` arriba. Al principio de `RegistrarMovilizacion`:

```csharp
        var validacion = await movilizacionValidator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                // string.Join y no Errors[0]: es el patrón de FaenamientoController
                // y ProductorasController. Con solo el primero, un operador que
                // falle dos reglas a la vez ve un mensaje, reenvía, y descubre el
                // segundo — fricción que el resto del API no tiene.
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });
```

- [ ] **Paso 8: Persistir la declaración**

En `Features/Recepcion/Services/MovilizacionService.cs`, en la construcción de la entidad, sustituir la línea de `DiasRetiroMedicamentos` por:

```csharp
            TipoForraje = dto.TipoForraje,
            SinAntibioticos7Dias = dto.SinAntibioticos7Dias,
```

En `Mapear`, añadir el campo nuevo tras `DiasRetiroMedicamentos`:

```csharp
        DiasRetiroMedicamentos: m.DiasRetiroMedicamentos,
        SinAntibioticos7Dias: m.SinAntibioticos7Dias,
```

- [ ] **Paso 9: Cambiar la línea de la guía PDF**

En `Features/Recepcion/Services/GuiaMovilizacionService.cs`, añadir el método público:

```csharp
    /// <summary>
    /// Línea sanitaria de la guía. Es público y estático para poder fijarlo
    /// por unidad: el PDF comprime su texto y no hay forma razonable de
    /// afirmar nada sobre el binario.
    /// </summary>
    public static string TextoDeclaracionSanitaria(Movilizacion movilizacion) =>
        movilizacion.SinAntibioticos7Dias == true
            ? "Sin antibióticos últimos 7 días: declarado por " +
              movilizacion.ResponsableDespacho
            // Movilización anterior al cambio: se conserva el dato que sí se
            // capturó entonces en vez de imprimir una línea vacía.
            : movilizacion.DiasRetiroMedicamentos is int dias
                ? $"Retiro de medicamentos: {dias} días"
                : "Declaración sanitaria: sin declaración";
```

y sustituir el bloque de las líneas 236-241 por:

```csharp
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Forraje: {movilizacion.TipoForraje ?? "-"}");
                                r.RelativeItem().Text(
                                    TextoDeclaracionSanitaria(movilizacion));
                            });
```

- [ ] **Paso 10: Ejecutar y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: las 4 de integración y las 3 de guía en verde, y las 3 pruebas antiguas de `GuiaMovilizacionTests` siguen pasando.

- [ ] **Paso 11: Commit**

```bash
git add Features/Recepcion Infrastructure/Data/Migrations tests/CoopagcuyApi.Tests
git commit -m "feat: declaración obligatoria de ausencia de antibióticos en movilización"
```

---

### Tarea 6: Front — módulo de reglas, colores y textos

**Archivos:**
- Crear: `CoopagcuyFront/coopagcuy-frontend/src/domain/reglasRecepcion.ts`
- Modificar: `src/types/recepcion.ts:3`
- Modificar: `src/components/recepcion/FormLote.tsx:24-30,55,78-110,499,813-814`
- Modificar: `src/components/recepcion/JaulaEnArmado.tsx:41,77,112`
- Modificar: `src/components/reportes/graficos/AnilloNovedades.tsx:17`

**Interfaces:**
- Produce: `CAPACIDAD_JAULA: number`, `PESO_MINIMO_GRAMOS: number`, `PESO_MAXIMO_GRAMOS: number`, `COLORES: {valor: ColorPelaje; icono: string}[]`, `evaluarCuy(c: CuyRegistro): {nivel: NivelCuy | null; motivos: string[]}`, `type NivelCuy`. La Tarea 7 consume `evaluarCuy` y `COLORES`.

> Todos los comandos de esta tarea y las dos siguientes se ejecutan desde `C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`.

- [ ] **Paso 1: Crear el módulo de reglas**

Crear `src/domain/reglasRecepcion.ts`:

```ts
import type { ColorPelaje, CuyRegistro } from "../types/recepcion";

// Espejo de Common/ReglasRecepcion.cs del API. Se duplica a propósito: el
// wizard evalúa animales SIN señal, así que las reglas no pueden llegar por
// endpoint. Si cambian allí, cambian aquí — el servidor reevalúa igualmente
// al sincronizar, así que una tablet desactualizada muestra mal pero no
// guarda mal.
export const CAPACIDAD_JAULA = 15;
export const PESO_MINIMO_GRAMOS = 1200;
export const PESO_MAXIMO_GRAMOS = 1500;

// Tope de una entrega individual: dos jaulas completas
export const MAX_ENTREGA = CAPACIDAD_JAULA * 2;

// Opciones como tarjetas grandes con pictograma: pensadas para operadoras
// con poca experiencia digital, en tablet de 7"
export const COLORES: { valor: ColorPelaje; icono: string }[] = [
    { valor: "Blanco", icono: "⚪" },
    { valor: "Amarillo", icono: "🟡" },
    { valor: "Rojo", icono: "🔴" },
    { valor: "Combinado", icono: "🟤" },
];

export type NivelCuy = "ok" | "sobrepeso" | "novedad" | "rechazo";

// "sobrepeso" es su propio nivel y no una novedad más: el animal está sano y
// se acepta, solo queda fuera del rango comercial. Mezclarlo con el rechazo
// bajo el mismo color hacía que la operadora leyera "problema" en los dos.
// Un nivel posterior solo sube (ok → sobrepeso → novedad → rechazo).
const ORDEN: NivelCuy[] = ["ok", "sobrepeso", "novedad", "rechazo"];
const subir = (actual: NivelCuy, nuevo: NivelCuy): NivelCuy =>
    ORDEN.indexOf(nuevo) > ORDEN.indexOf(actual) ? nuevo : actual;

// Evaluación local por animal: espejo de EvaluarCuyIndividual del backend.
export function evaluarCuy(c: CuyRegistro): {
    nivel: NivelCuy | null;
    motivos: string[];
} {
    if (c.pesoGramos <= 0) return { nivel: null, motivos: [] };

    const motivos: string[] = [];
    let nivel: NivelCuy = "ok";

    if (c.pesoGramos < PESO_MINIMO_GRAMOS) {
        nivel = subir(nivel, "rechazo");
        motivos.push(`peso bajo el mínimo (${PESO_MINIMO_GRAMOS} g)`);
    } else if (c.pesoGramos > PESO_MAXIMO_GRAMOS) {
        nivel = subir(nivel, "sobrepeso");
        motivos.push(`peso sobre ${PESO_MAXIMO_GRAMOS} g`);
    }

    if (c.estadoOreja === "Dura") {
        nivel = subir(nivel, "novedad");
        motivos.push("oreja dura");
    }
    if (c.signosClinicos?.trim()) {
        nivel = subir(nivel, "novedad");
        motivos.push("signos clínicos");
    }

    return { nivel, motivos };
}
```

- [ ] **Paso 2: Restringir el tipo de color**

En `src/types/recepcion.ts`, línea 3:

```ts
// "Plomo" y "Negro" salieron del catálogo en 2026-08 y "Bayo" pasó a
// "Amarillo". Los registros históricos conservan sus valores antiguos: el
// campo es texto libre en la base y las lecturas usan `string`, no este tipo.
export type ColorPelaje = "Blanco" | "Amarillo" | "Rojo" | "Combinado";
```

- [ ] **Paso 3: Consumir el módulo en FormLote**

En `src/components/recepcion/FormLote.tsx`:

1. Borrar el bloque `const COLORES` (líneas 23-30), la constante `MAX_ENTREGA` (línea 55), el `type NivelCuy`, `ORDEN`, `subir` y la función `evaluarCuy` completa (líneas ~66-110).
2. Añadir el import:

```ts
import {
    COLORES, MAX_ENTREGA, evaluarCuy, PESO_MAXIMO_GRAMOS, CAPACIDAD_JAULA
} from "../../domain/reglasRecepcion";
```

3. Línea ~499, cambiar el texto:

```tsx
                                    centro de acopio (máximo {CAPACIDAD_JAULA} por jaula). Si la jaula se llena,
```

4. Líneas ~813-814, cambiar el aviso de sobrepeso:

```tsx
                                                ? `cuy supera los ${PESO_MAXIMO_GRAMOS} g`
                                                : `cuyes superan los ${PESO_MAXIMO_GRAMOS} g`}. Se
```

5. Cambiar el color por defecto de `CUY_INICIAL` solo si era uno de los eliminados — hoy es `"Blanco"`, que sigue existiendo: **no tocar**.

- [ ] **Paso 4: Consumir el módulo en JaulaEnArmado**

En `src/components/recepcion/JaulaEnArmado.tsx`, añadir el import y sustituir los tres `20`:

```ts
import { CAPACIDAD_JAULA } from "../../domain/reglasRecepcion";
```

```ts
    const progreso = jaula ? (jaula.cantidadAnimales / CAPACIDAD_JAULA) * 100 : 0;
```

```tsx
                        title={`Cierra la jaula aunque no llegue a ${CAPACIDAD_JAULA}, dejándola lista para enviar a la planta`}
```

```tsx
                          ${jaula.cantidadAnimales >= CAPACIDAD_JAULA
```

Revisar también el comentario de la línea 11 ("progreso hacia los 20 cuyes") y actualizarlo.

- [ ] **Paso 5: Corregir la etiqueta del gráfico**

En `src/components/reportes/graficos/AnilloNovedades.tsx`, línea 17:

```ts
    SobrePeso: { nombre: "Sobre peso (>1500g)", color: INFORMATIVO },
```

- [ ] **Paso 6: Verificar que compila**

```bash
pnpm exec tsc -b
```

Esperado: sin errores. Si aparece `Type '"Bayo"' is not assignable to type 'ColorPelaje'`, queda algún literal antiguo sin migrar — el compilador dice el archivo y la línea.

```bash
pnpm build
```

Esperado: build correcto.

- [ ] **Paso 7: Commit**

```bash
git add src/domain/reglasRecepcion.ts src/types/recepcion.ts src/components/recepcion/FormLote.tsx src/components/recepcion/JaulaEnArmado.tsx src/components/reportes/graficos/AnilloNovedades.tsx
git commit -m "feat: reglas de recepción centralizadas, jaula de 15 y paleta de colores nueva"
```

---

### Tarea 7: Front — captura de la foto clínica

**Archivos:**
- Crear: `src/utils/comprimirImagen.ts`
- Modificar: `src/types/recepcion.ts` (`CuyRegistro`, `Novedad`)
- Modificar: `src/components/recepcion/FormLote.tsx` (paso 3)

**Interfaces:**
- Consume: `CuyRegistroDto.FotoBase64` y `NovedadResponseDto.TieneFoto` (Tarea 4).
- Produce: `comprimirImagen(archivo: File): Promise<string>` — devuelve base64 **sin** el prefijo `data:image/jpeg;base64,`, que es lo que el API espera.

- [ ] **Paso 1: Crear el compresor**

Crear `src/utils/comprimirImagen.ts`:

```ts
// Lado mayor y calidad elegidos para que una foto de tablet quede en ~100 KB:
// la entrega viaja entera en un solo JSON y puede llevar varias, y la tablet
// la guarda en IndexedDB hasta que haya señal.
const LADO_MAXIMO = 1024;
const CALIDAD = 0.6;

/**
 * Reescala y recomprime una foto a JPEG, devolviendo base64 SIN el prefijo
 * `data:`: el API hace Convert.FromBase64String directamente y el prefijo lo
 * haría fallar.
 */
export function comprimirImagen(archivo: File): Promise<string> {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(archivo);
        const img = new Image();

        img.onload = () => {
            URL.revokeObjectURL(url);

            const escala = Math.min(1, LADO_MAXIMO / Math.max(img.width, img.height));
            const canvas = document.createElement("canvas");
            canvas.width = Math.round(img.width * escala);
            canvas.height = Math.round(img.height * escala);

            const ctx = canvas.getContext("2d");
            if (!ctx) {
                reject(new Error("No se pudo preparar la imagen."));
                return;
            }

            ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

            const dataUrl = canvas.toDataURL("image/jpeg", CALIDAD);
            resolve(dataUrl.slice(dataUrl.indexOf(",") + 1));
        };

        img.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error("No se pudo leer la foto."));
        };

        img.src = url;
    });
}
```

- [ ] **Paso 2: Añadir los campos a los tipos**

En `src/types/recepcion.ts`, en `CuyRegistro`:

```ts
export interface CuyRegistro {
    pesoGramos: number;
    colorPelaje: ColorPelaje;
    estadoOreja: EstadoOreja;
    tamanoAnimal: TamanoAnimal;
    signosClinicos?: string;
    // Evidencia del defecto en base64 sin prefijo data:. Solo se envía junto
    // a una novedad clínica; se guarda en IndexedDB con el resto de la
    // entrega y sube cuando vuelve la señal.
    fotoBase64?: string;
}
```

y en `Novedad`:

```ts
export interface Novedad {
    id: number;
    tipo: string;
    descripcion: string;
    pesoRegistradoGramos: number | null;
    fechaRegistro: string;
    registradoPor: string;
    // La evidencia caduca a los 90 días: el servidor devuelve false cuando ya
    // no hay nada que pedir.
    tieneFoto: boolean;
}
```

- [ ] **Paso 3: Añadir la captura al paso 3 de FormLote**

En `src/components/recepcion/FormLote.tsx`, importar el compresor:

```ts
import { comprimirImagen } from "../../utils/comprimirImagen";
```

Añadir el estado de error junto a los demás `useState` del componente:

```ts
    const [errorFoto, setErrorFoto] = useState<string | null>(null);
```

Justo **debajo** del `textarea` de signos clínicos (el de clases `border-bayo-400`, dentro del bloque `{(!!cuy.signosClinicos || conObservacionSanitaria) && (…)}`), y **dentro de ese mismo bloque condicional**, insertar:

> Nombres reales de ese ámbito, ya verificados en el archivo: el cuy en edición es `cuy`, el mutador es `actualizarCuy({...})`, y `cuyActual` es el **índice** numérico del cuy — no el objeto. No los confundas.

```tsx
{/* La foto solo tiene sentido con una novedad clínica descrita: es la
    evidencia de ESE defecto para reclamar al proveedor, no una foto
    suelta del animal. */}
{cuy.signosClinicos?.trim() && (
    <div className="mt-3">
        <label className="block text-xs font-bold uppercase tracking-wide
                          text-gray-500 mb-1">
            Foto del defecto (opcional)
        </label>

        {cuy.fotoBase64 ? (
            <div className="flex items-center gap-3">
                <img
                    src={`data:image/jpeg;base64,${cuy.fotoBase64}`}
                    alt="Evidencia del defecto"
                    className="w-20 h-20 object-cover rounded-xl border-2 border-gray-200"
                />
                <button
                    type="button"
                    onClick={() => actualizarCuy({ fotoBase64: undefined })}
                    className="min-h-[44px] px-4 rounded-xl border-2 border-gray-200
                               text-sm font-semibold text-gray-700 hover:bg-gray-50"
                >
                    Quitar foto
                </button>
            </div>
        ) : (
            <label className="flex items-center justify-center gap-2 min-h-[56px]
                              rounded-xl border-2 border-dashed border-gray-300
                              text-sm font-semibold text-gray-600 cursor-pointer
                              hover:bg-gray-50 transition">
                📷 Tomar foto
                <input
                    type="file"
                    accept="image/*"
                    capture="environment"
                    className="hidden"
                    onChange={async (e) => {
                        const archivo = e.target.files?.[0];
                        if (!archivo) return;
                        setErrorFoto(null);
                        try {
                            const base64 = await comprimirImagen(archivo);
                            actualizarCuy({ fotoBase64: base64 });
                        } catch {
                            setErrorFoto("No se pudo procesar la foto. Intenta de nuevo.");
                        } finally {
                            // Permite volver a elegir el MISMO archivo: sin
                            // esto el input no dispara change la segunda vez.
                            e.target.value = "";
                        }
                    }}
                />
            </label>
        )}

        <p className="mt-1 text-xs text-gray-400">
            Se guarda 90 días y luego se borra sola.
        </p>

        {errorFoto && (
            <p className="mt-1 text-xs text-teja-700">{errorFoto}</p>
        )}
    </div>
)}
```

- [ ] **Paso 4: Limpiar la foto al pasar al siguiente cuy**

`CUY_INICIAL` no declara `fotoBase64`, así que cada cuy nuevo nace sin foto. Verificarlo leyendo el punto donde se crea el arreglo de cuyes: si en algún sitio se clona el cuy anterior en vez de partir de `CUY_INICIAL`, añadir `fotoBase64: undefined` ahí — de lo contrario la foto de un animal se pegaría al siguiente, que es exactamente el error que arruina una evidencia.

- [ ] **Paso 5: Verificar que compila**

```bash
pnpm exec tsc -b
```

Esperado: sin errores.

```bash
pnpm build
```

Esperado: build correcto.

- [ ] **Paso 6: Commit**

```bash
git add src/utils/comprimirImagen.ts src/types/recepcion.ts src/components/recepcion/FormLote.tsx
git commit -m "feat: captura de foto en novedad clínica, con soporte offline"
```

---

### Tarea 8: Front — ver la evidencia fotográfica

**Archivos:**
- Crear: `src/components/recepcion/EvidenciaNovedad.tsx`
- Modificar: `src/api/recepcion.ts`
- Modificar: `src/pages/Recepcion.tsx:204-208`

**Interfaces:**
- Consume: `NovedadResponseDto.TieneFoto` y `GET /api/recepcion/novedades/{id}/foto` (Tarea 4), `Novedad.tieneFoto` (Tarea 7).
- Produce: `recepcionApi.fotoNovedad(novedadId: number): Promise<Blob>` y el componente `<EvidenciaNovedad novedadId={n} />`.

> **Por qué no basta con un `<img src>`.** El access token vive **en memoria** (`tokenStore`) y lo adjunta un interceptor de axios; una etiqueta `<img>` no pasa por ahí y recibiría 401. La foto se descarga como blob por el cliente autenticado y se muestra con `URL.createObjectURL`.

- [ ] **Paso 1: Añadir la descarga al cliente del API**

En `src/api/recepcion.ts`, dentro del objeto `recepcionApi`:

```ts
    // responseType blob: el endpoint devuelve image/jpeg, no JSON. Pasa por
    // `client` y no por fetch directo para que el interceptor adjunte el
    // Bearer (el token está en memoria, no en una cookie que viaje sola).
    fotoNovedad: async (novedadId: number): Promise<Blob> => {
        const res = await client.get(
            `/api/recepcion/novedades/${novedadId}/foto`,
            { responseType: "blob" });
        return res.data;
    },
```

- [ ] **Paso 2: Crear el visor**

Crear `src/components/recepcion/EvidenciaNovedad.tsx`:

```tsx
import { useEffect, useState } from "react";
import { recepcionApi } from "../../api/recepcion";

interface Props {
    novedadId: number;
}

/**
 * Miniatura de la evidencia de una novedad clínica. La foto se pide solo al
 * tocar el botón: la tabla de lotes puede tener decenas de filas y no tiene
 * sentido descargar imágenes que nadie va a mirar.
 *
 * La evidencia caduca a los 90 días; pasada esa fecha el API responde 404 y
 * aquí se dice, en vez de dejar un hueco sin explicación.
 */
export function EvidenciaNovedad({ novedadId }: Props) {
    const [url, setUrl] = useState<string | null>(null);
    const [cargando, setCargando] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // Los object URL no se liberan solos: sin esto, abrir muchas fotos
    // mantiene los blobs vivos hasta recargar la página.
    useEffect(() => () => { if (url) URL.revokeObjectURL(url); }, [url]);

    const abrir = async () => {
        if (url) return;
        setCargando(true);
        setError(null);
        try {
            const blob = await recepcionApi.fotoNovedad(novedadId);
            setUrl(URL.createObjectURL(blob));
        } catch {
            setError("La evidencia ya no está disponible (se borra a los 90 días).");
        } finally {
            setCargando(false);
        }
    };

    if (error) return <span className="text-xs text-gray-400">{error}</span>;

    if (url) {
        return (
            <a href={url} target="_blank" rel="noreferrer"
                title="Abrir la evidencia en tamaño completo">
                <img
                    src={url}
                    alt="Evidencia de la novedad clínica"
                    className="w-12 h-12 object-cover rounded-lg border border-gray-200"
                />
            </a>
        );
    }

    return (
        <button
            type="button"
            onClick={abrir}
            disabled={cargando}
            className="text-xs font-semibold text-primary-700 hover:text-primary-600
                       disabled:opacity-50"
        >
            {cargando ? "Cargando…" : "📷 Ver foto"}
        </button>
    );
}
```

- [ ] **Paso 3: Enlazarlo desde la lista de lotes**

En `src/pages/Recepcion.tsx`, sustituir la celda de novedades (líneas 204-208) por:

```tsx
                                        <td className="px-4 py-3 text-gray-500 text-xs">
                                            {l.novedades.length > 0
                                                ? l.novedades.map(n => n.tipo).join(", ")
                                                : "—"}
                                            {/* Las novedades con evidencia son siempre
                                                clínicas: es lo que se reclama al
                                                proveedor, así que se enlaza aquí. */}
                                            {l.novedades.filter(n => n.tieneFoto).map(n => (
                                                <span key={n.id} className="block mt-1">
                                                    <EvidenciaNovedad novedadId={n.id} />
                                                </span>
                                            ))}
                                        </td>
```

Añadir el import:

```ts
import { EvidenciaNovedad } from "../components/recepcion/EvidenciaNovedad";
```

- [ ] **Paso 4: Verificar que compila**

```bash
pnpm exec tsc -b
```

Esperado: sin errores. Si dice `Property 'tieneFoto' does not exist on type 'Novedad'`, el Paso 2 de la Tarea 7 no se aplicó.

```bash
pnpm build
```

- [ ] **Paso 5: Verificación manual del ciclo completo**

Es la única parte del trabajo que ninguna prueba automática cubre (no hay Vitest ni Playwright todavía). Con el API corriendo:

1. Registrar una entrega con un cuy marcado "Veo algo raro", escribir una observación y tomar una foto.
2. En la lista de lotes, comprobar que la fila muestra "📷 Ver foto" y que al pulsarlo aparece la miniatura.
3. En la base, poner el `FotoExpiraEn` de esa novedad en el pasado y recargar: el botón debe decir que la evidencia ya no está disponible, no dejar un hueco.

- [ ] **Paso 6: Commit**

```bash
git add src/api/recepcion.ts src/components/recepcion/EvidenciaNovedad.tsx src/pages/Recepcion.tsx
git commit -m "feat: visor de la evidencia fotográfica de novedades clínicas"
```

---

### Tarea 9: Front — forraje y declaración de antibióticos

**Archivos:**
- Modificar: `src/types/recepcion.ts` (`RegistrarMovilizacionRequest`, `Movilizacion`)
- Modificar: `src/components/recepcion/FormMovilizacion.tsx:15-23,36-37,180-222`
- Modificar: `src/pages/Faenamiento.tsx:294-296`

**Interfaces:**
- Consume: `RegistrarMovilizacionDto.SinAntibioticos7Dias` y `MovilizacionResponseDto.SinAntibioticos7Dias` (Tarea 5).

- [ ] **Paso 1: Ajustar los tipos**

En `src/types/recepcion.ts`, en `RegistrarMovilizacionRequest`, quitar `diasRetiroMedicamentos` y añadir:

```ts
    tipoForraje?: string;
    // Obligatoria: el servidor rechaza con 400 si no llega true.
    sinAntibioticos7Dias?: boolean;
```

En `Movilizacion` (respuesta), **conservar** `diasRetiroMedicamentos` y añadir:

```ts
    diasRetiroMedicamentos: number | null;
    sinAntibioticos7Dias: boolean | null;
```

- [ ] **Paso 2: Añadir el forraje nuevo**

En `src/components/recepcion/FormMovilizacion.tsx`, en `TIPOS_FORRAJE`:

```ts
const TIPOS_FORRAJE = [
    "Alfalfa",
    "Pasto de corte",
    "Kikuyo",
    "Raygrass",
    "Maíz forrajero",
    "Mezcla de forrajes",
    "Concentrado sin proteína animal",
    "Otro",
];
```

- [ ] **Paso 3: Sustituir el campo de días por la declaración**

En el estado inicial del formulario, cambiar:

```ts
        tipoForraje: "",
        sinAntibioticos7Dias: false,
```

Sustituir el `<div>` completo del campo "Días desde el último medicamento" por:

```tsx
                        {/* Sustituye a la pregunta por los días de retiro: lo
                            que importa no es cuántos días pasaron sino que
                            alguien responda por el periodo de carencia. */}
                        <div className="rounded-xl border-2 border-teja-200 bg-teja-50 p-3">
                            <p className="text-sm font-semibold text-teja-800">
                                ⚠️ Los cuyes registrados no debieron recibir
                                antibióticos en los últimos 7 días.
                            </p>
                            <label className="flex items-start gap-3 mt-3 min-h-[44px] cursor-pointer">
                                <input
                                    type="checkbox"
                                    checked={form.sinAntibioticos7Dias ?? false}
                                    onChange={(e) => setForm({
                                        ...form, sinAntibioticos7Dias: e.target.checked
                                    })}
                                    className="w-5 h-5 mt-0.5 accent-primary-600 shrink-0"
                                />
                                <span className="text-sm text-gray-800">
                                    Confirmo que los cuyes de este lote no recibieron
                                    antibióticos en los últimos 7 días.
                                </span>
                            </label>
                        </div>
```

- [ ] **Paso 4: Bloquear el envío sin la casilla**

En el botón de registrar del `footer`:

```tsx
                    <button type="submit" form="form-movilizacion"
                        disabled={mutation.isPending || !form.sinAntibioticos7Dias}
                        className="flex-1 h-12 bg-primary-600 hover:bg-primary-700
                       disabled:bg-primary-300 text-white rounded-2xl
                       text-sm font-bold transition">
                        {mutation.isPending ? "Registrando…" : "Registrar salida"}
                    </button>
```

- [ ] **Paso 5: Mostrar la declaración en la ficha del lote**

En `src/pages/Faenamiento.tsx`, sustituir las líneas 295-296 por:

```tsx
                                                {m.sinAntibioticos7Dias === true
                                                    ? " · Sin antibióticos últimos 7 días"
                                                    : m.diasRetiroMedicamentos !== null
                                                        ? ` · Retiro medicamentos: ${m.diasRetiroMedicamentos} días`
                                                        : ""}
```

- [ ] **Paso 6: Verificar que compila**

```bash
pnpm exec tsc -b
```

Esperado: sin errores. Si aparece `Property 'diasRetiroMedicamentos' does not exist` sobre el request, queda algún sitio que aún lo envía.

```bash
pnpm build
```

- [ ] **Paso 7: Commit**

```bash
git add src/types/recepcion.ts src/components/recepcion/FormMovilizacion.tsx src/pages/Faenamiento.tsx
git commit -m "feat: forraje concentrado y declaración de antibióticos en el envío a planta"
```

---

### Tarea 10: Provisión del contenedor y política de caducidad

**Archivos:**
- Modificar: `infra/bootstrap.azcli:49-52,69-77`
- Modificar: `docs/DESPLIEGUE.md`

**Interfaces:**
- Consume: la variable de configuración `AzureBlob__ContainerEvidencias` que lee `BlobStorageService` (Tarea 3).

- [ ] **Paso 1: Crear el contenedor privado en el bootstrap**

En `infra/bootstrap.azcli`, tras la línea que crea el contenedor `qr-prod`:

```bash
# Contenedor SEPARADO para la evidencia clínica. Privado (sin --public-access):
# se sirve solo por el endpoint autenticado del API. Separado del de QR porque
# la política de caducidad de abajo se aplica por contenedor y borraría también
# los QR de los lotes.
az storage container create --account-name "$STORAGE" -n evidencias-prod
```

- [ ] **Paso 2: Aplicar la política de ciclo de vida**

Añadir a continuación:

```bash
# Borrado automático de la evidencia clínica a los 90 días. Corre dentro de
# Azure Storage, no en la aplicación: el Container App escala a CERO, así que
# un temporizador dentro del API no se ejecutaría de forma fiable.
cat > /tmp/politica-evidencias.json <<'JSON'
{
  "rules": [
    {
      "enabled": true,
      "name": "borrar-evidencias-clinicas-90d",
      "type": "Lifecycle",
      "definition": {
        "filters": {
          "blobTypes": [ "blockBlob" ],
          "prefixMatch": [ "evidencias-prod/" ]
        },
        "actions": {
          "baseBlob": {
            "delete": { "daysAfterCreationGreaterThan": 90 }
          }
        }
      }
    }
  ]
}
JSON

az storage account management-policy create \
  --account-name "$STORAGE" -g "$RG" \
  --policy @/tmp/politica-evidencias.json
```

- [ ] **Paso 3: Pasar la variable al Container App**

En el bloque `az containerapp create`, junto a las otras variables de `AzureBlob`:

```bash
      AzureBlob__ContainerName="$BLOBC" \
      AzureBlob__ContainerEvidencias=evidencias-prod
```

- [ ] **Paso 4: Verificar la sintaxis del script**

```bash
bash -n infra/bootstrap.azcli
```

Esperado: sin salida (sintaxis válida). No ejecuta nada contra Azure.

- [ ] **Paso 5: Documentar el paso manual**

Añadir a `docs/DESPLIEGUE.md`, en la sección de pasos manuales:

```markdown
### Evidencia fotográfica de novedades clínicas

El contenedor `evidencias-prod` y su política de caducidad a 90 días se crean
con `infra/bootstrap.azcli`. Si el entorno ya existía cuando se desplegó esta
función, hay que aplicarlos a mano una sola vez —los dos comandos del
bootstrap, `az storage container create` y `az storage account
management-policy create`— y añadir `AzureBlob__ContainerEvidencias` a las
variables del Container App.

La política es lo único que borra los binarios: el API deja de servir la foto
en cuanto pasa su fecha de caducidad, pero no borra nada por su cuenta. Si la
política no está puesta, los blobs se acumulan indefinidamente sin que nada
falle a la vista.
```

- [ ] **Paso 6: Commit**

```bash
git add infra/bootstrap.azcli docs/DESPLIEGUE.md
git commit -m "chore: contenedor de evidencias y política de caducidad a 90 días"
```

---

## Orden de despliegue

1. **Migraciones primero.** `EvidenciaFotograficaNovedad` y `DeclaracionAntibioticos` son puramente aditivas y compatibles con la imagen anterior del API: no hay ventana de incompatibilidad.
2. **Contenedor y política de Blob** (Tarea 9), antes de que el API empiece a subir evidencias.
3. **API.**
4. **Front.**

El último paso sigue siendo manual (`az containerapp update`) mientras la cuenta Azure for Students no tenga el rol Application Developer: el workflow lo publica en el resumen del job.

**Tablets con caché antigua.** Un dispositivo que no haya actualizado el service worker seguirá mostrando las bandas viejas y ofreciendo Plomo y Negro. Lo que se guarda es correcto igualmente: `EvaluarCuyIndividual` reevalúa cada animal en el servidor al sincronizar. No hace falta forzar nada.

## Verificación final

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: las 12 pruebas originales más las 25 nuevas (3 de reglas, 2 de jaula, 5 de peso, 8 de evidencia, 4 de antibióticos, 3 de textos de guía), todas en verde.

```bash
pnpm exec tsc -b && pnpm build
```

Esperado: sin errores de tipos y build correcto.

No declares el trabajo terminado sin haber visto esas dos salidas.
