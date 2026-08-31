using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Lo que se vendió en la comunidad no puede acabar faenado. Este es el
/// cuarto sitio del proyecto que tiene que restar lo vendido —después de la
/// movilización, el selector de lotes pendientes de pago y el botón "A
/// planta"— y es el único que decide qué animal concreto pasa por el cuchillo:
/// si se equivoca, un consumidor lee en el QR la trazabilidad de un animal
/// que nunca salió del CAT.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class FaenamientoVentaLocalTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    private sealed record LoteListo(
        int LoteId, string CodigoLote, int[] CuyIds, int Vendidos);

    /// <summary>
    /// Entrega 5 cuyes en PAT, vende localmente los primeros `vendidos` y
    /// deja el resto movilizado y recibido en planta: justo el estado que
    /// exige LotesDisponiblesAsync para ofrecer el lote a faenar.
    /// </summary>
    private async Task<LoteListo> PrepararLoteConVentaAsync(int vendidos)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, "PAT");

        var cuyes = Enumerable.Range(0, 5).Select(_ => new
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

        int loteId;
        int[] ids;
        string codigoLote;
        await using (var db = api.NuevoDbContext())
        {
            ids = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .OrderBy(c => c.NumeroEnLote)
                .Select(c => c.Id)
                .ToArrayAsync();

            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0])
                .Select(c => c.LoteId)
                .FirstAsync();

            codigoLote = await db.Lotes
                .Where(l => l.Id == loteId)
                .Select(l => l.CodigoLote)
                .FirstAsync();
        }

        if (vendidos > 0)
        {
            var venta = await api.ComoOperadorCat("PAT")
                .PostAsJsonAsync("/api/pagos/venta-local", new
                {
                    productoraId = productora.Id,
                    loteId,
                    cuyRegistroIds = ids.Take(vendidos).ToArray(),
                    montoUsd = 15m * vendidos,
                    metodoPago = "Efectivo",
                    responsable = "Operadora de prueba"
                });
            venta.EnsureSuccessStatusCode();
        }

        var cierre = await api.ComoOperadorCat("PAT")
            .PostAsync($"/api/recepcion/lotes/{codigoLote}/cerrar", null);
        cierre.EnsureSuccessStatusCode();

        var restantes = ids.Length - vendidos;
        var movilizacion = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigoLote}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = restantes,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });
        movilizacion.EnsureSuccessStatusCode();

        int movilizacionId;
        await using (var db = api.NuevoDbContext())
        {
            movilizacionId = await db.Movilizaciones
                .Where(m => m.LoteId == loteId)
                .Select(m => m.Id)
                .FirstAsync();
        }

        var recepcion = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movilizacionId}/recepcion", new
            {
                recibidoPor = "Operario de planta",
                // El checklist de transporte de este sembrado quedo incompleto
                // (solo "JaulasLimpias"), asi que desde la fusion con
                // trazabilidad la pregunta de llegada es obligatoria. Se
                // responde que si: a estas pruebas les da igual como llego, lo
                // que verifican es el faenamiento aguas abajo.
                llegaronEnBuenEstado = true,
                condicionLlegada = "Buena"
            });
        recepcion.EnsureSuccessStatusCode();

        return new LoteListo(loteId, codigoLote, ids, vendidos);
    }

    private record LoteParcial(
        int LoteId, string CodigoLote, int CantidadAnimales,
        int Disponibles, List<CuyParcial> CuyesDisponibles);
    private record CuyParcial(int NumeroEnLote);

    [Fact]
    public async Task UnLoteConVentaParcialMuestraElSaldoCorrecto()
    {
        // 5 entregados, 2 vendidos en la comunidad, 3 movilizados a planta:
        // el saldo por faenar es 3, no 5.
        var lote = await PrepararLoteConVentaAsync(vendidos: 2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/faenamiento/lotes-disponibles");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var lotes = await respuesta.Content.ReadFromJsonAsync<List<LoteParcial>>();
        var propio = lotes!.Single(l => l.LoteId == lote.LoteId);

        propio.Disponibles.ShouldBe(3,
            "el saldo por faenar debe restar tanto lo ya faenado como lo " +
            "vendido en la comunidad, no solo lo faenado");
    }

    [Fact]
    public async Task LosAnimalesVendidosNoSalenEnLaListaDeDisponibles()
    {
        var lote = await PrepararLoteConVentaAsync(vendidos: 2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/faenamiento/lotes-disponibles");

        var lotes = await respuesta.Content.ReadFromJsonAsync<List<LoteParcial>>();
        var propio = lotes!.Single(l => l.LoteId == lote.LoteId);

        propio.CuyesDisponibles.Count.ShouldBe(3);
        // Los animales #1 y #2 (los primeros por NumeroEnLote) son los que
        // se vendieron en PrepararLoteConVentaAsync: no pueden ofrecerse
        // para faenar aunque su número siga dentro del rango del lote.
        propio.CuyesDisponibles.ShouldNotContain(c => c.NumeroEnLote == 1);
        propio.CuyesDisponibles.ShouldNotContain(c => c.NumeroEnLote == 2);
        propio.CuyesDisponibles.Select(c => c.NumeroEnLote)
            .ShouldBe(new[] { 3, 4, 5 }, ignoreOrder: true);
    }

    [Fact]
    public async Task RegistrarUnaSesionSobreUnAnimalVendidoSeRechaza()
    {
        var lote = await PrepararLoteConVentaAsync(vendidos: 2);

        // El animal #1 se vendió en la comunidad: nunca llegó a la planta.
        // Registrar una sesión de faenamiento citándolo debe rechazarse, no
        // solo excluirlo del listado — de lo contrario un cliente que salte
        // el selector normal podría faenar un animal fantasma.
        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/batch", new
            {
                operarioResponsable = "Operario de prueba",
                lotes = new[]
                {
                    new
                    {
                        loteId = lote.LoteId,
                        cuyes = new[]
                        {
                            new
                            {
                                numeroEnLote = 1,
                                pesoCanalGramos = 600m,
                                estado = "Apto"
                            }
                        }
                    }
                }
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("vendió");

        await using var db = api.NuevoDbContext();
        (await db.Faenamientos.AnyAsync(f => f.LoteId == lote.LoteId))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Entrega 3 cuyes en PAT y cierra el lote, pero NO lo moviliza ni lo
    /// recibe en planta: sigue físicamente en el CAT. Es el estado que
    /// LotesDisponiblesAsync ya excluye de la lista (Movilizacion == null);
    /// esta prueba comprueba que RegistrarBatchAsync lo rechaza también
    /// cuando el cliente se salta ese listado y cita el lote a mano.
    /// </summary>
    private async Task<string> PrepararLoteSinMovilizarAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, "PAT");

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

        string codigoLote;
        await using (var db = api.NuevoDbContext())
        {
            codigoLote = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.Lote!.CodigoLote)
                .FirstAsync();
        }

        var cierre = await api.ComoOperadorCat("PAT")
            .PostAsync($"/api/recepcion/lotes/{codigoLote}/cerrar", null);
        cierre.EnsureSuccessStatusCode();

        return codigoLote;
    }

    /// <summary>
    /// Arreglo 1 de la re-revisión: el hueco espejo del arreglo anterior.
    /// Aquel cerró "vendido → faenado"; este cierra "no recibido en planta →
    /// faenado". Sin la guarda de FechaRecepcionPlanta en
    /// RegistrarSesionLoteAsync, un cliente que se saltara el selector de
    /// lotes disponibles podía faenar un lote que sigue en el CAT y luego
    /// venderlo localmente: el animal quedaría faenado (dentro del
    /// LoteFaenado que lee el QR) Y cobrado como venta local a la vez.
    /// </summary>
    [Fact]
    public async Task RegistrarUnaSesionSobreUnLoteNoRecibidoEnPlantaSeRechaza()
    {
        var codigoLote = await PrepararLoteSinMovilizarAsync();

        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.Lotes
                .Where(l => l.CodigoLote == codigoLote)
                .Select(l => l.Id)
                .FirstAsync();
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/batch", new
            {
                operarioResponsable = "Operario de prueba",
                lotes = new[]
                {
                    new
                    {
                        loteId,
                        cuyes = new[]
                        {
                            new
                            {
                                numeroEnLote = 1,
                                pesoCanalGramos = 600m,
                                estado = "Apto"
                            }
                        }
                    }
                }
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("recibido en planta");

        await using var db2 = api.NuevoDbContext();
        (await db2.Faenamientos.AnyAsync(f => f.LoteId == loteId))
            .ShouldBeFalse();
    }
}
