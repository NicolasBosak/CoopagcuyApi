namespace CoopagcuyApi.Common.Auth.Recuperacion;

/// <summary>
/// Asigna a un usuario una contraseña temporal de un solo uso.
///
/// Es una función pura sobre la entidad: NO toca la base de datos y NO guarda
/// nada. Cada llamador decide cuándo persistir, si revoca sesiones y si deja
/// rastro de auditoría — que es justo lo que difiere entre las tres rutas que
/// la usan (alta de usuario, resolución de una solicitud y restablecimiento por
/// iniciativa del administrador). Meter esas diferencias aquí convertiría esto
/// en un método con tres banderas booleanas.
///
/// Vive aparte porque es una regla de seguridad: el día que la temporal deba
/// caducar o cambiar de formato, hay un solo sitio que tocar.
/// </summary>
public static class CredencialTemporal
{
    /// <summary>
    /// Devuelve la contraseña EN CLARO. Es la única vez que existe fuera del
    /// hash: el llamador se la entrega al administrador para que la dicte, y
    /// el sistema la olvida.
    /// </summary>
    public static string Asignar(Usuario usuario)
    {
        var temporal = GeneradorPasswordTemporal.Generar();

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporal);
        usuario.DebeCambiarPassword = true;

        return temporal;
    }
}
