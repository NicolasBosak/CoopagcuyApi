namespace CoopagcuyApi.Common;

public enum EstadoLote
{
    Aceptado,
    ConNovedad,
    Rechazado
}

public enum TipoNovedad
{
    BajoPeso,        // 850g–874g
    OrejaDura,       // animal viejo
    ColorNoConforme, // piel negra
    SinAyuno,
    SobrePeso,       // > 1300g: fuera del rango operativo
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
