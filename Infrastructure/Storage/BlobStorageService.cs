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

    /// Sube una captura de transferencia al contenedor PRIVADO de
    /// comprobantes y devuelve su URI.
    Task<string> SubirComprobanteAsync(string nombre, byte[] imagen);

    /// Bytes de la captura, o null si el blob ya no existe.
    Task<byte[]?> DescargarComprobanteAsync(string nombre);

    /// Borra la captura. No lanza si ya no está: el barrido oportunista
    /// puede pisarse consigo mismo y no puede tumbar la petición que lo
    /// dispara.
    Task BorrarComprobanteAsync(string nombre);
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

    // IsNullOrWhiteSpace y no `??`, por el mismo motivo que la cadena de
    // conexión de arriba: appsettings.json declara estas claves con cadena
    // VACÍA como superficie de documentación, y `??` solo cubre null. Con
    // `??`, una clave vacía dejaba el nombre del contenedor en "", la URL
    // salía sin contenedor y Azure respondía InvalidQueryParameterValue
    // (comp) — un 500 en cada entrega con foto, ilegible desde el cliente.
    private readonly string _containerName =
        !string.IsNullOrWhiteSpace(configuration["AzureBlob:ContainerName"])
            ? configuration["AzureBlob:ContainerName"]!
            : "qr-coopagcuy";

    // Contenedor SEPARADO del de QR, por dos motivos: el de QR es público a
    // propósito (tiene que escanearse desde fuera) y una foto de defectos de
    // un proveedor no debe serlo; y la política de caducidad se aplica por
    // contenedor, así que compartirlo borraría también los QR a los 90 días.
    private readonly string _containerEvidencias =
        !string.IsNullOrWhiteSpace(configuration["AzureBlob:ContainerEvidencias"])
            ? configuration["AzureBlob:ContainerEvidencias"]!
            : "evidencias-clinicas";

    // Tercer contenedor, y no una carpeta dentro de evidencias: la política
    // de ciclo de vida se aplica POR CONTENEDOR. Compartirlo borraría las
    // evidencias clínicas a los 30 días en vez de a los 90.
    //
    // IsNullOrWhiteSpace y no `??`, por lo mismo que los otros dos.
    private readonly string _containerComprobantes =
        !string.IsNullOrWhiteSpace(configuration["AzureBlob:ContainerComprobantes"])
            ? configuration["AzureBlob:ContainerComprobantes"]!
            : "comprobantes-pago";

    public async Task<string> SubirQRAsync(string codigoLote, byte[] imagenPng)
    {
        // PublicAccessType.Blob: público A PROPÓSITO. Es el único de los tres
        // contenedores que lo es, porque el QR se escanea desde fuera del
        // sistema. Si esto se invierte con el de comprobantes, se publican
        // capturas de transferencias bancarias.
        var contenedor = await ContenedorAsync(_containerName, PublicAccessType.Blob);
        return await SubirAsync(contenedor, $"qr/{codigoLote}.png", imagenPng);
    }

    public async Task<string> SubirEvidenciaAsync(string nombre, byte[] jpeg)
    {
        var contenedor = await ContenedorEvidenciasAsync();
        return await SubirAsync(contenedor, nombre, jpeg);
    }

    public async Task<byte[]?> DescargarEvidenciaAsync(string nombre)
    {
        var contenedor = await ContenedorEvidenciasAsync();
        // null en 404 y no excepción: la política de ciclo de vida ya borró
        // el blob. No es un error: la fila de la novedad sobrevive al
        // binario por diseño.
        return await DescargarAsync(contenedor, nombre);
    }

    private async Task<BlobContainerClient> ContenedorEvidenciasAsync() =>
        // PublicAccessType.None, no Blob: la evidencia se sirve solo a través
        // del endpoint autenticado del API.
        await ContenedorAsync(_containerEvidencias, PublicAccessType.None);

    public async Task<string> SubirComprobanteAsync(string nombre, byte[] imagen)
    {
        var contenedor = await ContenedorComprobantesAsync();
        return await SubirAsync(contenedor, nombre, imagen);
    }

    public async Task<byte[]?> DescargarComprobanteAsync(string nombre)
    {
        var contenedor = await ContenedorComprobantesAsync();
        // null en 404 y no excepción: ya lo borró el barrido o la política
        // de Azure. No es un error: la fila del pago sobrevive al binario
        // por diseño.
        return await DescargarAsync(contenedor, nombre);
    }

    public async Task BorrarComprobanteAsync(string nombre)
    {
        var contenedor = await ContenedorComprobantesAsync();
        // DeleteIfExists y no Delete: dos consultas simultáneas pueden barrer
        // el mismo blob, y la segunda no puede reventar por llegar tarde.
        await contenedor.GetBlobClient(nombre).DeleteIfExistsAsync();
    }

    private async Task<BlobContainerClient> ContenedorComprobantesAsync() =>
        // None y no Blob: una captura de transferencia bancaria no puede ser
        // pública. Se sirve solo por el endpoint autenticado del API.
        await ContenedorAsync(_containerComprobantes, PublicAccessType.None);

    // Machinery compartida por los tres contenedores. El incidente del
    // 2026-08-20 (nombre de contenedor leído con `??` en vez de
    // IsNullOrWhiteSpace, cadena vacía en la URL, Azure respondiendo
    // InvalidQueryParameterValue en cada entrega con foto) se arregló en un
    // único sitio en vez de tres precisamente porque esto está centralizado
    // aquí: cada contenedor nuevo hereda la guardia sin poder olvidarla.
    private async Task<BlobContainerClient> ContenedorAsync(
        string nombre, PublicAccessType acceso)
    {
        var cliente = new BlobServiceClient(_connectionString);
        var contenedor = cliente.GetBlobContainerClient(nombre);
        await contenedor.CreateIfNotExistsAsync(acceso);
        return contenedor;
    }

    private static async Task<string> SubirAsync(
        BlobContainerClient contenedor, string blobNombre, byte[] datos)
    {
        var blob = contenedor.GetBlobClient(blobNombre);

        using var stream = new MemoryStream(datos);
        await blob.UploadAsync(stream, overwrite: true);

        return blob.Uri.ToString();
    }

    private static async Task<byte[]?> DescargarAsync(
        BlobContainerClient contenedor, string nombre)
    {
        var blob = contenedor.GetBlobClient(nombre);

        try
        {
            var respuesta = await blob.DownloadContentAsync();
            return respuesta.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
