# 0010 — Identidade tipada e value objects

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

`Guid` e `decimal` são tipos honestos e péssimos modeladores de domínio, porque todos os `Guid` são
intercambiáveis para o compilador:

```csharp
await servico.Transferir(pedidoId, clienteId);   // compila
await servico.Transferir(clienteId, pedidoId);   // também compila
```

O segundo está errado e ninguém avisa. O mesmo vale para `decimal`: somar um valor em reais com um em dólares
produz um número — e o número está errado, sem que nada falhe.

Esta é a classe de defeito que não estoura. Ela devolve resposta plausível, e o erro aparece na conciliação,
semanas depois.

## Decisão

**Toda entidade tem identidade tipada** (`OrderId`, `CustomerId`, `ProductId`) — nunca `Guid` cru em
assinatura. **Todo valor monetário é `Money`** — nunca `decimal` solto. Documento é `Document`, e-mail é
`Email`.

Com isso, trocar a ordem dos argumentos deixa de compilar, e somar moedas diferentes retorna **falha, não
exception nem número errado**.

Três decisões de desenho que valem registro, porque foram tomadas antes de escrever código:

**`ValueObject` compara por membros declarados, não por reflexão.** Cada value object implementa
`GetEqualityComponents()` listando os campos que contam. É mais verboso, e é o ponto: com reflexão, acrescentar
um campo muda a igualdade **em silêncio**, sem ninguém decidir — e `Money` é comparado em laço, onde reflexão
custa caro.

**`Money` guarda a moeda como `string` validada, não como enum.** Valida a *forma* (três letras, normalizadas
para maiúsculas), não a existência: manter a tabela ISO-4217 completa num kit de exemplo seria manutenção sem
propósito. Enum foi descartado porque obrigaria a mudar código para somar uma moeda nova.

**`Money` aceita valor negativo.** Estorno, desconto e saldo devedor são quantias negativas legítimas;
restringir aqui exigiria um segundo tipo para representá-las. Quem precisa de "só positivo" valida no seu
contexto — o preço de um item é problema do `OrderItem`, não do `Money`.

**`Document` calcula o dígito verificador de verdade**, detecta CPF/CNPJ pelo tamanho e rejeita sequências
repetidas (`111.111.111-11` passa no módulo 11 e não é documento). Guarda só os dígitos e formata na saída,
porque `529.982.247-25` e `52998224725` são o mesmo documento — guardar as duas formas produziria cadastro
duplicado. Validar só o formato foi descartado: num kit de referência, ensinaria a validação errada.

## Consequências

**O que se ganha.** Uma classe inteira de defeito deixa de compilar, e a assinatura passa a documentar o que
aceita. `Order.Place(CustomerId, ...)` não precisa de comentário dizendo qual `Guid` vai ali.

**O que custa.** Conversão nas fronteiras. O DTO recebe `Guid`, o endpoint converte, a resposta desconverte — e
o mapeamento precisa de um conversor de valor por tipo.

> ⚠️ **A armadilha do conversor de valor, que este projeto encontrou três vezes:** dentro de uma expressão LINQ,
> `order.Id.Value` **compila e não traduz** para SQL. A regra que ficou: comparar o value object inteiro
> (`order.Id == id`), nunca navegar até a propriedade interna. O sintoma é uma exception de tradução em tempo de
> execução, e é fácil de reintroduzir.

**Uma identidade que ordena é útil por si só.** O `OrderId` ganhou `IComparable` porque a ordenação por
`Id.Value` não traduzia — e, com UUID v7, a ordem da identidade é a ordem de criação.

**Serialização fica aninhada.** Uma identidade tipada vira `{"value": "..."}` no JSON, e não uma string. Os DTOs
expõem `Guid` para não levar essa forma ao contrato público; o outbox convive com ela, porque a mensagem é
interna.

**Há verbosidade que não se paga em todo projeto.** Num CRUD de cadastro simples, `Guid` cru e `decimal`
resolvem. O custo se justifica quando existem várias identidades circulando juntas — que é exatamente quando
trocá-las passa a ser fácil.
