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
    Task<IEnumerable<LoteFaenadoDespachableDto>> ListarDespachablesAsync();
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
    // Tope de los listados generales: se carga lo más reciente y el
    // histórico se consulta con los filtros de fecha. Evita que las
    // pantallas degraden a medida que crece la historia.
    private const int MaxRegistrosListado = 300;

    // ── Lotes con saldo pendiente de faenar ───────────────────────────
    // La planta solo ve lotes cerrados con animales disponibles: al llegar
    // el saldo a cero, el lote desaparece de esta vista.

    public async Task<IEnumerable<LoteDisponibleDto>> LotesDisponiblesAsync()
    {
        var lotes = await db.Lotes
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
            .Where(l => l.Cerrado && l.Estado != EstadoLote.Rechazado)
            // La jaula solo es faenable cuando un operador de planta
            // confirmó su llegada (eslabón de transporte cerrado)
            .Where(l => l.Movilizacion != null &&
                        l.Movilizacion.FechaRecepcionPlanta != null)
            // Pre-filtro en SQL: los lotes ya agotados no se cargan (sin
            // esto la consulta arrastraría toda la historia en memoria).
            // Es un filtro "superset": el saldo exacto —que contempla
            // sesiones antiguas sin detalle por cuy— se recalcula abajo.
            .Where(l => l.Faenamientos.SelectMany(f => f.Cuyes).Count()
                        < l.CantidadAnimales)
            .OrderBy(l => l.FechaRecepcion)
            .AsNoTracking()
            .AsSplitQuery()
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

        // La transacción explícita debe correr dentro de la estrategia de
        // reintentos de Npgsql; si un intento falla a medias, se limpia el
        // change tracker para que el reintento parta de cero
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            var registros = new List<FaenamientoResponseDto>();
            var alertas = new List<AlertaNovedadPreviaDto>();

            await using var transaccion = await db.Database.BeginTransactionAsync();

            // Serializa las sesiones del mismo día: el código FAE-…-SEC se
            // calcula contando registros y dos sesiones simultáneas chocarían
            var claveLock = $"fae-{dto.FechaFaenamiento:yyyyMMdd}";
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({claveLock}))");

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
        });
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
            .AsNoTracking()
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
            .AsNoTracking()
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
            .Take(MaxRegistrosListado)
            .AsNoTracking()
            .ToListAsync();

        return lista.Select(f => MapearFaenamiento(f, f.Lote));
    }

    // ── Registro de despacho ──────────────────────────────────────────

    // Lotes faenados con saldo despachable: animales no rechazados que
    // aún no están en ningún despacho. Al agotarse el saldo, el lote
    // desaparece del selector automáticamente.
    public async Task<IEnumerable<LoteFaenadoDespachableDto>> ListarDespachablesAsync()
    {
        var lotes = await db.LotesFaenados
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Cuyes)
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
            .OrderByDescending(lf => lf.FechaFaenamiento)
            .Take(MaxRegistrosListado)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync();

        var despachadosIds = (await db.DespachoCuys
            .Select(dc => dc.CuyFaenamientoId)
            .ToListAsync())
            .ToHashSet();

        return lotes
            .Select(lf =>
            {
                var faenados = lf.Sesiones
                    .SelectMany(f => f.Cuyes
                        .Select(c => (Cuy: c, Jaula: f.Lote.CodigoLote)))
                    .Where(x => x.Cuy.Estado != EstadoCanal.Rechazado)
                    .ToList();

                var disponibles = faenados
                    .Where(x => !despachadosIds.Contains(x.Cuy.Id))
                    .OrderBy(x => x.Jaula)
                    .ThenBy(x => x.Cuy.NumeroEnLote)
                    .Select(x => new CuyDespachableDto(
                        x.Cuy.Id, x.Jaula, x.Cuy.NumeroEnLote,
                        x.Cuy.PesoCanalGramos, x.Cuy.Estado.ToString()))
                    .ToList();

                return new LoteFaenadoDespachableDto(
                    lf.Id, lf.Codigo, lf.FechaFaenamiento,
                    TotalFaenadas: faenados.Count,
                    Despachadas: faenados.Count - disponibles.Count,
                    Disponibles: disponibles.Count,
                    Cuyes: disponibles);
            })
            .Where(lf => lf.Disponibles > 0)
            .ToList();
    }

    public async Task<DespachoResponseDto> RegistrarDespachoAsync(
        RegistrarDespachoDto dto)
    {
        // La disponibilidad se valida bajo un advisory lock por lote
        // faenado; el índice único de DespachoCuys.CuyFaenamientoId es la
        // red final: un animal jamás puede despacharse dos veces
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaccion = await db.Database.BeginTransactionAsync();

            var claveLock = $"despacho-fae-{dto.LoteFaenadoId}";
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({claveLock}))");

            var loteFaenado = await db.LotesFaenados
                .Include(lf => lf.Sesiones).ThenInclude(f => f.Cuyes)
                .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                .AsNoTracking()
                .AsSplitQuery()
                .FirstOrDefaultAsync(lf => lf.Id == dto.LoteFaenadoId)
                ?? throw new KeyNotFoundException(
                    $"Lote faenado con Id {dto.LoteFaenadoId} no encontrado.");

            // Animales del lote faenado con su jaula de origen
            var animales = loteFaenado.Sesiones
                .SelectMany(f => f.Cuyes
                    .Select(c => (Cuy: c, Jaula: f.Lote.CodigoLote)))
                .ToDictionary(x => x.Cuy.Id);

            var idsSolicitados = dto.CuyFaenamientoIds.Distinct().ToList();

            var yaDespachados = (await db.DespachoCuys
                .Where(dc => idsSolicitados.Contains(dc.CuyFaenamientoId))
                .Select(dc => dc.CuyFaenamientoId)
                .ToListAsync())
                .ToHashSet();

            var detalle = new List<CuyDespachadoDto>();
            foreach (var cuyId in idsSolicitados)
            {
                if (!animales.TryGetValue(cuyId, out var animal))
                    throw new InvalidOperationException(
                        $"Uno de los animales seleccionados no pertenece " +
                        $"al lote faenado {loteFaenado.Codigo}.");

                if (animal.Cuy.Estado == EstadoCanal.Rechazado)
                    throw new InvalidOperationException(
                        $"El cuy #{animal.Cuy.NumeroEnLote} de la jaula " +
                        $"{animal.Jaula} fue rechazado en planta y no puede " +
                        "despacharse.");

                if (yaDespachados.Contains(cuyId))
                    throw new InvalidOperationException(
                        $"El cuy #{animal.Cuy.NumeroEnLote} de la jaula " +
                        $"{animal.Jaula} ya fue despachado anteriormente.");

                detalle.Add(new CuyDespachadoDto(
                    animal.Jaula, animal.Cuy.NumeroEnLote));
            }

            var despacho = new Despacho
            {
                LoteFaenadoId = loteFaenado.Id,
                ClienteDestino = dto.ClienteDestino,
                FechaDespacho = DateTime.SpecifyKind(
                    dto.FechaDespacho, DateTimeKind.Utc),
                CantidadUnidades = detalle.Count,
                Responsable = dto.Responsable,
                Chofer = dto.Chofer,
                Ruta = dto.Ruta,
                TipoMercado = string.IsNullOrWhiteSpace(dto.TipoMercado)
                    ? "Local" : dto.TipoMercado.Trim(),
                Ciudad = dto.Ciudad,
                Pais = dto.Pais,
                Observaciones = dto.Observaciones,
                Cuyes = idsSolicitados
                    .Select(id => new DespachoCuy { CuyFaenamientoId = id })
                    .ToList()
            };

            db.Despachos.Add(despacho);
            await db.SaveChangesAsync();
            await transaccion.CommitAsync();

            return new DespachoResponseDto(
                Id: despacho.Id,
                CodigoLoteFaenado: loteFaenado.Codigo,
                CodigoLote: null,
                ClienteDestino: despacho.ClienteDestino,
                FechaDespacho: despacho.FechaDespacho,
                CantidadUnidades: despacho.CantidadUnidades,
                UnidadesDevueltas: 0,
                Responsable: despacho.Responsable,
                Chofer: despacho.Chofer,
                Ruta: despacho.Ruta,
                TipoMercado: despacho.TipoMercado,
                Ciudad: despacho.Ciudad,
                Pais: despacho.Pais,
                Observaciones: despacho.Observaciones,
                Cuyes: detalle
            );
        });
    }

    // Historial completo de despachos con filtro opcional por fecha.
    public async Task<IEnumerable<DespachoResponseDto>> ListarDespachosAsync(
        DateTime? desde, DateTime? hasta)
    {
        var query = db.Despachos
            .Include(d => d.LoteFaenado)
            .Include(d => d.Lote)
            .Include(d => d.Cuyes)
                .ThenInclude(dc => dc.CuyFaenamiento)
                .ThenInclude(c => c.Registro)
                .ThenInclude(f => f.Lote)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(d =>
                d.FechaDespacho >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(d =>
                d.FechaDespacho <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        var lista = await query
            .OrderByDescending(d => d.FechaDespacho)
            .Take(MaxRegistrosListado)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync();

        // Unidades ya devueltas por despacho: el formulario de devolución
        // solo ofrece despachos con restante y topa la cantidad
        var devueltas = await db.Devoluciones
            .Where(v => v.DespachoId != null)
            .GroupBy(v => v.DespachoId!.Value)
            .Select(g => new { DespachoId = g.Key, Total = g.Sum(v => v.CantidadUnidades) })
            .ToDictionaryAsync(x => x.DespachoId, x => x.Total);

        return lista.Select(d => new DespachoResponseDto(
            d.Id,
            d.LoteFaenado?.Codigo,
            d.Lote?.CodigoLote,
            d.ClienteDestino, d.FechaDespacho, d.CantidadUnidades,
            devueltas.GetValueOrDefault(d.Id),
            d.Responsable, d.Chofer, d.Ruta,
            d.TipoMercado, d.Ciudad, d.Pais, d.Observaciones,
            d.Cuyes
                .OrderBy(dc => dc.CuyFaenamiento.Registro.Lote.CodigoLote)
                .ThenBy(dc => dc.CuyFaenamiento.NumeroEnLote)
                .Select(dc => new CuyDespachadoDto(
                    dc.CuyFaenamiento.Registro.Lote.CodigoLote,
                    dc.CuyFaenamiento.NumeroEnLote))
                .ToList()));
    }

    // ── Devoluciones de clientes — RF-307 ─────────────────────────────

    public async Task<DevolucionResponseDto> RegistrarDevolucionAsync(
        RegistrarDevolucionDto dto)
    {
        if (dto.CantidadUnidades <= 0)
            throw new InvalidOperationException(
                "La cantidad devuelta debe ser mayor a cero.");

        // La devolución nace de un despacho concreto: el cliente y el lote
        // faenado se derivan de él. El tope (enviadas − ya devueltas) se
        // valida bajo un advisory lock por despacho.
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaccion = await db.Database.BeginTransactionAsync();

            var claveLock = $"devolucion-despacho-{dto.DespachoId}";
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({claveLock}))");

            var despacho = await db.Despachos
                .Include(d => d.LoteFaenado)
                .Include(d => d.Lote).ThenInclude(l => l!.Productora)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == dto.DespachoId)
                ?? throw new KeyNotFoundException(
                    $"Despacho con Id {dto.DespachoId} no encontrado.");

            var yaDevueltas = await db.Devoluciones
                .Where(v => v.DespachoId == dto.DespachoId)
                .SumAsync(v => (int?)v.CantidadUnidades) ?? 0;

            var restante = despacho.CantidadUnidades - yaDevueltas;
            if (dto.CantidadUnidades > restante)
                throw new InvalidOperationException(
                    $"El despacho a {despacho.ClienteDestino} tiene " +
                    $"{restante} unidades sin devolver y se intentó " +
                    $"devolver {dto.CantidadUnidades}.");

            var devolucion = new Devolucion
            {
                DespachoId = despacho.Id,
                LoteId = despacho.LoteId,
                // El cliente se copia del despacho: no se vuelve a digitar
                ClienteDevuelve = despacho.ClienteDestino,
                FechaDevolucion = DateTime.SpecifyKind(
                    dto.FechaDevolucion, DateTimeKind.Utc),
                CantidadUnidades = dto.CantidadUnidades,
                Motivo = dto.Motivo,
                Responsable = dto.Responsable,
                Observaciones = dto.Observaciones
            };

            db.Devoluciones.Add(devolucion);
            await db.SaveChangesAsync();
            await transaccion.CommitAsync();

            return MapearDevolucion(
                devolucion, despacho.Lote, null, despacho.LoteFaenado?.Codigo);
        });
    }

    public async Task<IEnumerable<DevolucionResponseDto>> ListarDevolucionesAsync(
        DateTime? desde, DateTime? hasta, int? productoraId)
    {
        var query = db.Devoluciones
            .Include(d => d.Despacho).ThenInclude(x => x!.LoteFaenado)
            .Include(d => d.Lote).ThenInclude(l => l!.Productora)
            .Include(d => d.RegistroFaenamiento)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(d =>
                d.FechaDevolucion >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(d =>
                d.FechaDevolucion <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        if (productoraId.HasValue)
            query = query.Where(d =>
                d.Lote != null && d.Lote.ProductoraId == productoraId.Value);

        var lista = await query
            .OrderByDescending(d => d.FechaDevolucion)
            .Take(MaxRegistrosListado)
            .AsNoTracking()
            .ToListAsync();

        return lista.Select(d => MapearDevolucion(
            d, d.Lote, d.RegistroFaenamiento?.NumeroSesion,
            d.Despacho?.LoteFaenado?.Codigo));
    }

    private static DevolucionResponseDto MapearDevolucion(
        Devolucion d, Lote? lote, int? numeroSesion,
        string? codigoLoteFaenado) => new(
        Id: d.Id,
        LoteId: d.LoteId,
        CodigoLoteFaenado: codigoLoteFaenado,
        CodigoLote: lote?.CodigoLote,
        NumeroSesion: numeroSesion,
        // Un lote faenado puede reunir varias productoras: el nombre
        // individual solo existe en devoluciones legadas por jaula
        NombreProductora: lote?.Productora?.NombreCompleto ?? "Varias productoras",
        Comunidad: lote?.Productora?.Comunidad ?? "—",
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
                .AsNoTracking()
                .AsSplitQuery()
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
            .AsNoTracking()
            .AsSplitQuery()
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
