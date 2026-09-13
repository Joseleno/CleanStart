using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using CleanStart.Application.Common.Messaging;
using CleanStart.Domain.Common;

namespace CleanStart.ArchitectureTests;

/// <summary>
/// Regras sobre como a Application escreve commands, queries e handlers, e sobre como ela usa o Mediator.
/// </summary>
/// <remarks>
/// As regras 5 e 6 da T0.3 (handler <c>sealed</c>, command/query <c>record</c>) entraram aqui na T2.3, com o
/// slice <c>PlaceOrder</c> — antes dele não havia handler nem command para inspecionar, e regra que varre zero
/// tipos passa por vacuidade. Mesmo critério que levou a regra 7 da T0.3 para a T1.3. Ver decisão 17 do HANDOFF.
/// <para>
/// Com elas, **as sete regras da T0.3 estão todas implementadas.**
/// </para>
/// </remarks>
public sealed class RegrasDeMensageriaTests
{
    private static readonly Assembly Application = typeof(CleanStart.Application.AssemblyMarker).Assembly;

    /// <summary>
    /// Os dois namespaces onde o acoplamento ao Mediator é permitido.
    /// </summary>
    /// <remarks>
    /// <c>Common/Messaging</c> são os marcadores, que envolvem o Mediator por definição.
    /// <para>
    /// <c>Common/Behaviors</c> é exceção consciente: um behavior **é** infraestrutura de pipeline — existe
    /// porque o Mediator tem pipeline, e implementa `IPipelineBehavior` com `MessageHandlerDelegate` na
    /// assinatura. Envolvê-lo numa abstração própria criaria indireção cujo único propósito seria satisfazer
    /// esta regra. O que a regra protege é o custo de migrar: os behaviors são poucos, ficam numa pasta só, e
    /// a conta de ajustá-los numa troca de versão é conhecida. Um handler vazando é que seria problema, porque
    /// handler se multiplica com cada caso de uso.
    /// </para>
    /// </remarks>
    private static readonly string[] NamespacesComAcoplamentoPermitido =
    [
        "CleanStart.Application.Common.Messaging",
        "CleanStart.Application.Common.Behaviors",
    ];

    /// <summary>
    /// Handlers são <c>sealed</c> — regra 5 da T0.3.
    /// </summary>
    /// <remarks>
    /// Handler é ponto final de orquestração, não ponto de extensão: herdar de um para "reaproveitar" acopla
    /// casos de uso que deveriam ser independentes, e o que se compartilha por herança logo vira o lugar onde
    /// alguém põe uma regra que vale para dois handlers e não para o terceiro. Lógica comum vira serviço
    /// injetado.
    /// <para>
    /// Esperou da T0.3 até aqui porque não havia handler para inspecionar — ver decisão 17.
    /// </para>
    /// </remarks>
    [Fact]
    public void Handlers_SaoSealed()
    {
        List<Type> handlers = [.. Application.GetTypes().Where(EhHandler)];

        handlers.Should().NotBeEmpty("o teste precisa de handlers para inspecionar");

        List<string> violacoes = [.. handlers
            .Where(handler => !handler.IsSealed)
            .Select(handler => handler.FullName ?? handler.Name)];

        violacoes.Should().BeEmpty(
            "handler é ponto final de orquestração, não ponto de extensão — lógica compartilhada vira serviço "
            + "injetado, não classe base. Tipos violadores: " + string.Join(", ", violacoes));
    }

    /// <summary>
    /// Commands e queries são <c>record</c> — regra 6 da T0.3.
    /// </summary>
    /// <remarks>
    /// Mensagem é dado em trânsito: imutável, comparada por valor, sem comportamento — e <c>record</c> entrega
    /// as três coisas. Com <c>class</c> de setters, um behavior do pipeline poderia alterar a mensagem no meio
    /// do caminho, e o handler receberia algo diferente do que foi enviado.
    /// </remarks>
    [Fact]
    public void CommandsEQueries_SaoRecord()
    {
        List<Type> mensagens = [.. Application.GetTypes().Where(EhMensagem)];

        mensagens.Should().NotBeEmpty("o teste precisa de mensagens para inspecionar");

        List<string> violacoes = [.. mensagens
            .Where(mensagem => !EhRecord(mensagem))
            .Select(mensagem => mensagem.FullName ?? mensagem.Name)];

        violacoes.Should().BeEmpty(
            "command e query são dado em trânsito: imutável e comparado por valor. Tipos violadores: "
            + string.Join(", ", violacoes));
    }

