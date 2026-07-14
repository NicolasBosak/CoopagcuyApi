namespace CoopagcuyApi.Features.Productoras.Models;

/// <summary>
/// Pago realizado a una productora por la entrega de cuyes en el CAT.
/// Reemplaza el registro de pagos en cuaderno manual identificado como
/// brecha en el diagnóstico PRODUCTO1.
/// </summary>
public class Pago
{
    public int Id { get; set; }

    public int ProductoraId { get; set; }
    public Productora Productora { get; set; } = null!;

    // Opcional: el pago puede asociarse a un lote específico
    public int? LoteId { get; set; }
    public Lote? Lote { get; set; }

    public decimal MontoUsd { get; set; }
    public DateTime FechaPago { get; set; }
    // Modalidad de pago: "Contado" | "Credito". (Los pagos antiguos
    // guardan "Efectivo"/"Transferencia" como valores legados.)
    public string MetodoPago { get; set; } = string.Empty;
    // Solo para crédito: en cuántos días se difiere el pago y cuánto
    // corresponde a cada uno (monto ÷ número de días)
    public int? NumeroDias { get; set; }
    public decimal? ValorPorDia { get; set; }
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}
