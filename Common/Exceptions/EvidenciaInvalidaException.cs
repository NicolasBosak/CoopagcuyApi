namespace CoopagcuyApi.Common.Exceptions;

// Señala que una foto de evidencia clínica no es base64 válido o supera el
// tope de tamaño. Es una excepción propia (no ArgumentException) para que
// el controlador la distinga con precisión: ArgumentException es también
// la clase padre de ArgumentNullException y ArgumentOutOfRangeException, y
// capturarla a ciegas convertiría cualquier bug ajeno de esa familia en un
// 400 que además expone el mensaje interno de .NET al cliente.
public class EvidenciaInvalidaException(string mensaje) : Exception(mensaje);
