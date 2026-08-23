namespace CoopagcuyApi.Common;

/// <summary>
/// Claves de los <c>pg_advisory_xact_lock</c> que se comparten entre más de
/// un servicio. Están aquí y no repetidas como texto libre en cada sitio
/// porque el lock solo sirve si la clave coincide carácter por carácter en
/// los dos lados que lo toman — un solo cambio en un archivo, sin tocar el
/// otro, lo desincroniza en silencio (no hay excepción ni prueba que lo note
/// sola: los dos lados seguirían tomando "un" lock, cada uno el suyo, y la
/// carrera que el Arreglo 4 de la revisión final vino a cerrar volvería a
/// abrirse sin que ninguna prueba lo marque roja). Centralizarla la vuelve
/// indesincronizable por construcción.
/// </summary>
public static class ClavesLock
{
    /// <summary>
    /// Serializa la venta local (<c>PagoService.RegistrarVentaLocalAsync</c>)
    /// contra la movilización (<c>MovilizacionService.RegistrarAsync</c>) del
    /// mismo lote: las dos deciden sobre el mismo saldo de animales
    /// disponibles y no pueden hacerlo a la vez sin verse la una a la otra.
    /// </summary>
    public static string LoteMovilizacion(int loteId) => $"movilizacion-lote-{loteId}";
}
