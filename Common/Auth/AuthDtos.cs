namespace CoopagcuyApi.Common.Auth;

public record LoginRequestDto(
    string Email,
    string Password
);

public record LoginResponseDto(
    string Token,
    string NombreCompleto,
    string Email,
    string Rol,
    DateTime Expira
);