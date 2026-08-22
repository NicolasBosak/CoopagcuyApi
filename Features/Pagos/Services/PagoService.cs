using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Exceptions;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Infrastructure.Data;
using CoopagcuyApi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Pagos.Services;

public interface IPagoService
{
    // filtroCat: si viene un CAT (operador acotado), el servicio solo opera
    // sobre productoras de ese centro. null = sin restricción (administrador).
    Task<PagoResponseDto> RegistrarAsync(RegistrarPagoDto dto, CentroAcopio? filtroCat);
    Task<IEnumerable<PagoResponseDto>> ListarAsync(
        int? productoraId, DateTime? desde, DateTime? hasta, CentroAcopio? filtroCat);
    Task<IEnumerable<LotePendientePagoDto>> ListarLotesPendientesAsync(
        int productoraId, CentroAcopio? filtroCat);

    /// Si el pago pertenece a una productora de ese centro. Sirve para
    /// responder 404 sin revelar la existencia del recurso.
    Task<bool> EsDeCentroAsync(int pagoId, CentroAcopio cat);

    /// Tickets pendientes de pago, de TODOS los centros.
    Task<IEnumerable<TicketPorPagarDto>> ListarPorPagarAsync();

    /// Cuyes de esa productora en ese lote que traen novedad del CAT.
    Task<IEnumerable<CuyConNovedadDto>> ListarCuyesConNovedadAsync(int pagoId);

    /// Registra la transferencia de la planta: valida los descuentos, sube la
    /// captura y pasa el ticket a Pagado. Todo o nada.
    Task<PagoResponseDto> RegistrarPagoEfectivoAsync(
        int pagoId, RegistrarPagoEfectivoDto dto);

    /// Bytes de la captura, o null si no hay, si caducó, o si el pago es de
    /// otro centro. Un solo null para los tres casos: el controlador responde
    /// 404 sin distinguirlos, que es justo lo que se quiere.
    Task<byte[]?> ObtenerComprobanteAsync(int pagoId, CentroAcopio? filtroCat);

    /// Marca el pago como recibido y arranca la cuenta atrás de la captura.
    Task<PagoResponseDto> VerificarAsync(
        int pagoId, VerificarPagoDto dto, CentroAcopio? filtroCat);

    /// Borra los blobs de las capturas ya caducadas y limpia su referencia.
    /// Devuelve cuántas borró. Se invoca desde el listado: el contenedor del
    /// API se apaga sin tráfico, así que una tarea programada no correría.
    Task<int> BarrerComprobantesCaducadosAsync();

    /// Cuyes de esa productora en ese lote que todavía no se han vendido.
    Task<IEnumerable<CuyDisponibleDto>> ListarCuyesDisponiblesAsync(
        int loteId, int productoraId, CentroAcopio? filtroCat);

    /// Registra una venta local: crea el pago ya cobrado y marca los animales.
    /// Todo o nada — si otro se llevó alguno mientras tanto, no queda pago.
    Task<PagoResponseDto> RegistrarVentaLocalAsync(
        RegistrarVentaLocalDto dto, CentroAcopio? filtroCat);
}

