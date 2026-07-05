namespace CoopagcuyApi.Common.Exceptions;

// Señala que una entrega offline ya fue sincronizada antes (reintento del
// dispositivo tras un corte de red). No es un error: el registro original
// está a salvo y el cliente debe marcar la entrega como sincronizada.
public class EntregaDuplicadaException(string idCliente) : Exception(
    $"La entrega {idCliente} ya fue sincronizada anteriormente.")
{
    public string IdCliente { get; } = idCliente;
}
