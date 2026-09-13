# Do clone ao primeiro request

Este guia vai do `git clone` a um pedido criado por HTTP. São dois caminhos: **tudo em container** (mais rápido
de começar) ou **a API na máquina** (melhor para depurar).

## O que é preciso ter

| | Para quê |
|---|---|
| **.NET SDK 10.0.401** ou mais novo | A versão está fixada no `global.json` |
| **Docker** | PostgreSQL e Redis — e **também para rodar os testes** |

O Docker não é opcional: cerca de 37% da suíte sobe containers de verdade
([ADR 0007](adr/0007-testcontainers-para-integracao.md)). Sem o daemon rodando, esses testes falham — e o
sintoma parece defeito de código.

---

## Caminho 1 — tudo em container

```bash
git clone <url-do-repositorio>
cd CleanStart

docker compose up -d --build
```

Sobe cinco serviços: a API, PostgreSQL, Redis, Seq (logs) e Jaeger (traces).

**Prepare o banco** — as migrations não são aplicadas automaticamente
([por quê](#por-que-migrar-nao-e-automatico)):

```bash
docker compose run --rm api dotnet CleanStart.Api.dll --migrate --seed
```

O `--seed` grava dez clientes e cinco pedidos de exemplo, para haver o que consultar.

| Onde | Endereço |
|---|---|
| API | http://localhost:8080 |
| Documentação interativa | http://localhost:8080/scalar/v1 |
| Traces (Jaeger) | http://localhost:16686 |
| Logs (Seq) | http://localhost:5341 |

---

## Caminho 2 — a API na máquina

Útil para depurar: ponto de parada na IDE, recompilação rápida.

**Suba só as dependências:**

```bash
docker compose up -d postgres redis
```

**Configure os segredos** — a connection string e a chave JWT **não** ficam em arquivo versionado:

```bash
cd src/CleanStart.Api

dotnet user-secrets set "Database:ConnectionString" \
  "Host=localhost;Port=5432;Database=cleanstart;Username=postgres;Password=postgres"

dotnet user-secrets set "Jwt:SigningKey" "uma-chave-de-desenvolvimento-com-32-caracteres"
```

> A chave precisa de **pelo menos 32 caracteres**. A aplicação recusa subir com menos, e é deliberado: chave
> curta é preenchida por umas bibliotecas e rejeitada por outras — nos dois casos, a segurança que se acredita
> ter não existe.

**Prepare o banco e rode:**

```bash
dotnet run -- --migrate --seed    # prepara e encerra
dotnet run                        # sobe a API
```

A API fica em http://localhost:5131, e a documentação em http://localhost:5131/scalar/v1.

---

## O primeiro request

**Os endpoints de pedido exigem autenticação.** Sem token, a resposta é 401.

Em desenvolvimento há um emissor de exemplo — ele **não existe fora de `Development`**, e não é protegido: é
ausente, que é a única garantia que não depende de configuração correta.

```bash
# 1. Peça um token
curl -s -X POST http://localhost:8080/api/v1/dev/token \
  -H "Content-Type: application/json" \
  -d '{"nome":"eu"}'
# → {"token":"eyJ...","usuarioId":"019..."}

# 2. Use-o
TOKEN="eyJ..."

curl -s http://localhost:8080/api/v1/orders \
  -H "Authorization: Bearer $TOKEN"
```

**Crie um pedido** (use um `customerId` que o seed gravou — pegue um da listagem acima):

```bash
curl -i -X POST http://localhost:8080/api/v1/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "customerId": "COLE-AQUI-UM-ID",
        "currency": "BRL",
        "items": [{ "productId": "0199e0a0-0000-7000-8000-000000000001",
                    "quantity": 2, "unitPrice": 10.50 }]
      }'
```

Responde **201** com o `Location` do recurso criado. E, na mesma transação, gravou um evento no outbox — o
despachante o publica no ciclo seguinte. Veja no log: `Outbox: 1 mensagem(ns) despachada(s)`.

---

## Rodando os testes

```bash
dotnet test
```

São **303 testes** em cinco níveis. Na primeira execução o Docker baixa `postgres:17-alpine` e `redis:7-alpine`,
o que leva mais tempo; depois as imagens ficam em cache.

Os testes **não dependem do `docker-compose`** — eles sobem os próprios containers e os derrubam ao final. Um
clone novo roda `dotnet test` sem subir nada antes.

---

## Quando algo não funciona

**Todos os testes de integração falham.** O Docker não está rodando. É a primeira coisa a verificar quando a
suíte quebra inteira.

**A aplicação não sobe, reclamando de configuração.** A validação acontece no startup, de propósito — falta a
connection string ou a chave JWT. A mensagem diz qual.

**Todo endpoint responde 401.** Falta o token, ou ele expirou (validade padrão: 60 minutos). Peça outro.

**`/health/ready` responde 503.** A aplicação está de pé e uma dependência não: Postgres ou Redis fora do ar. O
`/health/live` continua 200 — e essa diferença é intencional
([por quê](#por-que-dois-health-checks)).

---

## Duas perguntas que o código responde com "não"

### Por que migrar não é automático

Migrar no startup é conveniente e perigoso: com várias instâncias subindo ao mesmo tempo, todas tentam migrar o
mesmo banco, e uma migration destrutiva roda antes que alguém possa conferir o plano.

Com a flag, aplicar é um passo do deploy — deliberado, uma vez, com alguém olhando. E `--migrate`/`--seed`
**encerram** o processo em vez de servir: descrevem uma tarefa, o que é o que um *init container* espera.

### Por que dois health checks

`live` responde "o processo está de pé" e **não** consulta dependência: se o banco cair, o orquestrador não deve
reiniciar o pod — reiniciar não conserta banco fora do ar e só remove capacidade de servir o que ainda funciona.

`ready` responde "posso receber tráfego" e aí sim checa Postgres e Redis: sem banco, a instância deve sair do
balanceador até voltar.

Apontar os dois para o mesmo lugar é o erro comum, e ele transforma indisponibilidade de banco em reinício em
massa de pods.

---

## Próximos passos

- **[Acrescentando uma funcionalidade](adding-a-feature.md)** — um caso de uso completo, do domínio ao endpoint
- **[Decisões técnicas](adr/)** — por que cada escolha, e o que ela custou
