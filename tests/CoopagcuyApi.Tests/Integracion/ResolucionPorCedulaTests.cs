using System.Net.Http.Json;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Sin señal el operador no tiene catálogo y captura la entrega por cédula. La
/// pregunta que motiva estas pruebas: si la cédula coincide con una productora
/// real pero el nombre está mal escrito, ¿a quién se le asigna el lote?
///
/// A la de la cédula — y por un motivo más fuerte que "el nombre no entra en
/// la búsqueda": RegistrarEntregaDto NO TIENE campo de nombre. Lo que la
/// operadora vea o escriba en la tablet no viaja al servidor, así que no puede
/// desviar nada. Estas pruebas fijan esa conducta, que no tenía ninguna.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ResolucionPorCedulaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task LaCedulaAsignaElLoteAunqueLaTabletNoSepaElNombre()
    {
        // La productora se siembra con el nombre "Productora 0104576277". La
        // entrega viaja SOLO con la cédula: es todo lo que el DTO admite.
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, "PAT");

        await SincronizarAsync(cedula: Cedula, centro: "PAT");

        await using var db = api.NuevoDbContext();

        // El animal quedó con la productora de la cédula, no en cuarentena.
        var cuyes = await db.CuyRegistros
            .Where(c => c.ProductoraId == productora.Id)
            .CountAsync();
        cuyes.ShouldBe(1);

        var enCuarentena = await db.EntregasPendientesVinculacion
            .AnyAsync(v => v.Cedula == Cedula);
        enCuarentena.ShouldBeFalse();
    }

    [Fact]
    public async Task UnaCedulaDeOtroCentroVaALaBandejaDeVinculacion()
    {
        // La cédula es válida y la productora existe, pero pertenece a NIE y
        // la entrega se capturó en PAT. Queda en cuarentena para que un
        // administrador la resuelva. Es lo correcto —el lote es de un centro y
        // la productora de otro— pero no estaba escrito en ningún lado.
        await Sembrador.ProductoraAsync(api, Cedula, "NIE",
            comunidadId: 2);

        await SincronizarAsync(cedula: Cedula, centro: "PAT");

        await using var db = api.NuevoDbContext();

        var pendiente = await db.EntregasPendientesVinculacion
            .FirstOrDefaultAsync(v => v.Cedula == Cedula);

        pendiente.ShouldNotBeNull();
        pendiente.Estado.ShouldBe(EstadoVinculacion.Pendiente);
        pendiente.CentroAcopio.ShouldBe("PAT");

        // Y NO se coló como entrega de la productora de NIE.
        var cuyes = await db.CuyRegistros.CountAsync();
        cuyes.ShouldBe(0);
    }

    /// Una entrega de un cuy capturada sin conexión, identificada solo por
    /// cédula. FechaCapturaOffline es obligatoria en esta vía: la entrega pudo
    /// capturarse días antes de recuperar señal.
    private async Task SincronizarAsync(string cedula, string centro)
    {
        var respuesta = await api.ComoOperadorCat(centro)
            .PostAsJsonAsync("/api/recepcion/sync-entregas", new
            {
                dispositivoId = "tablet-de-prueba",
                entregas = new[] { new
                {
                    idCliente = Guid.NewGuid().ToString(),
                    dispositivoId = "tablet-de-prueba",
                    centroAcopio = centro,
                    productoraId = 0,
                    cedulaProductora = cedula,
                    fechaCapturaOffline = DateTime.UtcNow.AddHours(-2),
                    enAyunas = true,
                    responsableRecepcion = "Operadora de prueba",
                    sincronizadoOffline = true,
                    cuyes = new[] { new
                    {
                        pesoGramos = 1300m,
                        colorPelaje = "Blanco",
                        estadoOreja = "Blanda",
                        tamanoAnimal = "Normal"
                    }}
                }}
            });

        // El sync responde 200 con un resultado POR entrega: una entrega que
        // va a cuarentena no es un fallo HTTP.
        respuesta.EnsureSuccessStatusCode();
    }
}
