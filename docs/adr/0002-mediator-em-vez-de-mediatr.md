# 0002 — Mediator em vez de MediatR

**Data:** 13/09/2026 · **Situação:** aceita

## Contexto

O padrão mediator desacopla quem pede de quem executa: o endpoint envia uma mensagem e não conhece o handler.
Em .NET, a implementação conhecida é o **MediatR** — e ela mudou de licença.

A partir da versão 12, o MediatR passou a exigir licença comercial para uso acima de certo porte. Quem já o
usava descobriu que uma dependência gratuita virou uma linha de custo, com prazo.

Para este repositório o problema é mais direto: um kit publicado como referência é **copiado**. Se ele traz uma
dependência com trava de licença, cada pessoa que o usar herda a trava — sem necessariamente perceber, porque a
descoberta acontece meses depois, quando o projeto cresce.

## Decisão

**`Mediator` (`martinothamar/Mediator`), versão 3.0.2.** Licença Apache-2.0.

A regra que governa isso vale para qualquer pacote novo do projeto:

> **Licença é critério de bloqueio, não detalhe:** este kit argumenta publicamente contra dependência de licença
> comercial, então adicionar uma quebra o próprio discurso.

Pelo mesmo critério estão fora o **AutoMapper** (ADR 0003), o **FluentAssertions** (ADR 0008) e o **Moq** — este
último não por licença, mas por ter embarcado o SponsorLink, que extraía o e-mail de quem compilava.

Há um ganho técnico junto, e é justo mencioná-lo sem exagerar: o Mediator resolve os handlers por **source
generator**, em tempo de compilação, em vez de por reflexão em tempo de execução. Handler que não casa é erro de
build.

**A versão está fixada em 3.0.2, que é um pacote `net8.0`.** O 3.1.0, alinhado ao .NET 10, está em RC — e o kit
não fixa pré-lançamento. Rodar um pacote `net8.0` sobre `net10.0` funciona, mas é uma pergunta previsível para
quem lê um kit que se vende como .NET 10; a resposta é esta, e o ADR 0009 trata de como a migração foi mantida
barata.

## Consequências

**O que se ganha.** Nenhuma trava de licença para quem copiar o kit, e resolução de handlers verificada na
compilação.

**O que custa.** O Mediator é mantido por um projeto bem menor que o MediatR. Menos exemplos na internet, menos
respostas prontas, e a possibilidade real de o mantenedor perder o interesse. É o risco de qualquer alternativa
a um padrão de mercado.

**A API não é idêntica à do MediatR.** Quem chega com a experiência do outro pacote encontra diferenças de
nomenclatura e de registro. A abstração do ADR 0009 reduz isso, porque o código do dia a dia fala com os
marcadores próprios.

**Fica uma dívida datada:** subir para o 3.1 quando ele estabilizar. O ADR 0009 é o que torna essa mudança uma
edição de um arquivo, e não de todos os handlers.
