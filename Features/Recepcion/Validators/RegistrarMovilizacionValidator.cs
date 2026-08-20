using CoopagcuyApi.Features.Recepcion.DTOs;
using FluentValidation;

namespace CoopagcuyApi.Features.Recepcion.Validators;

/// <summary>
/// La declaración sanitaria es lo que da valor probatorio a la guía de
/// movilización: sin ella el documento afirma un transporte sin que nadie
/// responda por el estado de los animales.
/// </summary>
public class RegistrarMovilizacionValidator
    : AbstractValidator<RegistrarMovilizacionDto>
{
    public RegistrarMovilizacionValidator()
    {
        RuleFor(m => m.SinAntibioticos7Dias)
            .NotNull()
            .WithMessage("Debes confirmar que los cuyes no recibieron " +
                         "antibióticos en los últimos 7 días.")
            .Must(v => v == true)
            .WithMessage("No se puede registrar el envío: los cuyes no deben " +
                         "haber recibido antibióticos en los últimos 7 días.");

        RuleFor(m => m.Conductor)
            .NotEmpty().WithMessage("El conductor es obligatorio.");

        RuleFor(m => m.ResponsableDespacho)
            .NotEmpty().WithMessage("El responsable del despacho es obligatorio.");

        RuleFor(m => m.CantidadMovilizada)
            .GreaterThan(0).WithMessage("La cantidad movilizada debe ser mayor que cero.");
    }
}
