# Desempenho — verificação do requisito de 50 req/s

> *"Durante momentos de pico, o sistema de consolidação chega a processar 50 chamadas por segundo,
> tolerando uma perda máxima de 5% dessas requisições."*

Este documento registra a **medição** desse requisito — não o argumento de projeto, que está no
[README](../README.md#requisitos-não-funcionais). Todos os números abaixo são reprodutíveis com o
gerador de carga incluído no repositório.

---

## Ambiente

| Item | Valor |
|---|---|
| CPU | AMD Ryzen 7 5700X — 8 núcleos / 16 threads |
| Memória | 16 GB |
| SO | Windows 11 Pro |
| Runtime | .NET 10.0.302, build `Release` |
| Contêineres | Docker 29.7.2 — PostgreSQL 17.11, RabbitMQ 4.3.4 |
| Topologia | 1 réplica de cada API, 1 PostgreSQL, 1 RabbitMQ, tudo na mesma máquina |

> Máquina de desenvolvimento, com o gerador de carga disputando CPU com os serviços medidos.
> É um cenário **pessimista** em relação a um ambiente real com nós dedicados.

## Massa de dados

| Item | Quantidade |
|---|---|
| Lojistas | 200 (+ 1 com histórico de 30 dias) |
| Lançamentos registrados | ~4.400 |
| Dias consolidados (`daily_balances`) | 234 |

## Metodologia

O gerador ([`tools/CashFlow.LoadTest`](../tools/CashFlow.LoadTest)) é **open-loop**: dispara
requisições em um cronograma fixo, independentemente da velocidade de resposta do servidor. Um
gerador closed-loop desaceleraria junto com o servidor e esconderia exatamente a degradação que o
teste deve expor.

Duas decisões para não medir a coisa errada:

- **Rotação entre 200 lojistas distintos.** Martelar uma única URL mediria o `OutputCache`, não o
  sistema. Com 200 chaves distintas e TTL de 5s, a maior parte das requisições atravessa o banco.
- **Timeouts e falhas de conexão contam como perda**, não como resposta rápida.

Cada cenário tem 5s de aquecimento (JIT, pool de conexões, cache de query do EF) antes da medição.

```bash
cd tools/CashFlow.LoadTest
dotnet run -c Release -- --urls-file urls.txt --rps 50 --duration 60 --warmup 5
```

---

## Resultados

### Leitura — `GET /daily-balance/{date}`

| Cenário | Taxa | Duração | Sucesso | Perda | p50 | p95 | p99 | máx |
|---|---|---|---|---|---|---|---|---|
| **A — requisito** | 50 req/s | 60s | 3.001 (100%) | **0,00%** | 1,97 ms | 3,05 ms | 3,75 ms | 13,1 ms |
| **B — 3× o requisito** | 150 req/s | 60s | 9.000 (100%) | **0,00%** | 1,03 ms | 2,56 ms | 3,31 ms | 12,6 ms |

### Leitura pesada — `GET /statement?from=&to=` (janela de 30 dias)

| Cenário | Taxa | Duração | Sucesso | Perda | p50 | p95 | p99 | máx |
|---|---|---|---|---|---|---|---|---|
| **C — extrato** | 50 req/s | 60s | 3.001 (100%) | **0,00%** | 2,43 ms | 3,41 ms | 3,97 ms | 11,9 ms |

### Escrita — `POST /api/v1/entries`

| Cenário | Taxa | Duração | Sucesso | Perda | p50 | p95 | p99 | máx |
|---|---|---|---|---|---|---|---|---|
| **E — lançamentos** | 50 req/s | 60s | 3.002 (100%) | **0,00%** | 3,36 ms | 4,46 ms | 5,50 ms | 48,2 ms |

Ao fim da carga de escrita, **a fila de outbox já estava vazia** (`0s` de espera até drenar
completamente): o dispatcher acompanhou o ritmo de ingestão em tempo real, sem acumular backlog.

### Veredito do requisito

**Atendido com folga.** A 50 req/s a perda é **0,00%** contra um orçamento de 5%, com p99 de
**3,75 ms**. O sistema mantém 0% de perda até pelo menos 150 req/s — 3× o pico especificado.

---

## Sobrecarga deliberada — 600 req/s (12× o requisito)

Este cenário existe para responder "o que acontece quando o pico é muito maior que o previsto?".

| Momento | Sucesso | Descartadas (429) | p50 dos atendidos | p99 dos atendidos |
|---|---|---|---|---|
| Antes do ajuste | 34,5% | 65,5% | **985 ms** | 1.006 ms |
| Depois do ajuste | 33,5% | 66,5% | **86 ms** | 105 ms |

Descartar ~66% a 600 req/s é o comportamento **desejado**: o limiter admite ~200 req/s e rejeita o
excedente imediatamente com `429` + `Retry-After`, protegendo quem já está dentro. Nenhum timeout,
nenhuma conexão recusada, nenhuma falha em cascata.

### O ajuste que o teste provocou

A primeira medição expôs um defeito de tuning que nenhuma revisão de código teria pego: as
requisições **admitidas** demoravam ~1 segundo.

A causa era o `TokenBucketRateLimiter` configurado com `ReplenishmentPeriod` de 1 segundo. Ele
liberava toda a cota de uma vez no tique e deixava a fila parada até o tique seguinte — quem entrava
na fila esperava, em média, meio período. Um `429` rápido é degradação graciosa; um `200` que
demora 1 segundo é só lentidão disfarçada.

Correção: repor em janelas de 100 ms (`TokensPerPeriod: 20`, `ReplenishmentPeriod: 100ms`) e reduzir
a fila de 200 para 20, limitando a espera máxima a ~100 ms.

```csharp
// src/Consolidation/CashFlow.Consolidation.Api/Program.cs
new TokenBucketRateLimiterOptions
{
    TokenLimit = 100,                                        // rajada
    TokensPerPeriod = 20,                                    // 200 req/s sustentados
    ReplenishmentPeriod = TimeSpan.FromMilliseconds(100),    // era 1s
    QueueLimit = 20,                                         // era 200
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
}
```

**Resultado: p50 de 985 ms → 86 ms** sob a mesma sobrecarga, sem alterar a taxa de admissão.

---

## Integridade sob carga

Verificação de que velocidade não custou correção. Contabilidade completa do banco ao final de todos
os cenários:

| Métrica | Valor |
|---|---|
| Lançamentos registrados | 4.369 |
| Eventos `registered` consolidados | 4.367 |
| Eventos `cancelled` consolidados | 1 |
| Mensagens na dead-letter queue | **0** |
| Outbox pendente | **0** |

A diferença de 2 eventos é **integralmente atribuível ao bug de concorrência corrigido antes destes
testes** — os 3 lançamentos daquele lojista específico (`79b7d972…`) seguem com apenas 1 consolidado,
como esperado, já que a correção não reprocessa retroativamente. **Considerando apenas o período
pós-correção: 4.366 de 4.366 eventos consolidados, perda zero.**

---

## Onde está o próximo gargalo

Nada nestes testes chegou perto de saturar o sistema — a 150 req/s a latência p99 é *menor* que a
50 req/s, sinal de que o custo dominante é overhead por requisição e não contenção. Os limites reais
aparecem bem acima:

| Limite | Estimativa | Como elevar |
|---|---|---|
| Rate limiter (por IP) | ~200 req/s admitidos | Configuração; particionar por lojista em vez de IP |
| Pool de conexões do PostgreSQL | ~100 conexões | Réplicas de leitura; pgBouncer |
| Consumidor RabbitMQ | `prefetch` × concorrência | Configuração ou mais réplicas |
| Contenção no mesmo `(lojista, dia)` | retries otimistas | `UPDATE` atômico via `ExecuteUpdate` |

O último merece nota: a projeção usa concorrência otimista com retry, o que é correto e mantém a
lógica no domínio. Se um único lojista concentrasse milhares de lançamentos por segundo no mesmo
dia, a taxa de retry subiria e valeria trocar por um `UPDATE ... SET total = total + @valor`
atômico — abrindo mão do modelo rico em troca de vazão. Nos volumes deste desafio, não se justifica.
