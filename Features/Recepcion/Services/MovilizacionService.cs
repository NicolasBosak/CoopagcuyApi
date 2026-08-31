using CoopagcuyApi.Common.Exceptions;
using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Recepcion.Services;

public interface IMovilizacionService
{
    Task<MovilizacionResponseDto> RegistrarAsync(string codigoLote, RegistrarMovilizacionDto dto);
    Task<MovilizacionResponseDto?> ConfirmarRecepcionAsync(int id, ConfirmarRecepcionPlantaDto dto);
    Task<IEnumerable<MovilizacionResponseDto>> ListarAsync(bool? pendientesRecepcion);
    Task<MovilizacionResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote);
}

/// <summary>
/// Eslabón de transporte CAT → planta. El registro de movilización
/// mantiene la cadena de trazabilidad durante el traslado, con los
/// datos del conductor y la declaración de tratamientos.
/// </summary>
public class MovilizacionService(AppDbContext db) : IMovilizacionService
{
    public async Task<MovilizacionResponseDto> RegistrarAsync(
        string codigoLote, RegistrarMovilizacionDto dto)
    {
        // Solo entran claves del catálogo: si el front manda cualquier otra
        // cosa se rechaza en vez de guardarla, que es justo lo que hacía el
        // texto libre que este checklist viene a reemplazar. No toca la base,
        // así que puede validarse antes de abrir la transacción.
        var desconocidas = dto.CondicionesTransporte
            .Where(c => !CondicionTransporte.EsValida(c))
            .ToList();
        if (desconocidas.Count > 0)
            throw new InvalidOperationException(
                $"Condición de transporte no reconocida: {string.Join(", ", desconocidas)}.");

        // La transacción explícita debe correr dentro de la estrategia de
        // reintentos de Npgsql; mismo patrón que FaenamientoService y
        // PagoService.RegistrarVentaLocalAsync.
        //
        // El advisory lock por lote serializa esta escritura frente a una
        // venta local concurrente sobre el MISMO lote (Arreglo 4 de la
        // revisión final): sin él, una venta local y esta movilización
        // pueden entrelazarse — la venta lee "sin movilizar" mientras esta
        // consulta cuenta "0 vendidos", y las dos se aceptan aunque juntas
        // excedan el total de animales del lote. El lock no evita que se
        // muevan datos: evita que dos lecturas viejas decidan a la vez sobre
        // el mismo saldo.
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaccion = await db.Database.BeginTransactionAsync();

            var lote = await db.Lotes
                .Include(l => l.Productora)
                .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
                ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");

            var claveLock = Common.ClavesLock.LoteMovilizacion(lote.Id);
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({claveLock}))");

            // Releído DESPUÉS del lock: Estado y CantidadAnimales salieron
            // del FindAsync/FirstOrDefaultAsync de arriba, anterior al lock
            // por definición (hace falta lote.Id para armar la clave). Bajo
            // READ COMMITTED (el nivel por defecto de Postgres) un cambio
            // confirmado por otra transacción entre esa lectura y este punto
            // no se vería sin este Reload — mismo motivo por el que yaExiste
            // y vendidos, más abajo, se consultan aquí y no antes. No cambia
            // el resultado de ningún escenario de la batería hoy (nada toca
            // Estado o CantidadAnimales de un lote entre su lectura y el
            // lock salvo esta misma operación, que el lock ya serializa),
            // pero deja el patrón consistente: todo lo que decide algo
            // dentro de esta transacción se lee DESPUÉS de tomar el lock.
            await db.Entry(lote).ReloadAsync();

            if (lote.Estado == Common.EstadoLote.Rechazado)
                throw new InvalidOperationException(
                    $"El lote {codigoLote} está rechazado y no puede movilizarse a la planta.");

            var yaExiste = await db.Movilizaciones.AnyAsync(m => m.LoteId == lote.Id);
            if (yaExiste)
                throw new InvalidOperationException(
                    $"El lote {codigoLote} ya tiene una movilización registrada.");

