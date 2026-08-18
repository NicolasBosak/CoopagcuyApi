using System.Security.Claims;
using CoopagcuyApi.Common;

namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Definición ÚNICA de "qué puede ver este usuario". Antes esta lógica vivía
/// duplicada y privada en varios controladores; centralizarla evita que un
/// endpoint nuevo olvide el filtro por CAT y termine exponiendo datos de
/// otro centro de acopio.
///
/// Regla del piloto: un OperadorCAT está acotado al centro de acopio de su
/// claim "cat". Los administradores no tienen restricción de centro.
/// </summary>
public static class AlcanceUsuario
{
    public static bool EsOperadorCat(this ClaimsPrincipal user) =>
        user.IsInRole("OperadorCAT");

    public static bool EsAdmin(this ClaimsPrincipal user) =>
        user.IsInRole("AdminCooperativa") || user.IsInRole("AdminTecnico");

    /// <summary>
    /// CAT (como texto: "PAT", "NIE"…) al que está limitado el usuario, o
    /// null si no tiene restricción de centro (administradores u otros roles).
    /// </summary>
    public static string? CatRestringido(this ClaimsPrincipal user) =>
        user.EsOperadorCat() ? user.FindFirst("cat")?.Value : null;

    /// <summary>
    /// true si el usuario NO puede tocar recursos del centro indicado. Un
    /// operador sin CAT asignado se considera fuera de alcance de todo (no
    /// debería poder operar hasta que un admin le asigne centro).
    /// </summary>
    public static bool FueraDeAlcance(this ClaimsPrincipal user, string? catRecurso)
    {
        var catUsuario = user.CatRestringido();
        if (catUsuario is null) return false;              // sin restricción
        return !string.Equals(catUsuario, catRecurso,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// true si el usuario NO puede operar sobre la comunidad indicada. Una
    /// comunidad pertenece a un centro de acopio (Comunidad.CatReferencia),
    /// así que el alcance por CAT alcanza también a las comunidades sin
    /// necesidad de un campo nuevo en el usuario.
    ///
    /// Vive aquí y no en el controlador por la misma razón que el resto de
    /// este archivo: la respuesta a "qué puede tocar este usuario" tiene un
    /// solo sitio, o el próximo endpoint se olvidará de preguntarla.
    /// </summary>
    public static bool ComunidadFueraDeAlcance(
        this ClaimsPrincipal user, CentroAcopio? catDeLaComunidad)
    {
        var catUsuario = user.CatRestringido();
        if (catUsuario is null) return false;              // sin restricción
        if (catDeLaComunidad is null) return true;         // comunidad inexistente
        return !string.Equals(catUsuario, catDeLaComunidad.Value.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
