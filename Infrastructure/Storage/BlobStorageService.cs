using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CoopagcuyApi.Infrastructure.Storage;

public interface IBlobStorageService
{
    Task<string> SubirQRAsync(string codigoLote, byte[] imagenPng);
    Task<bool> EliminarAsync(string blobPath);
}

public class BlobStorageService(IConfiguration configuration) : IBlobStorageService
{
    private readonly string _connectionString =
        configuration["AzureBlob:ConnectionString"]
        ?? throw new InvalidOperationException("AzureBlob:ConnectionString no configurado.");

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

    public async Task<bool> EliminarAsync(string blobPath)
    {
        var cliente = new BlobServiceClient(_connectionString);
        var contenedor = cliente.GetBlobContainerClient(_containerName);
        var blob = contenedor.GetBlobClient(blobPath);
        var resultado = await blob.DeleteIfExistsAsync();
        return resultado.Value;
    }
}
