using System.ComponentModel.DataAnnotations;

namespace CleanStart.Infrastructure.Configuration;

/// <summary>
/// Configuração do despachante do outbox — o processo que lê as mensagens pendentes e as publica.
/// </summary>
/// <remarks>
/// <para>
/// Tudo aqui é política operacional, não regra de negócio: com que frequência procurar trabalho, quanto pegar de
/// cada vez, quantas vezes insistir antes de desistir e por quanto tempo guardar o que já saiu. São os números
/// que mudam entre um ambiente e outro sem que uma linha de código mude.
/// </para>
/// <para>
/// <b>O máximo de tentativas vive aqui e não no schema</b> — nem como coluna, nem no filtro parcial do índice de
/// pendentes. Gravado no banco, mudar de cinco para três tentativas viraria uma migration.
/// </para>
/// </remarks>
public sealed class OutboxOptions
{
    /// <summary>Seção correspondente no arquivo de configuração.</summary>
    public const string SectionName = "Outbox";

    /// <summary>Se o despachante sobe junto com a aplicação.</summary>
    /// <remarks>
    /// <para>
    /// Ligado por padrão: uma aplicação que grava no outbox e não o despacha acumula eventos que ninguém recebe,
    /// em silêncio. Desligar é decisão explícita.
    /// </para>
    /// <para>
    /// Serve a dois casos reais. Em produção, permite subir instâncias só-API e escalar o despachante à parte —
    /// são cargas diferentes. Nos testes funcionais, é o que impede o worker de competir com o teste pela mesma
    /// tabela: a fábrica da API executa o <c>Program.cs</c> de verdade e não substitui registro de serviço, então
    /// sem esta flag o despachante subiria em toda classe de teste e a limpeza de mensagens antigas apagaria, no
    /// meio do caminho, a linha que o teste está prestes a conferir.
    /// </para>
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>Intervalo entre duas varreduras da tabela, em segundos.</summary>
    /// <remarks>
    /// É a latência típica entre o commit do dado e a publicação do evento. Baixar aumenta a carga de consultas
    /// ociosas quando não há o que fazer; subir atrasa todo evento.
    /// </remarks>
    [Range(1, 3_600, ErrorMessage = "O intervalo de varredura deve estar entre 1 segundo e 1 hora.")]
    public int PollingIntervalSeconds { get; init; } = 5;

    /// <summary>Quantas mensagens são trazidas por varredura.</summary>
    /// <remarks>
    /// O lote limita quanto trabalho uma iteração segura de uma vez. Lote grande demais mantém linhas travadas
    /// por mais tempo e atrasa o desligamento gracioso; pequeno demais não vaza a fila no ritmo em que ela enche.
    /// </remarks>
    [Range(1, 1_000, ErrorMessage = "O tamanho do lote deve estar entre 1 e 1000 mensagens.")]
    public int BatchSize { get; init; } = 20;

    /// <summary>Quantas tentativas uma mensagem recebe antes de parar de ser lida.</summary>
    /// <remarks>
    /// Alcançado o limite, a mensagem deixa de satisfazer o filtro da consulta e permanece na tabela com o erro
    /// da última tentativa — é o dead-letter deste projeto, e ele é a <i>ausência</i> de uma condição, não uma
    /// estrutura nova.
    /// </remarks>
    [Range(1, 50, ErrorMessage = "O máximo de tentativas deve estar entre 1 e 50.")]
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Atraso da primeira retentativa, em segundos; as seguintes dobram a partir dele.</summary>
    /// <remarks>
    /// O atraso cresce como <c>BaseRetryDelaySeconds * 2^(tentativas-1)</c>, limitado por
    /// <see cref="MaxRetryDelaySeconds"/> e acrescido de uma variação aleatória. A variação não é refinamento:
    /// quando o destino cai, todas as mensagens falham no mesmo ciclo e, sem ela, voltam todas no mesmo instante
    /// — em cima do serviço que acabou de se recuperar.
    /// </remarks>
    [Range(1, 3_600, ErrorMessage = "O atraso base deve estar entre 1 segundo e 1 hora.")]
    public int BaseRetryDelaySeconds { get; init; } = 10;

    /// <summary>Teto do atraso entre tentativas, em segundos.</summary>
    /// <remarks>
    /// Com poucas tentativas o teto quase não é alcançado, e ele existe assim mesmo: é o que impede o dobro
    /// sucessivo de virar dias quando alguém aumentar <see cref="MaxAttempts"/>.
    /// </remarks>
    [Range(1, 86_400, ErrorMessage = "O teto do atraso deve estar entre 1 segundo e 24 horas.")]
    public int MaxRetryDelaySeconds { get; init; } = 300;

    /// <summary>Por quantas horas uma mensagem já despachada permanece na tabela.</summary>
    /// <remarks>
    /// Mensagem processada é histórico: útil para conferir que um evento saiu, inútil para sempre. Sem limpeza a
    /// tabela cresce indefinidamente e nada falha — ela apenas fica lenta ao longo de meses, que é o pior modo de
    /// falha possível para quem copiou o kit e não sabe que a limpeza não existia.
    /// </remarks>
    [Range(1, 8_760, ErrorMessage = "A retenção deve estar entre 1 hora e 1 ano.")]
    public int ProcessedRetentionHours { get; init; } = 72;

    /// <summary>A cada quantas varreduras a limpeza de processadas roda.</summary>
    /// <remarks>
    /// A limpeza é manutenção, não caminho quente: rodar a cada varredura desperdiça consulta, e mantê-la na
    /// mesma transação do lote faria uma remoção grande segurar as mensagens pendentes que estão travadas.
    /// </remarks>
    [Range(1, 10_000, ErrorMessage = "O intervalo de limpeza deve estar entre 1 e 10000 varreduras.")]
    public int CleanupEveryCycles { get; init; } = 60;
}
