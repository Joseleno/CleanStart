# CleanStart.Templates

Template `dotnet new` para uma solução .NET 10 com Clean Architecture, DDD e CQRS.

## Instalação

    dotnet new install CleanStart.Templates

## Uso

    dotnet new cleanstart -n MinhaEmpresa.MeuProjeto

Também disponível no diálogo *File → New → Project* do Visual Studio.

## Nenhuma dependência que cobre do seu empregador

Sem MediatR, sem AutoMapper, sem Moq, sem FluentAssertions — os quatro passaram a
exigir licença comercial ou tiveram incidente de licenciamento. No lugar deles:
Mediator e Mapperly, que são source generators e movem o custo de reflection de
runtime para compile time, e AwesomeAssertions, um fork Apache-2.0 com a mesma API.

O critério é explícito: quem clona isto para um projeto de cliente não deveria ter
que auditar licença depois.

## O que vem pronto

Quatro camadas (Domain, Application, Infrastructure, Api) e uma **feature de
pedidos implementada ponta a ponta** — não um `WeatherForecast`. Cinco endpoints
que exercitam padrões diferentes: invariantes no agregado, evento via outbox,
paginação por keyset, cache com invalidação e transição de estado com erro tipado.

Junto vêm EF Core 10 com PostgreSQL, HybridCache com Redis, Carter, FluentValidation
no pipeline, Serilog, OpenTelemetry e resiliência com Polly. Mais Docker Compose com
a API, Postgres, Redis, Seq e Jaeger de pé.

## As regras de arquitetura são testes

Dependência entre camadas, domínio sem setter público, handler `sealed`, nenhum
repositório devolvendo `IQueryable` — tudo verificado por NetArchTest. Quebrar uma
regra **quebra o build**, com o nome do tipo infrator no erro.

A suíte tem 322 testes em cinco níveis: unidade de domínio, unidade de aplicação,
arquitetura, integração com Testcontainers (PostgreSQL de verdade, não `InMemory`)
e funcional pela API inteira.

## Documentação

[github.com/Joseleno/CleanStart](https://github.com/Joseleno/CleanStart) — com dez
ADRs registrando o porquê de cada decisão.
