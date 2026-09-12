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

Cada uma foi verificada **reprovando**, não só passando: introduzir `DbContext` no Domain faz a regra 2 falhar
nomeando o tipo, e as outras quatro seguem verdes (a falha é específica, não em bloco).

## Adiadas para a Fase 2 — decisão, não esquecimento

As regras 5, 6 e 7 da T0.3 do `IMPLEMENTACAO.md` dependem de tipos que **ainda não existem**:

| # | Regra | Por que não agora |
|---|---|---|
| 5 | Handlers são `sealed` | Não há handler até a Fase 2 |
| 6 | Commands e queries são `record` | Idem |
| 7 | Entidade não expõe setter público | Não há entidade até a Fase 1 |

Escritas hoje, elas varreriam zero tipos e passariam por vacuidade — verde que não prova nada e que, pior,
**continuaria verde se alguém escrevesse o primeiro handler violando a regra**, porque ninguém revisita um teste
que nunca reclamou. O critério adotado aqui é o mesmo das regras 1 a 4: uma regra entra quando é capaz de
reprovar.

Elas entram junto com o primeiro tipo de cada categoria — na T1.2 (entidade, regra 7) e na T2.1 (handler e
command, regras 5 e 6) — e o teste deve ser verificado reprovando antes de ser considerado pronto.
