using System.Security.Cryptography;

namespace CoopagcuyApi.Common.Auth.Recuperacion;

/// <summary>
/// Genera la contraseña temporal que el administrador dicta al operador por
/// teléfono. El formato —palabra corta + guion + cinco dígitos— está elegido
/// para DICTARSE, que es el requisito real de este sistema: una cadena como
/// "xK7mQ2vP" es más fuerte sobre el papel e inservible cuando hay que
/// deletreársela a alguien en el campo con mala cobertura.
///
/// La entropía es baja a propósito y se compensa por tres vías: la temporal
/// vive minutos, /api/auth/login limita a 10 intentos por minuto y por IP, y
/// queda inutilizada en cuanto el operador la cambia (DebeCambiarPassword).
/// </summary>
public static class GeneradorPasswordTemporal
{
    // Palabras del entorno de trabajo: fáciles de decir y de recordar el
    // tiempo que tarda el operador en teclearlas. Sin tildes ni "ñ": el
    // teclado de la tablet las esconde detrás de una pulsación larga.
    private static readonly string[] Palabras =
    [
        "cuy", "andes", "sierra", "campo", "valle", "monte", "rio",
        "sol", "trigo", "maiz", "cedro", "pino", "nube", "paramo"
    ];

    public static string Generar()
    {
        var palabra = Palabras[RandomNumberGenerator.GetInt32(Palabras.Length)];

        // El rango arranca en 10 000 para que siempre salgan cinco dígitos:
        // un cero a la izquierda se pierde al dictarlo
        var numero = RandomNumberGenerator.GetInt32(10_000, 100_000);

        return $"{palabra}-{numero}";
    }
}
