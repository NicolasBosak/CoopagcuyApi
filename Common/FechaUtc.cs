namespace CoopagcuyApi.Common;

public static class FechaUtc
{
    /// <summary>
    /// Normaliza a UTC una fecha recibida del cliente. El front envía ISO-8601
    /// con Z, pero un cliente que omita la zona llegaría como Unspecified: se
    /// interpreta como UTC y no como hora del servidor, que en un contenedor
    /// puede estar en cualquier zona.
    /// </summary>
    public static DateTime Normalizar(DateTime valor) => valor.Kind switch
    {
        DateTimeKind.Utc => valor,
        DateTimeKind.Local => valor.ToUniversalTime(),
        _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc)
    };
}
