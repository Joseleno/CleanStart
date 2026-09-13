# 0009 — Abstrações próprias sobre o Mediator

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

O ADR 0002 fixou o **Mediator 3.0.2**, que é um pacote `net8.0`. A versão alinhada ao .NET 10 — a 3.1.0 — está
em RC, e o kit não fixa pré-lançamento.

Roda sem problema sobre `net10.0`, mas deixa uma dívida datada: um dia será preciso subir. E a pergunta é quanto
isso vai custar.

Se cada handler declarar `Mediator.ICommandHandler<TCommand, TResponse>` diretamente, a resposta é: toda a
camada de aplicação. Uma mudança de assinatura no pacote alcança todo arquivo que o menciona — e o mesmo vale
para uma eventual troca do próprio pacote.

## Decisão

**Marcadores próprios em `Application/Common/Messaging`, e nenhum tipo da Application referencia o namespace
`Mediator` fora dessa pasta.**

São seis interfaces, todas declarações sem corpo:

```csharp
public interface ICommand<TResponse> : Mediator.ICommand<Result<TResponse>>;
public interface ICommand : Mediator.ICommand<Result>;
public interface IQuery<TResponse> : Mediator.IQuery<Result<TResponse>>;

public interface ICommandHandler<in TCommand, TResponse>
    : Mediator.ICommandHandler<TCommand, Result<TResponse>> where TCommand : ICommand<TResponse>;
public interface ICommandHandler<in TCommand>
    : Mediator.ICommandHandler<TCommand, Result> where TCommand : ICommand;
public interface IQueryHandler<in TQuery, TResponse>
    : Mediator.IQueryHandler<TQuery, Result<TResponse>> where TQuery : IQuery<TResponse>;
```

**A herança é o que mantém o source generator funcionando** — ele precisa reconhecer os tipos do pacote. O
acoplamento não desaparece; ele fica **num lugar só**, em seis linhas.

**A abstração faz duas coisas, e a segunda não é acidental:** além de isolar a versão do pacote, ela embute
`Result<T>` no tipo de retorno. Com isso, "comando devolve `Result`" deixa de ser convenção documentada e passa
a ser **assinatura** — um handler que tentasse devolver outra coisa não compila.

**A regra é verificada por teste:** `ForaDosMarcadores_NadaReferenciaOMediatorDiretamente`, em
`RegrasDeMensageriaTests`. Subir para o 3.1 passa a ser a edição de um arquivo.

## Consequências

**O que se ganha.** A migração vira mudança de uma camada, e o retorno `Result` fica garantido pelo compilador.

**O que custa, e é o ponto honesto: uma indireção que só se paga no dia da migração.** Até lá, é uma camada a
mais entre o handler e a biblioteca — e quem chega ao projeto precisa descobrir que `ICommandHandler` é do
projeto, não do pacote. São seis linhas de custo contra um benefício que talvez nunca seja cobrado.

**A regra de arquitetura tem dois limites conhecidos**, e é justo declará-los:

- Ela **exclui os tipos gerados pelo source generator**, que referenciam o namespace do Mediator em toda
  assinatura por definição. Incluí-los acusaria dezenas de violações que ninguém escreveu.
- Ela proíbe **declarar diretamente** a interface do pacote; herdar através de um marcador é esperado, porque é
  assim que os marcadores funcionam. E não cobre interfaces, porque a API de reflexão usada não aceita interface
  como alvo.

Há ainda uma falha da régua: um tipo que receba `IPublisher` **no construtor** passaria, porque a regra varre
métodos públicos. É falha da verificação, não permissão de desenho.

**O despachante do outbox não usa o Mediator** (ADR 0006), então não há marcador de notificação aqui. A regra do
`sealed` foi estendida para alcançar reações a evento por assinatura — sem isso, ela ficaria verde sem
inspecionar essa família de tipos.