/// <summary>
/// Pagos a productoras por entregas en el CAT. Digitaliza el registro
/// de pagos que hoy se lleva en cuaderno manual (brecha del diagnóstico).
/// </summary>
public class PagoService(
    AppDbContext db, IBlobStorageService blobs, ILogger<PagoService> log) : IPagoService
{
    private const int MaxBytesComprobante = 2 * 1024 * 1024;

    // Días que la captura sigue disponible tras la verificación. Lo que la
    // borra de verdad es el barrido; esta fecha es lo que decide cuándo el
    // API deja de servirla.
    private const int DiasGraciaComprobante = 5;

    // Igual al ciclo de vida que Azure aplica al contenedor "comprobantes-pago"
    // (ver BlobStorageService). Se fija ya al pagar, no solo al verificar: si
    // se dejara en null hasta la verificación, un pago que la CAT nunca
    // verifica no cumpliría el predicado de BarrerComprobantesCaducadosAsync
    // (exige ComprobanteExpiraEn no nulo) y jamás se barrería — Azure sí
    // borraría el blob a los 30 días por su cuenta, pero ComprobanteUrl
    // quedaría apuntando a nada para siempre, y TieneComprobante mentiría sin
    // límite. Al fijarla aquí, la base queda de acuerdo con la regla de Azure
    // en vez de solo estar cubierta por ella.
    private const int DiasVigenciaComprobanteSinVerificar = 30;

    // Catálogo cerrado, como CondicionTransporte: el servidor no acepta un
    // método que no reconozca en vez de guardarlo. "Transferencia" también
    // vale en una venta local — alguien de la comunidad puede transferirle a
    // la CAT — y no se confunde con el pago de la planta porque eso lo
    // distingue EsVentaLocal, no el método.
    private static readonly HashSet<string> MetodosVentaLocal =
        new(StringComparer.OrdinalIgnoreCase)
        { "Efectivo", "Transferencia", "Cuotas" };

    public async Task<PagoResponseDto> RegistrarAsync(
        RegistrarPagoDto dto, CentroAcopio? filtroCat)
    {
        var productora = await db.Productoras.FindAsync(dto.ProductoraId)
            ?? throw new KeyNotFoundException(
                $"Productora con Id {dto.ProductoraId} no encontrada.");

        // Un operador solo paga a productoras de su propio centro
        if (filtroCat is CentroAcopio cat && productora.CatAsignado != cat)
            throw new UnauthorizedAccessException(
                "Tu usuario solo puede registrar pagos de productoras de su centro.");

        // El ticket es por los cuyes de un lote concreto. Sin lote no hay nada
        // que imprimir ni novedades que trazar después.
        if (dto.LoteId is not int loteId)
            throw new InvalidOperationException(
                "El pago debe corresponder a un lote.");

        var lote = await db.Lotes.FindAsync(loteId)
            ?? throw new KeyNotFoundException($"Lote con Id {loteId} no encontrado.");

        // La jaula es multi-productora: el pago es válido si la productora
        // entregó cuyes en ese lote (Lote.ProductoraId es solo la referencia
        // histórica de quien abrió la jaula)
        var participo = lote.ProductoraId == dto.ProductoraId
            || await db.CuyRegistros.AnyAsync(c =>
                c.LoteId == loteId && c.ProductoraId == dto.ProductoraId);

        if (!participo)
            throw new InvalidOperationException(
                "La productora no registra entregas en ese lote.");

        if (dto.MontoUsd <= 0)
            throw new InvalidOperationException(
                "El monto del pago debe ser mayor a cero.");

        var pago = new Pago
        {
            ProductoraId = dto.ProductoraId,
            LoteId = loteId,
            MontoUsd = dto.MontoUsd,
            FechaPago = DateTime.UtcNow,
            // Fijado por el servidor, no por el cliente: desde el paso a
            // transferencia única no hay nada que elegir.
            MetodoPago = "Transferencia",
            Estado = EstadoPago.Pendiente,
            Responsable = dto.Responsable.Trim(),
            Observaciones = dto.Observaciones
        };

        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        return Mapear(pago, productora.NombreCompleto, lote.CodigoLote);
    }

    public async Task<IEnumerable<PagoResponseDto>> ListarAsync(
        int? productoraId, DateTime? desde, DateTime? hasta, CentroAcopio? filtroCat)
    {
        // Barrido oportunista. Va aquí y no en una tarea programada porque el
        // contenedor del API escala a cero: sin tráfico no hay proceso vivo
        // que ejecute nada. La CAT y la planta entran a diario, así que en la
        // práctica la captura se borra al día siguiente de vencer.
        await BarrerComprobantesCaducadosAsync();

        var query = db.Pagos
            .Include(p => p.Productora)
            .Include(p => p.Lote)
            .AsQueryable();

        // Operador acotado: solo pagos de productoras de su centro
        if (filtroCat is CentroAcopio cat)
            query = query.Where(p => p.Productora.CatAsignado == cat);

        if (productoraId.HasValue)
            query = query.Where(p => p.ProductoraId == productoraId.Value);

        if (desde.HasValue)
            query = query.Where(p =>
                p.FechaPago >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(p =>
                p.FechaPago <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        var lista = await query
            .OrderByDescending(p => p.FechaPago)
            .Take(300)
            .AsNoTracking()
            .ToListAsync();

        return lista.Select(p => Mapear(
            p, p.Productora.NombreCompleto, p.Lote?.CodigoLote));
    }

    /// <summary>
    /// Lotes por los que aún se le debe pagar a esta productora.
    ///
    /// El pago es por par (productora, lote): una jaula reúne cuyes de varias
    /// productoras, así que pagarle a una no salda a las demás. Por eso el
    /// lote se excluye solo si YA existe un pago de ESA productora por ESE
    /// lote, y no en cuanto alguien cobre la jaula.
    ///
    /// La pertenencia se resuelve con la misma regla que valida RegistrarAsync
    /// (entregó cuyes, o abrió la jaula): si la lista mostrara otra cosa,
    /// ofrecería lotes que el servidor luego rechaza, o escondería lotes que sí
    /// acepta.
    /// </summary>
    public async Task<IEnumerable<LotePendientePagoDto>> ListarLotesPendientesAsync(
        int productoraId, CentroAcopio? filtroCat)
    {
        // Un operador no puede sondear los lotes pendientes de productoras de
        // otro centro pasando su Id a mano
        if (filtroCat is CentroAcopio cat)
        {
            var productora = await db.Productoras.FindAsync(productoraId);
            if (productora is null || productora.CatAsignado != cat)
                throw new UnauthorizedAccessException(
                    "Tu usuario solo puede consultar productoras de su centro.");
        }

        var pagados = db.Pagos
            .Where(p => p.ProductoraId == productoraId && p.LoteId != null)
            .Select(p => p.LoteId!.Value);

        return await db.Lotes
            .Where(l =>
                (l.ProductoraId == productoraId
                 || l.Cuyes.Any(c => c.ProductoraId == productoraId))
                && !pagados.Contains(l.Id))
            .OrderByDescending(l => l.FechaRecepcion)
            .Select(l => new LotePendientePagoDto(
                l.Id,
                l.CodigoLote,
                l.CentroAcopio.ToString(),
                l.FechaRecepcion,
                // Lo que aportó ESTA productora, no el total de la jaula:
                // es la base sobre la que se le paga
                l.Cuyes.Count(c => c.ProductoraId == productoraId),
                l.Cuyes
                    .Where(c => c.ProductoraId == productoraId)
                    .Sum(c => (decimal?)c.PesoGramos) ?? 0))
            .AsNoTracking()
            .ToListAsync();
    }

    public Task<bool> EsDeCentroAsync(int pagoId, CentroAcopio cat) =>
        db.Pagos.AnyAsync(p => p.Id == pagoId && p.Productora.CatAsignado == cat);

    public async Task<IEnumerable<TicketPorPagarDto>> ListarPorPagarAsync() =>
        await db.Pagos
            .Where(p => p.Estado == EstadoPago.Pendiente && p.LoteId != null)
            .OrderBy(p => p.FechaPago)
            .Select(p => new TicketPorPagarDto(
                p.Id,
                p.ProductoraId,
                p.Productora.NombreCompleto,
                p.Productora.Cedula,
                p.LoteId!.Value,
                p.Lote!.CodigoLote,
                p.Lote.CentroAcopio.ToString(),
                p.Lote.FechaRecepcion,
                // Aporte de ESTA productora, no el total de la jaula
                p.Lote.Cuyes.Count(c => c.ProductoraId == p.ProductoraId),
                p.MontoUsd,
                p.FechaPago))
            .AsNoTracking()
            .ToListAsync();

    public async Task<IEnumerable<CuyConNovedadDto>> ListarCuyesConNovedadAsync(
        int pagoId)
    {
        var pago = await db.Pagos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        if (pago.LoteId is not int loteId)
            return [];

        var ahora = DateTime.UtcNow;

        // Se parte de Novedades y no de CuyRegistros porque lo que la planta
        // necesita es el Id de la NOVEDAD: es lo que va a citar al descontar.
        return await db.Novedades
            .Where(n => n.LoteId == loteId
                // Deliberadamente redundante con la comparación de
                // ProductoraId de abajo: en SQL, si CuyRegistro es null (por
                // el LEFT JOIN), TODAS sus columnas son null, y comparar una
                // columna null contra un valor nunca da verdadero — esa fila
                // ya queda excluida sin este chequeo. Se deja igual porque
                // dice la regla en voz alta ("una novedad de la entrega, sin
                // animal, jamás es descontable") en vez de obligar a
                // deducirla de la semántica de NULL de SQL. Por eso mismo
                // ninguna prueba puede pinnearla: no hay dato que haga que su
                // presencia o ausencia cambie el resultado. Si una prueba no
                // la detecta al borrarla, NO es una prueba floja — no la
                // busques ni la borres pensando que es código muerto.
                && n.CuyRegistro != null
                && n.CuyRegistro.ProductoraId == pago.ProductoraId)
            .OrderBy(n => n.CuyRegistro!.NumeroEnLote)
            .Select(n => new CuyConNovedadDto(
                n.CuyRegistroId!.Value,
                n.CuyRegistro!.NumeroEnLote,
                n.CuyRegistro.PesoGramos,
                n.Id,
                n.Tipo.ToString(),
                n.Descripcion,
                n.FotoUrl != null && n.FotoExpiraEn > ahora))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<byte[]?> ObtenerComprobanteAsync(
        int pagoId, CentroAcopio? filtroCat)
    {
        var pago = await db.Pagos
            .Include(p => p.Productora)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId);

        if (pago is null) return null;

        // Centro ajeno: null, no excepción. El controlador responde 404 y no
        // 403 porque confirmar la existencia ya sería filtrar el dato.
        if (filtroCat is CentroAcopio cat && pago.Productora.CatAsignado != cat)
            return null;

        if (string.IsNullOrWhiteSpace(pago.ComprobanteUrl)) return null;

        // Caducada: el API deja de servirla en el momento exacto, sin esperar
        // a que pase el barrido ni la política de Azure.
        if (pago.ComprobanteExpiraEn is DateTime expira
            && expira <= DateTime.UtcNow)
            return null;

        var nombre = NombreDeBlob(pago.ComprobanteUrl);
        if (nombre is null) return null;

        return await blobs.DescargarComprobanteAsync(nombre);
    }

    /// <summary>
    /// Último segmento de la URI del blob. Devuelve null ante cualquier cosa
    /// que no sea una URI con contenedor y nombre: una fila corrupta no puede
    /// convertirse en un 500.
    /// </summary>
    private static string? NombreDeBlob(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Segments.Length < 3 ? null : uri.Segments[^1];
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    // Presupuesto de tiempo para la fase de borrado en Blob dentro de una
    // pasada del barrido. Con la política de reintentos por defecto del SDK
    // de Azure, un Blob caído puede hacer que una sola llamada tarde del
    // orden de 100 segundos antes de agotar sus reintentos; sin este tope,
    // 20 filas secuenciales podrían colgar ListarAsync varios minutos. 5
    // segundos es deliberadamente pequeño frente a eso: es tiempo suficiente
    // para que las ~20 llamadas normales (que tardan milisegundos) terminen
    // sin recortarse, pero corto para quien está esperando la lista de
    // pagos. Si se agota, las filas que quedaron sin procesar simplemente se
    // reintentan en la siguiente pasada (la siguiente consulta a
    // /api/pagos) — no se pierden ni quedan corruptas.
    private const int PresupuestoBarridoBlobMs = 5000;

    public async Task<int> BarrerComprobantesCaducadosAsync()
    {
        var ahora = DateTime.UtcNow;

        List<Pago> caducados;
        try
        {
            caducados = await db.Pagos
                .Where(p => p.ComprobanteUrl != null
                    && p.ComprobanteExpiraEn != null
                    && p.ComprobanteExpiraEn <= ahora)
                // Orden explícito y no implícito: sin él, Postgres no
                // garantiza qué 20 filas caducadas trae el Take, y eso puede
                // variar de una pasada a otra. No es para procesar en orden
                // de llegada (FIFO) — es solo para que el resultado sea
                // determinista; las filas que no entren en esta pasada las
                // recoge la siguiente igual.
                .OrderBy(p => p.Id)
                // Tope por pasada: el barrido va colgado de una consulta que
                // un operador está esperando. Mejor barrer 20 por vez, muchas
                // veces, que dejar la lista congelada mientras se limpian
                // doscientos.
                .Take(20)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            // La consulta de pagos NO puede caerse porque el SELECT del
            // barrido falle. Un problema transitorio de Postgres aislado a
            // esta consulta (una desconexión puntual, el pool agotado) no
            // tiene por qué tumbar el SELECT de abajo, que es el que de
            // verdad le importa al operador: se registra y se sigue como si
            // no hubiera nada que barrer esta vez.
            log.LogWarning(ex,
                "No se pudo obtener los comprobantes caducados a barrer");
            return 0;
        }

        if (caducados.Count == 0) return 0;

        var borrados = 0;
        using var presupuesto = new CancellationTokenSource(PresupuestoBarridoBlobMs);

        foreach (var pago in caducados)
        {
            // El presupuesto ya se agotó: se deja el resto tal cual, sin
            // intentar más llamadas a Blob. La siguiente pasada las recoge.
            if (presupuesto.IsCancellationRequested) break;

            var nombre = NombreDeBlob(pago.ComprobanteUrl!);
            if (nombre is null)
            {
                // Fila corrupta: se limpia la referencia igual, o volvería a
                // intentarlo en cada consulta para siempre.
                pago.ComprobanteUrl = null;
                continue;
            }

            try
            {
                await blobs.BorrarComprobanteAsync(nombre, presupuesto.Token);
                pago.ComprobanteUrl = null;
                borrados++;
            }
            catch (Exception ex)
            {
                // La consulta de pagos NO puede caerse por un borrado. Si
                // Blob está caído, faltan permisos, o se agotó el
                // presupuesto de tiempo de arriba, se registra y se sigue:
                // la política de Azure lo borrará igual el día 30.
                log.LogWarning(ex,
                    "No se pudo borrar la captura del pago {PagoId}", pago.Id);
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Mismo motivo que el SELECT de arriba: un fallo de guardado
            // aislado a esta escritura (deadlock, conexión que se cae a
            // mitad del commit) no puede propagarse hacia ListarAsync. Los
            // blobs que sí se lograron borrar quedan borrados en Azure —eso
            // no se deshace— pero sin el commit sus filas siguen apuntando
            // al blob que ya no existe, así que se reintentan (y
            // DeleteIfExists ya tolera reintentar un borrado) en la próxima
            // pasada en vez de reportarse como borradas ahora.
            log.LogWarning(ex,
                "No se pudo guardar el resultado del barrido de comprobantes");
            return 0;
        }

        return borrados;
    }

    public async Task<PagoResponseDto> RegistrarPagoEfectivoAsync(
        int pagoId, RegistrarPagoEfectivoDto dto)
    {
        var pago = await db.Pagos
            .Include(p => p.Productora)
            .Include(p => p.Lote)
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        if (pago.Estado != EstadoPago.Pendiente)
            throw new TransicionInvalidaException(
                $"El ticket ya está en estado {pago.Estado} y no admite un pago nuevo.");

        // ── Validación completa ANTES de tocar el blob ───────────────
        // Si se intercalara, un descuento inválido detectado después de la
        // subida dejaría una captura huérfana que nadie va a limpiar.

        var comprobante = ValidarComprobante(dto.ComprobanteBase64);

        // Aquí y no en el Trim de más abajo: si el primer uso de PagadoPor
        // fuera el que arma la fila, un valor en blanco se detectaría DESPUÉS
        // de subir la captura y el blob quedaría huérfano. Y sin guardia
        // alguna, "   " se guardaba tal cual: el ticket quedaba pagado sin
        // decir por quién, que es lo que la CAT necesita para reclamar.
        var pagadoPor = dto.PagadoPor?.Trim() ?? string.Empty;
        if (pagadoPor.Length == 0)
            throw new CuerpoInvalidoException(
                "El pago debe decir quién lo registró en la planta.");

        var descuentos = await ValidarDescuentosAsync(pago, dto, pagadoPor);

        // >= y no solo >: un pago de $0.00 no es un pago, es un ticket con
        // descuentos que se comieron el total. El formulario de la planta ya
        // lo rechaza (FormPagoProductora.tsx: `aPagar <= 0`); sin este
        // espejo en el servidor, un cliente que se salte la pantalla podía
        // registrar un ticket "Pagado" de $0 con una captura de una
        // transferencia de $0.
        var total = descuentos.Sum(d => d.MontoUsd);
        if (total >= pago.MontoUsd)
            throw new TransicionInvalidaException(
                $"Los descuentos suman {total:N2} y el ticket es de " +
                $"{pago.MontoUsd:N2}. Un pago de cero o negativo no significa nada.");

        // ── Subida, fuera de la transacción ──────────────────────────
        // CreateExecutionStrategy REINTENTA el delegado ante fallos
        // transitorios de Neon: subir ahí dentro duplicaría el blob en cada
        // reintento.
        var nombre = $"pago-{pago.Id:D6}-{Guid.NewGuid():N}.jpg";
        var url = await blobs.SubirComprobanteAsync(nombre, comprobante);

        var ahora = DateTime.UtcNow;
        pago.Estado = EstadoPago.Pagado;
        pago.MontoPagadoUsd = pago.MontoUsd - total;
        pago.FechaPagoEfectivo = ahora;
        pago.PagadoPor = pagadoPor;
        pago.ComprobanteUrl = url;
        // Se acorta a los 5 días si la CAT verifica (VerificarAsync); si
        // nunca verifica, esto es lo único que va a barrer el blob.
        pago.ComprobanteExpiraEn = ahora.AddDays(DiasVigenciaComprobanteSinVerificar);

        foreach (var d in descuentos) db.Descuentos.Add(d);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // La captura YA está subida. Si la base rechaza la escritura hay
            // que retirarla o queda huérfana: es el mismo invariante que
            // protege validar antes de subir, aplicado al único tramo que la
            // validación previa no puede cubrir —el índice único de
            // (PagoId, NovedadCatId) y demás reglas que solo conoce el motor—.
            //
            // El borrado va antes del throw y sin try propio: si fallara
            // también, el pago ya está perdido de todos modos y la excepción
            // de borrado dice más que la que la tapó.
            await blobs.BorrarComprobanteAsync(nombre);

            // El mensaje NO adivina la causa. Antes decía "es posible que ya
            // se hubiera registrado", que para un NOT NULL, una restricción
            // de comprobación o un corte de conexión a mitad del guardado era
            // sencillamente falso, y encima invitaba a NO reintentar un pago
            // que no se había guardado. Dice lo único que consta: la
            // escritura no entró y el ticket sigue pendiente.
            //
            // La excepción original viaja encadenada: el cliente ve el texto
            // de arriba, pero el log conserva el error real de Postgres. Sin
            // encadenarla se perdía entero.
            throw new TransicionInvalidaException(
                "No se pudo guardar el pago: la base de datos rechazó la " +
                "escritura. La captura se retiró y el ticket sigue pendiente.",
                ex);
        }

        return Mapear(pago, pago.Productora.NombreCompleto, pago.Lote?.CodigoLote);
    }

    /// <summary>
    /// Decodifica y mide la captura. Excepción propia y no ArgumentException:
    /// esta última es la clase padre de ArgumentNullException, y capturarla a
    /// ciegas convertiría cualquier bug ajeno en un 400 que además expone el
    /// mensaje interno de .NET.
    /// </summary>
    private static byte[] ValidarComprobante(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new EvidenciaInvalidaException(
                "El pago debe adjuntar la captura de la transferencia.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new EvidenciaInvalidaException(
                "La captura de la transferencia no es base64 válido.");
        }

        if (bytes.Length > MaxBytesComprobante)
            throw new EvidenciaInvalidaException(
                $"La captura pesa {bytes.Length / 1024} KB y el máximo es " +
                $"{MaxBytesComprobante / 1024} KB.");

        return bytes;
    }

    /// <summary>
    /// Las cinco reglas del descuento, menos la del tope que necesita la
    /// suma. Devuelve las filas listas para guardar, sin guardarlas.
    ///
    /// Reparto de códigos: lo que se sabe leyendo el cuerpo (monto no
    /// positivo, decimales de más, descripción en blanco) sale como
    /// CuerpoInvalidoException → 400; lo que exige mirar el servidor (a qué
    /// productora y lote pertenece la novedad, y la unicidad de la cita)
    /// sale como TransicionInvalidaException → 409.
    /// </summary>
    private async Task<List<DescuentoPago>> ValidarDescuentosAsync(
        Pago pago, RegistrarPagoEfectivoDto dto, string pagadoPor)
    {
        if (dto.Descuentos.Count == 0) return [];

        var citadas = dto.Descuentos.Select(d => d.NovedadCatId).ToList();

        // Regla 3: no repetir la misma novedad dentro de la propia petición.
        // El índice único cubre el caso de dos peticiones a la vez; esto
        // cubre el de una petición mal formada, con un mensaje legible.
        if (citadas.Distinct().Count() != citadas.Count)
            throw new TransicionInvalidaException(
                "Un mismo defecto no puede descontarse dos veces.");

        // Reglas 1 y 2: la novedad tiene que pertenecer a un cuy de ESA
        // productora en ESE lote. Las novedades sin cuy —las de entrega y las
        // filas históricas— quedan fuera por el CuyRegistro != null.
        var validas = await db.Novedades
            .Where(n => citadas.Contains(n.Id)
                && n.LoteId == pago.LoteId
                && n.CuyRegistro != null
                && n.CuyRegistro.ProductoraId == pago.ProductoraId)
            .Select(n => n.Id)
            .ToListAsync();

        // Except y no FirstOrDefault: sobre int, FirstOrDefault devuelve 0
        // cuando no encuentra nada, y ese 0 no se distingue de una cita
        // legítima del id 0 —el valor por defecto de un int, que llega solo
        // con que el cliente omita el campo—. Con el centinela anterior esa
        // cita se colaba hasta el INSERT y reventaba contra la FK: 500 y
        // captura huérfana, justo lo que validar antes de subir evita.
        var invalidas = citadas.Except(validas).ToList();
        if (invalidas.Count > 0)
            throw new TransicionInvalidaException(
                $"La novedad {invalidas[0]} no corresponde a un cuy de esta " +
                "productora en este lote, así que no puede justificar un " +
                "descuento.");

        foreach (var d in dto.Descuentos)
        {
            if (d.MontoUsd <= 0)
                throw new CuerpoInvalidoException(
                    "Un descuento debe ser mayor a cero.");

            // Ambas columnas son HasPrecision(10,2), y nada limita lo que
            // entra: 10.005 se persiste redondeado a 10.01 mientras que el
            // MontoPagadoUsd se calcula con 10.005, así que la resta deja de
            // cuadrar con la fila que la justifica. Y 0.001 pasa el "> 0"
            // para acabar guardado como un descuento de 0.00. El ticket se
            // lleva en centavos: lo que no quepa en centavos se rechaza.
            if (decimal.Round(d.MontoUsd, 2) != d.MontoUsd)
                throw new CuerpoInvalidoException(
                    $"El descuento de {d.MontoUsd} tiene más de dos " +
                    "decimales y el ticket se lleva en centavos.");

            if (string.IsNullOrWhiteSpace(d.Descripcion))
                throw new CuerpoInvalidoException(
                    "Cada descuento debe decir qué se observó en la planta.");
        }

        return [.. dto.Descuentos.Select(d => new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = d.NovedadCatId,
            Descripcion = d.Descripcion.Trim(),
            MontoUsd = d.MontoUsd,
            RegistradoPor = pagadoPor
        })];
    }

    public async Task<PagoResponseDto> VerificarAsync(
        int pagoId, VerificarPagoDto dto, CentroAcopio? filtroCat)
    {
        var pago = await db.Pagos
            .Include(p => p.Productora)
            .Include(p => p.Lote)
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        // Centro ajeno: KeyNotFound y no Unauthorized, para que el controlador
        // responda 404. Confirmar la existencia filtraría datos de otro CAT.
        if (filtroCat is CentroAcopio cat && pago.Productora.CatAsignado != cat)
            throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        if (pago.Estado != EstadoPago.Pagado)
            throw new TransicionInvalidaException(
                pago.Estado == EstadoPago.Pendiente
                    ? "No se puede verificar un pago que la planta todavía no ha hecho."
                    : "Este pago ya estaba verificado.");

        // Mismo motivo que PagadoPor en RegistrarPagoEfectivoAsync: es el
        // registro de QUIÉN confirmó que el dinero llegó, y sin guardia
        // "   " se guardaba tal cual como la verificadora de un pago que
        // nadie verificó de verdad.
        var verificadoPor = dto.VerificadoPor?.Trim() ?? string.Empty;
        if (verificadoPor.Length == 0)
            throw new CuerpoInvalidoException(
                "La verificación debe decir quién confirmó que el pago llegó.");

        var ahora = DateTime.UtcNow;
        pago.Estado = EstadoPago.Recibido;
        pago.FechaVerificacion = ahora;
        pago.VerificadoPor = verificadoPor;
        pago.ComprobanteExpiraEn = ahora.AddDays(DiasGraciaComprobante);

        await db.SaveChangesAsync();

        return Mapear(pago, pago.Productora.NombreCompleto, pago.Lote?.CodigoLote);
    }

    public async Task<IEnumerable<CuyDisponibleDto>> ListarCuyesDisponiblesAsync(
        int loteId, int productoraId, CentroAcopio? filtroCat)
    {
        if (filtroCat is CentroAcopio cat)
        {
            var productora = await db.Productoras.FindAsync(productoraId);
            if (productora is null || productora.CatAsignado != cat)
                throw new UnauthorizedAccessException(
                    "Tu usuario solo puede consultar productoras de su centro.");
        }

        return await db.CuyRegistros
            .Where(c => c.LoteId == loteId
                && c.ProductoraId == productoraId
                && c.VentaLocalPagoId == null)
            .OrderBy(c => c.NumeroEnLote)
            .Select(c => new CuyDisponibleDto(
                c.Id, c.NumeroEnLote, c.PesoGramos,
                c.Estado.ToString(), c.MotivoNovedad))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<PagoResponseDto> RegistrarVentaLocalAsync(
        RegistrarVentaLocalDto dto, CentroAcopio? filtroCat)
    {
        var productora = await db.Productoras.FindAsync(dto.ProductoraId)
            ?? throw new KeyNotFoundException(
                $"Productora con Id {dto.ProductoraId} no encontrada.");

        if (filtroCat is CentroAcopio cat && productora.CatAsignado != cat)
            throw new UnauthorizedAccessException(
                "Tu usuario solo puede registrar ventas de productoras de su centro.");

        // ── Lo que se ve leyendo el cuerpo: 400 ──────────────────────
        if (dto.CuyRegistroIds.Count == 0)
            throw new CuerpoInvalidoException(
                "La venta local debe indicar al menos un cuy.");

        if (dto.MontoUsd <= 0)
            throw new CuerpoInvalidoException(
                "El monto de la venta debe ser mayor a cero.");

        if (!MetodosVentaLocal.Contains(dto.MetodoPago))
            throw new CuerpoInvalidoException(
                $"Método de pago no reconocido: '{dto.MetodoPago}'. " +
                $"Debe ser Efectivo, Transferencia o Cuotas.");

        var esCuotas = string.Equals(dto.MetodoPago, "Cuotas",
            StringComparison.OrdinalIgnoreCase);

        if (esCuotas && (dto.NumeroDias is not > 0 || dto.ValorPorDia is not > 0))
            throw new CuerpoInvalidoException(
                "Una venta a cuotas debe indicar el número de días y el valor por día.");

        if (string.IsNullOrWhiteSpace(dto.Responsable))
            throw new CuerpoInvalidoException("El responsable es obligatorio.");

        // Ids repetidos en la misma petición inflarían el conteo de filas
        // afectadas y harían pasar la comprobación de concurrencia de abajo.
        var ids = dto.CuyRegistroIds.Distinct().ToList();
        if (ids.Count != dto.CuyRegistroIds.Count)
            throw new CuerpoInvalidoException(
                "La venta local repite algún cuy.");

        // ── Lo que exige consultar el estado del servidor: 409 ───────
        var lote = await db.Lotes.FindAsync(dto.LoteId)
            ?? throw new KeyNotFoundException($"Lote con Id {dto.LoteId} no encontrado.");

        if (await db.Movilizaciones.AnyAsync(m => m.LoteId == lote.Id))
            throw new TransicionInvalidaException(
                "El lote ya se movilizó a la planta: sus animales ya no están en el centro.");

        // Misma regla que gobierna los descuentos: solo animales de ESA
        // productora en ESE lote, y que sigan disponibles.
        var validos = await db.CuyRegistros
            .Where(c => ids.Contains(c.Id)
                && c.LoteId == dto.LoteId
                && c.ProductoraId == dto.ProductoraId
                && c.VentaLocalPagoId == null)
            .Select(c => c.Id)
            .ToListAsync();

        var invalidos = ids.Except(validos).ToList();
        if (invalidos.Count > 0)
            throw new TransicionInvalidaException(
                $"Estos cuyes no pertenecen a la productora en ese lote, o ya se " +
                $"vendieron: {string.Join(", ", invalidos)}.");

        var pago = new Pago
        {
            ProductoraId = dto.ProductoraId,
            LoteId = dto.LoteId,
            MontoUsd = dto.MontoUsd,
            // Nace cobrada: el dinero lo recibió la propia CAT y no queda
            // nada que nadie tenga que hacer dentro del sistema.
            MontoPagadoUsd = dto.MontoUsd,
            Estado = EstadoPago.Recibido,
            EsVentaLocal = true,
            FechaPago = DateTime.UtcNow,
            MetodoPago = dto.MetodoPago,
            NumeroDias = esCuotas ? dto.NumeroDias : null,
            ValorPorDia = esCuotas ? dto.ValorPorDia : null,
            Responsable = dto.Responsable.Trim(),
            Observaciones = dto.Observaciones
        };

        // La transacción explícita debe correr dentro de la estrategia de
        // reintentos de Npgsql (EnableRetryOnFailure): un BeginTransactionAsync
        // suelto revienta con "The configured execution strategy does not
        // support user initiated transactions". Mismo patrón que
        // RecepcionService y FaenamientoService.
        var estrategia = db.Database.CreateExecutionStrategy();

        await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var tx = await db.Database.BeginTransactionAsync();

            db.Pagos.Add(pago);
            await db.SaveChangesAsync();

            // El marcado es CONDICIONAL y se compara por filas afectadas. La
            // comprobación de arriba no basta: entre ella y esta escritura
            // otra venta puede llevarse el mismo animal, y sin esto el
            // último en escribir lo pisa y los dos pagos cobran por él.
            var afectadas = await db.CuyRegistros
                .Where(c => ids.Contains(c.Id) && c.VentaLocalPagoId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    c => c.VentaLocalPagoId, pago.Id));

            if (afectadas != ids.Count)
            {
                await tx.RollbackAsync();
                throw new TransicionInvalidaException(
                    "Otra venta se registró sobre alguno de estos cuyes. " +
                    "Vuelve a abrir la lista de disponibles.");
            }

            await tx.CommitAsync();
        });

        return Mapear(pago, productora.NombreCompleto, lote.CodigoLote);
    }

    private static PagoResponseDto Mapear(
        Pago p, string nombreProductora, string? codigoLote) => new(
        Id: p.Id,
        ProductoraId: p.ProductoraId,
        NombreProductora: nombreProductora,
        LoteId: p.LoteId,
        CodigoLote: codigoLote,
        MontoUsd: p.MontoUsd,
        FechaPago: p.FechaPago,
        MetodoPago: p.MetodoPago,
        Estado: p.Estado.ToString(),
        MontoPagadoUsd: p.MontoPagadoUsd,
        FechaPagoEfectivo: p.FechaPagoEfectivo,
        PagadoPor: p.PagadoPor,
        TieneComprobante: p.ComprobanteUrl != null,
        FechaVerificacion: p.FechaVerificacion,
        VerificadoPor: p.VerificadoPor,
        Responsable: p.Responsable,
        Observaciones: p.Observaciones,
        EsVentaLocal: p.EsVentaLocal
    );
}
