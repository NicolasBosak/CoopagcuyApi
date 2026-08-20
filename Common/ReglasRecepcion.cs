namespace CoopagcuyApi.Common;

/// <summary>
/// Parámetros de negocio de la recepción en el CAT. Están aquí y no dispersos
/// por el servicio porque cambian por decisión de la cooperativa, no por
/// refactor: quien los ajuste debe encontrarlos en un solo sitio.
///
/// El front mantiene un espejo en src/domain/reglasRecepcion.ts. No se sirven
/// por endpoint a propósito: el wizard de campo evalúa animales SIN señal, y
/// un catálogo remoto lo dejaría sin reglas justo cuando más las necesita.
/// </summary>
public static class ReglasRecepcion
{
    /// Capacidad de la jaula de transporte del CAT — SRS RF-104.
    public const int CapacidadJaula = 15;

    /// Por debajo de este peso el animal se rechaza.
    public const decimal PesoMinimoGramos = 1200m;

    /// Por encima se acepta, pero queda fuera del rango comercial y se anota.
    public const decimal PesoMaximoGramos = 1500m;
}
