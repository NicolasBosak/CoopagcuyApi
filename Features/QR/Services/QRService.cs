using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.QR.DTOs;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Infrastructure.Data;
using CoopagcuyApi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace CoopagcuyApi.Features.QR.Services;

public interface IQRService
{
    Task<QRResponseDto> GenerarQRAsync(string codigo);
    Task<QRResponseDto?> ObtenerPorCodigoLoteAsync(string codigo);
    Task<PaginaPublicaDto?> ObtenerPaginaPublicaAsync(string codigo);
    Task<byte[]?> DescargarQRPngAsync(string codigo);
}

/// <summary>
/// El QR pertenece al lote de producto terminado (FAE-…): una sesión de
/// planta genera un solo código aunque reúna jaulas de varias comunidades.
/// Los códigos de jaula (PAT-…, NIE-…) se mantienen para QR históricos.
/// </summary>
public class QRService(
    AppDbContext db,
    IBlobStorageService blobService,
    IConfiguration configuration) : IQRService
{
    private static bool EsLoteFaenado(string codigo) =>
        codigo.StartsWith("FAE-", StringComparison.OrdinalIgnoreCase);

    // ── Generación de QR — RF-401 ─────────────────────────────────────

    public async Task<QRResponseDto> GenerarQRAsync(string codigo)
    {
        if (!EsLoteFaenado(codigo))
            throw new InvalidOperationException(
                "El código QR se genera sobre el lote faenado (FAE-…), " +
                "no sobre la jaula de recepción.");

        var loteFaenado = await db.LotesFaenados
            .FirstOrDefaultAsync(lf => lf.Codigo == codigo)
            ?? throw new KeyNotFoundException(
                $"Lote faenado {codigo} no encontrado.");

        // Si ya tiene QR activo, devolverlo
        var existente = await db.CodigosQR.FirstOrDefaultAsync(
            q => q.LoteFaenadoId == loteFaenado.Id && q.Activo);
        if (existente is not null)
            return MapearQR(existente, codigo);

        var baseUrl = configuration["QR:BaseUrl"]
            ?? "https://localhost:7275/qr";
        var urlPublica = $"{baseUrl}/{codigo}";

        var pngBytes = GenerarImagenQR(urlPublica);
        var urlBlob = await blobService.SubirQRAsync(codigo, pngBytes);

        var codigoQR = new CodigoQR
        {
            LoteFaenadoId = loteFaenado.Id,
            UrlPublica = urlPublica,
            BlobPath = urlBlob,
            FechaGeneracion = DateTime.UtcNow,
            Activo = true
        };

        db.CodigosQR.Add(codigoQR);
        await db.SaveChangesAsync();

        return MapearQR(codigoQR, codigo);
    }

    // ── Consultas ─────────────────────────────────────────────────────

    public async Task<QRResponseDto?> ObtenerPorCodigoLoteAsync(string codigo)
    {
        var qr = await BuscarQRAsync(codigo);
        return qr is null ? null : MapearQR(qr, codigo);
    }

    public async Task<byte[]?> DescargarQRPngAsync(string codigo)
    {
        var qr = await BuscarQRAsync(codigo);
        if (qr is null) return null;

        // Regenerar el PNG desde la URL pública almacenada
        return GenerarImagenQR(qr.UrlPublica);
    }

    private async Task<CodigoQR?> BuscarQRAsync(string codigo)
    {
        return EsLoteFaenado(codigo)
            ? await db.CodigosQR
                .Include(q => q.LoteFaenado)
                .AsNoTracking()
                .FirstOrDefaultAsync(q =>
                    q.LoteFaenado != null && q.LoteFaenado.Codigo == codigo)
            : await db.CodigosQR
                .Include(q => q.Lote)
                .AsNoTracking()
                .FirstOrDefaultAsync(q =>
                    q.Lote != null && q.Lote.CodigoLote == codigo);
    }

    private static QRResponseDto MapearQR(CodigoQR qr, string codigo) => new(
        qr.Id, codigo, qr.UrlPublica, qr.BlobPath ?? string.Empty,
        qr.Activo, qr.FechaGeneracion);

    // ── Página pública del consumidor — RF-402 ────────────────────────

    public async Task<PaginaPublicaDto?> ObtenerPaginaPublicaAsync(string codigo)
    {
        List<RegistroFaenamiento> sesiones;
        DateTime fechaFaenamiento;

        if (EsLoteFaenado(codigo))
        {
            // Endpoint público y anónimo: es la consulta más golpeada del
            // sistema, siempre sin tracking y con includes separados
            var loteFaenado = await db.LotesFaenados
                .Include(lf => lf.Sesiones).ThenInclude(f => f.Cuyes)
                .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                    .ThenInclude(l => l.Productora)
                .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                    .ThenInclude(l => l.Cuyes).ThenInclude(c => c.Productora)
                .AsNoTracking()
                .AsSplitQuery()
                .FirstOrDefaultAsync(lf => lf.Codigo == codigo);

            if (loteFaenado is null) return null;

            var tieneQR = await db.CodigosQR.AnyAsync(
                q => q.LoteFaenadoId == loteFaenado.Id && q.Activo);
            if (!tieneQR) return null;

            sesiones = loteFaenado.Sesiones.ToList();
            fechaFaenamiento = loteFaenado.FechaFaenamiento;
        }
        else
        {
            // QR histórico generado sobre la jaula de recepción
            var lote = await db.Lotes
                .Include(l => l.Productora)
                .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
                .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
                .Include(l => l.CodigoQR)
                .AsNoTracking()
                .AsSplitQuery()
                .FirstOrDefaultAsync(l => l.CodigoLote == codigo);

            if (lote is null || lote.CodigoQR is null || !lote.CodigoQR.Activo)
                return null;

            sesiones = lote.Faenamientos.ToList();
            fechaFaenamiento = sesiones
                .OrderByDescending(f => f.FechaFaenamiento)
                .FirstOrDefault()?.FechaFaenamiento ?? DateTime.UtcNow;
        }

        return await ConstruirPaginaAsync(codigo, sesiones, fechaFaenamiento);
    }

    private async Task<PaginaPublicaDto> ConstruirPaginaAsync(
        string codigo, List<RegistroFaenamiento> sesiones,
        DateTime fechaFaenamiento)
    {
        // Cada animal faenado con su origen: la sesión de planta puede
        // reunir jaulas de varias comunidades
        var animales = sesiones
            .SelectMany(f => f.Cuyes
                .Where(cf => cf.Estado != EstadoCanal.Rechazado)
                .Select(cf => (
                    Faenado: cf,
                    Jaula: f.Lote,
                    Comunidad: f.Lote.Cuyes
                        .FirstOrDefault(c => c.NumeroEnLote == cf.NumeroEnLote)
                        ?.Productora?.Comunidad
                        ?? f.Lote.Productora?.Comunidad
                        ?? "Azuay")))
            .ToList();

        var comunidadesAporte = animales
            .GroupBy(a => a.Comunidad)
            .Select(g => new ComunidadAporteDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Cantidad)
            .ToList();

        var comunidadOrigen = comunidadesAporte.Count > 0
            ? string.Join(" y ", comunidadesAporte.Select(c => c.Comunidad))
            : "Azuay";

        var detalleCuyes = animales
            .OrderBy(a => a.Jaula.CodigoLote)
            .ThenBy(a => a.Faenado.NumeroEnLote)
            .Select(a => new CuyPublicoDto(
                Comunidad: a.Comunidad,
                CodigoJaula: a.Jaula.CodigoLote,
                NumeroEnLote: a.Faenado.NumeroEnLote,
                PesoCanalGramos: a.Faenado.PesoCanalGramos,
                Estado: a.Faenado.Estado == EstadoCanal.ConNovedad
                    ? "Con novedad" : "Apto"))
            .ToList();

        var observacionesProceso = sesiones
            .SelectMany(f => f.Cuyes)
            .Where(cf => cf.Estado == EstadoCanal.ConNovedad &&
                         !string.IsNullOrWhiteSpace(cf.Motivo))
            .Select(cf => cf.Motivo!.Trim())
            .Distinct()
            .ToList();

        var unidadesTotales = animales.Count;
        var pesoCanalTotal = animales.Sum(a => a.Faenado.PesoCanalGramos ?? 0);
        var promedio = unidadesTotales > 0 ? pesoCanalTotal / unidadesTotales : 0;

        var parametros = new List<string>();
        if (promedio >= 907)
            parametros.Add($"✓ Peso canal óptimo ({promedio:F0}g)");
        else if (promedio >= 880)
            parametros.Add($"✓ Peso canal aceptable ({promedio:F0}g)");
        parametros.Add("✓ Alimentación a base de forraje vegetal");
        parametros.Add("✓ Crianza familiar — Azuay, Ecuador");

        var lotesOrigen = sesiones.Select(s => s.LoteId).Distinct().ToList();
        var faeIds = sesiones
            .Where(s => s.LoteFaenadoId != null)
            .Select(s => s.LoteFaenadoId!.Value)
            .Distinct()
            .ToList();
        var primerLote = sesiones.Select(s => s.Lote)
            .OrderBy(l => l.FechaRecepcion)
            .FirstOrDefault();

        var cantones = sesiones
            .Select(s => s.Lote.Productora?.Canton)
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct()
            .ToList();

        // Trazabilidad hacia adelante: último despacho del lote faenado
        // (o de las jaulas de origen, para despachos legados)
        var ultimoDespacho = await db.Despachos
            .Where(d =>
                (d.LoteFaenadoId != null && faeIds.Contains(d.LoteFaenadoId.Value))
                || (d.LoteId != null && lotesOrigen.Contains(d.LoteId.Value)))
            .OrderByDescending(d => d.FechaDespacho)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        var conNovedad = detalleCuyes.Any(c => c.Estado != "Apto");

        return new PaginaPublicaDto(
            CodigoLote: codigo,
            ComunidadOrigen: comunidadOrigen,
            Canton: cantones.Count > 0 ? string.Join(" y ", cantones) : "Azuay",
            NombreProductora: comunidadesAporte.Count > 1
                ? $"Familias productoras de {comunidadOrigen}"
                : comunidadesAporte.Count == 1
                    ? $"Familia productora de {comunidadesAporte[0].Comunidad}"
                    : "Familias productoras de COOPAGCUY",
            CentroAcopio: string.Join(" y ", sesiones
                .Select(s => s.Lote.CentroAcopio.ToString()).Distinct()),
            FechaRecepcion: primerLote?.FechaRecepcion ?? fechaFaenamiento,
            CantidadAnimales: unidadesTotales,
            EstadoCalidad: conNovedad ? "ConNovedad" : "Aceptado",
            ParametrosAprobados: parametros,
            FechaFaenamiento: fechaFaenamiento,
            PesoPromedioCanalGramos: Math.Round(promedio, 0),
            EstadoCanal: conNovedad ? "ConNovedad" : "Apto",
            Marca: "Cuy Azuayito — COOPAGCUY",
            FechaComercializacion: ultimoDespacho?.FechaDespacho,
            DestinoComercial: ultimoDespacho?.ClienteDestino,
            ObservacionesProceso: observacionesProceso,
            ComunidadesAporte: comunidadesAporte,
            DetalleCuyes: detalleCuyes
        );
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
