# 0005 — Vertical slices na Application

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

Há duas maneiras de organizar a camada de aplicação, e a escolha parece estética até o projeto crescer.

**Por papel técnico** — `Commands/`, `Handlers/`, `Validators/`, `Responses/` — é o que a maioria dos exemplos
mostra. O problema aparece no uso: acrescentar um caso de uso obriga a tocar quatro pastas distantes, e
entender um caso de uso existente obriga a abrir quatro arquivos em quatro lugares. Nenhuma pasta responde "o
que este sistema faz"; todas respondem "que tipo de arquivo é este", que é a pergunta menos útil.

Pior: como os arquivos de um caso de uso ficam longe uns dos outros, **remover funcionalidade fica difícil**.
Sempre sobra um validator órfão numa pasta que ninguém revisita.

## Decisão

**Uma pasta por caso de uso**, contendo tudo que ele precisa:

```
Application/Orders/
├── PlaceOrder/          PlaceOrderCommand, PlaceOrderHandler,
│                        PlaceOrderValidator, PlaceOrderResponse, OrderMapper
├── GetOrderById/        GetOrderByIdQuery, Handler, OrderResponse, IOrderReader
├── ListOrders/          ListOrdersQuery, Handler, OrdersPage, OrdersCursor, IOrdersPageReader
├── CancelOrder/         CancelOrderCommand, CancelOrderHandler
└── NotifyOrderPlaced/   OrderPlacedNotifier
```

Não existem pastas `Commands/`, `Handlers/` ou `Validators/`. O que fica em `Common/` é apenas o que **três ou
mais** slices compartilham: os marcadores de mensageria, as abstrações e os behaviors do pipeline.

**As pastas têm tamanhos diferentes, e isso é a favor.** `CancelOrder/` tem dois arquivos — não há response
porque o comando não devolve valor; `ListOrders/` tem cinco, incluindo o cursor de paginação. A estrutura
acompanha a necessidade do caso de uso em vez de impor um gabarito.

**Detalhe que revela a coesão:** `GetOrderById/` e `ListOrders/` declaram a própria interface de leitura
(`IOrderReader`, `IOrdersPageReader`) **dentro do slice**. Uma interface de leitura compartilhada cresceria até
virar o repositório genérico que o CQRS existe para evitar — a porta de saída nasce junto do caso de uso que a
usa, e morre com ele.

**A pasta tem o nome da reação, não do papel técnico**, mesmo para eventos: `NotifyOrderPlaced/`, nunca
`EventHandlers/`. Agrupar por papel é o caminho de volta à organização em camadas.

## Consequências

**O que se ganha.** Acrescentar um caso de uso é criar uma pasta; removê-lo é apagá-la, sem deixar órfãos. E a
listagem de `Orders/` responde o que o sistema faz — criar, buscar, listar, cancelar, notificar.

**O que custa.** Alguma repetição entre slices, e é deliberada: dois casos de uso parecidos evoluem em direções
diferentes, e a abstração criada cedo para eliminar a duplicação vira o acoplamento que impede os dois de
mudarem sozinhos. Duplicar é mais barato que desfazer uma abstração errada.

**Exige critério para o que vai em `Common/`.** A régua adotada — três ou mais consumidores — é arbitrária, e
sem ela `Common/` vira o depósito onde tudo acaba, recriando o agrupamento por papel com outro nome.

**Navegar por tipo fica pior.** Quem quiser ver "todos os validators" vai precisar de busca, não de uma pasta.
É a troca: otimiza-se para a pergunta frequente ("como funciona o cancelamento?") em detrimento da rara.
