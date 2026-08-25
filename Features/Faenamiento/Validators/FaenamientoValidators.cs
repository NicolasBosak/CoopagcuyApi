using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.DTOs;
using FluentValidation;

namespace CoopagcuyApi.Features.Faenamiento.Validators;

public class RegistrarFaenamientoBatchValidator
    : AbstractValidator<RegistrarFaenamientoBatchDto>
{
    public RegistrarFaenamientoBatchValidator()
    {
        RuleFor(x => x.OperarioResponsable)
            .NotEmpty()
            .WithMessage("El operario responsable es obligatorio.");

        RuleFor(x => x.Lotes)
            .NotEmpty()
            .WithMessage("Selecciona al menos un lote para faenar.");
    }
}

public class RegistrarDespachoValidator : AbstractValidator<RegistrarDespachoDto>
{
    public RegistrarDespachoValidator()
    {
        RuleFor(x => x.LoteFaenadoId)
            .GreaterThan(0)
            .WithMessage("Debe seleccionar un lote faenado válido.");

        RuleFor(x => x.CuyFaenamientoIds)
            .NotEmpty()
            .WithMessage("Selecciona al menos un animal para despachar.");

        RuleFor(x => x.ClienteDestino)
            .NotEmpty()
            .WithMessage("El cliente de destino es obligatorio.")
            .MaximumLength(200);

        RuleFor(x => x.Responsable)
            .NotEmpty()
            .WithMessage("El responsable del despacho es obligatorio.");

        RuleFor(x => x.TipoMercado)
            .Must(t => t is "Local" or "Nacional" or "Internacional")
            .WithMessage("El mercado de destino debe ser Local, Nacional o Internacional.");

        // S4: se sabe leyendo el cuerpo, sin consultar nada del servidor —
        // el mismo criterio que el resto de las reglas de este validador.
        // Vivía como un guard aparte en FaenamientoService (lanzando
        // CuerpoInvalidoException DESPUÉS de este paso), así que un
        // operador con precio faltante Y mercado inválido solo veía el
        // error de mercado, lo corregía, reenviaba, y recién ahí se
        // enteraba del precio — dos viajes de ida y vuelta en una tablet de
        // 7 pulgadas en el campo. FluentValidation junta todos los errores
        // del cuerpo en una sola respuesta.
        RuleFor(x => x.PrecioUnitarioUsd)
            .NotNull()
            .WithMessage("El precio unitario de venta es obligatorio y debe ser mayor a cero.")
            .GreaterThan(0)
            .WithMessage("El precio unitario de venta es obligatorio y debe ser mayor a cero.");

        // El despacho es el único registro que se agenda: la entrega puede
        // pactarse con el cliente para más adelante. Lo que no se permite es
        // retroceder la fecha e inventar una salida que ya habría ocurrido.
        // Los 5 minutos de gracia absorben el tiempo de llenar el formulario
        // tras elegir "ahora", que si no llegaría al servidor ya vencido.
        RuleFor(x => x.FechaDespacho)
            .Must(f => FechaUtc.Normalizar(f) >= DateTime.UtcNow.AddMinutes(-5))
            .WithMessage("La fecha de despacho no puede ser pasada.");
    }
}