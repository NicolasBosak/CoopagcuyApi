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
