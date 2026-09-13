# 0001 — Clean Architecture

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

Um kit de referência precisa decidir onde cada coisa mora antes de existir a primeira funcionalidade, porque
depois é tarde: quando o primeiro handler consulta o banco direto, o padrão está estabelecido e o custo de
reverter cresce a cada arquivo novo.

O modo de falha que se quer evitar não é a má organização inicial — é a erosão. Toda estrutura em camadas
começa correta; ela apodrece quando alguém tem pressa, importa `DbContext` num caso de uso e ninguém percebe
na revisão. Seis meses depois a camada de domínio não compila sem o Entity Framework.

## Decisão

Quatro projetos, com as dependências apontando **para dentro**:

```
Api  →  Application  →  Domain
              ↑
       Infrastructure
```

| Projeto | Referencia | Contém |
|---|---|---|
| `CleanStart.Domain` | **nada** | Entidades, value objects, regras, `Result`, interfaces de repositório |
| `CleanStart.Application` | Domain | Casos de uso, validação, orquestração |
| `CleanStart.Infrastructure` | Domain, Application | EF Core, Redis, HTTP, implementações das interfaces |
| `CleanStart.Api` | Application, Infrastructure | Endpoints, middlewares, composição |

O `CleanStart.Domain.csproj` não tem nenhum `ProjectReference`, e isso está escrito lá: *"o Domain não depende
de nada — é o centro da arquitetura"*.

A referência `Api → Infrastructure` existe **apenas para composição**: a raiz precisa registrar as
implementações na injeção de dependência. A regra de negócio nunca a atravessa.

**A regra é verificada por teste, não por disciplina.** `tests/CleanStart.ArchitectureTests` tem cinco testes de
dependência, escritos com NetArchTest:

| Teste | Proíbe |
|---|---|
| `Domain_NaoDependeDeNenhumaOutraCamada` | Domain → Application, Infrastructure ou Api |
| `Domain_NaoReferenciaEfCore` | Domain → `Microsoft.EntityFrameworkCore` |
| `Application_NaoDependeDeInfrastructureNemDeApi` | Application → Infrastructure ou Api |
| `Application_NaoReferenciaEfCore` | Application → EF Core |
| `Infrastructure_NaoDependeDeApi` | Infrastructure → Api |

Mais dois sobre como o domínio é escrito: entidade não expõe setter público, raiz de agregado não expõe coleção
mutável.

## Consequências

**O que se ganha.** A erosão para de depender de quem revisa: uma violação quebra o build, com o nome do tipo
infrator na mensagem. E a direção das dependências torna o domínio testável sem banco — os testes de domínio
rodam em milissegundos, sem container.

**O que custa.** Indireção real. Uma consulta simples atravessa endpoint → command → handler → repositório →
`DbContext`, e alguém acostumado a um controller que consulta o banco vai achar isso cerimonioso. Para um CRUD
de três telas, provavelmente é.

**O que se assume.** Interfaces declaradas na camada de dentro e implementadas na de fora — a inversão que
sustenta tudo. `IOrderRepository` vive no Domain; `OrderRepository` vive na Infrastructure.

**Os testes de arquitetura têm guarda contra vacuidade.** Cada um afirma `NotBeEmpty()` sobre o conjunto que
inspeciona, porque uma regra que não encontra tipos passa sem verificar nada — e continuaria verde se o
primeiro violador nascesse. Isso é o que distingue um teste de arquitetura de uma decoração.
