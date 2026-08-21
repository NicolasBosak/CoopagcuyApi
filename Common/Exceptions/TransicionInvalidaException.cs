namespace CoopagcuyApi.Common.Exceptions;

/// <summary>
/// Señala un cambio de estado fuera de orden: pagar un ticket ya pagado,
/// verificar uno que nadie ha pagado.
///
/// Excepción propia y no InvalidOperationException porque el controlador
/// necesita distinguirla: el cuerpo de la petición es válido —lo que no
/// encaja es el momento— y eso es 409, no 400.
/// </summary>
public class TransicionInvalidaException(string mensaje) : Exception(mensaje);
