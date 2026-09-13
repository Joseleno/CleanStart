# 0008 — AwesomeAssertions em vez de FluentAssertions

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

O **FluentAssertions** é a biblioteca de asserções mais usada em .NET, e a partir da versão 8 passou a exigir
**licença comercial** para uso corporativo.

Para este repositório isso não é só uma questão de custo — é uma **contradição interna**. O kit bane o MediatR
(ADR 0002) e o AutoMapper (ADR 0003) exatamente por licença comercial, e argumenta isso publicamente no README.
Manter o FluentAssertions na tabela de dependências, ao lado desse argumento, era a incoerência que o primeiro
leitor atento notaria.

O agravante é o tamanho do acoplamento. **São 483 asserções `.Should()`** espalhadas pelos cinco projetos de
teste — 158 no domínio, 152 na integração, 78 na aplicação, 75 nos funcionais, 20 na arquitetura. Trocar a
biblioteca depois de escrever tudo isso é reescrever a suíte inteira; é o tipo de decisão que fica cara por
adiamento.

## Decisão

**`AwesomeAssertions` 9.6.0** — um fork do FluentAssertions sob licença **Apache-2.0** (verificado no nuspec,
não presumido).

A API é idêntica. Os 483 `.Should().Be()` continuam válidos sem alteração, e os exemplos de documentação que
mostram asserções não precisaram ser reescritos. Na prática, a troca custou uma linha no arquivo de versões.

Pelo mesmo critério de licença estão fora o MediatR e o AutoMapper. E o **Moq** está fora por motivo vizinho,
que vale registrar porque é o mesmo tipo de risco: ele embarcou o SponsorLink, que extraía o e-mail de quem
compilava o projeto. Em seu lugar, **NSubstitute**.

## Consequências

**O que se ganha.** Nenhuma trava de licença, e coerência entre o que o kit faz e o que ele argumenta.

**O que custa, e é o ponto honesto deste ADR: é um fork, e forks acompanham o upstream com atraso.** Correção
de defeito e recurso novo do FluentAssertions chegam depois — quando chegam. Se o fork perder o mantenedor, o
caminho de volta existe (a API é a mesma), mas passa a ser uma decisão de licença, não técnica.

**Há menos material na internet.** Uma dúvida sobre asserção resolve-se pela documentação do FluentAssertions,
que é compatível — mas quem não souber disso vai procurar por "AwesomeAssertions" e achar pouco.

**O risco de fork único é real e conhecido.** É o preço de não aceitar a trava de licença, e foi assumido
conscientemente. A alternativa seria voltar ao `Assert.Equal` do xUnit — sem dependência nenhuma, mas com
asserções bem menos legíveis, e o kit tem legibilidade como critério declarado.
