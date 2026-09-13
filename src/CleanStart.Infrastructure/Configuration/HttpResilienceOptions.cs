using System.ComponentModel.DataAnnotations;

namespace CleanStart.Infrastructure.Configuration;

/// <summary>
/// Política de resiliência do cliente HTTP de exemplo.
/// </summary>
/// <remarks>
/// <para>
/// Os números vivem em configuração porque são a parte que muda entre ambientes e entre parceiros: um serviço
/// interno na mesma rede tolera timeout curto e poucas tentativas; um parceiro do outro lado do país, não.
/// </para>
/// <para>
/// <b>Ligar retry em quem não é idempotente é o erro que esta classe não impede.</b> Repetir um <c>GET</c> é
/// inofensivo; repetir um <c>POST</c> que cobra um cartão cobra duas vezes. Quem decide isso é quem registra o
/// cliente, não esta configuração — ver <c>AddResiliencia</c>.
/// </para>
/// </remarks>
public sealed class HttpResilienceOptions
{
    /// <summary>Seção correspondente no arquivo de configuração.</summary>
    public const string SectionName = "HttpResilience";

    /// <summary>Quantas vezes repetir antes de desistir.</summary>
    /// <remarks>
    /// Poucas, de propósito. Cada tentativa segura uma conexão e adia a resposta de erro a quem chamou; se três
    /// tentativas não resolveram, o problema não é transiente e insistir só transforma indisponibilidade do
    /// parceiro em indisponibilidade nossa.
    /// </remarks>
    [Range(0, 10, ErrorMessage = "O número de tentativas deve estar entre 0 e 10.")]
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Atraso da primeira retentativa, em segundos.</summary>
    /// <remarks>
    /// Cresce exponencialmente a cada tentativa, com variação aleatória. A variação é o que impede que todas as
    /// instâncias, tendo falhado juntas, retentem no mesmo instante — em cima de um serviço que está tentando
    /// se recuperar.
    /// </remarks>
    [Range(1, 60, ErrorMessage = "O atraso base deve estar entre 1 e 60 segundos.")]
    public int BaseDelaySeconds { get; init; } = 1;

    /// <summary>Timeout de cada tentativa isolada, em segundos.</summary>
    [Range(1, 300, ErrorMessage = "O timeout por tentativa deve estar entre 1 e 300 segundos.")]
    public int AttemptTimeoutSeconds { get; init; } = 10;

    /// <summary>Timeout da operação inteira, incluindo todas as tentativas, em segundos.</summary>
    /// <remarks>
    /// Precisa ser maior que <see cref="AttemptTimeoutSeconds"/>, e é o limite que de fato importa para quem
    /// chamou: sem ele, três tentativas de dez segundos com espera entre elas deixariam o usuário aguardando
    /// mais de meio minuto por uma resposta que já se sabia perdida.
    /// </remarks>
    [Range(1, 600, ErrorMessage = "O timeout total deve estar entre 1 e 600 segundos.")]
    public int TotalTimeoutSeconds { get; init; } = 30;

    /// <summary>Proporção de falhas que abre o circuito, de 0 a 1.</summary>
    /// <remarks>
    /// <b>O circuit breaker existe para parar de tentar.</b> Quando um serviço está fora do ar, cada chamada
    /// custa o timeout inteiro e segura uma thread e uma conexão — com tráfego, isso esgota o pool e derruba
    /// também o que não dependia dele. Aberto o circuito, a falha é imediata e barata, e o serviço ganha espaço
    /// para voltar.
    /// </remarks>
    [Range(0.1, 1.0, ErrorMessage = "A proporção de falhas deve estar entre 0,1 e 1,0.")]
    public double FailureRatio { get; init; } = 0.5;

    /// <summary>Por quantos segundos o circuito fica aberto antes de testar de novo.</summary>
    [Range(1, 600, ErrorMessage = "A duração do circuito aberto deve estar entre 1 e 600 segundos.")]
    public int BreakDurationSeconds { get; init; } = 30;
}
