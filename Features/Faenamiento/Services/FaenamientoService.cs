using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.DTOs;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Faenamiento.Services;

public interface IFaenamientoService
{
    Task<FaenamientoBatchResultadoDto> RegistrarBatchAsync(RegistrarFaenamientoBatchDto dto);
    Task<IEnumerable<LoteDisponibleDto>> LotesDisponiblesAsync();
    Task<FaenamientoResponseDto?> ObtenerPorLoteIdAsync(int loteId);
    Task<FaenamientoResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote);
    Task<IEnumerable<FaenamientoResponseDto>> ListarAsync(DateTime? desde, DateTime? hasta);
    Task<DespachoResponseDto> RegistrarDespachoAsync(RegistrarDespachoDto dto);
    Task<IEnumerable<DespachoResponseDto>> ListarDespachosPorLoteAsync(int loteId);
    Task<IEnumerable<DespachoResponseDto>> ListarDespachosAsync(DateTime? desde, DateTime? hasta);
    Task<DevolucionResponseDto> RegistrarDevolucionAsync(RegistrarDevolucionDto dto);
    Task<IEnumerable<DevolucionResponseDto>> ListarDevolucionesAsync(
        DateTime? desde, DateTime? hasta, int? productoraId);
    Task<IEnumerable<RetornoProductoraResponseDto>> ListarRetornosAsync(
        DateTime? desde, DateTime? hasta, int? productoraId);
    Task<InkJetCodigoDto?> ObtenerDatosInkJetAsync(string codigoLote);
}

public class FaenamientoService(AppDbContext db) : IFaenamientoService
{
    // ── Lotes con saldo pendiente de faenar ───────────────────────────
    // La planta solo ve lotes cerrados con animales disponibles: al llegar
    // el saldo a cero, el lote desaparece de esta vista.

    public async Task<IEnumerable<LoteDisponibleDto>> LotesDisponiblesAsync()
    {
        var lotes = await db.Lotes
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
            .Where(l => l.Cerrado && l.Estado != EstadoLote.Rechazado)
            .OrderBy(l => l.FechaRecepcion)
            .ToListAsync();

        return lotes
            .Select(l =>
            {
                var numerosUsados = l.Faenamientos
                    .SelectMany(f => f.Cuyes.Select(c => c.NumeroEnLote))
                    .ToHashSet();

                var usados = l.Faenamientos.Sum(f =>
                    f.Cuyes.Count > 0 ? f.Cuyes.Count
                        : f.UnidadesFaenadas + f.UnidadesDecomisadas);
                var disponibles = Math.Max(0, l.CantidadAnimales - usados);

                var cuyesDisponibles = l.Cuyes
                    .Where(c => !numerosUsados.Contains(c.NumeroEnLote)
                                && c.Estado != EstadoLote.Rechazado)
                    .OrderBy(c => c.NumeroEnLote)
                    .Select(c => new CuyDisponibleDto(
                        c.NumeroEnLote, c.PesoGramos,
                        c.Estado.ToString(), c.MotivoNovedad,
                        c.Productora?.NombreCompleto, c.Productora?.Comunidad))
                    .ToList();

                return new LoteDisponibleDto(
                    l.Id, l.CodigoLote, l.CentroAcopio.ToString(),
                    l.FechaRecepcion, l.CantidadAnimales,
                    disponibles, cuyesDisponibles);
            })
            .Where(l => l.Disponibles > 0)
            .ToList();
    }

    // ── Faenamiento por cuota, tomando animales de varios lotes ───────

    public async Task<FaenamientoBatchResultadoDto> RegistrarBatchAsync(
        RegistrarFaenamientoBatchDto dto)
    {
        if (dto.Lotes.Count == 0 || dto.Lotes.All(l => l.Cuyes.Count == 0))
            throw new InvalidOperationException(
                "Selecciona al menos un cuy para faenar.");

        var registros = new List<FaenamientoResponseDto>();
        var alertas = new List<AlertaNovedadPreviaDto>();

        await using var transaccion = await db.Database.BeginTransactionAsync();

        // Toda la sesión de planta —aunque tome animales de varias jaulas
        // y comunidades— se agrupa bajo un solo lote de producto terminado
        var loteFaenado = new LoteFaenado
        {
            Codigo = await GenerarCodigoFaenadoAsync(dto.FechaFaenamiento),
            FechaFaenamiento = DateTime.SpecifyKind(
                dto.FechaFaenamiento, DateTimeKind.Utc),
            OperarioResponsable = dto.OperarioResponsable,
            TemperaturaAlmacenamiento = dto.TemperaturaAlmacenamiento,
            Observaciones = dto.Observaciones
        };
        db.LotesFaenados.Add(loteFaenado);
        await db.SaveChangesAsync();

        foreach (var sesionLote in dto.Lotes.Where(l => l.Cuyes.Count > 0))
        {
            var (registro, alertasLote) = await RegistrarSesionLoteAsync(
                sesionLote, dto, loteFaenado);
            registros.Add(registro);
            alertas.AddRange(alertasLote);
        }

        await transaccion.CommitAsync();

        return new FaenamientoBatchResultadoDto(
            loteFaenado.Codigo, registros, alertas);
    }

