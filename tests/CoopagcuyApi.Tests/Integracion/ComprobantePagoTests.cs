using System.Text;
using CoopagcuyApi.Infrastructure.Storage;
using CoopagcuyApi.Tests.Infra;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Contenedor de capturas de transferencia. Separado del de evidencias
/// clínicas porque la política de caducidad se aplica POR CONTENEDOR y los
/// plazos son distintos: compartirlo borraría las evidencias a los 30 días.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ComprobantePagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static IBlobStorageService ServicioBlob(string? contenedor = null)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureBlob:ConnectionString"] = ApiFactory.CadenaBlob,
                ["AzureBlob:ContainerComprobantes"] = contenedor
            })
            .Build();

        return new BlobStorageService(configuracion);
    }

    [Fact]
    public async Task ElComprobanteSubeYVuelveIgual()
    {
        var servicio = ServicioBlob("comprobantes-test");
        var nombre = $"prueba-{Guid.NewGuid():N}.jpg";
        var contenido = Encoding.UTF8.GetBytes("captura-de-transferencia");

        await servicio.SubirComprobanteAsync(nombre, contenido);
        var recuperado = await servicio.DescargarComprobanteAsync(nombre);

        recuperado.ShouldBe(contenido);
    }

    [Fact]
    public async Task ConElNombreDeContenedorVacioSeUsaElPorDefecto()
    {
        // Misma trampa que costó un 500 en producción el 2026-08-20:
        // appsettings.json declara la clave con cadena VACÍA y `??` solo
        // cubre null. Con `??` la URL saldría sin contenedor.
        var servicio = ServicioBlob("");
        var nombre = $"prueba-{Guid.NewGuid():N}.jpg";

        var uri = await servicio.SubirComprobanteAsync(
            nombre, Encoding.UTF8.GetBytes("respaldo"));

        uri.ShouldContain("/comprobantes-pago/");
    }

    [Fact]
    public async Task BorrarUnComprobanteInexistenteNoRevienta()
    {
        // El barrido oportunista puede intentar borrar dos veces el mismo
        // blob si dos consultas coinciden. No puede tumbar la petición.
        var servicio = ServicioBlob("comprobantes-test");

        await Should.NotThrowAsync(() =>
            servicio.BorrarComprobanteAsync($"no-existe-{Guid.NewGuid():N}.jpg"));
    }

    [Fact]
    public async Task DescargarUnComprobanteBorradoDevuelveNulo()
    {
        var servicio = ServicioBlob("comprobantes-test");
        var nombre = $"prueba-{Guid.NewGuid():N}.jpg";

        await servicio.SubirComprobanteAsync(
            nombre, Encoding.UTF8.GetBytes("captura"));
        await servicio.BorrarComprobanteAsync(nombre);

        var recuperado = await servicio.DescargarComprobanteAsync(nombre);

        recuperado.ShouldBeNull();
    }
}
