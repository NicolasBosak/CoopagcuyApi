namespace CoopagcuyApi.Features.Faenamiento.Models;

/// <summary>
/// Detalle por animal de un despacho: vincula cada canal despachada con su
/// registro individual de faenamiento. El índice único sobre
/// CuyFaenamientoId garantiza a nivel de base de datos que un animal solo
/// puede despacharse una vez.
/// </summary>
public class DespachoCuy
{
    public int Id { get; set; }

    public int DespachoId { get; set; }
    public Despacho Despacho { get; set; } = null!;

    public int CuyFaenamientoId { get; set; }
    public CuyFaenamiento CuyFaenamiento { get; set; } = null!;
}
