using System.ComponentModel.DataAnnotations;

namespace CleanStart.Infrastructure.Configuration;

/// <summary>
/// Configuração do token JWT.
/// </summary>
/// <remarks>
/// A autenticação em si é a Fase 4; estas opções existem desde agora porque o plano as pede validadas no startup,
/// e porque chave de assinatura ausente precisa derrubar a aplicação ao subir — não na primeira tentativa de
/// login.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Seção correspondente no arquivo de configuração.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Quem emite o token.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Para quem o token vale.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = string.Empty;

    /// <summary>Chave de assinatura.</summary>
    /// <remarks>
    /// O mínimo de 32 caracteres não é arbitrário: HMAC-SHA256 usa chave de 256 bits, e uma chave mais curta é
    /// preenchida ou rejeitada dependendo da biblioteca — nos dois casos, a segurança que se acredita ter não
    /// existe. Validar aqui transforma isso em erro de startup.
    /// <para>
    /// Nunca versionada: User Secrets em desenvolvimento, cofre ou variável de ambiente em produção.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "A chave de assinatura precisa de ao menos 32 caracteres (256 bits).")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Por quantos minutos o token vale.</summary>
    [Range(1, 1_440, ErrorMessage = "A validade do token deve estar entre 1 minuto e 24 horas.")]
    public int ExpirationMinutes { get; init; } = 60;
}
