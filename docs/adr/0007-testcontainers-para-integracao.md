# 0007 — Testcontainers para teste de integração

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

Teste que toca o banco precisa de um banco. As opções são três, e duas delas mentem.

**O provider InMemory do EF Core** é o atalho popular e o pior dos três: não tem constraint, não tem transação e
não fala SQL. Ele aprova o que o PostgreSQL reprovaria — índice único violado, chave estrangeira órfã, SQL que
não traduz — e reprova nada do que importa. Um teste verde nele não diz nada sobre produção.

Este projeto encontrou o caso concreto mais de uma vez: `order.Id.Value` dentro de uma expressão LINQ **compila
e não traduz**, porque `OrderId` é conversor de valor. No InMemory a consulta funciona, porque tudo roda em
memória; contra o PostgreSQL, estoura. E o cursor de paginação truncava a data ao usar milissegundos, quando o
PostgreSQL guarda microssegundos — defeito que só aparece com registros no mesmo instante, e que nenhum teste
sem banco real encontraria.

**Um banco compartilhado de desenvolvimento** resolve a fidelidade e cria outros problemas: exige instalação
prévia, acumula lixo entre execuções, e um teste derruba o outro quando dois desenvolvedores rodam ao mesmo
tempo.

## Decisão

**Testcontainers 4.15.0**, subindo `postgres:17-alpine` e `redis:7-alpine` sob demanda.

> O provider InMemory do EF Core é **proibido** em teste de integração neste repositório.

Duas fixtures, com escopos diferentes:

| Fixture | Sobe | Para quê |
|---|---|---|
| `PostgresFixture` | Postgres | Repositórios, interceptors, migrations, o despachante do outbox |
| `CleanStartApiFactory` | **API inteira** + Postgres + Redis | A pilha completa, entrando por HTTP |

Um container por classe de teste, derrubado ao final. Nenhum serviço local precisa estar instalado — os testes
rodam num clone novo sem subir nada antes, o que também significa que **não dependem do `docker-compose`** do
repositório.

A `CleanStartApiFactory` executa o **`Program.cs` real**: mesmos middlewares, mesmo pipeline, mesma injeção de
dependência. O que se substitui é apenas a configuração. Trocar registro de serviço ali faria o teste exercitar
uma composição que não existe em produção.

## Consequências

**O que custa, e é o custo principal: 113 dos 303 testes — cerca de 37% da suíte — exigem Docker rodando.** Sem o
daemon, eles falham, e o sintoma parece defeito de código. É a primeira coisa a verificar quando a suíte quebra
inteira.

**A suíte fica mais lenta.** Subir container leva segundos, e testes de domínio que rodariam em milissegundos
esperam. A mitigação é o escopo por classe, não por teste.

**A fixture precisa espelhar a configuração de produção**, e isso já foi aprendido da maneira difícil: ela não
ligava `EnableRetryOnFailure`, e com a estratégia de retry ativa o EF Core recusa transação iniciada pelo
usuário fora de `CreateExecutionStrategy`. Um worker escrito errado **passaria na suíte inteira** e quebraria no
primeiro tique em produção. É o mesmo princípio que proíbe o InMemory: teste que não reproduz a configuração
real aprova o que o banco real reprova.

**As migrations são aplicadas de verdade** (`MigrateAsync`), nunca `EnsureCreated`. O segundo monta o schema a
partir do modelo e passaria com uma migration quebrada — que é exatamente o que se quer verificar.

**Os testes compartilham a base dentro da classe**, o que exige disciplina: filtrar pelo próprio dado, nunca por
"o último registro". Documento de teste precisa ser único por caso, porque os índices únicos são reais.