    /// <summary>
    /// Fora de <c>Common/Messaging</c> e <c>Common/Behaviors</c>, nada na Application menciona o namespace
    /// <c>Mediator</c>.
    /// </summary>
    /// <remarks>
    /// É o critério de aceite da T2.1 em forma de teste. Os marcadores próprios só valem a indireção se o
    /// acoplamento ficar de fato contido: um único handler que implemente <c>Mediator.ICommandHandler</c>
    /// direto transforma a migração do 3.0.2 para o 3.1 numa varredura pela Application inteira — exatamente
    /// o que a decisão 2 do HANDOFF quer evitar.
    /// <para>
    /// Verifica a assinatura dos tipos (interfaces implementadas, bases, parâmetros e retornos de métodos
    /// públicos), que é onde o vazamento importa.
    /// </para>
    /// </remarks>
    [Fact]
    public void ForaDosMarcadores_NadaReferenciaOMediatorDiretamente()
    {
        List<string> violacoes = [];

        // Só classes escritas à mão.
        //
        // Interface fora dos marcadores também não deveria tocar o Mediator, mas `GetInterfaceMap` não aceita
        // interface como alvo, e o vazamento que importa é o de código executável: handler, behavior, serviço.
        //
        // O source generator do Mediator emite neste assembly a implementação do pipeline — `Mediator`,
        // `RequestHandlerWrapper`, `ContainerProbe` —, e ela referencia o namespace dele em toda assinatura,
        // porque é exatamente o trabalho dela. Incluí-la faria a regra acusar 40 violações que ninguém escreveu
        // e ninguém pode corrigir. O que a regra governa é o código do autor.
        IEnumerable<Type> tiposForaDosMarcadores = Application
            .GetTypes()
            .Where(tipo => !NamespacesComAcoplamentoPermitido.Contains(tipo.Namespace))
            .Where(tipo => tipo is { IsClass: true })
            .Where(tipo => !EhGeradoPorFerramenta(tipo));

        foreach (Type tipo in tiposForaDosMarcadores)
        {
            foreach (Type implementada in tipo.GetInterfaces().Where(EhDoMediator))
            {
                // Herdar indiretamente é esperado: nossos marcadores derivam do Mediator, então todo command
                // carrega Mediator.IBaseCommand na árvore. O que a regra proíbe é o tipo *declarar* a
                // interface do Mediator, em vez de declarar a nossa.
                if (!DeclaraDiretamente(tipo, implementada))
                {
                    continue;
                }

                violacoes.Add($"{tipo.Name} implementa {implementada.Name} diretamente");
            }

            IEnumerable<MethodInfo> metodos = tipo.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (MethodInfo metodo in metodos)
            {
                if (EhDoMediator(metodo.ReturnType))
                {
                    violacoes.Add($"{tipo.Name}.{metodo.Name} retorna {metodo.ReturnType.Name}");
                }

                violacoes.AddRange(metodo
                    .GetParameters()
                    .Where(parametro => EhDoMediator(parametro.ParameterType))
                    .Select(parametro =>
                        $"{tipo.Name}.{metodo.Name} recebe {parametro.ParameterType.Name}"));
            }
        }

        violacoes.Should().BeEmpty(
            "o acoplamento ao Mediator fica contido em Common/Messaging, para que subir de versão seja "
            + "mudança de uma camada. Violações: " + string.Join(" | ", violacoes));
    }

    private static bool EhHandler(Type tipo) =>
        tipo is { IsClass: true, IsAbstract: false }
        && !EhGeradoPorFerramenta(tipo)
        && (tipo.GetInterfaces().Any(EhInterfaceDeHandlerPropria) || EhReacaoAEvento(tipo));

