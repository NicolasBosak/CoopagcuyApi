namespace CoopagcuyApi.Common.Auth;

public record LoginRequestDto(
    string Cedula,
    string Password
);

public record LoginResponseDto(
    string Token,
    string NombreCompleto,
    string Cedula,
    string Rol,
    string? CatAsignado,
    DateTime Expira
);