            // Lo que se vendió en la comunidad ya no está en el centro. El
            // cálculo es POR ANIMAL y no por productora: eso es lo que
            // resuelve solo la jaula compartida, donde una vendió lo suyo y
            // la otra no.
            //
            // Se cuenta lo VENDIDO y se resta, en vez de contar lo
            // disponible. Parece lo mismo y no lo es: una jaula histórica
            // cargada sin detalle por animal no tiene filas en CuyRegistros,
            // y contar disponibles ahí daría cero — bloqueando el envío de un
            // lote que nadie vendió. Restando, esa jaula da vendidos = 0 y
            // conserva exactamente la conducta de hoy.
            var vendidos = await db.CuyRegistros
                .CountAsync(c => c.LoteId == lote.Id && c.VentaLocalPagoId != null);

            var disponibles = lote.CantidadAnimales - vendidos;

            if (disponibles <= 0)
                throw new InvalidOperationException(
                    $"El lote {codigoLote} se vendió completo en la comunidad: " +
                    $"no queda ningún animal que enviar a la planta.");

            if (dto.CantidadMovilizada > disponibles)
                throw new InvalidOperationException(
                    $"La cantidad movilizada ({dto.CantidadMovilizada}) supera los " +
                    $"animales disponibles del lote ({disponibles}): " +
                    $"{vendidos} se vendieron en la comunidad.");

            var movilizacion = new Movilizacion
            {
                LoteId = lote.Id,
                FechaDespacho = DateTime.UtcNow,
                Conductor = dto.Conductor.Trim(),
                CantidadMovilizada = dto.CantidadMovilizada,
                CondicionesTransporte =
                    CondicionTransporte.Describir(dto.CondicionesTransporte),
                // Las claves, ademas de la frase. La frase se conserva porque
                // es lo unico que tienen las movilizaciones anteriores a este
                // cambio, y reimprimir una guia antigua no puede perder ese
                // dato.
                CondicionesClaves = string.Join(
                    CondicionTransporte.Separador,
                    dto.CondicionesTransporte.Distinct()),
                TipoForraje = dto.TipoForraje,
                SinAntibioticos7Dias = dto.SinAntibioticos7Dias,
                ResponsableDespacho = dto.ResponsableDespacho.Trim(),
                Observaciones = dto.Observaciones
            };

            db.Movilizaciones.Add(movilizacion);
            await db.SaveChangesAsync();
            await transaccion.CommitAsync();

