namespace CoopagcuyApi.Common.Exceptions;

// Señala que una entrega capturada offline traía una cédula ecuatoriana
// válida que NO corresponde a ninguna productora del centro: en vez de
// registrarse, quedó en la bandeja de vinculación para que un administrador
// la asigne a la productora correcta. No es un error de la sincronización:
// el dispositivo debe marcar la entrega como "en revisión" y dejar de
// reenviarla.
public class EntregaEnVinculacionException(string? idCliente) : Exception(
    $"La entrega {idCliente} quedó pendiente de vincular a una productora.")
{
    public string? IdCliente { get; } = idCliente;
}
