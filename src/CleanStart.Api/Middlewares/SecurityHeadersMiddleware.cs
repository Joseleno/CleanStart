namespace CleanStart.Api.Middlewares;

/// <summary>
/// Acrescenta os cabeçalhos de segurança a toda resposta.
/// </summary>
/// <remarks>
/// <para>
/// <b>São instruções ao navegador</b>, e por isso quase não importam para um cliente que fala só JSON — um
/// <c>HttpClient</c> as ignora. Importam quando a resposta chega a um navegador: uma API costuma acabar sendo
/// aberta direto na barra de endereços, ou consumida por uma página, e é aí que eles agem.
/// </para>
/// <para>
/// <b>Definidos em <c>OnStarting</c>, não antes de chamar o próximo.</b> Os cabeçalhos precisam estar na resposta
/// no instante em que ela começa a ser escrita — e quem escreve pode ser o endpoint, o handler de exception ou o
/// limitador de requisições. O <c>OnStarting</c> é o único ponto que alcança todos eles.
/// </para>
/// </remarks>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(static estado =>
        {
            IHeaderDictionary cabecalhos = ((HttpContext)estado).Response.Headers;

            // Impede o navegador de adivinhar o tipo do conteúdo. Sem ele, uma resposta que o servidor diz ser
            // JSON pode ser interpretada como HTML — e um texto controlado pelo usuário vira script executado.
            cabecalhos["X-Content-Type-Options"] = "nosniff";

            // Recusa a página dentro de frame, o que elimina clickjacking: uma página hostil sobrepõe a sua,
            // invisível, e o clique do usuário vai para onde ela quiser.
            cabecalhos["X-Frame-Options"] = "DENY";

            // Não vaza a URL desta API para terceiros. A rota costuma conter identificador de recurso, e o
            // Referer o entregaria a todo domínio externo que a página viesse a acessar.
            cabecalhos["Referrer-Policy"] = "no-referrer";

            // A API não serve HTML: negar toda origem de conteúdo é o mais restritivo possível e não custa nada.
            // Uma aplicação com interface precisa de uma política de verdade, montada a partir do que ela carrega.
            cabecalhos["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

            // O cabeçalho do servidor diz qual servidor e qual versão estão rodando — informação que só serve a
            // quem procura uma vulnerabilidade conhecida.
            cabecalhos.Remove("Server");

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