    /// <summary>
    /// Reação a domain event — reconhecida por assinatura, não por interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quem reage a evento no outbox não implementa marcador nenhum: o despacho não passa pelo Mediator
    /// (decisão 22), então não há <c>INotificationHandler</c> para varrer. Sem esta cláusula, a regra do
    /// <c>sealed</c> simplesmente não alcançaria essa família de tipos — ficaria verde sem inspecionar nada,
    /// que é exatamente o modo de falha que a decisão 17 existe para evitar.
    /// </para>
    /// <para>
    /// O critério é o formato: um método <c>HandleAsync</c> cujo primeiro parâmetro é um
    /// <c>IDomainEvent</c>. Reconhecer pela assinatura em vez de pelo nome da classe evita que renomear o tipo
    /// tire-o da vigilância sem que ninguém perceba.
    /// </para>
    /// </remarks>
    private static bool EhReacaoAEvento(Type tipo) =>
        tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(metodo => metodo.Name == "HandleAsync")
            .Select(metodo => metodo.GetParameters())
            .Any(parametros =>
                parametros.Length > 0 && typeof(IDomainEvent).IsAssignableFrom(parametros[0].ParameterType));

    private static bool EhInterfaceDeHandlerPropria(Type interfaceImplementada)
    {
        if (!interfaceImplementada.IsGenericType)
        {
            return false;
        }

        Type definicao = interfaceImplementada.GetGenericTypeDefinition();

        return definicao == typeof(ICommandHandler<,>)
            || definicao == typeof(ICommandHandler<>)
            || definicao == typeof(IQueryHandler<,>);
    }

    private static bool EhMensagem(Type tipo) =>
        tipo is { IsClass: true, IsAbstract: false }
        && !EhGeradoPorFerramenta(tipo)
        && tipo.GetInterfaces().Any(EhInterfaceDeMensagemPropria);

    private static bool EhInterfaceDeMensagemPropria(Type interfaceImplementada)
    {
        if (interfaceImplementada == typeof(ICommand))
        {
            return true;
        }

        if (!interfaceImplementada.IsGenericType)
        {
            return false;
        }

        Type definicao = interfaceImplementada.GetGenericTypeDefinition();

        return definicao == typeof(ICommand<>) || definicao == typeof(IQuery<>);
    }

    /// <summary>
    /// <c>record</c> não tem marca própria em metadados: o sinal confiável é o método sintetizado
    /// <c>&lt;Clone&gt;$</c>, que o compilador gera para todo record e para nada mais.
    /// </summary>
    private static bool EhRecord(Type tipo) =>
        tipo.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

    /// <summary>
    /// Distingue código emitido por source generator do código escrito à mão.
    /// </summary>
    /// <remarks>
    /// O compilador marca o tipo gerado com <c>CompilerGeneratedAttribute</c> ou <c>GeneratedCodeAttribute</c>.
    /// O namespace do Mediator dentro deste assembly é o pipeline que ele gera — não é autoria nossa.
    /// </remarks>
    private static bool EhGeradoPorFerramenta(Type tipo) =>
        tipo.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || tipo.IsDefined(typeof(GeneratedCodeAttribute), inherit: false)
        || tipo.Namespace?.StartsWith("Mediator", StringComparison.Ordinal) == true;

    private static bool EhDoMediator(Type tipo) =>
        tipo.Namespace?.StartsWith("Mediator", StringComparison.Ordinal) == true;

    /// <summary>
    /// Distingue "declara esta interface" de "herda por causa de outra".
    /// </summary>
    private static bool DeclaraDiretamente(Type tipo, Type interfaceProcurada)
    {
        InterfaceMapping mapa = tipo.GetInterfaceMap(interfaceProcurada);

        // Se a interface vem por herança das nossas, alguma outra interface implementada pelo tipo também a
        // carrega. Declaração direta é quando nenhuma outra interface do tipo a traz.
        return !tipo
            .GetInterfaces()
            .Where(outra => outra != interfaceProcurada)
            .Any(outra => interfaceProcurada.IsAssignableFrom(outra))
            && mapa.TargetType == tipo;
    }
}
