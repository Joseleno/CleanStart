using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using NetArchTest.Rules;

// `TestResult` existe no NetArchTest e no Xunit. O alias deixa explícito qual é qual.
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace CleanStart.ArchitectureTests;

/// <summary>
/// Regras sobre a camada Api — o que um endpoint pode e não pode fazer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Estas regras fecham um ponto cego.</b> O <c>.csproj</c> deste projeto referencia
/// <c>CleanStart.Api</c> desde a T0.3, mas nenhum teste inspecionava aquele assembly: toda regra que a Api
/// podia violar estava no honor system. Como o próprio kit argumenta, regra que vive só em documento não é
/// regra — e a prova é que duas vazaram sem que nada acusasse.
/// </para>
/// <para>
/// O critério do que entra aqui é custo-benefício: regra que um teste consegue afirmar sem ambiguidade e cuja
/// violação é plausível. "Endpoint não tem regra de negócio" é verdadeiro e importante, mas não é testável —
/// nenhuma assinatura distingue orquestração de decisão. Fica para a revisão humana, e é o que a documentação
/// da camada existe para dizer.
/// </para>
/// </remarks>
public sealed class RegrasDaApiTests
{
    private static readonly Assembly Api = typeof(CleanStart.Api.AssemblyMarker).Assembly;

    private const string NamespaceEfCore = "Microsoft.EntityFrameworkCore";
    private const string NamespaceMediator = "Mediator";

    /// <summary>
    /// Os módulos Carter, onde o acoplamento ao <c>ISender</c> é permitido.
    /// </summary>
    /// <remarks>
    /// <b>Exceção consciente, pelo mesmo critério de <c>Common/Behaviors</c>.</b> O endpoint existe para
    /// converter HTTP em mensagem e devolver a resposta: enviar pelo <c>ISender</c> <i>é</i> o trabalho dele.
    /// Envolvê-lo num marcador próprio da Api criaria uma indireção cujo único propósito seria satisfazer esta
    /// regra — e o custo de migrar continua conhecido, porque os módulos são poucos e ficam numa pasta só.
    /// <para>
    /// O que a regra protege é o resto da Api: um middleware, um serviço ou um filtro que passe a depender do
    /// Mediator espalha o acoplamento para onde ele não é inerente.
    /// </para>
    /// </remarks>
    private static readonly string[] NamespacesComAcoplamentoAoMediatorPermitido =
    [
        "CleanStart.Api.Modules",
    ];

    /// <summary>
    /// A Api não conhece o EF Core — nem <c>DbContext</c>, nem <c>IQueryable</c> dele.
    /// </summary>
    /// <remarks>
    /// Endpoint que alcança o <c>DbContext</c> pula a Application inteira: a validação do pipeline, a
    /// transação do <c>TransactionBehavior</c> e a invalidação de cache deixam de acontecer, sem que nada
    /// falhe. O resultado aparece semanas depois como inconsistência sem erro no log.
    /// </remarks>
    [Fact]
    public void Api_NaoReferenciaEfCore()
    {
        ArchTestResult resultado = Types.InAssembly(Api)
            .Should()
            .NotHaveDependencyOn(NamespaceEfCore)
            .GetResult();

        resultado.Should().NaoTerViolacao(
            "o endpoint envia uma mensagem e traduz o Result; persistência é da Infrastructure, e o caminho "
            + "até ela passa pela Application");
    }