    // Código del producto terminado: FAE-AAAAMMDD-SEC (mismo formato que
    // el código de jaula, con prefijo de la planta)
    private async Task<string> GenerarCodigoFaenadoAsync(DateTime fecha)
    {
        var fechaUtc = DateTime.SpecifyKind(fecha, DateTimeKind.Utc);
        var baseStr = $"FAE-{fechaUtc:yyyyMMdd}-";

        var conteo = await db.LotesFaenados
            .CountAsync(lf => lf.Codigo.StartsWith(baseStr));

        return $"{baseStr}{(conteo + 1):D3}";
    }

    private async Task<(FaenamientoResponseDto, List<AlertaNovedadPreviaDto>)>
        RegistrarSesionLoteAsync(
            FaenamientoLoteDto sesion, RegistrarFaenamientoBatchDto dto,
            LoteFaenado loteFaenado)
    {
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
            .FirstOrDefaultAsync(l => l.Id == sesion.LoteId)
            ?? throw new KeyNotFoundException(
                $"Lote con Id {sesion.LoteId} no encontrado.");

        if (lote.Estado == EstadoLote.Rechazado)
            throw new InvalidOperationException(
                $"El lote {lote.CodigoLote} está rechazado y no puede faenarse.");

        // Validar que los números elegidos siguen disponibles
        var numerosUsados = lote.Faenamientos
            .SelectMany(f => f.Cuyes.Select(c => c.NumeroEnLote))
            .ToHashSet();

        foreach (var c in sesion.Cuyes)
        {
            if (numerosUsados.Contains(c.NumeroEnLote))
                throw new InvalidOperationException(
                    $"El cuy #{c.NumeroEnLote} del lote {lote.CodigoLote} " +
                    "ya fue procesado en una sesión anterior.");

            if (c.NumeroEnLote < 1 || c.NumeroEnLote > lote.CantidadAnimales)
                throw new InvalidOperationException(
                    $"El cuy #{c.NumeroEnLote} no existe en el lote {lote.CodigoLote}.");
        }

        var rechazados = sesion.Cuyes
            .Where(c => c.Estado == EstadoCanal.Rechazado).ToList();
        var unidadesFaenadas = sesion.Cuyes.Count - rechazados.Count;
        var pesoTotal = sesion.Cuyes
            .Where(c => c.Estado != EstadoCanal.Rechazado)
            .Sum(c => c.PesoCanalGramos ?? 0);

        var estadoCanal = rechazados.Count == sesion.Cuyes.Count
            ? EstadoCanal.Rechazado
            : rechazados.Count > 0 ||
              sesion.Cuyes.Any(c => c.Estado == EstadoCanal.ConNovedad)
                ? EstadoCanal.ConNovedad
                : EstadoCanal.Apto;

        var faenamiento = new RegistroFaenamiento
        {
            LoteId = lote.Id,
            LoteFaenadoId = loteFaenado.Id,
            LoteFaenado = loteFaenado,
            // Distintivo secuencial de la sesión dentro del lote (F1, F2…)
            NumeroSesion = lote.Faenamientos.Count + 1,
            FechaFaenamiento = DateTime.SpecifyKind(
                dto.FechaFaenamiento, DateTimeKind.Utc),
            OperarioResponsable = dto.OperarioResponsable,
            UnidadesFaenadas = unidadesFaenadas,
            PesoTotalCanalGramos = pesoTotal,
            TemperaturaAlmacenamiento = dto.TemperaturaAlmacenamiento,
            EstadoCanal = estadoCanal,
            Observaciones = dto.Observaciones,
            UnidadesDecomisadas = rechazados.Count(c => !c.RetornarAProductora),
            MotivoDecomiso = rechazados.Count > 0
                ? string.Join("; ", rechazados
                    .Where(c => !string.IsNullOrWhiteSpace(c.Motivo))
                    .Select(c => $"Cuy #{c.NumeroEnLote}: {c.Motivo!.Trim()}"))
                : null
        };

        db.Faenamientos.Add(faenamiento);
        await db.SaveChangesAsync();

        var alertas = new List<AlertaNovedadPreviaDto>();

        foreach (var c in sesion.Cuyes)
        {
            db.CuyFaenamientos.Add(new CuyFaenamiento
            {
                RegistroFaenamientoId = faenamiento.Id,
                NumeroEnLote = c.NumeroEnLote,
                PesoCanalGramos = c.PesoCanalGramos,
                Estado = c.Estado,
                Motivo = string.IsNullOrWhiteSpace(c.Motivo) ? null : c.Motivo.Trim(),
                RetornadoAProductora = c.Estado == EstadoCanal.Rechazado
                    && c.RetornarAProductora
            });

            var cuyRecepcion = lote.Cuyes
                .FirstOrDefault(x => x.NumeroEnLote == c.NumeroEnLote);
            var productoraCuy = cuyRecepcion?.Productora ?? lote.Productora;

            // Cruce con recepción: si la novedad de planta ya venía
            // registrada desde el CAT, se identifica a la productora
            // que envió ese cuy específico
            if (c.Estado != EstadoCanal.Apto &&
                cuyRecepcion?.MotivoNovedad is not null)
            {
                alertas.Add(new AlertaNovedadPreviaDto(
                    lote.CodigoLote,
                    c.NumeroEnLote,
                    cuyRecepcion.MotivoNovedad,
                    productoraCuy?.NombreCompleto,
                    productoraCuy?.Comunidad));
            }

            if (c.Estado == EstadoCanal.Rechazado && c.RetornarAProductora)
            {
                if (productoraCuy is null)
                    throw new InvalidOperationException(
                        $"No se puede retornar el cuy #{c.NumeroEnLote}: " +
                        "no tiene productora de origen registrada.");

                db.RetornosProductora.Add(new RetornoProductora
                {
                    LoteId = lote.Id,
                    ProductoraId = productoraCuy.Id,
                    NumeroEnLote = c.NumeroEnLote,
                    Motivo = string.IsNullOrWhiteSpace(c.Motivo)
                        ? "No apto para faenamiento" : c.Motivo.Trim(),
                    Responsable = dto.OperarioResponsable
                });
            }
        }

        await db.SaveChangesAsync();

        return (MapearFaenamiento(faenamiento, lote), alertas);
    }

