using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Common;

/// <summary>
/// Único borde que valida un código de CAT contra el catálogo real
/// (tabla CentrosAcopio) en las rutas de ESCRITURA. Antes de la Task 6 había
/// cuatro copias de la misma forma "^[A-Z]{3}$" (Usuario, Productora,
/// Recepción, y el filtro de lectura de Reportes), repartidas en dos
/// implementaciones distintas, que solo comprobaban la FORMA: aceptaban un
/// código de tres letras bien formado que no existía en ningún centro real
/// o que ya estaba desactivado.
///
/// Con la tabla CentrosAcopio ya en pie (Task 5), la comprobación correcta ya
/// no es de forma sino contra el catálogo — y es más estricta: un código de
/// forma perfecta pero inexistente o inactivo también se rechaza. Vive en un
/// solo sitio para que cambiar la regla no exija tocar cuatro archivos.
///
/// El código debe llegar ya normalizado (Trim + ToUpperInvariant) por quien
/// llama: aquí no se normaliza, para no esconder ese paso —ya presente en
/// los tres bordes de escritura— detrás de esta validación.
/// </summary>
public static class ValidadorCat
{
    /// <summary>
    /// Lanza si el código no corresponde a un centro de acopio ACTIVO del
    /// catálogo. Se usa en las tres rutas de ESCRITURA que asignan un CAT
    /// (alta de usuario operador, alta/edición de productora, registro de
    /// entrega): un centro desactivado no debe recibir asignaciones ni
    /// entregas nuevas, aunque su historial siga existiendo.
    /// </summary>
    public static async Task ValidarCatActivoAsync(this AppDbContext db, string codigo)
    {
        if (!await db.CentrosAcopio.AnyAsync(c => c.Codigo == codigo && c.Activo))
            throw new InvalidOperationException(
                $"El centro de acopio '{codigo}' no existe o está inactivo.");
    }
}
