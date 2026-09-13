# 0003 — Mapperly em vez de AutoMapper

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

Converter entidade em DTO é trabalho repetitivo, e a resposta tradicional em .NET é o **AutoMapper** — que
mudou de licença pelo mesmo caminho do MediatR, e do mesmo autor.

Mas aqui há um segundo motivo, independente da licença, e ele é mais interessante: **o AutoMapper resolve o
mapeamento em tempo de execução**. Um campo renomeado na entidade e não atualizado no destino compila
normalmente e falha — ou, pior, não falha: a propriedade fica com o valor padrão, e o DTO sai com um zero ou um
nulo onde deveria haver dado.

Esse é o modo de falha caro. Não é o erro que estoura; é o que produz resposta plausível e errada.

## Decisão

**`Riok.Mapperly`, versão 4.3.1.** Gera o código de mapeamento em **tempo de compilação**, por source generator.

A consequência prática: mapeamento incompleto é **erro de build**, com o nome da propriedade não mapeada. O que
antes se descobria em produção passa a impedir o commit.

O pacote entra com `PrivateAssets="all"`, para que o source generator não vaze como dependência transitiva de
quem consumir o assembly — detalhe que num kit de referência importa, porque as pessoas copiam o `.csproj`.

**Uso hoje: exatamente um mapeador**, em `Application/Orders/PlaceOrder/OrderMapper.cs`. É honesto dizer que a
adoção é pontual e não difusa — a maioria dos slices projeta direto para o DTO na consulta, o que é mais simples
do que mapear.

## Consequências

**O que se ganha.** Mapeamento errado deixa de ser classe de bug em runtime. E o código gerado é legível: dá
para abri-lo e ver atribuição a atribuição, em vez de confiar num mecanismo que resolve por convenção.

**O que custa.** Menos "mágica" significa mais explicitação. Convenções que o AutoMapper resolveria sozinho —
achatamento de propriedades aninhadas, por exemplo — aqui pedem configuração declarada.

**Source generator tem custo de build**, pequeno mas real, e depende de suporte da IDE para que o código gerado
apareça na navegação.

**A adoção pontual é um sinal a observar.** Com um único mapeador, vale a pergunta periódica: o pacote ainda se
justifica, ou duas linhas de atribuição manual resolveriam? Hoje ele se paga por mostrar o padrão a quem lê o
kit; num projeto real, a resposta depende de quantos mapeamentos existem.
