<!--
  Um PR resolve uma coisa. Se há dois assuntos aqui, são dois PRs — e os dois serão revisados mais rápido.
-->

## O que muda, e por quê

<!--
  Comece pelo porquê. O diff já mostra o quê.
  Se resolve uma issue: "Fecha #123".
-->

## Como foi verificado

<!--
  O que você rodou, e o que aconteceu. "Deve funcionar" não é verificação.

  Se a mudança toca entrega (Dockerfile, CI, referência de pacote), vale rodar `docker build --no-cache`: há
  uma classe inteira de defeito que só aparece em ambiente limpo, porque o cache local esconde.
-->

- [ ] `dotnet build` — 0 erros, 0 warnings
- [ ] `dotnet test` — todos passando
- [ ] Testes novos, no nível apropriado

## Custo desta mudança

<!--
  Toda decisão custa algo: uma dependência, um conceito novo, uma indireção, mais código para manter.
  Declare aqui. Um PR que só lista benefícios é mais difícil de revisar, não mais fácil de aprovar.

  Se não há custo relevante, escreva "nenhum relevante" — e a revisão confere.
-->

## Antes de enviar

- [ ] Comentários explicam **por quê**, não o quê
- [ ] Documentação afetada atualizada **neste mesmo PR** <!-- doc desatualizada é pior que doc ausente -->
- [ ] Nenhuma regra de arquitetura foi relaxada para o build passar <!-- se atrapalhou, abra uma issue -->
- [ ] Nenhuma dependência nova — ou, se há, a licença foi verificada e a versão é estável
- [ ] Nenhum segredo, connection string real ou dado pessoal no diff

<!--
  Decisão técnica nova merece um ADR em docs/adr/ — contexto → decisão → consequências.
  A seção de consequências não lista só benefícios.
-->
