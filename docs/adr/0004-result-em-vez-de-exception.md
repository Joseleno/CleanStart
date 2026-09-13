# 0004 — `Result` em vez de exception para erro de negócio

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

"Pedido já enviado não pode ser cancelado" é uma resposta do domínio, não uma falha do sistema. Modelar isso
como exception tem três problemas concretos:

1. **A assinatura mente.** `Task<Order> Cancel()` promete um pedido. Que ela possa lançar `DomainException` não
   está escrito em lugar nenhum — quem chama descobre lendo a implementação ou sendo surpreendido.
2. **O custo é desproporcional.** Exception carrega captura de stack trace, e um fluxo de validação que reprova
   com frequência paga esse preço a cada requisição.
3. **Erro de negócio e defeito viram a mesma coisa.** Um `catch (Exception)` que trata "moeda inválida" também
   engole `NullReferenceException`, e o defeito real passa despercebido.

## Decisão

**Erro de negócio retorna `Result` ou `Result<T>`. Exception fica reservada para falha de infraestrutura e
para bug.**

`Result`, `Result<T>` e `Error` vivem no **Domain** — em `Domain/Common` e `Domain/Errors` — porque é o domínio
quem os produz. Um tipo não pode morar numa camada acima de quem o cria. **Nunca existem dois `Result` na
solução**; isso é critério de aceite verificado por `grep`.

A forma:

```csharp
public static Result<Money> Of(decimal amount, string currency)
{
    if (string.IsNullOrWhiteSpace(currency))
        return Result.Failure<Money>(DomainErrors.General.TextoObrigatorio(nameof(currency)));

    string normalizada = currency.Trim().ToUpperInvariant();

    if (normalizada.Length != 3 || !normalizada.All(char.IsAsciiLetterUpper))
        return Result.Failure<Money>(DomainErrors.Money.MoedaInvalida(currency));

    return new Money(amount, normalizada);   // conversão implícita
}
```

O mesmo padrão em `Document.Of`, `Email.Of`, `Customer.Register` e `Order.Place`. São **46 usos de `Result` em
14 arquivos** da Application — praticamente todo caso de uso passa por ele.

**O `Error` carrega um `ErrorType`**, com quatro valores: `Validation`, `NotFound`, `Conflict`, `Failure`. É ele
que a Api traduz em status HTTP (400, 404, 409, 500), num lugar só — sem `if` de tradução espalhado pelos
endpoints.

**`Result` carrega um `Error` único, não uma coleção.** Agregar múltiplas falhas de validação é trabalho do
pipeline do FluentValidation, onde isso de fato acontece; com coleção, todo consumidor pagaria o preço de
iterar mesmo quando há um erro só.

## Consequências

**O que se ganha.** A assinatura passa a dizer a verdade: `Result<Order>` anuncia que pode falhar, e o
compilador obriga a decidir o que fazer. Erro de negócio e defeito deixam de se confundir — o que chega ao
`catch` genérico é, de fato, defeito.

**O que custa.** Verbosidade. Cada chamada exige verificar `IsFailure` e propagar, e um `Result` que entra numa
sequência de operações precisa ser encadeado à mão.

**O `Result` não atravessa serialização, e isso já custou caro.** O `System.Text.Json` exige que cada parâmetro
do construtor case com uma propriedade do próprio tipo; como `IsSuccess` e `Error` vivem na classe base, a
desserialização falha. A consequência prática foi arquitetural: cache de consulta guarda o **DTO**, nunca o
`Result` — a descoberta e as cinco alternativas descartadas estão registradas no histórico do projeto.

**`Unauthorized` e `Forbidden` não estão no `ErrorType`**, e é deliberado: 401 e 403 vêm do middleware de
autenticação, não de um handler. Acrescentá-los criaria valores que nada produz. Entram no dia em que houver
regra de propriedade — "este pedido não é seu" — que é erro de negócio de verdade.

> ⚠️ Ao estender o `ErrorType`, estenda **junto** a tradução em `ResultExtensions`. Ela tem um `_ => 500`
> deliberado, então um valor novo não mapeado vira "Erro interno" **com o build passando** — o `_` satisfaz a
> exaustividade e nenhum aviso aparece.
