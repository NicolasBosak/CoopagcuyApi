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

        RuleFor(x => x.Cedula)
            .NotEmpty().WithMessage("La cédula es obligatoria.")
            .Length(10, 13).WithMessage("La cédula debe tener entre 10 y 13 caracteres.");

        RuleFor(x => x.Comunidad)
            .NotEmpty().WithMessage("La comunidad es obligatoria.");

        RuleFor(x => x.Canton)
            .NotEmpty().WithMessage("El cantón es obligatorio.");
    }
}