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
