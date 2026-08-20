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

public enum CentroAcopio
{
    PAT, // Patococha
    NIE, // Las Nieves
    HUE, // Huertas
    NAB, // Nabón/El Progreso
    PEL  // Pelincay
}
