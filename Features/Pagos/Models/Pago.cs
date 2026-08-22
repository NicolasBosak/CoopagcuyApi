using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Pagos.Models;

/// <summary>
/// Pago a una productora por los cuyes que aportó a un lote.
///
/// No es un apunte, es un ciclo: la CAT reconoce lo que se debe y entrega un
/// ticket impreso, la planta transfiere y sube la captura, la CAT confirma que
/// el dinero llegó. Cada actor escribe su propio bloque de campos y ninguno
/// reescribe el del otro.
/// </summary>
public class Pago
{
    public int Id { get; set; }

    public int ProductoraId { get; set; }
    public Productora Productora { get; set; } = null!;

    // Anulable en el esquema por las filas anteriores a este ciclo, pero
    // OBLIGATORIO en el servicio para los pagos nuevos: un ticket que dice
    // "por los cuyes que aportó a cierto lote" no puede existir sin lote, y
    // sin lote tampoco hay novedades que trazar.
    public int? LoteId { get; set; }
    public Lote? Lote { get; set; }

    // ── Lo que emite la CAT ──────────────────────────────────────────
    public decimal MontoUsd { get; set; }
    public DateTime FechaPago { get; set; }
    // Desde 2026-08 siempre "Transferencia". Los valores "Contado",
    // "Credito", "Efectivo" son legados de filas anteriores.
    public string MetodoPago { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Ya no se escriben. Las columnas permanecen por las filas del pago a
    // crédito, retirado con el paso a transferencia única — igual que se
    // hizo con el color Negro en TipoNovedad.
    public int? NumeroDias { get; set; }
    public decimal? ValorPorDia { get; set; }

    public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;

    // ── Lo que escribe la planta al transferir ───────────────────────

    // MontoUsd menos la suma de descuentos. Lo calcula SIEMPRE el servidor:
    // es la cifra que la productora cobra, y no puede depender de lo que
    // mande el cliente. Nulo mientras el pago siga pendiente.
    public decimal? MontoPagadoUsd { get; set; }
    public DateTime? FechaPagoEfectivo { get; set; }
    public string? PagadoPor { get; set; }
    public string? ComprobanteUrl { get; set; }

    // ── Lo que escribe la CAT al verificar ───────────────────────────
    public DateTime? FechaVerificacion { get; set; }
    public string? VerificadoPor { get; set; }

    // Verificación + 5 días. Permite que el API deje de servir la captura en
    // el momento exacto, sin depender de cuándo pase el barrido.
    public DateTime? ComprobanteExpiraEn { get; set; }

    public ICollection<DescuentoPago> Descuentos { get; set; } = [];
}
