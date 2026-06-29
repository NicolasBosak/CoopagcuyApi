using CoopagcuyApi.Features.QR.DTOs;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Infrastructure.Data;
using CoopagcuyApi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace CoopagcuyApi.Features.QR.Services;

public interface IQRService
{
    Task<QRResponseDto> GenerarQRAsync(string codigoLote);
    Task<QRResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote);
    Task<PaginaPublicaDto?> ObtenerPaginaPublicaAsync(string codigoLote);
    Task<byte[]?> DescargarQRPngAsync(string codigoLote);
}

public class QRService(
    AppDbContext db,
    IBlobStorageService blobService,
    IConfiguration configuration) : IQRService
{
    // ── Generación de QR — RF-401 ─────────────────────────────────────

    public async Task<QRResponseDto> GenerarQRAsync(string codigoLote)
    {
        // 1. Verificar que el lote existe y tiene faenamiento
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Faenamiento)
            .Include(l => l.CodigoQR)
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException(
                $"Lote {codigoLote} no encontrado.");

        if (lote.Faenamiento is null)
            throw new InvalidOperationException(
                $"El lote {codigoLote} no tiene faenamiento registrado. " +
                "Complete el faenamiento antes de generar el QR.");

        // 2. Si ya tiene QR activo, devolverlo
        if (lote.CodigoQR is not null && lote.CodigoQR.Activo)
            return new QRResponseDto(
                lote.CodigoQR.Id,
                codigoLote,
                lote.CodigoQR.UrlPublica,
                lote.CodigoQR.BlobPath ?? string.Empty,
                lote.CodigoQR.Activo,
                lote.CodigoQR.FechaGeneracion);

        // 3. Construir URL pública de la página del consumidor
        var baseUrl = configuration["QR:BaseUrl"]
            ?? "https://localhost:7275/qr";
        var urlPublica = $"{baseUrl}/{codigoLote}";

        // 4. Generar imagen PNG del QR
        var pngBytes = GenerarImagenQR(urlPublica);

        // 5. Subir PNG a Azure Blob Storage
        var urlBlob = await blobService.SubirQRAsync(codigoLote, pngBytes);

        // 6. Guardar registro en la base de datos
        var codigoQR = new CodigoQR
        {
            LoteId = lote.Id,
            UrlPublica = urlPublica,
            BlobPath = urlBlob,
            FechaGeneracion = DateTime.UtcNow,
            Activo = true
        };

        db.CodigosQR.Add(codigoQR);
        await db.SaveChangesAsync();

        return new QRResponseDto(
            codigoQR.Id,
            codigoLote,
            codigoQR.UrlPublica,
            codigoQR.BlobPath,
            codigoQR.Activo,
            codigoQR.FechaGeneracion);
    }

    // ── Consultas ─────────────────────────────────────────────────────

    public async Task<QRResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote)
    {
        var qr = await db.CodigosQR
            .Include(q => q.Lote)
            .FirstOrDefaultAsync(q => q.Lote.CodigoLote == codigoLote);

        if (qr is null) return null;

        return new QRResponseDto(
            qr.Id,
            codigoLote,
            qr.UrlPublica,
            qr.BlobPath ?? string.Empty,
            qr.Activo,
            qr.FechaGeneracion);
    }

    // ── Página pública del consumidor — RF-402 ────────────────────────

    public async Task<PaginaPublicaDto?> ObtenerPaginaPublicaAsync(string codigoLote)
    {
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            .Include(l => l.Faenamiento)
            .Include(l => l.CodigoQR)
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote);

        if (lote is null || lote.CodigoQR is null || !lote.CodigoQR.Activo)
            return null;

        // Construir lista de parámetros aprobados para mostrar al consumidor
        var parametros = new List<string>();

        if (lote.Faenamiento is not null)
        {
            var promedio = lote.Faenamiento.UnidadesFaenadas > 0
                ? lote.Faenamiento.PesoTotalCanalGramos / lote.Faenamiento.UnidadesFaenadas
                : 0;

            if (promedio >= 907)
                parametros.Add($"✓ Peso canal óptimo ({promedio:F0}g)");
            else if (promedio >= 880)
                parametros.Add($"✓ Peso canal aceptable ({promedio:F0}g)");
        }

        if (!lote.Novedades.Any(n =>
            n.Tipo == Common.TipoNovedad.ColorNoConforme))
            parametros.Add("✓ Color de pelaje conforme");

        if (!lote.Novedades.Any(n =>
            n.Tipo == Common.TipoNovedad.OrejaDura))
            parametros.Add("✓ Edad óptima (3–4 meses)");

        if (!lote.Novedades.Any(n =>
            n.Tipo == Common.TipoNovedad.SinAyuno))
            parametros.Add("✓ Recibido en ayunas");

        parametros.Add("✓ Alimentación a base de forraje vegetal");
        parametros.Add("✓ Crianza familiar — Azuay, Ecuador");

        var promediCanal = lote.Faenamiento?.UnidadesFaenadas > 0
            ? lote.Faenamiento!.PesoTotalCanalGramos / lote.Faenamiento.UnidadesFaenadas
            : 0;

        return new PaginaPublicaDto(
            CodigoLote: codigoLote,
            ComunidadOrigen: lote.Productora.Comunidad,
            Canton: lote.Productora.Canton,
            NombreProductora: $"Familia productora de {lote.Productora.Comunidad}",
            CentroAcopio: lote.CentroAcopio.ToString(),
            FechaRecepcion: lote.FechaRecepcion,
            CantidadAnimales: lote.CantidadAnimales,
            EstadoCalidad: lote.Estado.ToString(),
            ParametrosAprobados: parametros,
            FechaFaenamiento: lote.Faenamiento?.FechaFaenamiento ?? DateTime.UtcNow,
            PesoPromedioCanalGramos: Math.Round(promediCanal, 0),
            EstadoCanal: lote.Faenamiento?.EstadoCanal.ToString() ?? string.Empty,
            Marca: "Cuy Azuayito — COOPAGCUY"
        );
    }

    // ── Descarga del PNG — RF-405 ─────────────────────────────────────

    public async Task<byte[]?> DescargarQRPngAsync(string codigoLote)
    {
        var qr = await db.CodigosQR
            .Include(q => q.Lote)
            .FirstOrDefaultAsync(q => q.Lote.CodigoLote == codigoLote);

        if (qr is null) return null;

        // Regenerar el PNG desde la URL pública almacenada
        return GenerarImagenQR(qr.UrlPublica);
    }

    // ── Generación de imagen QR ───────────────────────────────────────

    private static byte[] GenerarImagenQR(string contenido)
    {
        using var generador = new QRCodeGenerator();
        var datos = generador.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(datos);
        return qrCode.GetGraphic(10); // 10 px por módulo → ~330x330 px
    }
}