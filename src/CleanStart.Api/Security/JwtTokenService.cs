using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CleanStart.Api.Security;

/// <summary>
/// Emite tokens JWT assinados com a chave da configuração.
/// </summary>
/// <remarks>
/// <para>
/// <b>É um exemplo, e o kit não esconde isso.</b> Num sistema real, quem emite token é um provedor de identidade
/// — Entra ID, Keycloak, Auth0, Cognito — que cuida de senha, MFA, revogação e rotação de chave. Emitir o próprio
/// token significa assumir tudo isso, e quase nunca é o que se quer.
/// </para>
/// <para>
/// O que existe aqui serve a dois propósitos legítimos: permitir experimentar a API logo depois do clone, sem
/// montar um IdP, e dar aos testes funcionais um token de verdade, para que eles exercitem o mesmo caminho de
/// validação que produção usa.
/// </para>
/// <para>
/// <b>Para ligar um IdP de verdade</b>, apague este serviço e o endpoint que o expõe, e troque a validação por
/// autoridade e JWKS — <c>options.Authority</c> em vez de <c>IssuerSigningKey</c>. A configuração da validação
/// está em <c>AddAutenticacao</c>, e é o único outro lugar que muda.
/// </para>
/// </remarks>
internal sealed class JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>
    /// Emite um token para o usuário informado.
    /// </summary>
    /// <remarks>
    /// O identificador vai em <c>sub</c>, que é o claim padrão do registro do JWT para "quem é o sujeito deste
    /// token". O <c>HttpCurrentUser</c> o lê como <see cref="ClaimTypes.NameIdentifier"/> porque o handler do
    /// ASP.NET Core faz esse mapeamento por padrão — mantê-lo ligado é o que permite trocar este emissor por um
    /// IdP sem tocar no resto do código.
    /// </remarks>
    public string Emitir(Guid usuarioId, string nome)
    {
        DateTime agora = clock.UtcNow.UtcDateTime;

        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
            new(JwtRegisteredClaimNames.Name, nome),

            // Identificador único do token. Não é usado hoje, e é o que uma lista de revogação precisaria para
            // invalidar um token específico antes de ele expirar.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        ];

        SymmetricSecurityKey chave = new(Encoding.UTF8.GetBytes(_options.SigningKey));

        JwtSecurityToken token = new(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: agora,
            expires: agora.AddMinutes(_options.ExpirationMinutes),
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
