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

Cada uma foi verificada **reprovando**, não só passando: introduzir `DbContext` no Domain faz a regra 2 falhar
nomeando o tipo, e trocar `private set` por `set` em `Order.Status` faz a regra 7 falhar nomeando a propriedade.
As demais seguem verdes — a falha é específica, não em bloco.

As regras de domínio usam reflexão direta (`RegrasDeDominioTests`), não o NetArchTest: a pergunta é sobre o
**membro** ("esta propriedade tem setter público?") e a API do NetArchTest opera sobre tipos. Ambas começam
afirmando que encontraram algo para inspecionar — sem isso, um namespace renomeado faria o teste passar
vazio.

`private set` e `init` são permitidos de propósito: o primeiro é como o método de domínio atribui, o segundo só
atua na construção. O que a regra proíbe é o setter **acessível de fora**.

## Adiadas para a Fase 2 — decisão, não esquecimento

As regras 5 e 6 da T0.3 do `IMPLEMENTACAO.md` dependem de tipos que **ainda não existem**:

| # | Regra | Por que não agora |
|---|---|---|
| 5 | Handlers são `sealed` | Não há handler até a Fase 2 |
| 6 | Commands e queries são `record` | Idem |

Escritas hoje, elas varreriam zero tipos e passariam por vacuidade — verde que não prova nada e que, pior,
**continuaria verde se alguém escrevesse o primeiro handler violando a regra**, porque ninguém revisita um teste
que nunca reclamou. O critério adotado aqui é o mesmo das regras 1 a 4: uma regra entra quando é capaz de
reprovar.

Entram na **T2.1**, com o primeiro handler e o primeiro command — e o teste deve ser verificado reprovando antes
de ser considerado pronto. A regra 7 seguiu esse caminho: ficou de fora da T0.3 e entrou na T1.3, junto com a
primeira entidade.