    // ── Consultas ─────────────────────────────────────────────────────

    public async Task<FaenamientoResponseDto?> ObtenerPorLoteIdAsync(int loteId)
    {
        var faenamiento = await db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .Include(f => f.LoteFaenado)
            .Include(f => f.Cuyes)
            .FirstOrDefaultAsync(f => f.LoteId == loteId);

        return faenamiento is null
            ? null
            : MapearFaenamiento(faenamiento, faenamiento.Lote);
    }

    public async Task<FaenamientoResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote)
    {
        var faenamiento = await db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .Include(f => f.LoteFaenado)
            .Include(f => f.Cuyes)
            .FirstOrDefaultAsync(f => f.Lote.CodigoLote == codigoLote);

        return faenamiento is null
            ? null
            : MapearFaenamiento(faenamiento, faenamiento.Lote);
    }

    public async Task<IEnumerable<FaenamientoResponseDto>> ListarAsync(
        DateTime? desde, DateTime? hasta)
    {
        var query = db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .Include(f => f.LoteFaenado)
            .Include(f => f.Cuyes)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(f =>
                f.FechaFaenamiento >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(f =>
                f.FechaFaenamiento <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        var lista = await query
            .OrderByDescending(f => f.FechaFaenamiento)
            .ToListAsync();

        return lista.Select(f => MapearFaenamiento(f, f.Lote));
    }

    // ── Registro de despacho ──────────────────────────────────────────

    public async Task<DespachoResponseDto> RegistrarDespachoAsync(
        RegistrarDespachoDto dto)
    {
        // Verificar que el lote existe y tiene faenamiento registrado
        var lote = await db.Lotes
            .Include(l => l.Faenamientos)
            .FirstOrDefaultAsync(l => l.Id == dto.LoteId)
            ?? throw new KeyNotFoundException(
                $"Lote con Id {dto.LoteId} no encontrado.");

        if (lote.Faenamientos.Count == 0)
            throw new InvalidOperationException(
                $"El lote {lote.CodigoLote} no tiene faenamiento registrado. " +
                "Registre el faenamiento antes de despachar.");

        var despacho = new Despacho
        {
            LoteId = dto.LoteId,
            ClienteDestino = dto.ClienteDestino,
            FechaDespacho = DateTime.SpecifyKind(dto.FechaDespacho, DateTimeKind.Utc),
            CantidadUnidades = dto.CantidadUnidades,
            Responsable = dto.Responsable,
            Transporte = dto.Transporte,
            Observaciones = dto.Observaciones
        };

        db.Despachos.Add(despacho);
        await db.SaveChangesAsync();

        return new DespachoResponseDto(
            Id: despacho.Id,
            LoteId: despacho.LoteId,
            CodigoLote: lote.CodigoLote,
            ClienteDestino: despacho.ClienteDestino,
            FechaDespacho: despacho.FechaDespacho,
            CantidadUnidades: despacho.CantidadUnidades,
            Responsable: despacho.Responsable,
            Transporte: despacho.Transporte,
            Observaciones: despacho.Observaciones
        );
    }

    public async Task<IEnumerable<DespachoResponseDto>> ListarDespachosPorLoteAsync(
        int loteId)
    {
        var lote = await db.Lotes.FindAsync(loteId)
            ?? throw new KeyNotFoundException($"Lote con Id {loteId} no encontrado.");

        return await db.Despachos
            .Where(d => d.LoteId == loteId)
            .OrderByDescending(d => d.FechaDespacho)
            .Select(d => new DespachoResponseDto(
                d.Id, d.LoteId, lote.CodigoLote, d.ClienteDestino,
                d.FechaDespacho, d.CantidadUnidades, d.Responsable,
                d.Transporte, d.Observaciones))
            .ToListAsync();
    }

    // Historial completo de despachos con filtro opcional por fecha.
    public async Task<IEnumerable<DespachoResponseDto>> ListarDespachosAsync(
        DateTime? desde, DateTime? hasta)
    {
        var query = db.Despachos
            .Include(d => d.Lote)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(d =>
                d.FechaDespacho >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(d =>
                d.FechaDespacho <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        return await query
            .OrderByDescending(d => d.FechaDespacho)
            .Select(d => new DespachoResponseDto(
                d.Id, d.LoteId, d.Lote.CodigoLote, d.ClienteDestino,
                d.FechaDespacho, d.CantidadUnidades, d.Responsable,
                d.Transporte, d.Observaciones))
            .ToListAsync();
    }

    // ── Devoluciones de clientes — RF-307 ─────────────────────────────

    public async Task<DevolucionResponseDto> RegistrarDevolucionAsync(
        RegistrarDevolucionDto dto)
    {
        // La devolución solo aplica a producto despachado: el lote debe
        // tener faenamiento registrado.
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Faenamientos)
            .FirstOrDefaultAsync(l => l.Id == dto.LoteId)
            ?? throw new KeyNotFoundException(
                $"Lote con Id {dto.LoteId} no encontrado.");

        if (lote.Faenamientos.Count == 0)
            throw new InvalidOperationException(
                $"El lote {lote.CodigoLote} no tiene faenamiento registrado. " +
                "No es posible registrar una devolución.");

        // La sesión indicada debe pertenecer al lote
        RegistroFaenamiento? sesion = null;
        if (dto.RegistroFaenamientoId is int sesionId)
        {
            sesion = lote.Faenamientos.FirstOrDefault(f => f.Id == sesionId)
                ?? throw new InvalidOperationException(
                    $"La sesión de faenamiento indicada no pertenece " +
                    $"al lote {lote.CodigoLote}.");
        }

        var devolucion = new Devolucion
        {
            LoteId = dto.LoteId,
            RegistroFaenamientoId = sesion?.Id,
            ClienteDevuelve = dto.ClienteDevuelve,
            FechaDevolucion = DateTime.SpecifyKind(dto.FechaDevolucion, DateTimeKind.Utc),
            CantidadUnidades = dto.CantidadUnidades,
            Motivo = dto.Motivo,
            Responsable = dto.Responsable,
            Observaciones = dto.Observaciones
        };

        db.Devoluciones.Add(devolucion);
        await db.SaveChangesAsync();

        return MapearDevolucion(devolucion, lote, sesion?.NumeroSesion);
    }

    public async Task<IEnumerable<DevolucionResponseDto>> ListarDevolucionesAsync(
        DateTime? desde, DateTime? hasta, int? productoraId)
    {
        var query = db.Devoluciones
            .Include(d => d.Lote).ThenInclude(l => l.Productora)
            .Include(d => d.RegistroFaenamiento)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(d =>
                d.FechaDevolucion >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(d =>
                d.FechaDevolucion <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        if (productoraId.HasValue)
            query = query.Where(d => d.Lote.ProductoraId == productoraId.Value);

        var lista = await query
            .OrderByDescending(d => d.FechaDevolucion)
            .ToListAsync();

        return lista.Select(d => MapearDevolucion(
            d, d.Lote, d.RegistroFaenamiento?.NumeroSesion));
    }

    private static DevolucionResponseDto MapearDevolucion(
        Devolucion d, Lote lote, int? numeroSesion) => new(
        Id: d.Id,
        LoteId: d.LoteId,
        CodigoLote: lote.CodigoLote,
        NumeroSesion: numeroSesion,
        NombreProductora: lote.Productora?.NombreCompleto ?? string.Empty,
        Comunidad: lote.Productora?.Comunidad ?? string.Empty,
        ClienteDevuelve: d.ClienteDevuelve,
        FechaDevolucion: d.FechaDevolucion,
        CantidadUnidades: d.CantidadUnidades,
        Motivo: d.Motivo,
        Responsable: d.Responsable,
        Observaciones: d.Observaciones
    );

    // ── Retornos de cuyes a su productora de origen ───────────────────

    public async Task<IEnumerable<RetornoProductoraResponseDto>> ListarRetornosAsync(
        DateTime? desde, DateTime? hasta, int? productoraId)
    {
        var query = db.RetornosProductora
            .Include(r => r.Lote)
            .Include(r => r.Productora)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(r =>
                r.FechaRetorno >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(r =>
                r.FechaRetorno <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        if (productoraId.HasValue)
            query = query.Where(r => r.ProductoraId == productoraId.Value);

        return await query
            .OrderByDescending(r => r.FechaRetorno)
            .Select(r => new RetornoProductoraResponseDto(
                r.Id, r.LoteId, r.Lote.CodigoLote,
                r.ProductoraId, r.Productora.NombreCompleto,
                r.Productora.Comunidad, r.NumeroEnLote,
                r.Motivo, r.FechaRetorno, r.Responsable))
            .ToListAsync();
    }

    // ── Datos para codificador Ink Jet — RF-305 ───────────────────────

    public async Task<InkJetCodigoDto?> ObtenerDatosInkJetAsync(string codigoLote)
    {
        // Código de lote faenado (FAE-…): el empaque lleva los datos
        // agregados de toda la sesión de planta
        if (codigoLote.StartsWith("FAE-", StringComparison.OrdinalIgnoreCase))
        {
            var loteFaenado = await db.LotesFaenados
                .Include(lf => lf.Sesiones).ThenInclude(f => f.Cuyes)
                .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                    .ThenInclude(l => l.Cuyes).ThenInclude(c => c.Productora)
                .FirstOrDefaultAsync(lf => lf.Codigo == codigoLote);

            if (loteFaenado is null) return null;

            var comunidadesLf = loteFaenado.Sesiones
                .SelectMany(f => f.Cuyes
                    .Where(c => c.Estado != EstadoCanal.Rechazado)
                    .Select(c => f.Lote.Cuyes
                        .FirstOrDefault(x => x.NumeroEnLote == c.NumeroEnLote)
                        ?.Productora?.Comunidad))
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .Distinct()
                .ToList();

            var unidades = loteFaenado.Sesiones.Sum(f => f.UnidadesFaenadas);
            var pesoTotal = loteFaenado.Sesiones.Sum(f => f.PesoTotalCanalGramos);

            return new InkJetCodigoDto(
                CodigoLote: loteFaenado.Codigo,
                FechaFaenamiento: loteFaenado.FechaFaenamiento.ToString("dd/MM/yyyy"),
                FechaVencimiento: loteFaenado.FechaFaenamiento.AddDays(5)
                                             .ToString("dd/MM/yyyy"),
                ComunidadOrigen: comunidadesLf.Count > 0
                    ? string.Join(" y ", comunidadesLf) : "Azuay",
                NombreProductora: comunidadesLf.Count > 1
                    ? "Varias productoras" : "Familias productoras COOPAGCUY",
                UnidadesFaenadas: unidades,
                PesoPromedioCanalGramos: unidades > 0
                    ? Math.Round(pesoTotal / unidades, 0) : 0
            );
        }

        var faenamiento = await db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .Include(f => f.Lote).ThenInclude(l => l.Cuyes)
                .ThenInclude(c => c.Productora)
            .Include(f => f.Cuyes)
            .FirstOrDefaultAsync(f => f.Lote.CodigoLote == codigoLote);

        if (faenamiento is null) return null;

        var promedio = faenamiento.UnidadesFaenadas > 0
            ? faenamiento.PesoTotalCanalGramos / faenamiento.UnidadesFaenadas
            : 0;

        // Comunidades de origen de los animales faenados en esta sesión
        var numerosFaenados = faenamiento.Cuyes
            .Where(c => c.Estado != EstadoCanal.Rechazado)
            .Select(c => c.NumeroEnLote)
            .ToHashSet();

        var comunidades = faenamiento.Lote.Cuyes
            .Where(c => numerosFaenados.Contains(c.NumeroEnLote))
            .Select(c => c.Productora?.Comunidad)
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct()
            .ToList();

        var comunidadOrigen = comunidades.Count > 0
            ? string.Join(" y ", comunidades)
            : faenamiento.Lote.Productora?.Comunidad ?? "Azuay";

        return new InkJetCodigoDto(
            CodigoLote: faenamiento.Lote.CodigoLote,
            FechaFaenamiento: faenamiento.FechaFaenamiento.ToString("dd/MM/yyyy"),
            FechaVencimiento: faenamiento.FechaFaenamiento.AddDays(5)
                                         .ToString("dd/MM/yyyy"),
            ComunidadOrigen: comunidadOrigen,
            NombreProductora: comunidades.Count > 1
                ? "Varias productoras"
                : faenamiento.Lote.Productora?.NombreCompleto
                    ?? "Varias productoras",
            UnidadesFaenadas: faenamiento.UnidadesFaenadas,
            PesoPromedioCanalGramos: Math.Round(promedio, 0)
        );
    }

    // ── Mapeo ─────────────────────────────────────────────────────────

    private static FaenamientoResponseDto MapearFaenamiento(RegistroFaenamiento f, Lote lote)
        {
        var promedio = f.UnidadesFaenadas > 0
            ? f.PesoTotalCanalGramos / f.UnidadesFaenadas
            : 0;

        return new FaenamientoResponseDto(
            Id: f.Id,
            NumeroSesion: f.NumeroSesion,
            CodigoLoteFaenado: f.LoteFaenado?.Codigo,
            LoteId: f.LoteId,
            CodigoLote: lote.CodigoLote,
            NombreProductora: lote.Productora?.NombreCompleto ?? string.Empty,
            ComunidadOrigen: lote.Productora?.Comunidad ?? string.Empty,
            FechaFaenamiento: f.FechaFaenamiento,
            OperarioResponsable: f.OperarioResponsable,
            UnidadesFaenadas: f.UnidadesFaenadas,
            PesoTotalCanalGramos: f.PesoTotalCanalGramos,
            PesoPromedioCanalGramos: Math.Round(promedio, 2),
            TemperaturaAlmacenamiento: f.TemperaturaAlmacenamiento,
            EstadoCanal: f.EstadoCanal.ToString(),
            Observaciones: f.Observaciones,
            UnidadesDecomisadas: f.UnidadesDecomisadas,
            MotivoDecomiso: f.MotivoDecomiso,
            TiempoLavadoMinutos: f.TiempoLavadoMinutos,
            PresentacionEmpaque: f.PresentacionEmpaque,
            FechaIngresoFrio: f.FechaIngresoFrio,
            FechaSalidaFrio: f.FechaSalidaFrio,
            Cuyes: f.Cuyes
                .OrderBy(c => c.NumeroEnLote)
                .Select(c => new CuyFaenamientoResponseDto(
                    c.Id, c.NumeroEnLote, c.PesoCanalGramos,
                    c.Estado.ToString(), c.Motivo, c.RetornadoAProductora))
                .ToList()
        );
    }
}