    /// <summary>
    /// A Api não alcança repositório direto.
    /// </summary>
    /// <remarks>
    /// Complementa a regra do EF Core pelo outro lado: o repositório é abstração do Domain, então depender
    /// dele não acusa dependência de framework nenhum — mas fura a Application do mesmo jeito.
    /// </remarks>
    [Fact]
    public void Api_NaoDependeDeRepositorio()
    {
        List<string> violacoes = [];

        foreach (Type tipo in TiposEscritosAMao(Api))
        {
            IEnumerable<Type> dependencias = tipo
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                    | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(metodo => metodo.GetParameters().Select(parametro => parametro.ParameterType))
                .Concat(tipo
                    .GetConstructors()
                    .SelectMany(construtor =>
                        construtor.GetParameters().Select(parametro => parametro.ParameterType)));

            violacoes.AddRange(dependencias
                .Where(EhRepositorio)
                .Select(dependencia => $"{tipo.Name} recebe {dependencia.Name}"));
        }

        violacoes.Should().BeEmpty(
            "endpoint que carrega o agregado sozinho decide o que é caso de uso — e a regra que deveria estar "
            + "num handler passa a viver no HTTP. Violações: " + string.Join(" | ", violacoes));
    }

    /// <summary>
    /// Fora dos módulos Carter, nada na Api referencia o Mediator diretamente.
    /// </summary>
    /// <remarks>
    /// É a mesma regra que a <c>RegrasDeMensageriaTests</c> aplica à Application, apontada ao assembly da Api.
    /// A diferença é a lista de exceções: lá são os marcadores e os behaviors; aqui são os módulos, onde
    /// enviar mensagem é o próprio trabalho.
    /// <para>
    /// Varre métodos <b>não públicos</b> também, ao contrário da regra da Application: os handlers de endpoint
    /// são <c>private static</c>, e uma regra que só olhasse o que é público passaria por vacuidade — o pior
    /// modo de falha para um teste de arquitetura, porque o verde afirma o que ninguém verificou.
    /// </para>
    /// </remarks>
    [Fact]
    public void ForaDosModulos_NadaNaApiReferenciaOMediatorDiretamente()
    {
        List<string> violacoes = [];

        IEnumerable<Type> tiposForaDosModulos = TiposEscritosAMao(Api)
            .Where(tipo => !NamespacesComAcoplamentoAoMediatorPermitido.Contains(tipo.Namespace));

        foreach (Type tipo in tiposForaDosModulos)
        {
            violacoes.AddRange(tipo
                .GetInterfaces()
                .Where(EhDoMediator)
                .Select(implementada => $"{tipo.Name} implementa {implementada.Name}"));

            IEnumerable<MethodInfo> metodos = tipo.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);

            foreach (MethodInfo metodo in metodos)
            {
                if (EhDoMediator(metodo.ReturnType))
                {
                    violacoes.Add($"{tipo.Name}.{metodo.Name} retorna {metodo.ReturnType.Name}");
                }

                violacoes.AddRange(metodo
                    .GetParameters()
                    .Where(parametro => EhDoMediator(parametro.ParameterType))
                    .Select(parametro => $"{tipo.Name}.{metodo.Name} recebe {parametro.ParameterType.Name}"));
            }
        }

