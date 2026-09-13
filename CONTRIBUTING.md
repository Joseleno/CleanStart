# Contribuindo

Obrigado pelo interesse. Este documento diz como propor mudança e, mais importante, **quais regras o
repositório impõe automaticamente** — saber disso antes poupa uma ida e volta na revisão.

## Antes de abrir um PR grande, abra uma issue

Para correção pequena — erro de digitação, ajuste de comentário, defeito óbvio — vá direto ao PR.

Para qualquer coisa maior, **abra uma issue primeiro**. Este é um kit de referência, não uma biblioteca: uma
funcionalidade útil pode ainda assim ser recusada por tornar o exemplo mais difícil de ler. É frustrante
descobrir isso depois de implementar, e a issue existe para evitar esse desperdício.

## O que você precisa ter

- **.NET SDK 10.0.401** ou mais novo (a versão está fixada no `global.json`)
- **Docker** — cerca de 37% da suíte sobe PostgreSQL e Redis de verdade

```bash
git clone <sua-fork>
cd CleanStart
dotnet build
dotnet test
```

Se **todos** os testes de integração falharem, o Docker não está rodando. É a primeira coisa a verificar.

O [guia de primeiros passos](docs/getting-started.md) leva do clone ao primeiro request.

---

## As regras que o build impõe

Não são convenção documentada: são **erro de compilação ou teste vermelho**. Conhecê-las antes evita surpresa.

### `TreatWarningsAsErrors` está ligado

Todo aviso quebra o build. Inclusive os de estilo — o `.editorconfig` tem severidade de build, não de sugestão.

O caso que mais pega quem chega: **`var` só quando o tipo está aparente no lado direito.**

```csharp
var builder = WebApplication.CreateBuilder(args);              // NÃO compila
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);   // certo

var id = Guid.CreateVersion7();                                // compila: o tipo está à direita
```

A razão é o público: quem lê um kit de referência frequentemente o lê no navegador, sem IDE para passar o mouse
em cima de um `var`.

### Dez regras de arquitetura, como teste

`tests/CleanStart.ArchitectureTests` reprova, com o nome do tipo infrator na mensagem:

| Grupo | O que proíbe |
|---|---|
| Dependência (5) | Domain depender de qualquer camada ou do EF Core; Application depender de Infrastructure, Api ou EF Core; Infrastructure depender da Api |
| Domínio (2) | Entidade com setter público; raiz de agregado expondo coleção mutável |
| Mensageria (3) | Handler não-`sealed`; command/query que não seja `record`; qualquer tipo referenciando o namespace `Mediator` fora de `Common/Messaging` |

**Se uma regra atrapalhar o que você quer fazer, abra uma issue — não relaxe o teste.** Quase sempre a resposta
certa é mover o código, e a regra existe justamente porque a pressão para afrouxá-la aparece no pior momento.

### Teste no nível certo

Nenhuma mudança é considerada pronta sem teste no nível apropriado:

| O que mudou | Onde testar |
|---|---|
| Regra de domínio | `Domain.UnitTests` — sem dublê, sem banco |
| Handler | `Application.UnitTests` — repositórios com NSubstitute |
| Repositório, interceptor, migration | `Infrastructure.IntegrationTests` — PostgreSQL real |
| Endpoint | `Api.FunctionalTests` — caminho feliz **e pelo menos um erro** |
| Estrutura ou convenção | `ArchitectureTests` |

> **O provider InMemory do EF Core é proibido** em teste de integração. Ele não tem constraint, não tem
> transação e não fala SQL: aprova o que o PostgreSQL reprovaria. Ver [ADR 0007](docs/adr/0007-testcontainers-para-integracao.md).

### Dependência nova precisa de justificativa

**Licença é critério de bloqueio.** O kit argumenta publicamente contra dependência de licença comercial — o
MediatR, o AutoMapper e o FluentAssertions estão fora por isso, e o Moq por ter embarcado o SponsorLink.

Um pacote novo precisa de: licença permissiva verificada, versão **estável** (nada de RC ou beta), e uma razão
que sobreviva à pergunta "o que quebra se não tiver isso?".

---

## Como escrever a mudança

**Comentário explica *por quê*, nunca *o quê*.** O código já diz o que faz. O comentário existe para a decisão
que não é óbvia — e, num kit de referência, para o custo que ela tem.

```csharp
// RUIM: incrementa o contador de tentativas
mensagem.Attempts++;

// BOM: sem contador não existe backoff — o despachante não distinguiria uma mensagem nova
// de outra que falhou cinquenta vezes, e reprocessaria a morta a cada ciclo, para sempre.
mensagem.Attempts++;
```

Outras convenções: comentário em **português**, identificadores em **inglês**; erro de negócio retorna `Result`
e exception fica para falha de infraestrutura; `IDateTimeProvider` em vez de `DateTime.UtcNow`;
`CancellationToken` propagado em toda chamada assíncrona; um caso de uso é **uma pasta** com tudo dentro.

O guia de [como acrescentar uma funcionalidade](docs/adding-a-feature.md) percorre um caso de uso completo.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`,
`chore:`.

Título curto — **até 72 caracteres** — seguido de **linha em branco** e do corpo. Sem a linha em branco o git
junta tudo num título gigante, e o GitHub o trunca.

O corpo é onde está o valor: explique **por que**, não o que o diff já mostra. Se a mudança contraria uma
decisão registrada nos ADRs, diga isso explicitamente.

## Pull request

- Um PR resolve **uma coisa**. Dois assuntos viram dois PRs.
- `dotnet build` e `dotnet test` verdes — a CI confere, mas descobrir na sua máquina é mais rápido.
- Se mudou comportamento, **atualize a documentação afetada** no mesmo PR. Documentação que envelhece é pior que
  documentação ausente: afirma com confiança um estado que já não é verdade.
- Decisão técnica nova merece um **ADR** em `docs/adr/` — contexto → decisão → consequências, e a seção de
  consequências não lista só benefícios.

A CI roda build, testes e a construção da imagem. Os três precisam passar.

## Código de conduta

Seja direto sobre o código e respeitoso com as pessoas. Crítica técnica é bem-vinda — inclusive à arquitetura
do kit, que é o assunto dele: **critica-se a decisão, não quem a tomou.**

O texto completo está em [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md), com o canal para denúncias.

## Licença

Ao contribuir, você concorda que sua contribuição é licenciada sob a [MIT](LICENSE), como o resto do projeto.
