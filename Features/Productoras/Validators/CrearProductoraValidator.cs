using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Productoras.DTOs;
using FluentValidation;

namespace CoopagcuyApi.Features.Productoras.Validators;

public class CrearProductoraValidator : AbstractValidator<CrearProductoraDto>
{
    public CrearProductoraValidator()
    {
        RuleFor(x => x.NombreCompleto)
            .NotEmpty().WithMessage("El nombre completo es obligatorio.")
            .MaximumLength(150);

        // Misma validación que en el login: algoritmo ecuatoriano completo
        // (provincia y dígito verificador), no solo longitud
        RuleFor(x => x.Cedula)
            .NotEmpty().WithMessage("La cédula es obligatoria.")
            .Must(ValidadorCedula.EsValida)
            .WithMessage("El número de cédula no es válido.");

        RuleFor(x => x.Comunidad)
            .NotEmpty().WithMessage("La comunidad es obligatoria.");

        RuleFor(x => x.Canton)
            .NotEmpty().WithMessage("El cantón es obligatorio.");
    }
}