# CashFlow — Controle de Fluxo de Caixa

Solução para o desafio técnico de Desenvolvedor de Software: um lojista registra lançamentos
(créditos e débitos) e consulta o **saldo diário consolidado**.

O sistema é composto por **dois serviços independentes**, integrados de forma assíncrona por
mensageria, de modo que **a aplicação de lançamentos continua operando mesmo com o serviço de
consolidação (ou o broker) fora do ar** — requisito não funcional central do desafio.

```
┌──────────────┐   HTTP    ┌─────────────────────┐   outbox    ┌──────────┐   AMQP    ┌───────────────────────┐   HTTP   ┌──────────┐
│   Lojista    │ ────────► │   Launches API      │ ──────────► │ RabbitMQ │ ────────► │  Consolidation API    │ ◄─────── │ Relatório│
│  (cliente)   │           │  (escrita)          │             │          │           │  (projeção + leitura) │          │          │
└──────────────┘           └─────────────────────┘             └──────────┘           └───────────────────────┘          └──────────┘
                                    │                                                            │
                              PostgreSQL                                                   PostgreSQL
                           (entries + outbox)                                        (daily_balances + dedup)
```

---

## Índice

- [Como executar](#como-executar)
- [Endpoints](#endpoints)
- [Arquitetura](#arquitetura)
- [Decisões técnicas](#decisões-técnicas)
- [Requisitos não funcionais](#requisitos-não-funcionais) — [medição de desempenho](docs/desempenho.md)
- [Testes](#testes)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Melhorias futuras](#melhorias-futuras)

---

## Como executar

### Pré-requisitos

| Ferramenta | Versão | Necessário para |
|---|---|---|
| [Docker](https://www.docker.com/) + Docker Compose | 20.10+ | Executar a solução completa (**caminho recomendado**) |
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** | Compilar e rodar os testes localmente |

> Não é necessário instalar PostgreSQL, RabbitMQ nem o .NET para usar o Docker Compose,
> e não é necessário Docker para rodar a suíte de testes automatizados.

### Opção 1 — Docker Compose (recomendado)

```bash
git clone https://github.com/daviml/desafio-desenvolvedor-software-rox.git
cd desafio-desenvolvedor-software-rox

docker compose up -d --build
```

Sobem quatro contêineres: PostgreSQL, RabbitMQ e as duas APIs. O schema de cada banco é aplicado
automaticamente no start (migrations do EF Core), e a topologia do RabbitMQ (exchange, filas e
dead-letter) é declarada pelos próprios serviços.

| Serviço | URL | Observação |
|---|---|---|
| **Launches API** (lançamentos) | http://localhost:8081/swagger | Escrita |
| **Consolidation API** (saldo diário) | http://localhost:8082/swagger | Leitura/relatório |
| RabbitMQ Management | http://localhost:15672 | usuário `cashflow` / senha `cashflow` |
| PostgreSQL | `localhost:5432` | usuário `cashflow` / senha `cashflow` |

Verificando a saúde dos serviços:

```bash
curl http://localhost:8081/health/ready   # Healthy
curl http://localhost:8082/health/ready   # Healthy
```

Para derrubar tudo (incluindo os volumes de dados):

```bash
docker compose down -v
```

### Roteiro de demonstração ponta a ponta

O script abaixo registra lançamentos, comprova a idempotência, aguarda a consolidação assíncrona,
cancela um lançamento e imprime os relatórios:

```bash
bash scripts/smoke-test.sh
```

Ou manualmente:

```bash
MERCHANT=11111111-1111-1111-1111-111111111111
HOJE=$(date -u +%Y-%m-%d)

# 1) Registrar um crédito (o header Idempotency-Key torna o retry seguro)
curl -X POST http://localhost:8081/api/v1/entries \
  -H 'Content-Type: application/json' \
  -H "Idempotency-Key: venda-001" \
  -d "{\"merchantId\":\"$MERCHANT\",\"type\":\"Credit\",\"amount\":1500.00,\"entryDate\":\"$HOJE\",\"description\":\"Venda no cartao\"}"

# 2) Registrar um débito
curl -X POST http://localhost:8081/api/v1/entries \
  -H 'Content-Type: application/json' \
  -d "{\"merchantId\":\"$MERCHANT\",\"type\":\"Debit\",\"amount\":300.50,\"entryDate\":\"$HOJE\",\"description\":\"Compra de insumos\"}"

# 3) Consultar o saldo diário consolidado (atualizado de forma assíncrona, em ~1s)
curl "http://localhost:8082/api/v1/merchants/$MERCHANT/daily-balance/$HOJE"
# {"totalCredits":1500.00,"totalDebits":300.50,"balance":1199.50,"entryCount":2,...}

# 4) Extrato consolidado do período
curl "http://localhost:8082/api/v1/merchants/$MERCHANT/statement?from=$HOJE&to=$HOJE"
```

### Demonstração da resiliência (requisito não funcional)

```bash
# Derruba o broker e o serviço de consolidação
docker compose stop rabbitmq consolidation-api

# Os lançamentos continuam sendo aceitos normalmente (HTTP 201, ~10 ms)
curl -i -X POST http://localhost:8081/api/v1/entries \
  -H 'Content-Type: application/json' \
  -d "{\"merchantId\":\"$MERCHANT\",\"type\":\"Credit\",\"amount\":100.00,\"entryDate\":\"$HOJE\",\"description\":\"Venda offline\"}"

# Os eventos ficam pendentes na tabela de outbox
docker exec cashflow-postgres psql -U cashflow -d cashflow_launches \
  -c "select id, type, attempt_count, next_attempt_at_utc from launches.outbox_messages where processed_at_utc is null;"

# Ao restabelecer o broker, o outbox drena sozinho e o saldo se atualiza
docker compose start rabbitmq consolidation-api
curl "http://localhost:8082/api/v1/merchants/$MERCHANT/daily-balance/$HOJE"
```

### Opção 2 — Executando localmente sem Docker

A suíte de testes roda offline (SQLite em memória + transporte in-process):

```bash
dotnet test
```

Para subir as APIs sem Docker, é possível usar o perfil SQLite. Nesse modo o transporte in-process
**não atravessa processos** — a consolidação só recebe eventos com o RabbitMQ ativo (que pode ser
subido isoladamente com `docker compose up -d rabbitmq`):

```bash
dotnet run --project src/Launches/CashFlow.Launches.Api \
  --Database:Provider=Sqlite \
  --Database:ConnectionString="Data Source=launches.db" \
  --Messaging:Provider=RabbitMq
```

---

## Endpoints

### Launches API — `http://localhost:8081`

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/v1/entries` | Registra um crédito ou débito. Aceita o header `Idempotency-Key`. |
| `POST` | `/api/v1/entries/{entryId}/cancellation` | Cancela um lançamento (compensação, sem exclusão). |
| `GET` | `/api/v1/entries/{entryId}` | Consulta um lançamento. |
| `GET` | `/api/v1/merchants/{merchantId}/entries` | Lista paginada, com filtros `from`, `to`, `type`, `includeCancelled`. |

### Consolidation API — `http://localhost:8082`

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/v1/merchants/{merchantId}/daily-balance/{date}` | **Saldo diário consolidado.** |
| `GET` | `/api/v1/merchants/{merchantId}/daily-balance` | Saldo consolidado de hoje (UTC). |
| `GET` | `/api/v1/merchants/{merchantId}/statement?from=&to=` | Extrato do período com saldo acumulado. |

Ambas expõem `/health/live`, `/health/ready` e `/swagger`.

### Contrato de erros

Todas as falhas seguem [RFC 7807 (ProblemDetails)](https://datatracker.ietf.org/doc/html/rfc7807),
com um `code` estável para o cliente tratar programaticamente:

```jsonc
{
  "title": "Business rule violated",
  "status": 422,
  "detail": "The entry date 2026-12-31 is in the future.",
  "code": "entry.date_in_future",
  "correlationId": "0HNNSCSTQQJU2:00000001"
}
```

| Situação | HTTP |
|---|---|
| Payload malformado / validação de forma | `400` |
| Recurso inexistente | `404` |
| Conflito de estado (ex.: cancelar duas vezes) | `409` |
| Regra de negócio violada (invariante do domínio) | `422` |
| Excesso de requisições (rate limiting) | `429` |

---

## Arquitetura

O detalhamento — com diagramas de componentes, de sequência e de dados — está em
**[docs/arquitetura.md](docs/arquitetura.md)**.

### Visão geral

Dois **bounded contexts** independentes, cada um com seu banco e seu ciclo de vida:

| | **Launches** (Lançamentos) | **Consolidation** (Consolidado) |
|---|---|---|
| Responsabilidade | Registrar e cancelar lançamentos | Manter e servir o saldo diário |
| Natureza | Write-heavy, consistência forte | Read-heavy, consistência eventual |
| Modelo | Agregado `Entry` | Projeção `DailyBalance` |
| Falha isolada | Indisponibiliza o registro | **Não afeta o registro** |

### Camadas (Clean Architecture)

Cada serviço é dividido em quatro camadas, com dependências apontando **sempre para dentro**:

```
Api ──► Infrastructure ──► Application ──► Domain
                                              ▲
                            (interfaces declaradas pelo Domain/Application
                             e implementadas pela Infrastructure)
```

- **Domain** — entidades, objetos de valor e invariantes. Zero dependências externas.
- **Application** — casos de uso (um handler por operação), validações de forma, DTOs.
- **Infrastructure** — EF Core, RabbitMQ, outbox. Implementa as *portas* declaradas acima.
- **Api** — apenas transporte: recebe, despacha, traduz o resultado em HTTP.

A regra é verificável no `.csproj`: `CashFlow.Launches.Domain` não referencia nenhum pacote de
infraestrutura, e `CashFlow.*.Application` conhece apenas abstrações.

---

## Decisões técnicas

### 1. Transactional Outbox — a base da resiliência

O ponto mais delicado do desafio é: *"a aplicação de lançamentos precisa continuar operante mesmo
em caso de falha no sistema de consolidação"*.

Publicar no broker dentro do request seria o caminho óbvio — e o errado: um RabbitMQ indisponível
derrubaria o registro de vendas, e um `commit` seguido de falha na publicação perderia o evento.

Aqui, **o lançamento e o evento de integração são gravados na mesma transação do PostgreSQL**
([`OutboxSaveChangesInterceptor`](src/Launches/CashFlow.Launches.Infrastructure/Persistence/Outbox/OutboxSaveChangesInterceptor.cs)).
Um serviço em background ([`OutboxDispatcher`](src/Launches/CashFlow.Launches.Infrastructure/Persistence/Outbox/OutboxDispatcher.cs))
move as linhas pendentes para o RabbitMQ depois, com *backoff* exponencial, *jitter* e circuit
breaker.

Consequências:

- Escrita **nunca** depende do broker: uma indisponibilidade atrasa a consolidação, não recusa a venda.
- **Nenhum evento é perdido**: ou os dois inserts acontecem, ou nenhum.
- Entrega **at-least-once** — resolvida com deduplicação no consumidor (item 3).

### 2. CQRS entre serviços — o relatório é uma projeção

O saldo diário **não é calculado sob demanda**. Cada evento atualiza incrementalmente uma linha em
`daily_balances`, e a consulta é uma leitura indexada por `(merchant_id, date)`.

A alternativa (`SUM` sobre os lançamentos a cada request) tem custo proporcional ao histórico do
lojista e degradaria exatamente no cenário de pico descrito no desafio. Aqui o custo da leitura é
**constante**, independentemente de haver 10 ou 10 milhões de lançamentos.

### 3. Idempotência em duas frentes

| Onde | Mecanismo | Protege contra |
|---|---|---|
| Entrada HTTP | Header `Idempotency-Key` + índice único `(merchant_id, idempotency_key)` | Retry do cliente duplicar uma venda |
| Consumo de eventos | Tabela `processed_events` com PK no `event_id`, gravada na mesma transação da projeção | Redelivery do broker somar duas vezes |

Em ambos os casos **o banco é a autoridade** — a checagem prévia é só um atalho rápido; quem decide
o vencedor de uma corrida é a constraint única.

### 4. Result Pattern em vez de exceções para falhas esperadas

Handlers retornam `Result<T>`, com o erro classificado (`Validation`, `NotFound`, `Conflict`,
`Unprocessable`). Exceções ficam reservadas para o que é realmente excepcional.

Motivo: o caminho quente não paga o custo de `throw`/`catch`, e a assinatura do handler documenta
todos os modos de falha. A tradução para HTTP acontece em um único lugar
([`ResultExtensions`](src/BuildingBlocks/CashFlow.Web/ResultExtensions.cs)).

### 5. Mediator próprio com decorators

Em vez de uma biblioteca de mediator, há uma implementação de ~40 linhas
([`RequestDispatcher`](src/BuildingBlocks/CashFlow.SharedKernel/Application/RequestDispatcher.cs))
com dois decorators — validação e logging — compostos explicitamente no
[registro de handlers](src/BuildingBlocks/CashFlow.SharedKernel/Application/HandlerRegistrationExtensions.cs).

Motivos: sem dependência com restrição de licença comercial, a reflexão ocorre **uma vez por tipo**
(depois é chamada virtual em cache), e o pipeline que envolve todo caso de uso é legível em um
único arquivo — nada de *assembly scanning* mágico.

### 6. Concorrência otimista na projeção — e por que o retry mora ali

Vários consumidores podem tocar o mesmo dia simultaneamente. `DailyBalance.Version` é um
*concurrency token*: o EF adiciona o valor original ao `WHERE` do `UPDATE`, e quem perde a corrida
recebe `ConcurrencyConflictException`, sendo **reprocessado com estado fresco** por
[`RetryingDailyBalanceProjection`](src/Consolidation/CashFlow.Consolidation.Application/Projection/RetryingDailyBalanceProjection.cs)
— cada tentativa em um escopo de DI novo, porque um `SaveChanges` que falhou deixa a unidade de
trabalho anterior com estado sujo.

O retry fica na **projeção**, não no consumidor do RabbitMQ, porque o conflito é uma propriedade do
caso de uso: assim a garantia vale para qualquer transporte, inclusive replay e reprocessamento.

Um detalhe sutil e crítico: a violação de unicidade em `processed_events` (replay legítimo, pode
ser ignorado) e em `daily_balances` (dois consumidores abrindo o mesmo dia, **precisa** de retry)
precisam ser distinguidas. Tratar as duas como "evento duplicado" faz o valor sumir do saldo
silenciosamente — cenário que ocorreu ao validar a solução com o broker real e está coberto por
teste de regressão desde então.

### 7. Modelagem de dinheiro

`Money` é um *value object* imutável que arredonda para 2 casas (`MidpointRounding.ToEven`) e
recusa operações entre moedas diferentes. Valores são `decimal` — nunca `double` — e a coluna é
`numeric(18,2)`. O valor é sempre positivo: o sinal é responsabilidade do `EntryType`.

### 8. Cancelamento em vez de exclusão

Registro financeiro não se apaga. `Entry.Cancel()` marca o lançamento e emite
`cashflow.entry.cancelled`, que a consolidação usa para **compensar** o saldo. O histórico e a
trilha de auditoria permanecem íntegros.

### 9. Escolhas de infraestrutura

| Escolha | Por quê |
|---|---|
| **PostgreSQL** (um banco por serviço) | Sem tabelas compartilhadas entre contextos; cada serviço evolui e escala isoladamente. |
| **RabbitMQ** (topic exchange + DLQ) | Entrega confiável com *publisher confirms*, *prefetch* e dead-letter para mensagens envenenadas. |
| **SQLite** apenas em teste/offline | Permite rodar `dotnet test` sem infraestrutura, exercitando o mesmo modelo do EF Core. |
| **Migrations do EF Core** | Schema versionado e aplicado no start (`MigrateAsync`), com retry enquanto o banco sobe. |
| **Shouldly / NSubstitute** | Assertivas e mocks legíveis, sem restrição de licença comercial. |

---

## Requisitos não funcionais

### "Continuar operante mesmo com falha na consolidação"

| Mecanismo | Onde |
|---|---|
| Outbox transacional — a escrita não toca o broker | `OutboxSaveChangesInterceptor` |
| Publicação em background com backoff + jitter | `OutboxDispatcher` |
| Circuit breaker — pausa após falhas consecutivas | `OutboxDispatcherHostedService` |
| Health check do broker reportado como **Degraded**, nunca *Unhealthy* | `RabbitMqHealthCheck` |
| Reconexão automática do consumidor | `RabbitMqConnectionProvider` |

O último item merece destaque: se o RabbitMQ derrubasse o `/health/ready` da Launches API, um
orquestrador tiraria do balanceador um serviço **perfeitamente saudável**. Por isso a falha do
broker é *degradação*, não indisponibilidade.

Comportamento verificado na prática (broker parado, `POST /api/v1/entries`):

```
entry 1 -> HTTP 201 in 0.009s
entry 2 -> HTTP 201 in 0.008s
entry 3 -> HTTP 201 in 0.026s
/health/live  -> 200
/health/ready -> 200
```

E coberto de forma determinística pelos testes
[`OutboxDispatcherTests`](tests/CashFlow.Launches.IntegrationTests/OutboxDispatcherTests.cs).

### "50 req/s no pico, tolerando até 5% de perda"

| Mecanismo | Efeito |
|---|---|
| Leitura servida por projeção pré-agregada | Custo constante — 50 req/s é uma leitura indexada por request |
| `OutputCache` de 5s nos relatórios | Rajadas de requests idênticos colapsam em uma única ida ao banco |
| Rate limiting *token bucket* com fila limitada | Descarta rápido o excedente (`429` + `Retry-After`) em vez de deixar tudo enfileirar e estourar timeout |
| `AsNoTracking` + projeção direta no `SELECT` | Sem overhead de change tracking no caminho de leitura |
| Outbox: lotes de 200 msg a cada 500 ms | Vazão de ~400 msg/s — 8× a folga sobre o pico especificado |
| `prefetch` + concorrência configuráveis no consumidor | Escala horizontal por réplica sem mudar código |
| GUID v7 nas chaves | Inserção sequencial no índice, sem fragmentação de página |
| Logging via `LoggerMessage` gerado em tempo de compilação | Sem boxing nem formatação quando o nível está desabilitado |

A tolerância a 5% de perda é atendida por **degradação graciosa**: sob sobrecarga o sistema rejeita
rápido uma fatia das requisições (com `Retry-After`) para manter as demais dentro do orçamento de
latência — em vez de aceitar tudo e colapsar.

#### Medição

O requisito foi **medido**, não apenas argumentado. Resultados completos em
**[docs/desempenho.md](docs/desempenho.md)**:

| Cenário | Taxa | Perda | p99 |
|---|---|---|---|
| `GET /daily-balance` — **o requisito** | 50 req/s | **0,00%** (orçamento: 5%) | **3,75 ms** |
| `GET /daily-balance` — 3× o requisito | 150 req/s | 0,00% | 3,31 ms |
| `GET /statement` (janela de 30 dias) | 50 req/s | 0,00% | 3,97 ms |
| `POST /entries` (escrita) | 50 req/s | 0,00% | 5,50 ms |
| Sobrecarga deliberada | 600 req/s | 66% descartados com `429` | 105 ms nos atendidos |

A carga rotaciona entre 200 lojistas distintos, para medir o caminho do banco e não o cache. Ao fim
da carga de escrita o outbox já estava vazio — o dispatcher acompanhou a ingestão em tempo real.

O teste de sobrecarga rendeu uma correção concreta: o `TokenBucketRateLimiter` repunha a cota uma
vez por segundo, e as requisições **admitidas** esperavam ~1s na fila. Repondo a cada 100 ms e
encurtando a fila, o p50 sob sobrecarga caiu de **985 ms para 86 ms**.

Para reproduzir:

```bash
docker compose up -d
cd tools/CashFlow.LoadTest
dotnet run -c Release -- --urls-file urls.txt --rps 50 --duration 60
```

---

## Testes

```bash
dotnet test
```

**115 testes**, sem necessidade de Docker, banco ou broker:

| Projeto | Testes | Escopo |
|---|---|---|
| `CashFlow.SharedKernel.UnitTests` | 23 | `Money`, `Result`, decorator de validação |
| `CashFlow.Launches.UnitTests` | 38 | Invariantes do agregado `Entry`, handlers, validators, mapeamento de contratos |
| `CashFlow.Consolidation.UnitTests` | 29 | Aritmética do `DailyBalance`, projetor, retry sob concorrência, queries |
| `CashFlow.Launches.IntegrationTests` | 15 | API real ponta a ponta + **comportamento do outbox sob falha do broker** |
| `CashFlow.Consolidation.IntegrationTests` | 10 | Consumo de eventos, deduplicação, concorrência, compensação, relatórios |

Os testes de integração sobem a aplicação real (`WebApplicationFactory`) — mesmo container de DI,
mesmo pipeline de middlewares, mesmo modelo do EF Core — contra SQLite em memória. O que é
substituído é apenas a fronteira de infraestrutura, e nada da lógica de negócio.

Destaque para os cenários que provam os requisitos não funcionais:

- `Sweep_WhenTheBrokerIsDown_KeepsTheMessagePendingAndSchedulesARetry`
- `Sweep_WhenTheBrokerRecovers_PublishesThePendingMessageExactlyOnce`
- `TheSameEventDeliveredTwice_IsAppliedOnlyOnce`
- `ConcurrentEventsForTheSameDay_AreAllAccountedFor`
- `ApplyAsync_WhenTheRaceNeverResolves_SurfacesTheFailureSoTheMessageIsNotLost`
- `Post_Entry_WithTheSameIdempotencyKey_ReturnsTheOriginalEntry`

Além da suíte automatizada, a solução foi validada contra o stack real em contêineres. O cenário
mais severo — parar o broker, registrar 12 lançamentos e restabelecê-lo, fazendo o outbox drenar
os 12 eventos de uma só vez — resulta em `balance: 1200.00`, `entryCount: 12` e DLQ vazia.

---

## Estrutura do repositório

```
src/
├─ BuildingBlocks/
│  ├─ CashFlow.SharedKernel/        # Entity, AggregateRoot, Money, Result, mediator + decorators
│  ├─ CashFlow.Messaging/           # Abstrações + contratos de integração (versionados)
│  ├─ CashFlow.Messaging.RabbitMq/  # Adaptador RabbitMQ: conexão, pool, publisher, consumer, DLQ
│  └─ CashFlow.Web/                 # ProblemDetails, correlation id, health checks
├─ Launches/
│  ├─ CashFlow.Launches.Domain/           # Entry, Money, invariantes, eventos de domínio
│  ├─ CashFlow.Launches.Application/      # Casos de uso, validators, DTOs
│  ├─ CashFlow.Launches.Infrastructure/   # EF Core, repositórios, outbox, migrations
│  └─ CashFlow.Launches.Api/              # Minimal APIs, Swagger, rate limiting
├─ Consolidation/
│  ├─ CashFlow.Consolidation.Domain/          # DailyBalance
│  ├─ CashFlow.Consolidation.Application/     # Projetor, handlers de evento, queries
│  ├─ CashFlow.Consolidation.Infrastructure/  # EF Core, dedup, migrations
│  └─ CashFlow.Consolidation.Api/             # Minimal APIs, output cache
tests/                                        # 5 projetos, 115 testes
tools/CashFlow.LoadTest/                      # Gerador de carga open-loop, sem dependências
docs/arquitetura.md                           # Diagramas e detalhamento
docs/desempenho.md                            # Medição do requisito de 50 req/s
scripts/smoke-test.sh                         # Roteiro de demonstração ponta a ponta
docker-compose.yml                            # PostgreSQL + RabbitMQ + as duas APIs
```

### Padrões e princípios aplicados

**Design Patterns** — Transactional Outbox, Repository, Unit of Work, Mediator, Decorator
(validação e logging), Strategy (seleção de provider), Factory (tradução domínio → contrato),
Value Object, Aggregate Root, Domain Event, Options, Circuit Breaker, Retry with Backoff,
Dead Letter Queue, CQRS.

**SOLID** — um handler por caso de uso (SRP); pipeline estendido por decorator sem alterar handlers
(OCP); `IIntegrationEventPublisher` intercambiável entre RabbitMQ e in-process (LSP); interfaces
enxutas como `IEntryRepository` e `IEntryQueries`, separando escrita de leitura (ISP); domínio
declara as portas, infraestrutura as implementa (DIP).

---

## Melhorias futuras

O que ficou fora do escopo desta entrega, e por quê:

1. **Autenticação e autorização** — hoje qualquer chamador pode consultar qualquer `merchantId`.
   O próximo passo natural é JWT/OAuth2 com o `merchantId` derivado do token, eliminando o
   parâmetro de rota como fonte de autoridade.
2. **Observabilidade** — OpenTelemetry (traces distribuídos cobrindo HTTP → outbox → AMQP →
   projeção), métricas Prometheus (lag do outbox, profundidade da DLQ, latência p99) e Grafana.
   Há correlation id fim a fim, que é a base para isso.
3. **Outbox como worker dedicado** — hoje roda no processo da API. Extraí-lo permite escalar
   escrita e publicação de forma independente. Com múltiplas réplicas, usar
   `SELECT ... FOR UPDATE SKIP LOCKED` para claim exclusivo das linhas.
4. **Reprocessamento e replay** — endpoint administrativo para reprocessar a DLQ e reconstruir a
   projeção a partir do zero. A projeção já é derivável, mas falta a ferramenta operacional.
5. **Fechamento contábil** — hoje o lançamento retroativo é limitado a 365 dias por invariante.
   Um sistema real teria períodos formalmente fechados, com lançamentos de ajuste.
6. **Testes de carga em ambiente dedicado** — os números de [desempenho](docs/desempenho.md) foram
   medidos em máquina de desenvolvimento, com o gerador disputando CPU com os serviços. Faltam
   execuções em nós dedicados, com múltiplas réplicas, e teste de caos derrubando o broker
   *durante* a carga.
7. **Particionamento de `daily_balances`** por faixa de data, quando o volume justificar.
8. **Versionamento de contratos** — os eventos já têm nome de wire estável e desacoplado do tipo
   CLR; falta publicá-los como pacote NuGet versionado independentemente.
9. **Múltiplas moedas** — `Money` já é currency-aware e o `DailyBalance` recusa mistura de moedas,
   mas a API assume BRL como padrão. Faltaria consolidar por `(merchant, date, currency)`.
