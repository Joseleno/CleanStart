using System.Reflection;
using CleanStart.Domain.Common;

namespace CleanStart.ArchitectureTests;

/// <summary>
/// Regras sobre como o domínio é escrito — não sobre quem depende de quem.
/// </summary>
/// <remarks>
/// Usa reflexão direta em vez do NetArchTest porque a pergunta é sobre o <b>membro</b> ("esta propriedade tem
/// setter público?"), e a API do NetArchTest opera sobre tipos. Forçá-la aqui renderia um predicado menos
/// legível que o <c>foreach</c> explícito.
/// </remarks>
public sealed class RegrasDeDominioTests
{
    private static readonly Assembly Domain = typeof(CleanStart.Domain.AssemblyMarker).Assembly;

    /// <summary>
    /// Entidade não expõe setter público — regra 7 da T0.3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setter público transforma a entidade em saco de dados: qualquer chamador passa a poder mudar estado
    /// sem passar pela regra que o governa, e a invariante deixa de ter guardião. O estado muda por método
    /// de domínio, com nome que diz o que aconteceu no negócio.
    /// </para>
    /// <para>
    /// <c>private set</c> e <c>init</c> são permitidos: o primeiro é como o próprio método de domínio
    /// atribui, e o segundo só atua na construção. O que a regra proíbe é o setter <b>acessível de fora</b>.
    /// </para>
    /// </remarks>
    [Fact]
    public void Entidades_NaoExpoemSetterPublico()
    {
        List<Type> entidades = [.. Domain.GetTypes().Where(EhEntidade)];

        // Guarda contra o teste virar vacuidade: se o filtro deixar de encontrar entidades — namespace
        // renomeado, base trocada — ele passaria sem verificar nada e ninguém perceberia.
        entidades.Should().NotBeEmpty("o teste precisa ter entidades para inspecionar");

        List<string> violacoes = [];

        foreach (Type entidade in entidades)
        {
            IEnumerable<PropertyInfo> propriedades = entidade.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (PropertyInfo propriedade in propriedades)
            {
                MethodInfo? setter = propriedade.SetMethod;

                if (setter is { IsPublic: true } && !EhInitOnly(setter))
                {
                    violacoes.Add($"{entidade.Name}.{propriedade.Name}");
                }
            }
        }

        violacoes.Should().BeEmpty(
            "entidade com setter público perde o controle das próprias invariantes — o estado deve mudar "
            + "por método de domínio. Propriedades violadoras: " + string.Join(", ", violacoes));
    }

    /// <summary>
    /// Toda raiz de agregado expõe os eventos como coleção somente leitura.
    /// </summary>
    [Fact]
    public void RaizesDeAgregado_ExpoemColecoesSomenteLeitura()
    {
        List<Type> raizes = [.. Domain.GetTypes().Where(EhRaizDeAgregado)];

        raizes.Should().NotBeEmpty("o teste precisa ter raízes de agregado para inspecionar");

        List<string> violacoes = [];

        foreach (Type raiz in raizes)
        {
            IEnumerable<PropertyInfo> colecoes = raiz
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(propriedade => EhColecaoMutavel(propriedade.PropertyType));

            violacoes.AddRange(colecoes.Select(propriedade => $"{raiz.Name}.{propriedade.Name}"));
        }

        violacoes.Should().BeEmpty(
            "expor List ou ICollection deixa qualquer chamador inserir item sem passar pela validação do "
            + "agregado. Use IReadOnlyCollection. Propriedades violadoras: " + string.Join(", ", violacoes));
    }

    private static bool EhEntidade(Type tipo) =>
        !tipo.IsAbstract
        && !tipo.IsInterface
        && HerdaDeGenerico(tipo, typeof(Entity<>));

    private static bool EhRaizDeAgregado(Type tipo) =>
        !tipo.IsAbstract
        && !tipo.IsInterface
        && HerdaDeGenerico(tipo, typeof(AggregateRoot<>));

    private static bool HerdaDeGenerico(Type tipo, Type generico)
    {
        for (Type? atual = tipo.BaseType; atual is not null; atual = atual.BaseType)
        {
            if (atual.IsGenericType && atual.GetGenericTypeDefinition() == generico)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Propriedade com <c>init</c> compila como setter público marcado com <c>IsExternalInit</c>.
    /// </summary>
    private static bool EhInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modificador => modificador.Name == "IsExternalInit");

    private static bool EhColecaoMutavel(Type tipo)
    {
        if (!tipo.IsGenericType)
        {
            return false;
        }

        Type definicao = tipo.GetGenericTypeDefinition();

        return definicao == typeof(List<>)
            || definicao == typeof(ICollection<>)
            || definicao == typeof(IList<>)
            || definicao == typeof(HashSet<>);
    }
}
