using CleanStart.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CleanStart.Infrastructure.Persistence;

/// <summary>
/// Tarefas de startup pedidas por linha de comando: aplicar migrations e popular dados de exemplo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por flag, e não automaticamente a cada arranque.</b> Migrar no startup é conveniente e perigoso: com
/// várias instâncias subindo ao mesmo tempo, todas tentam migrar o mesmo banco, e uma migration destrutiva roda
/// antes que alguém possa conferir o plano. Com a flag, aplicar é um passo do deploy — deliberado, uma vez, com
/// alguém olhando.
/// </para>
/// <para>
/// <b>A aplicação encerra depois de executá-las.</b> <c>--migrate</c> e <c>--seed</c> descrevem uma tarefa, não
/// um modo de execução: o contêiner roda, faz o trabalho e sai com código 0, que é o que um <i>init container</i>
/// ou um passo de pipeline espera. Continuar servindo depois misturaria as duas coisas e deixaria um processo
/// vivo onde se esperava uma tarefa concluída.
/// </para>
/// <para>
/// Em desenvolvimento, <c>dotnet run -- --migrate --seed</c> prepara o banco; o compose faz o mesmo por um
/// serviço à parte, se se quiser.
/// </para>
/// </remarks>
public static partial class StartupTasks
{
    /// <summary>Nome da flag que aplica as migrations pendentes.</summary>
    private const string FlagMigrate = "--migrate";

    /// <summary>Nome da flag que popula dados de exemplo.</summary>
    private const string FlagSeed = "--seed";

    /// <summary>
    /// Executa as tarefas pedidas nos argumentos e diz se a aplicação deve encerrar em vez de servir.
    /// </summary>
    /// <returns><c>true</c> se alguma tarefa rodou — e, portanto, o processo deve terminar.</returns>
    public static async Task<bool> ExecutarAsync(
        IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);

        bool migrar = args.Contains(FlagMigrate, StringComparer.Ordinal);
        bool semear = args.Contains(FlagSeed, StringComparer.Ordinal);

        if (!migrar && !semear)
        {
            return false;
        }

        // Escopo próprio: o AppDbContext é scoped, e aqui ainda não existe requisição para fornecer um.
        await using AsyncServiceScope escopo = services.CreateAsyncScope();

        AppDbContext contexto = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        ILogger logger = escopo.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(StartupTasks));

        if (migrar)
        {
            // MigrateAsync e não EnsureCreatedAsync: o segundo monta o schema a partir do modelo, ignorando as
            // migrations — o banco fica parecido com o esperado e sem histórico nenhum, e a próxima migration
            // não tem de onde partir.
            MigrandoBanco(logger);
            await contexto.Database.MigrateAsync(cancellationToken);
            MigracaoConcluida(logger);
        }

        if (semear)
        {
            // Depois da migration, e não antes: semear numa tabela que ainda não existe falha, e a ordem entre
            // as duas flags na linha de comando não deve importar.
            DatabaseSeeder seeder = escopo.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SemearAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Verifica se o banco responde e, se não responder, explica o que fazer.
    /// </summary>
    /// <remarks>
    /// Sem isto, banco fora do ar não impede a aplicação de subir — ela sobe, e cada requisição falha com
    /// <c>NpgsqlException: Failed to connect to 127.0.0.1:5432</c>, repetida a cada tentativa. A exceção está
    /// correta e não diz o que resolve: em desenvolvimento, quase sempre, subir os contêineres.
    /// <para>
    /// <b>Só em desenvolvimento.</b> Em produção o banco pode demorar a aceitar conexão enquanto a aplicação já
    /// subiu, e recusar arranque por isso transforma indisponibilidade momentânea em pod que não sobe — o
    /// <c>/health/ready</c> é quem responde por essa pergunta lá. Aqui o objetivo é outro: encurtar o caminho
    /// entre o erro e a causa para quem acabou de clonar o repositório.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> se o banco respondeu.</returns>
    public static async Task<bool> BancoRespondeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope escopo = services.CreateAsyncScope();

        AppDbContext contexto = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        ILogger logger = escopo.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(StartupTasks));

        try
        {
            if (await contexto.Database.CanConnectAsync(cancellationToken))
            {
                return true;
            }
        }
        catch (NpgsqlException)
        {
            // A exceção em si não acrescenta nada ao diagnóstico — o que importa é a instrução abaixo, e ela é
            // a mesma nos dois casos (recusou conexão ou respondeu que não). Falha de configuração já teria
            // derrubado o startup antes daqui, no ValidateOnStart.
        }

        // O endereço é montado fora da chamada de log de propósito: passá-lo como expressão faria o analisador
        // apontar avaliação desnecessária caso o log estivesse desabilitado (CA1873).
        System.Data.Common.DbConnection conexao = contexto.Database.GetDbConnection();
        string servidor = $"{conexao.DataSource}/{conexao.Database}";

        BancoNaoResponde(logger, servidor);

        // E também no console, cru. O sink de Console está configurado com o formatador JSON compacto, que é o
        // certo para log estruturado e péssimo para uma instrução de várias linhas: as quebras viram "\r\n"
        // dentro de uma linha só. Como esta mensagem existe para ser lida por uma pessoa — e é a última coisa
        // que ela vê antes de o processo encerrar —, vale escrevê-la duas vezes.
        Console.Error.WriteLine($"""

            ┌─ O banco de dados não respondeu em {servidor}
            │
            │  Em desenvolvimento, o que quase sempre resolve é subir as dependências:
            │
            │      docker compose up -d postgres redis
            │
            │  Se o banco já estiver no ar, confira a connection string:
            │
            │      dotnet user-secrets list --project src/CleanStart.Api
            │
            └─ O passo a passo está em docs/getting-started.md (Caminho 2).

            """);

        return false;
    }

    [LoggerMessage(EventId = 5100, Level = LogLevel.Information, Message = "Aplicando migrations pendentes...")]
    private static partial void MigrandoBanco(ILogger logger);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information, Message = "Migrations aplicadas")]
    private static partial void MigracaoConcluida(ILogger logger);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Critical,
        Message = """
                  O banco de dados não respondeu em {Servidor}.

                  Em desenvolvimento, o que quase sempre resolve é subir as dependências:

                      docker compose up -d postgres redis

                  Se o banco já estiver no ar, confira a connection string em user secrets:

                      dotnet user-secrets list --project src/CleanStart.Api

                  O passo a passo está em docs/getting-started.md (Caminho 2).
                  """)]
    private static partial void BancoNaoResponde(ILogger logger, string servidor);
}
