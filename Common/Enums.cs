namespace CoopagcuyApi.Common;

public enum EstadoLote
{
    Aceptado,
    ConNovedad,
    Rechazado
}

public enum TipoNovedad
{
    BajoPeso,        // < 1200g: animal rechazado
    OrejaDura,       // animal viejo
    // Ya no se genera: "Negro" salió del catálogo de colores en 2026-08.
    // El valor permanece por las filas históricas y por AnilloNovedades.
    ColorNoConforme,
    SinAyuno,
    SobrePeso,       // > 1500g: fuera del rango comercial, se acepta
    SignosClinicos,  // condición sanitaria visual con observación
    Otro
}

public enum EstadoCanal
{
    Apto,
    ConNovedad,
    Rechazado
}

public enum RolUsuario
{
    OperadorCAT,
    OperadorFaenamiento,
    AdminCooperativa,
    AdminTecnico
}

// El centro de acopio ERA un enum de cinco valores. Dejó de serlo cuando la
// organización necesitó crear centros nuevos sin recompilar: ahora es un
// catálogo (Features/Catalogos/Models/CentroAcopio.cs) y su código de tres
// letras viaja como string. Las columnas de base no cambiaron: ya se
// persistían con HasConversion<string>().

/// <summary>
/// Ciclo de vida de un pago. Las transiciones son de un solo sentido: no se
/// anula un pago, se corrige con otro.
/// </summary>
public enum EstadoPago
{
    Pendiente,  // la CAT lo emitió, la planta aún no transfiere
    Pagado,     // la planta transfirió y subió la captura
    Recibido    // la CAT confirmó que el dinero llegó
}
