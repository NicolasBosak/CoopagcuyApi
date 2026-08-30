using System.Net;
using System.Text.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Infrastructure.Data;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Task 11: las coordenadas de cada comunidad viajan dentro de
/// `comunidadesAporte` — es lo que permite que el mapa público deje de
/// depender de la tabla escrita a mano del front (coordenadas.ts).
///
/// El segundo hallazgo que cubre esta clase es el motivo de agrupar por
/// `Comunidad.Id` y no por nombre: con el catálogo geográfico abierto puede
/// haber dos comunidades homónimas en cantones (o provincias) distintos, y
/// agruparlas por nombre sumaría animales de sitios distintos bajo un solo
/// pin en el mapa.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ComunidadesAporteTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ComunidadesAporte_llevaLasCoordenadasDelCatalogo()
    {
        var codigoFaenado = "FAE-20260830-001";
        var productora = await Sembrador.ProductoraAsync(
            api, "0104576277", "PAT", comunidadId: 1); // Patococha (HasData)

        await using (var db = api.NuevoDbContext())
        {
            await SembrarLoteFaenadoAsync(
                db, codigoFaenado, productora.Id, "PAT-20260830-001", 2);
        }

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{codigoFaenado}");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var aporte = doc.RootElement.GetProperty("comunidadesAporte")
            .EnumerateArray().Single();

        aporte.GetProperty("comunidad").GetString().ShouldBe("Patococha");
        // Valores sembrados en AppDbContext.HasData para la comunidad 1: si
        // esta prueba se rompe por un número distinto, lo primero a mirar es
        // esa semilla, no este archivo.
        aporte.GetProperty("latitud").GetDecimal().ShouldBe(-3.225944m);
        aporte.GetProperty("longitud").GetDecimal().ShouldBe(-79.504472m);
        aporte.GetProperty("altitudMinM").GetInt32().ShouldBe(3190);
        aporte.GetProperty("altitudMaxM").GetInt32().ShouldBe(3190);
    }

    [Fact]
    public async Task DosComunidadesHomonimasEnCantonesDistintos_noSeMezclanEnUnSoloAporte()
    {
        var codigoFaenado = "FAE-20260830-002";

        var comunidadHomonimaId = await ComunidadPatococha_EnLoja_Async();

        var productoraPucara = await Sembrador.ProductoraAsync(
            api, "0104576278", "PAT", comunidadId: 1); // Patococha, Pucará
        var productoraLoja = await Sembrador.ProductoraAsync(
            api, "0104576279", "PAT", comunidadId: comunidadHomonimaId);

        await using (var db = api.NuevoDbContext())
        {
            var loteFaenado = new LoteFaenado
            {
                Codigo = codigoFaenado,
                FechaFaenamiento = DateTime.UtcNow,
                OperarioResponsable = "Operario de prueba"
            };
            db.LotesFaenados.Add(loteFaenado);
            await db.SaveChangesAsync();

            await SembrarSesionAsync(
                db, loteFaenado.Id, productoraPucara.Id, "PAT-20260830-002", 1);
            await SembrarSesionAsync(
                db, loteFaenado.Id, productoraLoja.Id, "PAT-20260830-003", 3);

            db.CodigosQR.Add(new CodigoQR
            {
                LoteFaenadoId = loteFaenado.Id,
                UrlPublica = $"https://localhost/qr/{codigoFaenado}",
                Activo = true
            });
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{codigoFaenado}");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var aportes = doc.RootElement.GetProperty("comunidadesAporte")
            .EnumerateArray().ToList();

        // Agrupar por nombre las mezclaría en un solo elemento con
        // cantidad 4. Agrupadas por Id, quedan dos filas de "Patococha",
        // cada una con su propia coordenada y su propia cantidad.
        aportes.Count.ShouldBe(2);
        aportes.ShouldAllBe(a => a.GetProperty("comunidad").GetString() == "Patococha");

        var cantidades = aportes.Select(a => a.GetProperty("cantidad").GetInt32())
            .OrderBy(c => c).ToList();
        cantidades.ShouldBe([1, 3]);

        var longitudes = aportes
            .Select(a => a.GetProperty("longitud").GetDecimal())
            .OrderBy(l => l).ToList();
        // -79.50 (Pucará, semilla) y -79.20 (Loja, sembrada abajo): si se
        // mezclaran por nombre solo aparecería una de las dos.
        longitudes.Count.ShouldBe(2);
    }

    /// <summary>
    /// Comunidad homónima de la Patococha del piloto (misma cadena "Patococha"
    /// pero otro cantón y otra coordenada), para probar que el agrupado no
    /// se hace por nombre. Respawn no trunca Comunidades entre pruebas
    /// (ver AlcanceProductorasTests / CatalogoGeograficoTests), así que se
    /// reutiliza si ya existe de una corrida anterior.
    /// </summary>
    private async Task<int> ComunidadPatococha_EnLoja_Async()
    {
        await using var db = api.NuevoDbContext();

        var existente = await db.Comunidades
            .FirstOrDefaultAsync(c => c.CantonId == 108 && c.Nombre == "Patococha");
        if (existente is not null) return existente.Id;

        var comunidad = new Comunidad
        {
            Nombre = "Patococha",
            CantonId = 108, // Loja (Loja) — ver ComunidadLojanaAsync en PaginaPublicaTests
            CatReferencia = "PAT",
            Latitud = -3.9m,
            Longitud = -79.20m,
            AltitudMinM = 2100,
            AltitudMaxM = 2100,
        };
        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();
        return comunidad.Id;
    }

    private static async Task SembrarLoteFaenadoAsync(
        AppDbContext db, string codigoFaenado,
        int productoraId, string codigoLote, int cantidad)
    {
        var loteFaenado = new LoteFaenado
        {
            Codigo = codigoFaenado,
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba"
        };
        db.LotesFaenados.Add(loteFaenado);
        await db.SaveChangesAsync();

        await SembrarSesionAsync(db, loteFaenado.Id, productoraId, codigoLote, cantidad);

        db.CodigosQR.Add(new CodigoQR
        {
            LoteFaenadoId = loteFaenado.Id,
            UrlPublica = $"https://localhost/qr/{codigoFaenado}",
            Activo = true
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Un lote con su sesión de faenamiento, colgados del mismo lote
    /// faenado. `cantidad` cuyes, todos Aptos: no es el foco de esta clase.
    /// </summary>
    private static async Task SembrarSesionAsync(
        AppDbContext db, int loteFaenadoId,
        int productoraId, string codigoLote, int cantidad)
    {
        var lote = new Lote
        {
            CodigoLote = codigoLote,
            ProductoraId = productoraId,
            CentroAcopio = "PAT",
            CantidadAnimales = cantidad,
            PesoTotalGramos = 900 * cantidad,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Operadora de prueba"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var sesion = new RegistroFaenamiento
        {
            LoteId = lote.Id,
            LoteFaenadoId = loteFaenadoId,
            NumeroSesion = 1,
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba",
            UnidadesFaenadas = cantidad,
            PesoTotalCanalGramos = 600 * cantidad,
            EstadoCanal = EstadoCanal.Apto
        };
        db.Faenamientos.Add(sesion);
        await db.SaveChangesAsync();

        for (var i = 1; i <= cantidad; i++)
        {
            db.CuyFaenamientos.Add(new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id,
                NumeroEnLote = i,
                PesoCanalGramos = 600,
                Estado = EstadoCanal.Apto
            });
        }
        await db.SaveChangesAsync();
    }
}
