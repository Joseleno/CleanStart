using CleanStart.Domain.Common;
using CleanStart.Domain.Customers;
using CleanStart.Domain.Errors;
using CleanStart.Domain.ValueObjects;

namespace CleanStart.Domain.UnitTests.Customers;

/// <summary>
/// Cadastro e alteração de <see cref="Customer"/>.
/// </summary>
public sealed class CustomerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);

    private static Email EmailValido() => Email.Of("joao@example.com").Value;

    private static Document DocumentoValido() => Document.Of("529.982.247-25").Value;

    [Fact]
    public void Register_ComDadosValidos_RetornaSucesso()
    {
        Result<Customer> resultado = Customer.Register(
            "João da Silva",
            EmailValido(),
            DocumentoValido(),
            Agora);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Name.Should().Be("João da Silva");
        resultado.Value.CreatedAt.Should().Be(Agora);
        resultado.Value.UpdatedAt.Should().BeNull();
        resultado.Value.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("J")]       // curto demais
    [InlineData(null)]
    public void Register_ComNomeInvalido_RetornaFalha(string? nome)
    {
        Result<Customer> resultado = Customer.Register(
            nome!,
            EmailValido(),
            DocumentoValido(),
            Agora);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error.Should().Be(DomainErrors.Customer.NomeInvalido());
    }

    [Fact]
    public void Register_ComNomeAcimaDoLimite_RetornaFalha()
    {
        string longo = new('a', 201);

        Customer.Register(longo, EmailValido(), DocumentoValido(), Agora)
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Register_RemoveEspacosDasPontasDoNome()
    {
        Result<Customer> resultado = Customer.Register(
            "  João da Silva  ",
            EmailValido(),
            DocumentoValido(),
            Agora);

        resultado.Value.Name.Should().Be("João da Silva");
    }

    [Fact]
    public void ChangeName_ComNomeValido_AlteraEMarcaAtualizacao()
    {
        Customer cliente = ClienteValido();
        DateTimeOffset depois = Agora.AddDays(1);

        Result resultado = cliente.ChangeName("João Pedro da Silva", depois);

        resultado.IsSuccess.Should().BeTrue();
        cliente.Name.Should().Be("João Pedro da Silva");
        cliente.UpdatedAt.Should().Be(depois);
    }

    [Fact]
    public void ChangeName_ComNomeInvalido_NaoAlteraNada()
    {
        Customer cliente = ClienteValido();
        string original = cliente.Name;

        Result resultado = cliente.ChangeName("X", Agora.AddDays(1));

        // Falha não deixa estado pela metade: nem o nome mudou, nem a data de atualização foi tocada.
        resultado.IsFailure.Should().BeTrue();
        cliente.Name.Should().Be(original);
        cliente.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void ChangeEmail_Altera()
    {
        Customer cliente = ClienteValido();
        Email novo = Email.Of("joao.novo@example.com").Value;
        DateTimeOffset depois = Agora.AddDays(1);

        Result resultado = cliente.ChangeEmail(novo, depois);

        resultado.IsSuccess.Should().BeTrue();
        cliente.Email.Should().Be(novo);
        cliente.UpdatedAt.Should().Be(depois);
    }

    [Fact]
    public void Delete_MarcaComoExcluidoSemApagar()
    {
        Customer cliente = ClienteValido();
        DateTimeOffset depois = Agora.AddDays(1);

        Result resultado = cliente.Delete(depois);

        // Exclusão lógica: pedido antigo referencia o cliente, e apagar a linha deixaria o histórico
        // apontando para o vazio.
        resultado.IsSuccess.Should().BeTrue();
        cliente.IsDeleted.Should().BeTrue();
        cliente.DeletedAt.Should().Be(depois);
        cliente.Name.Should().NotBeEmpty("os dados continuam lá, só marcados como excluídos");
    }

    [Fact]
    public void Delete_DuasVezes_RetornaFalhaNaSegunda()
    {
        Customer cliente = ClienteValido();
        cliente.Delete(Agora);

        Result segunda = cliente.Delete(Agora);

        segunda.IsFailure.Should().BeTrue();
        segunda.Error.Should().Be(DomainErrors.Customer.JaExcluido());
    }

    [Fact]
    public void Clientes_ComMesmaIdentidade_SaoIguais()
    {
        // Herdado de Entity<TId>: o teste confirma que Customer participa da igualdade por identidade.
        Customer cliente = ClienteValido();

        cliente.Should().Be(cliente);
        cliente.Should().NotBe(ClienteValido());
    }

    private static Customer ClienteValido() =>
        Customer.Register("João da Silva", EmailValido(), DocumentoValido(), Agora).Value;
}
