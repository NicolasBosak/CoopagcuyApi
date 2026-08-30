namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// Centro de acopio y transformación. Era un enum de cinco valores compilado
/// en el binario; crear uno nuevo exigía recompilar y desplegar.
///
/// La clave primaria es el CÓDIGO de tres letras y no un Id entero. No es
/// pereza: ese código ya era la clave real en base —las cinco columnas que lo
/// referencian se persistían con HasConversion&lt;string&gt;()— y además
/// prefija el identificador de cada jaula (PAT-20260615-001). Clavando la
/// tabla encima del código, la migración es un ADD CONSTRAINT en vez de un
/// backfill de cinco columnas.
///
/// Por eso mismo el código es INMUTABLE una vez creado: cambiarlo dejaría
/// jaulas históricas con un prefijo que ya no corresponde a ningún centro, y
/// códigos ya impresos que nadie podría resolver.
///
/// El cantón dice dónde está el centro, no a quién atiende: una comunidad
/// entrega en el que le queda más cerca, aunque esté en otra provincia.
/// </summary>
public class CentroAcopio
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;

    public int CantonId { get; set; }
    public Canton Canton { get; set; } = null!;

    public bool Activo { get; set; } = true;
}