            return Mapear(movilizacion, lote);
        });
    }

    public async Task<MovilizacionResponseDto?> ConfirmarRecepcionAsync(
        int id, ConfirmarRecepcionPlantaDto dto)
    {
        var movilizacion = await db.Movilizaciones
            .Include(m => m.Lote).ThenInclude(l => l.Productora)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movilizacion is null) return null;

        if (movilizacion.FechaRecepcionPlanta is not null)
            throw new InvalidOperationException(
                "Esta movilización ya tiene la recepción en planta confirmada.");

        // Si el checklist salió incompleto, la pregunta deja de ser opcional:
        // es el único momento en que alguien puede contrastar lo que se
        // prometió al cargar con lo que de verdad llegó.
        //
        // Nulo en CondicionesClaves es una movilización anterior a esta
        // feature: ahí no se sabe qué se verificó, así que no se puede exigir
        // nada y la pregunta sigue siendo opcional.
        var faltaron = movilizacion.CondicionesClaves is not null
            && CondicionTransporte.NoVerificadas(
                   TextosGuia.ClavesDe(movilizacion.CondicionesClaves)).Count > 0;

        // TransicionInvalidaException y no CuerpoInvalidoException: esto
        // depende del estado guardado, no del cuerpo. TransicionInvalidaException
        // hereda de Exception, no de InvalidOperationException —ninguna
        // excepción propia del proyecto lo hace, ver Common/Exceptions—, así
        // que el controlador la captura explícitamente
        // (catch (TransicionInvalidaException) → 409) antes del catch
        // genérico. Sin ese catch explícito esto sería un 500 sin mensaje
        // para el operador de planta.
        if (faltaron && dto.LlegaronEnBuenEstado is null)
            throw new TransicionInvalidaException(
                "El checklist de transporte quedó incompleto: hay que indicar " +
                "si los animales llegaron en buen estado.");

        var claves = dto.CondicionesLlegada
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        var desconocidas = claves.Where(c => !CondicionLlegada.EsValida(c)).ToList();
        if (desconocidas.Count > 0)
            throw new CuerpoInvalidoException(
                $"Condición de llegada no reconocida: {string.Join(", ", desconocidas)}.");

        if (dto.LlegaronEnBuenEstado == false && claves.Count == 0)
            throw new CuerpoInvalidoException(
                "Si los animales no llegaron en buen estado, hay que indicar " +
                "al menos una condición.");

        movilizacion.FechaRecepcionPlanta = DateTime.UtcNow;
        movilizacion.RecibidoPor = dto.RecibidoPor.Trim();
        movilizacion.CondicionLlegada = dto.CondicionLlegada;
        movilizacion.LlegaronEnBuenEstado = dto.LlegaronEnBuenEstado;
        // Solo se guardan si la respuesta fue "no": un "sí" con casillas
        // marcadas de un intento anterior dejaría un cuestionario que
        // contradice su propia respuesta.
        movilizacion.CondicionesLlegadaClaves = dto.LlegaronEnBuenEstado == false
            ? string.Join(CondicionTransporte.Separador, claves)
            : null;

        await db.SaveChangesAsync();
        return Mapear(movilizacion, movilizacion.Lote);
    }

    public async Task<IEnumerable<MovilizacionResponseDto>> ListarAsync(
        bool? pendientesRecepcion)
    {
        var query = db.Movilizaciones
            .Include(m => m.Lote).ThenInclude(l => l.Productora)
            .AsQueryable();

        if (pendientesRecepcion == true)
            query = query.Where(m => m.FechaRecepcionPlanta == null);
        else if (pendientesRecepcion == false)
            query = query.Where(m => m.FechaRecepcionPlanta != null);

        var lista = await query
            .OrderByDescending(m => m.FechaDespacho)
            .Take(300)
            .AsNoTracking()
            .ToListAsync();

        return lista.Select(m => Mapear(m, m.Lote));
    }

    public async Task<MovilizacionResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote)
    {
        var movilizacion = await db.Movilizaciones
            .Include(m => m.Lote).ThenInclude(l => l.Productora)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Lote.CodigoLote == codigoLote);

        return movilizacion is null ? null : Mapear(movilizacion, movilizacion.Lote);
    }

    private static MovilizacionResponseDto Mapear(
        Movilizacion m, Productoras.Models.Lote lote) => new(
        Id: m.Id,
        LoteId: m.LoteId,
        CodigoLote: lote.CodigoLote,
        CentroAcopio: lote.CentroAcopio,
        NombreProductora: lote.Productora?.NombreCompleto ?? string.Empty,
        FechaDespacho: m.FechaDespacho,
        Conductor: m.Conductor,
        CantidadMovilizada: m.CantidadMovilizada,
        CondicionesTransporte: m.CondicionesTransporte,
        TipoForraje: m.TipoForraje,
        DiasRetiroMedicamentos: m.DiasRetiroMedicamentos,
        SinAntibioticos7Dias: m.SinAntibioticos7Dias,
        ResponsableDespacho: m.ResponsableDespacho,
        Observaciones: m.Observaciones,
        FechaRecepcionPlanta: m.FechaRecepcionPlanta,
        RecibidoPor: m.RecibidoPor,
        CondicionLlegada: m.CondicionLlegada,
        CondicionesClaves: m.CondicionesClaves,
        LlegaronEnBuenEstado: m.LlegaronEnBuenEstado,
        CondicionesLlegadaClaves: m.CondicionesLlegadaClaves
    );
}
