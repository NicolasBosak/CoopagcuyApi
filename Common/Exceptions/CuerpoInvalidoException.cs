namespace CoopagcuyApi.Common.Exceptions;

/// <summary>
/// Un campo del cuerpo trae un valor que no sirve por sí solo: en blanco
/// donde se exige texto, un monto que no es positivo, más decimales de los
/// que caben en centavos.
///
/// Distinta de TransicionInvalidaException por el criterio que separa 400 de
/// 409 en este módulo: si para saber que está mal basta con LEER el cuerpo,
/// es 400 y va aquí; si hace falta consultar el estado del servidor —el
/// ticket ya pagado, el monto del ticket, a quién pertenece la novedad—,
/// es 409 y va allá.
///
/// Distinta de EvidenciaInvalidaException, que es específica del binario
/// subido (base64 ilegible o demasiado pesado) y cuya documentación habla de
/// la foto: usarla para el nombre de una persona contradecía su contrato.
/// Ambas responden 400; lo que cambia es qué parte del cuerpo está mal.
/// </summary>
public class CuerpoInvalidoException(string mensaje) : Exception(mensaje);
