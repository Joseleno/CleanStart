using CleanStart.Application.Common.Abstractions;
using CleanStart.Application.Common.Behaviors;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace CleanStart.Application.UnitTests.Common.Behaviors;

/// <summary>
/// O que o <see cref="LoggingBehavior{TMessage,TResponse}"/> registra — e o que ele não registra.
/// </summary>
public sealed class LoggingBehaviorTests
{
    private const string CorrelationIdFixo = "corr-123";

    private sealed record CriarClienteCommand(string Nome, string Documento) : ICommand<int>;

    [Fact]
    public async Task ComSucesso_RegistraInicioEConclusao()
    {
        FakeLogger logger = new();
        LoggingBehavior<CriarClienteCommand, Result<int>> behavior = Criar(logger);

        await behavior.Handle(
            new CriarClienteCommand("João", "52998224725"),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            TestContext.Current.CancellationToken);

        logger.Mensagens.Should().HaveCount(2);
        logger.Mensagens[0].Should().Contain("Processando").And.Contain("CriarClienteCommand");
        logger.Mensagens[1].Should().Contain("Concluído").And.Contain(CorrelationIdFixo);
    }

    [Fact]
    public async Task ComExcecao_RegistraFalhaERelanca()
    {
        FakeLogger logger = new();
        LoggingBehavior<CriarClienteCommand, Result<int>> behavior = Criar(logger);

        Func<Task> acao = async () => await behavior.Handle(
            new CriarClienteCommand("João", "52998224725"),
            (_, _) => throw new InvalidOperationException("banco fora"),
            TestContext.Current.CancellationToken);

        // Relança: o behavior observa, não decide. Engolir transformaria falha em silêncio.
        await acao.Should().ThrowAsync<InvalidOperationException>();

        logger.Mensagens.Should().Contain(mensagem => mensagem.Contains("Falhou", StringComparison.Ordinal));
        logger.Niveis.Should().Contain(LogLevel.Error);
    }

    [Fact]
    public async Task NaoRegistraOConteudoDaMensagem()
    {
        // Command carrega PII — nome, documento, e-mail. Log vaza em lugar que ninguém trata como banco de
        // dados: arquivo, agregador de terceiros, console de container. O nome do tipo basta para identificar
        // a operação.
        FakeLogger logger = new();
        LoggingBehavior<CriarClienteCommand, Result<int>> behavior = Criar(logger);

        await behavior.Handle(
            new CriarClienteCommand("João da Silva", "52998224725"),
            (_, _) => ValueTask.FromResult(Result.Success(1)),
            TestContext.Current.CancellationToken);

        string tudo = string.Join(" ", logger.Mensagens);

        tudo.Should().NotContain("João da Silva");
        tudo.Should().NotContain("52998224725");
    }

    private static LoggingBehavior<CriarClienteCommand, Result<int>> Criar(ILogger logger)
    {
        ICorrelationIdProvider correlationId = Substitute.For<ICorrelationIdProvider>();
        correlationId.CorrelationId.Returns(CorrelationIdFixo);

        return new LoggingBehavior<CriarClienteCommand, Result<int>>(
            new LoggerAdapter<LoggingBehavior<CriarClienteCommand, Result<int>>>(logger),
            correlationId);
    }

    /// <summary>
    /// Captura as mensagens formatadas, que é o que estes testes afirmam.
    /// </summary>
    /// <remarks>
    /// Escrito à mão em vez de substituído com NSubstitute porque o <c>ILogger.Log</c> é genérico e recebe um
    /// estado opaco: interceptá-lo com mock renderia asserção sobre tipo interno do framework, em vez de sobre
    /// o texto que de fato sai.
    /// </remarks>
    private sealed class FakeLogger : ILogger
    {
        public List<string> Mensagens { get; } = [];

        public List<LogLevel> Niveis { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Niveis.Add(logLevel);
            Mensagens.Add(formatter(state, exception));
        }
    }

    /// <summary>Adapta um <see cref="ILogger"/> para o <see cref="ILogger{T}"/> que o behavior exige.</summary>
    private sealed class LoggerAdapter<T>(ILogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
