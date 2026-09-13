# 0006 — Outbox para eventos de domínio

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

Gravar no banco e publicar numa fila são **dois sistemas**, e nenhuma transação cobre os dois. Daí saem duas
maneiras de errar, e não existe uma terceira via sem alguma forma de outbox:

- **Publicar antes do commit** anuncia um fato que a transação pode desfazer. O consumidor reage a um pedido
  que não existe.
- **Publicar depois do commit** perde o evento se o processo cair no meio. O pedido existe e ninguém foi
  avisado — e, pior, nada falhou: não há erro para investigar.

O segundo é o mais comum e o mais difícil de diagnosticar, porque o sintoma aparece dias depois, num relatório
que não bate.

## Decisão

**O evento é gravado na mesma transação do dado, numa tabela `outbox_messages`, e um processo separado o
publica.**

A gravação acontece no `DomainEventInterceptor`, que sobrescreve `SavingChanges` — **antes** do commit:

> Roda em `SavingChanges`, antes do commit, justamente para que os `INSERT` do outbox entrem na mesma unidade de
> trabalho que o dado. Em `SavedChanges` seria tarde: a transação já teria fechado.

Como o `INSERT` entra no mesmo `DbContext`, ele participa do mesmo `SaveChanges` — ou os dois acontecem, ou
nenhum.

O despachante (`OutboxProcessor` + `OutboxWorker`) reserva um lote, publica e registra o resultado.

### O despachante não usa o Mediator

**Isto contraria o plano original do projeto**, que dizia "publica os eventos pelo Mediator". A decisão foi
revertida depois de três análises sobre o código, e o motivo é o modo de falha.

Publicando in-process, no mesmo escopo e no mesmo `AppDbContext` da transação do lote, um handler de evento que
chame `SaveChangesAsync` grava **dentro** dessa transação. Basta uma mensagem posterior do lote falhar: o
rollback desfaz também o efeito colateral do handler que já tinha dado certo — **sem exception e sem log**. O
lote é reprocessado depois, o handler roda de novo, e meses adiante isso é indistinguível de um bug de negócio.

Há ainda um argumento de fundo: **outbox existe para atravessar fronteira de processo**. Despachar de volta
in-process desfaz a razão de a tabela existir.

O despacho passa por uma abstração própria, `IOutboxPublisher`. Trocar a implementação liga um broker real sem
mudar mais nada.

### Três decisões de operação

**Concorrência por `FOR UPDATE SKIP LOCKED`.** Sem coordenação, N instâncias leem o mesmo lote e publicam tudo
em duplicidade — não de vez em quando, mas em **toda iteração**. Descartados: lock no Redis (é *advisory com
TTL*: um GC longo e o dono perde o lock sem saber) e claim otimista com coluna de estado (worker que morre
trava as linhas para sempre e exige um processo de limpeza).

**Duas transações, com a publicação entre elas.** Com `EnableRetryOnFailure` ligado, a estratégia de execução
do EF Core **repete o delegate inteiro** numa falha transiente. Publicar lá dentro republicaria mensagens que
já haviam saído — até quatro vezes, silenciosamente. Então: transação 1 reserva o lote, a publicação acontece
fora de qualquer transação, transação 2 registra o resultado.

**`ProcessedOn` é marcado sempre depois de publicar.** Na ordem inversa, uma falha de publicação deixaria a
mensagem marcada como entregue sem nunca ter saído — perda silenciosa, que é o defeito que o padrão existe para
evitar.

## Consequências

**A entrega é at-least-once, não exactly-once.** Se o processo cair entre publicar e marcar, a mensagem sai de
novo. Não há como evitar sem uma transação que cubra banco e broker ao mesmo tempo — que é justamente o que não
existe. **Quem reage ao evento precisa ser idempotente**, e o kit demonstra isso: o `OrderPlacedNotifier`
verifica "já fiz isto?" antes de agir, com teste que entrega o mesmo evento duas vezes e exige um efeito só.

**A ordem não é garantida entre agregados.** Eventos de pedidos diferentes podem ser entregues fora da ordem em
que ocorreram.

**Há latência.** O evento sai no próximo ciclo do poller — segundos, por padrão. Quem precisa de tempo real
precisa de outra coisa.

**A tabela precisa de manutenção**, e ela está implementada: o worker apaga em lotes as mensagens processadas
mais antigas que uma janela configurável. Documentar o problema sem resolvê-lo ensinaria a documentar problemas
— quem copiasse a tabela herdaria o crescimento infinito.

**Dead-letter é a ausência de uma condição**, não uma estrutura nova: alcançado o máximo de tentativas, a
mensagem deixa de satisfazer o filtro da consulta e permanece na tabela com o erro da última tentativa. Em
volume real isso viraria uma tabela separada, para manter a tabela quente pequena.

**O tipo do evento é gravado como nome curto** (`"order-placed"`), de um mapa explícito, e não como nome da
classe. Com o nome da classe, renomeá-la quebraria toda mensagem pendente, e versionar um evento obrigaria a
carregar um `...EventV2` para sempre.

**Cobertura: 12 testes de integração** contra PostgreSQL real — processamento, retry com recuo, dead-letter,
mensagem em espera, tipo desconhecido, retenção, e um `EXPLAIN` que exige o uso do índice parcial. Este último
existe porque uma divergência no `WHERE` derruba a consulta para varredura sequencial **sem erro nenhum**: o
despachante continua correto e fica lento conforme a tabela cresce.
