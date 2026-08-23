using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Lo que ve el operador de faenamiento: los tickets que le toca pagar, de
/// los TRES centros de acopio, y los cuyes con novedad de cada uno.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class BandejaPlantaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Dos cédulas válidas distintas: una productora por centro
    private const string CedulaPat = "0104576277";
    private const string CedulaNie = "0102030405";

    [Fact]
    public async Task LaPlantaVeLosTicketsDeTodosLosCentros()
    {
        // La planta es única y atiende a los tres CAT: acotarla por centro
        // la dejaría sin ver la mitad de lo que tiene que pagar.
        await PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m);
        await PagoSembradoAsync(CentroAcopio.NIE, CedulaNie, 90m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<TicketPorPagarDto>>("/api/pagos/por-pagar");

        respuesta.ShouldNotBeNull();
        respuesta.Count.ShouldBe(2);
        respuesta.Select(t => t.CentroAcopio)
            .ShouldBe(new[] { "PAT", "NIE" }, ignoreOrder: true);
    }

    [Fact]
    public async Task UnTicketYaPagadoDesapareceDeLaBandeja()
    {
        var pagoId = await PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m);

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.Estado = EstadoPago.Pagado;
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<TicketPorPagarDto>>("/api/pagos/por-pagar");

        respuesta!.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaOperadoraDeCatNoEntraALaBandejaDeLaPlanta()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/pagos/por-pagar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Pin de los filtros de ListarCuyesConNovedadAsync. La versión anterior
    /// de esta prueba solo sembraba la novedad que SÍ debía aparecer: con
    /// una sola fila en toda la tabla Novedades, el resultado era el mismo
    /// con los tres Where borrados. Aquí se siembran tres formas de "casi
    /// calificar" y se verifica, una por una, que ninguna se cuela:
    ///   - misma jaula, otra productora                  -> pin del filtro de ProductoraId
    ///   - misma productora, otra jaula                   -> pin del filtro de LoteId
    ///   - novedad de la entrega (SinAyuno), sin animal   -> pin de la REGLA (nunca
    ///     descontable), no del filtro "CuyRegistro != null" en sí: ese filtro es
    ///     deliberadamente redundante con el de ProductoraId (ver el comentario
    ///     en PagoService) y por construcción ninguna prueba puede pinnearlo.
    /// </summary>
    [Fact]
    public async Task SoloSeListanLosCuyesConNovedadDeEsaProductora()
    {
        // Las productoras se crean UNA vez (la cédula es única) y entregan
        // varias veces: así Pat puede tener entregas en dos jaulas distintas
        // sin violar el índice único de Productoras.Cedula.
        var productoraPat = await Sembrador.ProductoraAsync(api, CedulaPat, CentroAcopio.PAT, comunidadId: 1);
        var productoraNie = await Sembrador.ProductoraAsync(api, CedulaNie, CentroAcopio.PAT, comunidadId: 2);

        // Pat entrega con un cuy marcado: esta es la ÚNICA novedad que debe
        // sobrevivir a los tres filtros.
        var pat = await EntregarAsync(
            CentroAcopio.PAT, productoraPat.Id, signos: "lesion-visible");

        // Nie entrega en EL MISMO CAT (Patococha) mientras la jaula de Pat
        // sigue abierta (3 + 3 = 6 de 15): cae en LA MISMA jaula, aunque sea
        // una productora distinta. Su novedad no debe aparecer en el ticket
        // de Pat: pin del filtro de ProductoraId.
        var nie = await EntregarAsync(
            CentroAcopio.PAT, productoraNie.Id, signos: "lesion-nie");
        nie.LoteId.ShouldBe(pat.LoteId,
            "la prueba solo prueba algo si ambas productoras terminan en la " +
            "misma jaula; si la jaula se dividió, hay que revisar la capacidad usada");

        // Segunda entrega de la MISMA Pat, todavía en la jaula abierta
        // (6 + 3 = 9 de 15), pero sin ayunar: eso genera una novedad SinAyuno
        // que pertenece a la ENTREGA, no a ningún cuy (CuyRegistroId null).
        // No debe aparecer: pin de la REGLA de negocio ("una novedad sin
        // animal jamás es descontable"), no de una cláusula concreta —
        // esa garantía la da hoy el filtro de ProductoraId, no el chequeo
        // "CuyRegistro != null" (ver PagoService).
        await EntregarAsync(
            CentroAcopio.PAT, productoraPat.Id, enAyunas: false);

        // Se cierra la jaula manualmente (sin necesidad de llegar a 15) para
        // que la siguiente entrega de Pat abra una jaula NUEVA.
        await CerrarLoteAsync(CentroAcopio.PAT, pat.CodigoLote);

        // Tercera entrega de Pat, ya en otra jaula, con otra novedad clínica.
        // No debe aparecer en el ticket de la primera jaula: pin del filtro
        // de LoteId.
        var patOtraJaula = await EntregarAsync(
            CentroAcopio.PAT, productoraPat.Id, signos: "lesion-otra-jaula");
        patOtraJaula.LoteId.ShouldNotBe(pat.LoteId);

        // El pago cita la PRIMERA jaula de Pat.
        var pagoId = await RegistrarPagoAsync(CentroAcopio.PAT, productoraPat.Id, pat.LoteId, 120m);

        // Ids de las novedades que NO deben aparecer, para poder afirmarlo
        // por Id y no solo por conteo (mensajes de fallo más claros).
        await using var db = api.NuevoDbContext();
        var novedadNie = await db.Novedades
            .FirstAsync(n => n.Descripcion.Contains("lesion-nie"));
        var novedadOtraJaula = await db.Novedades
            .FirstAsync(n => n.Descripcion.Contains("lesion-otra-jaula"));
        var novedadSinAyuno = await db.Novedades
            .FirstAsync(n => n.LoteId == pat.LoteId && n.Tipo == TipoNovedad.SinAyuno);
        var novedadEsperada = await db.Novedades
            .FirstAsync(n => n.Descripcion.Contains("lesion-visible"));

        var cuyes = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<CuyConNovedadDto>>(
                $"/api/pagos/{pagoId}/cuyes-con-novedad");

        cuyes.ShouldNotBeNull();
        cuyes.Count.ShouldBe(1);
        cuyes[0].NovedadId.ShouldBe(novedadEsperada.Id);
        cuyes[0].TipoNovedad.ShouldBe("SignosClinicos");
        cuyes[0].Descripcion.ShouldContain("lesion-visible");

        var idsListados = cuyes.Select(c => c.NovedadId).ToList();
        idsListados.ShouldNotContain(novedadNie.Id);
        idsListados.ShouldNotContain(novedadOtraJaula.Id);
        idsListados.ShouldNotContain(novedadSinAyuno.Id);
    }

    /// <summary>
    /// Arreglo 5 de la revisión final, capa 1 (lo que la planta VE). Un cuy
    /// que entró al CAT con una novedad clínica y luego se vendió en la
    /// comunidad nunca llegó a la planta: su novedad no puede ofrecerse como
    /// justificación de un descuento sobre el pago de esa productora, porque
    /// ya cobró por ese animal en la venta local. Sin el filtro de
    /// VentaLocalPagoId en ListarCuyesConNovedadAsync, el operador de planta
    /// vería esa novedad como si el cuy siguiera disponible para descontar.
    /// </summary>
    [Fact]
    public async Task UnCuyVendidoLocalmenteNoApareceEnCuyesConNovedad()
    {
        var (pagoId, _) = await Sembrador.PagoConNovedadDeCuyVendidoAsync(
            api, CedulaPat, 90m);

        var cuyes = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<CuyConNovedadDto>>(
                $"/api/pagos/{pagoId}/cuyes-con-novedad");

        cuyes.ShouldNotBeNull();
        cuyes.ShouldBeEmpty(
            "el único cuy con novedad de este lote se vendió en la " +
            "comunidad: no llegó a la planta y no hay nada que descontarle " +
            "a este pago por su causa");
    }

    /// Entrega + ticket. Devuelve el Id del pago.
    private async Task<int> PagoSembradoAsync(
        CentroAcopio cat, string cedula, decimal monto, string? signos = null)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, cat, comunidadId: cat == CentroAcopio.PAT ? 1 : 2);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = signos },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = cat.ToString(),
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }

    /// <summary>
    /// Registra una entrega vía la API (no directo a la base) para que pase
    /// por las mismas reglas de armado de jaula que un caso real: acumula en
    /// la jaula abierta del CAT hasta 15 y solo entonces abre una nueva.
    /// Siempre manda 3 cuyes; el primero lleva los signos clínicos indicados
    /// (si los hay), los otros dos van sanos. Recibe la productora YA
    /// creada (no la cédula) porque una misma productora puede entregar
    /// varias veces en la prueba, y la cédula es única.
    /// </summary>
    private async Task<EntregaSembrada> EntregarAsync(
        CentroAcopio cat, int productoraId,
        string? signos = null, bool enAyunas = true)
    {
        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = signos },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var respuesta = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = cat.ToString(),
                productoraId,
                cuyes,
                enAyunas,
                responsableRecepcion = "Operadora de prueba"
            });
        respuesta.EnsureSuccessStatusCode();

        var resultado = await respuesta.Content.ReadFromJsonAsync<EntregaResultadoDto>();
        var lote = resultado!.LotesAfectados[0];

        return new EntregaSembrada(productoraId, lote.Id, lote.CodigoLote);
    }

    /// Cierra manualmente la jaula abierta del CAT indicado, aunque no
    /// llegue a 15 animales: así una prueba no necesita sembrar 15 cuyes
    /// solo para forzar que la siguiente entrega abra una jaula nueva.
    private async Task CerrarLoteAsync(CentroAcopio cat, string codigoLote)
    {
        var respuesta = await api.ComoOperadorCat(cat.ToString())
            .PostAsync($"/api/recepcion/lotes/{codigoLote}/cerrar", null);
        respuesta.EnsureSuccessStatusCode();
    }

    private async Task<int> RegistrarPagoAsync(
        CentroAcopio cat, int productoraId, int loteId, decimal monto)
    {
        var respuesta = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        respuesta.EnsureSuccessStatusCode();

        var pago = await respuesta.Content.ReadFromJsonAsync<PagoResponseDto>();
        return pago!.Id;
    }

    private record EntregaSembrada(int ProductoraId, int LoteId, string CodigoLote);
}
