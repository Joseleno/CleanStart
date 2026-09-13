// Teste de carga de fumaça.
//
// Não mede capacidade: confirma que a API responde corretamente sob tráfego concorrente, que é uma pergunta
// diferente da que a suíte funcional responde. Um teste funcional roda uma requisição por vez e nunca encontra
// o que só aparece com concorrência — exaustão de pool de conexões, deadlock, cache servindo dado de outro
// usuário, limitador disparando cedo demais.
//
// Como rodar:
//   docker compose up -d
//   k6 run tests/load/smoke.js
//
// O k6 não é instalado pelo repositório; veja https://k6.io/docs/get-started/installation.

import http from 'k6/http';
import { check, group } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';

export const options = {
  // Dez usuários por trinta segundos. É fumaça: carga suficiente para haver concorrência de verdade, curta o
  // bastante para caber no fim de uma sessão de trabalho.
  vus: 10,
  duration: '30s',

  thresholds: {
    // Critérios de aprovação. Sem eles o k6 imprime números bonitos e sai com código zero mesmo quando metade
    // das requisições falhou — e aí o teste não reprova nada.
    //
    // p(95) e não a média: a média esconde a cauda, e é a cauda que o usuário percebe. Uma média de 80 ms com
    // 5% das requisições em 4 s descreve um sistema que parece rápido e não é.
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
  },
};

// Pega um token uma vez, no início, em vez de a cada iteração: o endpoint de desenvolvimento não é o alvo da
// medição, e incluí-lo distorceria as estatísticas com uma chamada que produção não tem.
export function setup() {
  const resposta = http.post(
    `${BASE}/api/v1/dev/token`,
    JSON.stringify({ usuarioId: null, nome: 'k6' }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  check(resposta, {
    'o token de desenvolvimento foi emitido': (r) => r.status === 200,
  });

  if (resposta.status !== 200) {
    // Sem token, todas as iterações receberiam 401 e o resultado seria um relatório de falhas que não diz nada
    // sobre desempenho. Falhar aqui, com a causa, poupa a investigação.
    throw new Error(
      `Não foi possível obter um token (${resposta.status}). ` +
        'A API está no ar e em ambiente de desenvolvimento?',
    );
  }

  return { token: resposta.json('token') };
}

export default function (dados) {
  const autenticado = {
    headers: {
      Authorization: `Bearer ${dados.token}`,
      'Content-Type': 'application/json',
    },
  };

  group('health', () => {
    // Sem token, de propósito: o health check precisa continuar aberto, ou o orquestrador não consegue
    // consultá-lo e reinicia instâncias saudáveis em laço.
    const live = http.get(`${BASE}/health/live`);

    check(live, {
      'live responde 200': (r) => r.status === 200,
    });
  });

  group('pedidos', () => {
    const lista = http.get(`${BASE}/api/v1/orders?tamanho=20`, autenticado);

    check(lista, {
      'a listagem responde 200': (r) => r.status === 200,
      'a listagem devolve a estrutura de página': (r) => r.json('itens') !== undefined,

      // Sob concorrência, é aqui que apareceria exaustão do pool de conexões: as primeiras requisições passam e
      // as seguintes estouram o tempo de espera por uma conexão livre.
      'a listagem não estourou o tempo': (r) => r.timings.duration < 1000,
    });

    const semToken = http.get(`${BASE}/api/v1/orders`);

    check(semToken, {
      // Sob carga, um limitador ou um cache mal configurado poderia servir a resposta de outro usuário a quem
      // não se autenticou. Vale conferir a cada iteração, não só uma vez.
      'sem token continua sendo 401': (r) => r.status === 401,
    });
  });
}
