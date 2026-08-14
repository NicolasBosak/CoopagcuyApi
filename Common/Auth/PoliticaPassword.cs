namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Política mínima de contraseñas del sistema: 8 caracteres o más, con al
/// menos una letra y un dígito.
///
/// Vive aparte porque la aplican tres sitios distintos —alta de usuario,
/// edición de usuario y cambio de contraseña tras un restablecimiento— y
/// el texto del error es el mismo en los tres. Con copias separadas, subir
/// el mínimo a 10 caracteres exigiría acordarse de los tres.
/// </summary>
public static class PoliticaPassword
{
    public const string Requisitos =
        "La contraseña debe tener al menos 8 caracteres, " +
        "incluyendo una letra y un número.";

    public static bool EsValida(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= 8
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);

    public static void Validar(string password)
    {
        if (!EsValida(password))
            throw new InvalidOperationException(Requisitos);
    }
}
