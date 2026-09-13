# Architecture Decision Records

Cada arquivo registra **uma decisão**, no formato contexto → decisão → consequências.

O que estes documentos tentam responder é a pergunta que o código não responde: **por que não do outro jeito?**
O código mostra o que foi feito; o ADR mostra o que foi descartado e o que a escolha custou. É a diferença entre
poder mudar de ideia com conhecimento de causa e ter medo de mexer.

Por isso a seção "Consequências" nunca lista só benefícios. Uma decisão sem custo declarado é propaganda, não
registro — e quem vier depois merece saber o preço antes de pagá-lo.

| # | Decisão | Em uma linha |
|---|---|---|
| [0001](0001-clean-architecture.md) | Clean Architecture | Dependências apontam para dentro, e teste de arquitetura verifica |
| [0002](0002-mediator-em-vez-de-mediatr.md) | Mediator em vez de MediatR | Licença é critério de bloqueio |
| [0003](0003-mapperly-em-vez-de-automapper.md) | Mapperly em vez de AutoMapper | Mapeamento errado vira erro de build |
| [0004](0004-result-em-vez-de-exception.md) | `Result` em vez de exception | Erro de negócio não é falha de sistema |
| [0005](0005-vertical-slices-na-application.md) | Vertical slices | Uma pasta por caso de uso, não por papel técnico |
| [0006](0006-outbox-para-eventos.md) | Outbox para eventos | Gravar e publicar são dois sistemas |
| [0007](0007-testcontainers-para-integracao.md) | Testcontainers | Banco de verdade; o InMemory aprova o que o Postgres reprova |
| [0008](0008-awesomeassertions-em-vez-de-fluentassertions.md) | AwesomeAssertions | Mesmo critério de licença, com o custo de ser um fork |
| [0009](0009-abstracoes-proprias-sobre-o-mediator.md) | Abstrações sobre o Mediator | Seis linhas para que migrar seja mudança de um arquivo |
| [0010](0010-identidade-tipada-e-value-objects.md) | Identidade tipada e value objects | Trocar argumento deixa de compilar |

## Ao escrever um novo

Numere na sequência, use o mesmo formato e **não reescreva ADR antigo**: uma decisão que muda ganha um ADR novo
que a substitui, e o antigo passa a "substituída por 00XX". O histórico de por que se pensava diferente é parte
do valor — apagá-lo deixa o registro tão pobre quanto não tê-lo.
