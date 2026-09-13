using Bogus;
using CleanStart.Application.Common.Abstractions;
using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using CleanStart.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanStart.Infrastructure.Persistence.Seed;

/// <summary>
/// Popula o banco com dados de exemplo, para que a API tenha o que mostrar logo depois do clone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Só roda sob a flag <c>--seed</c>, e só se o banco estiver vazio.</b> As duas condições importam: a
/// primeira impede que dados fictícios apareçam onde ninguém pediu, e a segunda torna a operação repetível —
/// rodar duas vezes não duplica nada, o que é o que permite deixar a flag ligada num ambiente de
/// desenvolvimento sem pensar no assunto.
/// </para>
/// <para>
/// <b>Cria pelo domínio, não por SQL.</b> Cada cliente passa por <c>Customer.Register</c> e cada pedido por
/// <c>Order.Place</c> — as mesmas invariantes que uma requisição atravessaria. Inserir direto no banco seria
/// mais rápido e produziria dados que o domínio recusaria: documento com dígito errado, pedido sem item,
/// mistura de moedas. Dado de exemplo inválido é pior que nenhum, porque quem lê o kit o toma como referência.
/// </para>
/// <para>
/// <b>Semente fixa no Bogus</b> (<c>Randomizer.Seed</c>): duas execuções produzem os mesmos nomes e valores. É o
/// que permite escrever documentação que cita um dado concreto e conferir comportamento entre máquinas.
/// </para>
/// </remarks>
internal sealed partial class DatabaseSeeder(
    AppDbContext contexto,
    IDateTimeProvider clock,
    ILogger<DatabaseSeeder> logger)
{
    private const int QuantidadeDeClientes = 10;

    /// <summary>Popula o banco, se ainda não houver nada.</summary>
    public async Task SemearAsync(CancellationToken cancellationToken = default)
    {
        if (await contexto.Customers.AnyAsync(cancellationToken))
        {
            // Idempotente por checagem, e não por "apagar e recriar": apagar tornaria a flag capaz de destruir
            // dado de verdade caso alguém a ligasse no ambiente errado.
            SeedIgnorado(logger);
            return;
        }

        // Semente fixa: mesma execução, mesmos dados.
        Randomizer.Seed = new Random(20260913);

        DateTimeOffset agora = clock.UtcNow;
        List<Customer> clientes = [];

        Faker faker = new("pt_BR");

        for (int i = 0; i < QuantidadeDeClientes; i++)
        {
            Result<Customer> cliente = Customer.Register(
                faker.Person.FullName,

                // Email único por índice: o e-mail tem índice único no banco, e o Bogus repete valores com
                // frequência suficiente para colidir em dez registros.
                Email.Of($"cliente{i}@exemplo.test").Value,
                Document.Of(GerarCpf(faker)).Value,
                agora);

            if (cliente.IsFailure)
            {
                // Não deveria acontecer — mas se o domínio recusar, é defeito no gerador e precisa aparecer,
                // não ser ignorado deixando o banco pela metade.
                throw new InvalidOperationException(
                    $"O seed gerou um cliente inválido: {cliente.Error.Code} — {cliente.Error.Message}");
            }

            clientes.Add(cliente.Value);
        }

        contexto.AddRange(clientes);

        // Um pedido para os cinco primeiros clientes, e nenhum para os outros: quem abre a listagem precisa ver
        // os dois casos, senão "cliente sem pedido" fica sem cobertura visual.
        foreach (Customer cliente in clientes.Take(5))
        {
            Result<Order> pedido = Order.Place(
                cliente.Id,
                [
                    (ProductId.New(), faker.Random.Int(1, 3), Money.Of(faker.Random.Decimal(10, 500), "BRL").Value),
                    (ProductId.New(), faker.Random.Int(1, 2), Money.Of(faker.Random.Decimal(10, 200), "BRL").Value),
                ],
                agora);

            if (pedido.IsFailure)
            {
                throw new InvalidOperationException(
                    $"O seed gerou um pedido inválido: {pedido.Error.Code} — {pedido.Error.Message}");
            }

            contexto.Add(pedido.Value);
        }

        await contexto.SaveChangesAsync(cancellationToken);

        SeedConcluido(logger, clientes.Count, 5);
    }

    /// <summary>
    /// Gera um CPF com dígito verificador calculado.
    /// </summary>
    /// <remarks>
    /// O <c>Document</c> valida o dígito de verdade, então número aleatório de onze casas é recusado. Vale a
    /// pena que seja assim: um kit que semeia documento inválido ensina que a validação é decorativa.
    /// </remarks>
    private static string GerarCpf(Faker faker)
    {
        int[] digitos = new int[11];

        for (int i = 0; i < 9; i++)
        {
            digitos[i] = faker.Random.Int(0, 9);
        }

        digitos[9] = CalcularDigito(digitos, quantidade: 9, pesoInicial: 10);
        digitos[10] = CalcularDigito(digitos, quantidade: 10, pesoInicial: 11);

        return string.Concat(digitos);
    }

    private static int CalcularDigito(int[] digitos, int quantidade, int pesoInicial)
    {
        int soma = 0;

        for (int i = 0; i < quantidade; i++)
        {
            soma += digitos[i] * (pesoInicial - i);
        }

        int resto = soma % 11;

        return resto < 2 ? 0 : 11 - resto;
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "Seed: {Clientes} cliente(s) e {Pedidos} pedido(s) de exemplo gravados")]
    private static partial void SeedConcluido(ILogger logger, int clientes, int pedidos);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Seed ignorado: o banco já tem dados")]
    private static partial void SeedIgnorado(ILogger logger);
}
