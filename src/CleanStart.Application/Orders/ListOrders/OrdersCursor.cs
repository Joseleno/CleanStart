using System.Buffers.Text;
using System.Text;
using CleanStart.Domain.Common;
using CleanStart.Domain.Errors;

namespace CleanStart.Application.Orders.ListOrders;

/// <summary>
/// Posição de onde continuar a listagem.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que keyset e não offset — a razão que o plano pede documentada.</b>
/// </para>
/// <para>
/// Paginação por offset (<c>OFFSET 20 LIMIT 20</c>) tem dois defeitos que pioram com o tamanho da tabela:
/// </para>
/// <list type="number">
/// <item>
/// <b>Custo que cresce com a página.</b> O banco não pode pular linhas sem lê-las: para servir a página 500 ele
/// percorre e descarta 10 mil registros. A primeira página é instantânea e a milésima é um scan — e isso não se
/// resolve com índice.
/// </item>
/// <item>
/// <b>Itens repetidos e itens perdidos.</b> Se um pedido é criado enquanto o usuário navega, tudo desloca uma
/// posição: o último item da página 1 reaparece no topo da página 2, e um item que estava na fronteira some. O
/// resultado não é "ligeiramente desatualizado" — é uma lista que **omite registros** sem avisar.
/// </item>
/// </list>
/// <para>
/// Keyset resolve os dois perguntando <c>WHERE (created_at, id) &lt; (:ultimo_created_at, :ultimo_id)</c>: o
/// banco vai direto ao ponto pelo índice, o custo é o mesmo em qualquer página, e inserções não deslocam nada
/// porque a posição é o próprio dado, não uma contagem.
/// </para>
/// <para>
/// <b>O par <c>(CreatedAt, Id)</c> e não só a data.</b> Dois pedidos podem ser criados no mesmo instante — e são,
/// em importação em lote. Com ordenação só por data, a fronteira entre páginas fica ambígua e o item repetido
/// volta pela porta dos fundos. O <c>Id</c> desempata e torna a ordenação **estável**.
/// </para>
/// <para>
/// <b>O que se perde:</b> não dá para pular para a página 7 nem mostrar "de 140 a 160 de 3.219". Keyset serve
/// rolagem contínua e navegação sequencial; relatório que precisa saltar entre páginas usa offset e aceita o
/// custo — conscientemente.
/// </para>
/// </remarks>
/// <param name="CreatedAt">Data de criação do último item da página anterior.</param>
/// <param name="Id">Identidade do último item, para desempatar datas iguais.</param>
public sealed record OrdersCursor(DateTimeOffset CreatedAt, Guid Id)
{
    private const char Separador = '|';

    /// <summary>
    /// Codifica o cursor para trafegar na URL.
    /// </summary>
    /// <remarks>
    /// Base64 URL-safe não é segurança — é opacidade. O cliente não deve construir cursor à mão nem depender do
    /// formato, porque ele muda quando a ordenação muda. Codificar deixa isso explícito.
    /// </remarks>
    public string Codificar()
    {
        // Ticks UTC, não `ToUnixTimeMilliseconds()`: o PostgreSQL guarda timestamp com **microssegundos**, e
        // codificar em milissegundos trunca o valor. O cursor volta com uma data ligeiramente diferente da
        // gravada, a condição `CreatedAt == cursor` nunca casa, e o desempate por id jamais entra em ação —
        // pedidos criados no mesmo instante somem da paginação. Foi exatamente o que um teste pegou: cinco
        // pedidos criados no mesmo SaveChanges, e só dois apareciam.
        string bruto = $"{CreatedAt.UtcTicks}{Separador}{Id}";

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(bruto));
    }

    /// <summary>
    /// Decodifica um cursor recebido do cliente.
    /// </summary>
    /// <remarks>
    /// Devolve <c>Result</c> e não lança: cursor malformado é entrada do usuário — copiada errada, truncada, de
    /// uma versão anterior da API —, não bug do servidor. Vira 400, não 500.
    /// </remarks>
    public static Result<OrdersCursor> Decodificar(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return Result.Failure<OrdersCursor>(DomainErrors.General.TextoObrigatorio(nameof(cursor)));
        }

        try
        {
            string bruto = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            string[] partes = bruto.Split(Separador);

            if (partes.Length != 2
                || !long.TryParse(partes[0], out long ticks)
                || !Guid.TryParse(partes[1], out Guid id))
            {
                return Result.Failure<OrdersCursor>(DomainErrors.Order.CursorInvalido());
            }

            return new OrdersCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (FormatException)
        {
            // Base64 inválido. É a única exception que a decodificação lança por entrada ruim, e capturá-la aqui
            // evita que um cursor colado errado vire 500.
            return Result.Failure<OrdersCursor>(DomainErrors.Order.CursorInvalido());
        }
    }
}
