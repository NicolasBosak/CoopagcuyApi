using System.Net.Http.Json;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CoopagcuyApi.Infrastructure.Storage;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
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

    // Cuenta los blobs que hay HOY en el contenedor de evidencias. La
    // colección corre en serie sobre un único Azurite compartido y
    // ApiFactory.LimpiarAsync solo trunca la base, no el contenedor, así que
    // el contenedor nunca empieza vacío: hay que comparar por delta
    // (antes/después), nunca contra un valor absoluto.
    private static async Task<int> ContarBlobsEvidenciasAsync()
    {
        var cliente = new BlobServiceClient(ApiFactory.CadenaBlob);
        var contenedor = cliente.GetBlobContainerClient("evidencias-test");
        await contenedor.CreateIfNotExistsAsync(PublicAccessType.None);

        var conteo = 0;
        await foreach (var _ in contenedor.GetBlobsAsync())
        {
            conteo++;
        }

        return conteo;
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
    public async Task UnOperadorDeOtroCatNoPuedeDescargarLaFotoYRecibe404()
    {
        // Los ids de novedad son enteros secuenciales: sin este filtro, un
        // OperadorCAT de un centro podría recorrer /novedades/1/foto,
        // /2/foto... y bajarse la evidencia clínica de OTROS centros. Debe
        // ser 404 y no 403: un 403 confirmaría que ese id existe.
        var id = await RegistrarConFotoAsync(Convert.ToBase64String(JpegMinimo));

        var respuesta = await api.ComoOperadorCat("NIE")
            .GetAsync($"/api/recepcion/novedades/{id}/foto");

        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
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

    // Segundo JPEG mínimo válido, distinto en un byte del primero, para
    // poder distinguir por contenido cuál foto quedó anclada a cuál novedad.
    private static readonly byte[] JpegSegundo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0xFF, 0xD9
    ];

    [Fact]
    public async Task UnaFotoInvalidaEnUnCuyPosteriorNoDejaBlobsDeCuyesAnteriores()
    {
        // La validación pasa por TODAS las fotos antes de subir ninguna: si
        // el cuy #2 trae base64 inválido, la foto válida del cuy #1 nunca
        // debe llegar al blob ni a ninguna novedad. Antes del arreglo, la
        // excepción también saltaba antes de escribir ninguna fila en la
        // base -- pero el blob del cuy #1 SÍ quedaba subido. Por eso la
        // única aserción que distingue el código arreglado del roto es el
        // conteo de blobs, no el conteo de filas.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CoopagcuyApi.Common.CentroAcopio.PAT);

        var blobsAntes = await ContarBlobsEvidenciasAsync();

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
                    signosClinicos = "Lesión en el primer cuy",
                    fotoBase64 = Convert.ToBase64String(JpegMinimo)
                },
                new
                {
                    pesoGramos = 1300m,
                    colorPelaje = "Blanco",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Normal",
                    signosClinicos = "Lesión en el segundo cuy",
                    fotoBase64 = "no-es-base64!!"
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);

        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);

        var blobsDespues = await ContarBlobsEvidenciasAsync();
        (blobsDespues - blobsAntes).ShouldBe(0);

        await using var db = api.NuevoDbContext();
        var conFoto = await db.Novedades.AsNoTracking()
            .Where(n => n.FotoUrl != null)
            .ToListAsync();
        conFoto.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaEntregaQueSePartaEntreDosJaulasAnclaCadaFotoAlAnimalCorrecto()
    {
        // Jaula de 15 (ReglasRecepcion.CapacidadJaula): con 16 cuyes la
        // entrega se divide en dos lotes. Cada foto debe quedar anclada a
        // la novedad del animal correcto, no a la del otro lote.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CoopagcuyApi.Common.CentroAcopio.PAT);

        const int capacidad = CoopagcuyApi.Common.ReglasRecepcion.CapacidadJaula;
        var cuyes = new List<object>();
        for (var i = 0; i < capacidad + 1; i++)
        {
            var esPrimero = i == 0;
            var esUltimo = i == capacidad;
            cuyes.Add(new
            {
                pesoGramos = 1300m,
                colorPelaje = "Blanco",
                estadoOreja = "Blanda",
                tamanoAnimal = "Normal",
                signosClinicos = esPrimero ? "herida en primera jaula"
                    : esUltimo ? "herida en segunda jaula" : null,
                fotoBase64 = esPrimero ? Convert.ToBase64String(JpegMinimo)
                    : esUltimo ? Convert.ToBase64String(JpegSegundo) : null
            });
        }

        var cuerpo = new
        {
            centroAcopio = "PAT",
            productoraId = productora.Id,
            cuyes,
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var novedadPrimera = await db.Novedades.AsNoTracking()
            .SingleAsync(n => n.Descripcion.Contains("herida en primera jaula"));
        var novedadSegunda = await db.Novedades.AsNoTracking()
            .SingleAsync(n => n.Descripcion.Contains("herida en segunda jaula"));

        novedadPrimera.FotoUrl.ShouldNotBeNullOrWhiteSpace();
        novedadSegunda.FotoUrl.ShouldNotBeNullOrWhiteSpace();
        novedadPrimera.LoteId.ShouldNotBe(novedadSegunda.LoteId);

        var respuestaPrimera = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/novedades/{novedadPrimera.Id}/foto");
        respuestaPrimera.EnsureSuccessStatusCode();
        (await respuestaPrimera.Content.ReadAsByteArrayAsync()).ShouldBe(JpegMinimo);

        var respuestaSegunda = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/novedades/{novedadSegunda.Id}/foto");
        respuestaSegunda.EnsureSuccessStatusCode();
        (await respuestaSegunda.Content.ReadAsByteArrayAsync()).ShouldBe(JpegSegundo);
    }

    [Fact]
    public async Task UnaFotoEnUnCuySinSignosClinicosNoSubeNiSeAncla()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CoopagcuyApi.Common.CentroAcopio.PAT);

        var blobsAntes = await ContarBlobsEvidenciasAsync();

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
                    signosClinicos = (string?)null,
                    fotoBase64 = Convert.ToBase64String(JpegMinimo)
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);

        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        var blobsDespues = await ContarBlobsEvidenciasAsync();
        (blobsDespues - blobsAntes).ShouldBe(0);

        await using var db = api.NuevoDbContext();
        var conFoto = await db.Novedades.AsNoTracking()
            .Where(n => n.FotoUrl != null)
            .ToListAsync();
        conFoto.ShouldBeEmpty();
    }
}
