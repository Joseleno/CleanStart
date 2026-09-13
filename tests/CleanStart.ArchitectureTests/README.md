# Testes de arquitetura

As regras do `CLAUDE.md` como teste executável. Quando um destes reprova, a resposta é **mover o código** —
nunca afrouxar o teste.

A mensagem de falha nomeia os tipos violadores (ver `ArchTestResultExtensions`): `IsSuccessful.Should().BeTrue()`
diria apenas "expected true, found false", que informa que a arquitetura foi violada mas não onde.

## Implementadas

| # | Regra | Teste |
|---|---|---|
| 1 | Domain não depende de Application, Infrastructure nem Api | `Domain_NaoDependeDeNenhumaOutraCamada` |
| 2 | Domain não referencia EF Core | `Domain_NaoReferenciaEfCore` |
| 3 | Application não referencia Infrastructure nem Api | `Application_NaoDependeDeInfrastructureNemDeApi` |
| 3 | Application não referencia EF Core | `Application_NaoReferenciaEfCore` |
| 4 | Infrastructure não referencia Api | `Infrastructure_NaoDependeDeApi` |
| 7 | Entidade não expõe setter público | `Entidades_NaoExpoemSetterPublico` (T1.3) |
| — | Raiz de agregado expõe coleção somente leitura | `RaizesDeAgregado_ExpoemColecoesSomenteLeitura` (T1.3) |
| 5 | Handlers são `sealed` | `Handlers_SaoSealed` (T2.3) |
| 6 | Commands e queries são `record` | `CommandsEQueries_SaoRecord` (T2.3) |
| — | Fora de `Common/Messaging` e `Common/Behaviors`, nada referencia `Mediator` | `ForaDosMarcadores_NadaReferenciaOMediator...` (T2.1) |

**As sete regras da T0.3 estão implementadas.** As regras 5, 6 e 7 chegaram depois da fase que as previa, cada
uma junto com o primeiro tipo que elas podiam inspecionar — ver decisão 17.

Cada uma foi verificada **reprovando**, não só passando: introduzir `DbContext` no Domain faz a regra 2 falhar
nomeando o tipo, e trocar `private set` por `set` em `Order.Status` faz a regra 7 falhar nomeando a propriedade.
As demais seguem verdes — a falha é específica, não em bloco.

As regras de domínio usam reflexão direta (`RegrasDeDominioTests`), não o NetArchTest: a pergunta é sobre o
**membro** ("esta propriedade tem setter público?") e a API do NetArchTest opera sobre tipos. Ambas começam
afirmando que encontraram algo para inspecionar — sem isso, um namespace renomeado faria o teste passar
vazio.

`private set` e `init` são permitidos de propósito: o primeiro é como o método de domínio atribui, o segundo só
atua na construção. O que a regra proíbe é o setter **acessível de fora**.

A regra de mensageria **ignora código gerado** (`CompilerGeneratedAttribute`, `GeneratedCodeAttribute` e o
namespace `Mediator` dentro do assembly). O source generator do Mediator emite o pipeline inteiro na Application,
e ele referencia o namespace dele em toda assinatura porque é o trabalho dele — incluí-lo faria a regra acusar
~40 violações que ninguém escreveu e ninguém pode corrigir.

## O guard contra teste vazio

Toda regra que varre tipos começa afirmando que **encontrou algo para inspecionar**
(`handlers.Should().NotBeEmpty()`). Sem isso, um namespace renomeado ou uma base trocada fariam o teste passar
sem verificar nada, e ninguém perceberia — é o verde vacuoso que a decisão 17 do HANDOFF existe para evitar.

O guard já trabalhou: as regras 5 e 6 foram escritas na T2.1 e **removidas no mesmo passo**, porque ele reprovou
— a Application só tinha os marcadores, sem handler nem command de verdade. Voltaram na T2.3, quando havia o que
inspecionar, e foram verificadas reprovando.

Nota para quem escrever regra sobre `record`: ele não tem marca própria em metadados. O sinal confiável é o
método sintetizado `<Clone>$`, que o compilador gera para todo record e para nada mais.