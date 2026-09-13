using CleanStart.Domain.Common;
using CleanStart.Domain.Orders.Events;

namespace CleanStart.Infrastructure.Persistence.Outbox;

/// <summary>
/// O mapa entre cada domain event e o nome curto pelo qual ele trafega na tabela de outbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que um nome curto e não o nome do tipo.</b> Gravar <c>evento.GetType().FullName</c> parece gratuito e
/// custa caro depois: o identificador da mensagem passa a ser o nome da classe em C#, e aí renomear a classe —
/// ou mover o namespace — quebra toda mensagem já gravada que ninguém despachou ainda. E versionar um evento
/// obriga a escolher entre carregar um <c>OrderPlacedEventV2</c> para sempre ou quebrar o que está na fila.
/// Com o nome curto, os dois eixos se separam: a classe é livre para ser renomeada, e uma versão nova entra
/// como <c>"order-placed-v2"</c> enquanto <c>"order-placed"</c> continua resolvendo o que já está gravado.
/// </para>
/// <para>
/// <b>Por que um mapa explícito e não um atributo no evento.</b> Um <c>[OutboxEvent("order-placed")]</c> sobre
/// <c>OrderPlacedEvent</c> seria mais curto, e faria o domínio saber que existe um outbox — exatamente o que o
/// projeto diz não fazer: o domínio levanta <c>IDomainEvent</c> e não sabe que existe fila. O mecanismo de
/// entrega é assunto desta camada, e é aqui que ele fica.
/// </para>
/// <para>
/// <b>Por que explícito e não varredura do assembly.</b> Mesma razão pela qual os validators são registrados um
/// a um: uma linha por evento novo, em troca de se saber exatamente quais existem e como se chamam no fio. Uma
/// varredura acertaria sozinha e tiraria de quem lê a resposta para "que eventos este sistema publica?".
/// </para>
/// <para>
/// <b>Ao acrescentar um evento novo</b>, acrescente uma linha em <see cref="PorTipo"/>. Esquecer disso é erro de
/// escrita, não de leitura: o evento é levantado, ninguém o registra, e a gravação falha na hora — nunca em
/// silêncio. Há teste de arquitetura exigindo que todo <see cref="IDomainEvent"/> esteja aqui.
/// </para>
/// </remarks>
internal static class OutboxEventTypes
{
    /// <summary>Do tipo do evento para o nome curto — usado ao gravar a mensagem.</summary>
    private static readonly Dictionary<Type, string> PorTipo = new()
    {
        [typeof(OrderPlacedEvent)] = "order-placed",
        [typeof(OrderCancelledEvent)] = "order-cancelled",
    };

    /// <summary>Do nome curto para o tipo do evento — usado ao despachar.</summary>
    /// <remarks>
    /// Derivado do mapa acima em vez de escrito à mão: duas listas manuais divergem por um caractere, e o
    /// sintoma seria uma mensagem que se grava e nunca se consegue ler de volta.
    /// </remarks>
    private static readonly Dictionary<string, Type> PorNome =
        PorTipo.ToDictionary(par => par.Value, par => par.Key, StringComparer.Ordinal);

    /// <summary>Todos os eventos registrados, para o teste de arquitetura conferir que nenhum ficou de fora.</summary>
    internal static IReadOnlyCollection<Type> Registrados => PorTipo.Keys;

    /// <summary>O nome curto de um evento.</summary>
    /// <exception cref="InvalidOperationException">
    /// Se o evento não estiver registrado. Falhar aqui é deliberado: a alternativa seria gravar uma mensagem que
    /// o despachante nunca conseguiria interpretar, e descobrir isso só quando ela não fosse entregue.
    /// </exception>
    internal static string NomeDe(Type tipoDoEvento)
    {
        ArgumentNullException.ThrowIfNull(tipoDoEvento);

        return PorTipo.TryGetValue(tipoDoEvento, out string? nome)
            ? nome
            : throw new InvalidOperationException(
                $"O evento '{tipoDoEvento.Name}' não está registrado em {nameof(OutboxEventTypes)}. " +
                "Acrescente uma linha no mapa para que ele possa ser despachado.");
    }

    /// <summary>O tipo correspondente a um nome curto, ou <c>null</c> se o nome for desconhecido.</summary>
    /// <remarks>
    /// Devolve <c>null</c> em vez de lançar, ao contrário de <see cref="NomeDe"/>, porque os dois lados falham
    /// por motivos diferentes: na escrita, o nome desconhecido é erro de programação e deve interromper; na
    /// leitura, é uma mensagem antiga de um evento que já não existe no código — e derrubar o despachante por
    /// causa dela travaria a fila inteira atrás de uma linha órfã.
    /// </remarks>
    internal static Type? TipoDe(string nome) => PorNome.GetValueOrDefault(nome);
}
