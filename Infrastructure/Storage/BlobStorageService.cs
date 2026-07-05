using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CoopagcuyApi.Infrastructure.Storage;

public interface IBlobStorageService
{
    Task<string> SubirQRAsync(string codigoLote, byte[] imagenPng);
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
}
