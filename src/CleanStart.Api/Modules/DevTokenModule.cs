using Carter;
using CleanStart.Api.Security;

namespace CleanStart.Api.Modules;

/// <summary>
/// Emite um token de exemplo — apenas em desenvolvimento.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existe para que a API seja utilizável logo depois do clone.</b> Sem ele, experimentar qualquer endpoint
/// exigiria montar um provedor de identidade antes de ver o primeiro pedido ser criado — e um kit de referência
/// que não roda de primeira não cumpre o que promete.
/// </para>
/// <para>
/// <b>Não há autenticação nenhuma aqui: qualquer um pede um token para qualquer usuário.</b> É por isso que a
/// rota só é registrada em <c>Development</c>, e é por isso que o aviso está no topo do arquivo e não numa nota
/// de rodapé do README. Em produção esta rota não existe — não é "protegida", é ausente, que é a única garantia
/// que não depende de configuração correta.
/// </para>
/// <para>
/// <b>Ao ligar um provedor de identidade real</b>, apague este módulo e o <see cref="JwtTokenService"/>. Quem
/// emite token passa a ser o provedor, e a API só valida.
/// </para>
/// </remarks>
public sealed class DevTokenModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        IHostEnvironment ambiente = app.ServiceProvider.GetRequiredService<IHostEnvironment>();

        if (!ambiente.IsDevelopment())
        {
            return;
        }

        app.MapPost("/api/v1/dev/token", EmitirToken)
            .WithTags("Desenvolvimento")
            .WithName("EmitirTokenDeDesenvolvimento")
            .WithSummary("Emite um token de exemplo (somente em desenvolvimento)")
            .WithDescription(
                "Emite um JWT assinado com a chave local, para experimentar os endpoints protegidos. "
                + "Não exige credencial e não existe fora do ambiente de desenvolvimento.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)

            // Sem autorização, por definição: é o endpoint que se chama justamente por ainda não se ter token.
            .AllowAnonymous();
    }

    private static IResult EmitirToken(TokenRequest? requisicao, JwtTokenService emissor)
    {
        Guid usuarioId = requisicao?.UsuarioId ?? Guid.CreateVersion7();
        string nome = string.IsNullOrWhiteSpace(requisicao?.Nome) ? "usuario-de-exemplo" : requisicao.Nome;

        return Results.Ok(new TokenResponse(emissor.Emitir(usuarioId, nome), usuarioId));
    }

    /// <param name="UsuarioId">Identificador desejado, ou nulo para gerar um.</param>
    /// <param name="Nome">Nome do usuário no token.</param>
    public sealed record TokenRequest(Guid? UsuarioId, string? Nome);

    /// <param name="Token">O JWT, para enviar no cabeçalho <c>Authorization: Bearer</c>.</param>
    /// <param name="UsuarioId">O identificador que ficou no token — é ele que a auditoria vai gravar.</param>
    public sealed record TokenResponse(string Token, Guid UsuarioId);
}