        violacoes.Should().BeEmpty(
            "o acoplamento ao Mediator fica contido nos módulos Carter, onde enviar mensagem é o trabalho do "
            + "endpoint. Violações: " + string.Join(" | ", violacoes));
    }

    /// <summary>
    /// Nenhum <c>async void</c> em lugar nenhum da solução.
    /// </summary>
    /// <remarks>
    /// <c>async void</c> não tem como ser aguardado: a exceção que escapa dele não sobe pela pilha de quem
    /// chamou, vai para o contexto de sincronização e derruba o processo. Num endpoint, o cliente recebe uma
    /// resposta que não corresponde ao que aconteceu.
    /// <para>
    /// A regra varre as quatro camadas porque a proibição não é da Api — está no <c>CLAUDE.md</c> para o
    /// projeto inteiro. Fica neste arquivo por ser onde a inspeção por assinatura já mora.
    /// </para>
    /// </remarks>
    [Fact]
    public void NenhumMetodoEhAsyncVoid()
    {
        Assembly[] camadas =
        [
            typeof(CleanStart.Domain.AssemblyMarker).Assembly,
            typeof(CleanStart.Application.AssemblyMarker).Assembly,
            typeof(CleanStart.Infrastructure.AssemblyMarker).Assembly,
            Api,
        ];

        List<string> violacoes = [];

        foreach (Assembly camada in camadas)
        {
            foreach (Type tipo in TiposEscritosAMao(camada))
            {
                violacoes.AddRange(tipo
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                        | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(metodo => metodo.ReturnType == typeof(void)
                        && metodo.GetCustomAttribute<AsyncStateMachineAttribute>() is not null)
                    .Select(metodo => $"{camada.GetName().Name}: {tipo.Name}.{metodo.Name}"));
            }
        }

        violacoes.Should().BeEmpty(
            "exceção que escapa de async void não sobe para quem chamou — ela derruba o processo. "
            + "Violações: " + string.Join(" | ", violacoes));
    }

    /// <summary>
    /// Nenhum repositório devolve <c>IQueryable</c>.
    /// </summary>
    /// <remarks>
    /// Devolver <c>IQueryable</c> deixa a consulta aberta: quem chama passa a compor o SQL, e a fronteira entre
    /// caso de uso e persistência desaparece — junto com a garantia de que a query foi pensada. Pior, a
    /// execução acontece fora do escopo do repositório, onde o <c>DbContext</c> pode já ter sido descartado.
    /// </remarks>
    [Fact]
    public void NenhumRepositorioDevolveIQueryable()
    {
        Assembly[] camadas =
        [
            typeof(CleanStart.Domain.AssemblyMarker).Assembly,
            typeof(CleanStart.Application.AssemblyMarker).Assembly,
            typeof(CleanStart.Infrastructure.AssemblyMarker).Assembly,
        ];

        List<string> violacoes = [];

        foreach (Assembly camada in camadas)
        {
            IEnumerable<Type> repositorios = camada
                .GetTypes()
                .Where(tipo => !EhGeradoPorFerramenta(tipo))
                .Where(EhRepositorio);

            foreach (Type repositorio in repositorios)
            {
                violacoes.AddRange(repositorio
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(metodo => EhQueryable(metodo.ReturnType))
                    .Select(metodo => $"{repositorio.Name}.{metodo.Name}"));
            }
        }

        violacoes.Should().BeEmpty(
            "repositório devolve o agregado ou uma lista dele, nunca uma consulta por terminar. "
            + "Violações: " + string.Join(" | ", violacoes));
    }

    /// <summary>
    /// Toda <b>raiz de agregado</b> tem identidade tipada — nunca <c>Guid</c>, <c>int</c> ou <c>string</c> crus.
    /// </summary>
    /// <remarks>
    /// Identidade primitiva é a porta de entrada do bug mais silencioso que existe: passar um
    /// <c>CustomerId</c> onde se espera um <c>OrderId</c> compila, roda, e devolve "não encontrado" para
    /// sempre. Com identidade tipada, o compilador recusa.
    /// <para>
    /// <b>A regra cobre raiz de agregado, não toda entidade, e a distinção é o ponto.</b> O bug que ela
    /// previne exige que o id atravesse uma fronteira — assinatura de repositório, command, endpoint. A
    /// identidade de uma entidade interna não atravessa nenhuma: <c>OrderItem</c> é criado e alterado pelo
    /// <c>Order</c>, e o <c>Guid</c> dele não aparece em lugar nenhum onde pudesse ser trocado por outro.
    /// Exigir <c>OrderItemId</c> ali seria cerimônia — um tipo a mais para proteger de um erro que a
    /// visibilidade já impede.
    /// </para>
    /// <para>
    /// Esta é a regra 7 da T0.3 vista pelo outro lado — lá o alvo era a assinatura dos métodos de domínio;
    /// aqui é o parâmetro de tipo do próprio <c>Entity&lt;TId&gt;</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TodaRaizDeAgregadoTemIdentidadeTipada()
    {
        Assembly domain = typeof(CleanStart.Domain.AssemblyMarker).Assembly;

        List<string> violacoes = [];

        IEnumerable<Type> raizes = domain
            .GetTypes()
            .Where(tipo => tipo is { IsClass: true, IsAbstract: false })
            .Where(EhRaizDeAgregado);

        foreach (Type raiz in raizes)
        {
            Type? identidade = TipoDaIdentidadeDe(raiz);

            if (identidade is not null && EhPrimitivoDeIdentidade(identidade))
            {
                violacoes.Add($"{raiz.Name} usa {identidade.Name} como identidade");
            }
        }

        violacoes.Should().BeEmpty(
            "trocar OrderId por CustomerId numa chamada precisa ser erro de compilação, não bug em produção. "
            + "Violações: " + string.Join(" | ", violacoes));
    }

    /// <summary>
    /// Classes escritas à mão — exclui o que o compilador e os source generators emitem.
    /// </summary>
    /// <remarks>
    /// O que a regra governa é o código do autor. Incluir o gerado faria as regras acusarem violações que
    /// ninguém escreveu e ninguém pode corrigir — e o caminho de menor resistência para um build vermelho
    /// desses é afrouxar a regra, que é o oposto do que ela existe para fazer.
    /// </remarks>
    private static IEnumerable<Type> TiposEscritosAMao(Assembly assembly) => assembly
        .GetTypes()
        .Where(tipo => tipo is { IsClass: true })
        .Where(tipo => !EhGeradoPorFerramenta(tipo));

    private static bool EhGeradoPorFerramenta(Type tipo) =>
        tipo.GetCustomAttribute<GeneratedCodeAttribute>() is not null
        || tipo.GetCustomAttribute<CompilerGeneratedAttribute>() is not null
        || tipo.Name.Contains('<', StringComparison.Ordinal);

    private static bool EhDoMediator(Type tipo) =>
        tipo.Namespace?.StartsWith(NamespaceMediator, StringComparison.Ordinal) == true;

    /// <summary>
    /// Repositório reconhecido pelo nome — a convenção que o projeto segue.
    /// </summary>
    /// <remarks>
    /// Não há interface comum a herdar: <c>IOrderRepository</c> e <c>ICustomerRepository</c> são
    /// independentes de propósito, para que cada uma exponha só o que o seu agregado precisa. Sem tipo base,
    /// sobra a convenção de nome — que é frágil, mas explícita, e falha do lado seguro: um repositório com
    /// outro nome escapa da regra, mas nenhum tipo é acusado por engano.
    /// </remarks>
    private static bool EhRepositorio(Type tipo) =>
        tipo.Name.EndsWith("Repository", StringComparison.Ordinal);

    private static bool EhQueryable(Type tipo) =>
        tipo == typeof(IQueryable)
        || (tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(IQueryable<>));

    /// <summary>
    /// Raiz de agregado — herda de <c>AggregateRoot&lt;TId&gt;</c> em algum ponto da cadeia.
    /// </summary>
    private static bool EhRaizDeAgregado(Type tipo)
    {
        for (Type? atual = tipo; atual is not null; atual = atual.BaseType)
        {
            if (atual.IsGenericType
                && atual.GetGenericTypeDefinition() == typeof(CleanStart.Domain.Common.AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sobe a cadeia de herança até achar <c>Entity&lt;TId&gt;</c> e devolve o <c>TId</c>.
    /// </summary>
    private static Type? TipoDaIdentidadeDe(Type tipo)
    {
        for (Type? atual = tipo; atual is not null; atual = atual.BaseType)
        {
            if (atual.IsGenericType
                && atual.GetGenericTypeDefinition() == typeof(CleanStart.Domain.Common.Entity<>))
            {
                return atual.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static bool EhPrimitivoDeIdentidade(Type tipo) =>
        tipo == typeof(Guid) || tipo == typeof(string) || tipo.IsPrimitive;
}
