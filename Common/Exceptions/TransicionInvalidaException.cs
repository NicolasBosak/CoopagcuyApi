namespace CoopagcuyApi.Common.Exceptions;

/// <summary>
/// Señala un cambio de estado fuera de orden: pagar un ticket ya pagado,
/// verificar uno que nadie ha pagado.
///
/// Excepción propia y no InvalidOperationException porque el controlador
/// necesita distinguirla: el cuerpo de la petición es válido —lo que no
/// encaja es el momento— y eso es 409, no 400.
/// </summary>
public class TransicionInvalidaException : Exception
{
    public TransicionInvalidaException(string mensaje) : base(mensaje) { }

    /// <summary>
    /// Con causa encadenada. Existe por el rescate de DbUpdateException al
    /// guardar el pago: sin este constructor el error real de Postgres se
    /// perdía entero y el operador recibía una explicación inventada, sin
    /// que quedara rastro en el log de lo que de verdad falló.
    /// </summary>
    public TransicionInvalidaException(string mensaje, Exception interna)
        : base(mensaje, interna) { }
}
