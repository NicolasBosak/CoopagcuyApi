namespace CoopagcuyApi.Common;

public static class FechaUtc
{
    /// <summary>
    /// Desfase horario del piloto: Ecuador continental, UTC-5.
    ///
    /// Es un valor fijo y no una zona del sistema operativo a propósito.
    /// Ecuador no aplica horario de verano, así que -5 es exacto todo el año,
    /// y los identificadores de zona horaria no coinciden entre Windows
    /// ("SA Pacific Standard Time") y Linux ("America/Guayaquil"): resolverlos
    /// por nombre añadiría una forma de fallar que no aporta nada aquí.
    ///
    /// Se usa para traducir los DÍAS que elige el usuario en los filtros de
    /// reportes —que él entiende como días locales— al instante UTC que
    /// realmente delimita ese día en la base.
    /// </summary>
    public static readonly TimeSpan DesfasePiloto = TimeSpan.FromHours(-5);

    /// <summary>
    /// Instante UTC en el que empieza un día local del piloto.
    ///
    /// Tratar la fecha del filtro como si ya fuera UTC dejaba fuera de todo
    /// reporte las últimas cinco horas de cada día local (de 19:00 a
    /// medianoche): un despacho registrado a las 20:00 en el CAT no aparecía
    /// en el reporte de ese día porque en UTC ya pertenecía al siguiente.
    /// </summary>
    public static DateTime InicioDelDiaLocal(DateTime diaLocal) =>
        DateTime.SpecifyKind(diaLocal.Date - DesfasePiloto, DateTimeKind.Utc);

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
