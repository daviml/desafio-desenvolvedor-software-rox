# Arquitetura — CashFlow

Documento complementar ao [README](../README.md), com o detalhamento visual da solução.

---

## 1. Visão de contêineres

```mermaid
flowchart LR
    subgraph Cliente
        U["Lojista<br/>(app / portal)"]
    end

    subgraph LaunchesCtx["Bounded Context: Lançamentos"]
        LA["Launches API<br/>:8081"]
        LDB[("PostgreSQL<br/>cashflow_launches<br/><br/>entries<br/>outbox_messages")]
        OD["Outbox Dispatcher<br/>(BackgroundService)"]
    end

    MQ{{"RabbitMQ<br/>topic: cashflow.events<br/>DLQ: cashflow.consolidation.dlq"}}

    subgraph ConsolidationCtx["Bounded Context: Consolidação"]
        CC["Consumer<br/>(BackgroundService)"]
        CA["Consolidation API<br/>:8082"]
        CDB[("PostgreSQL<br/>cashflow_consolidation<br/><br/>daily_balances<br/>processed_events")]
    end

    U -- "POST /api/v1/entries" --> LA
    U -- "GET .../daily-balance" --> CA

    LA -- "entry + evento<br/>na MESMA transação" --> LDB
    OD -- "lê pendentes" --> LDB
    OD -- "publica<br/>(confirms)" --> MQ
    MQ -- "consome<br/>(ack manual)" --> CC
    CC -- "projeção + dedup<br/>na MESMA transação" --> CDB
    CA -- "leitura indexada" --> CDB

    style LA fill:#1f6feb,color:#fff
    style CA fill:#1f6feb,color:#fff
    style MQ fill:#f0883e,color:#000
    style OD fill:#238636,color:#fff
    style CC fill:#238636,color:#fff
```

**A leitura nunca atravessa a escrita.** A Launches API não conhece a Consolidation API — as duas
só compartilham um contrato de mensagem. Nenhuma chamada síncrona entre serviços significa que
nenhuma falha de um derruba o outro.

---

## 2. Camadas de cada serviço

```mermaid
flowchart TD
    API["**Api**<br/>Minimal APIs · Swagger · rate limiting<br/>ProblemDetails · correlation id"]
    INFRA["**Infrastructure**<br/>EF Core · repositórios · outbox<br/>RabbitMQ · migrations"]
    APP["**Application**<br/>Casos de uso (1 handler = 1 operação)<br/>validators · DTOs · Result&lt;T&gt;"]
    DOM["**Domain**<br/>Agregados · value objects · invariantes<br/>eventos de domínio · **zero dependências**"]

    API --> INFRA
    INFRA --> APP
    APP --> DOM
    INFRA -.->|"implementa as portas<br/>declaradas por"| DOM

    style DOM fill:#238636,color:#fff
    style APP fill:#1f6feb,color:#fff
    style INFRA fill:#8957e5,color:#fff
    style API fill:#f0883e,color:#000
```

A seta tracejada é a **inversão de dependência**: `IEntryRepository` é declarada no domínio e
implementada na infraestrutura. Trocar EF Core por Dapper não toca uma linha de regra de negócio.

---

## 3. Fluxo de escrita — Transactional Outbox

```mermaid
sequenceDiagram
    autonumber
    participant C as Cliente
    participant A as Launches API
    participant H as RegisterEntryCommandHandler
    participant E as Entry (agregado)
    participant I as OutboxInterceptor
    participant DB as PostgreSQL
    participant D as OutboxDispatcher
    participant MQ as RabbitMQ

    C->>A: POST /api/v1/entries<br/>Idempotency-Key: venda-001
    A->>H: dispatch(RegisterEntryCommand)
    Note over H: decorators: logging → validação → handler
    H->>H: já existe essa Idempotency-Key?
    H->>E: Entry.Register(...)
    E-->>E: valida invariantes<br/>emite EntryRegisteredDomainEvent
    H->>DB: SaveChangesAsync()
    activate DB
    I->>I: domain event → contrato de integração
    I->>DB: INSERT outbox_messages
    Note over DB: entry + evento na MESMA transação
    deactivate DB
    A-->>C: 201 Created ✅
    Note over C,A: A resposta NÃO depende do broker

    rect rgb(35, 134, 54, 0.12)
        Note over D,MQ: assíncrono, fora do request
        D->>DB: SELECT pendentes (lote de 200)
        D->>MQ: BasicPublish (persistente + confirm)
        MQ-->>D: ack do broker
        D->>DB: UPDATE processed_at_utc
    end
```

Se o passo de publicação falhar, a linha permanece pendente com `attempt_count` incrementado e
`next_attempt_at_utc` no futuro (backoff exponencial + jitter). **O cliente já recebeu 201 e nada
se perde.**

---

## 4. Fluxo de consolidação — consumo idempotente

```mermaid
sequenceDiagram
    autonumber
    participant MQ as RabbitMQ
    participant K as Consumer
    participant P as DailyBalanceProjector
    participant DB as PostgreSQL
    participant DLQ as Dead Letter Queue

    MQ->>K: entrega (prefetch 64, ack manual)
    K->>K: resolve contrato pelo wire name
    K->>P: dispatch(EntryRegisteredIntegrationEvent)
    P->>DB: esse event_id já foi aplicado?

    alt já aplicado (redelivery)
        P-->>K: no-op
        K->>MQ: ack
    else novo
        P->>DB: carrega/cria DailyBalance do dia
        P->>P: ApplyCredit / ApplyDebit
        P->>DB: UPDATE daily_balances (WHERE version = @original)<br/>+ INSERT processed_events
        alt sucesso
            K->>MQ: ack
        else conflito de concorrência / erro transitório
            K->>K: retry com backoff (in-process)
            Note over K: esgotadas as tentativas
            K->>DLQ: nack (requeue: false)
        end
    end
```

Três garantias que se somam:

| Garantia | Mecanismo |
|---|---|
| Nada é aplicado duas vezes | `processed_events` com PK no `event_id`, na mesma transação |
| Nada é perdido em caso de crash | Ack manual — só após o handler concluir |
| Nada trava a fila para sempre | Retry limitado, depois dead-letter para inspeção |

---

## 5. Modelo de dados

```mermaid
erDiagram
    ENTRIES {
        uuid id PK "GUID v7 - inserção sequencial"
        uuid merchant_id "IDX (merchant_id, entry_date)"
        int type "1=Credit 2=Debit"
        numeric amount "18,2 - sempre positivo"
        char currency "ISO-4217"
        date entry_date "dia útil do lançamento"
        varchar description
        varchar category "nullable"
        int status "1=Active 2=Cancelled"
        varchar idempotency_key "UNIQUE (merchant_id, idempotency_key)"
        timestamptz registered_at_utc
        timestamptz cancelled_at_utc "nullable"
        varchar cancellation_reason "nullable"
    }

    OUTBOX_MESSAGES {
        uuid id PK "= event_id do evento de integração"
        varchar type "wire name do contrato"
        text payload "JSON"
        timestamptz occurred_at_utc
        timestamptz processed_at_utc "NULL = pendente"
        int attempt_count
        timestamptz next_attempt_at_utc "backoff"
        varchar last_error
    }

    DAILY_BALANCES {
        uuid id PK
        uuid merchant_id "UNIQUE (merchant_id, date)"
        date date
        char currency
        numeric total_credits "18,2"
        numeric total_debits "18,2"
        int credit_count
        int debit_count
        timestamptz last_updated_at_utc
        int version "concurrency token"
    }

    PROCESSED_EVENTS {
        uuid event_id PK "dedup - exactly-once effect"
        varchar event_type
        timestamptz processed_at_utc
    }
```

`ENTRIES` e `OUTBOX_MESSAGES` vivem no banco do serviço de lançamentos; `DAILY_BALANCES` e
`PROCESSED_EVENTS`, no da consolidação. **Não há chave estrangeira entre os dois lados** — é
justamente essa ausência de acoplamento que permite escalar, versionar e falhar de forma
independente. O `balance` não é coluna: é derivado (`total_credits - total_debits`), para que não
exista a possibilidade de divergir dos totais.

---

## 6. Ciclo de vida de um lançamento

```mermaid
stateDiagram-v2
    [*] --> Active: Entry.Register()<br/>emite cashflow.entry.registered
    Active --> Cancelled: Entry.Cancel()<br/>emite cashflow.entry.cancelled
    Cancelled --> Cancelled: rejeitado ❌<br/>DomainException entry.already_cancelled

    note right of Active
        Consolidação: soma em
        total_credits ou total_debits
    end note

    note right of Cancelled
        Consolidação: compensa
        (reverse), nunca exclui
    end note
```

Não há transição de saída de `Cancelled`: registro financeiro não se apaga nem se ressuscita.
Correções são novos lançamentos.

---

## 7. Contratos de integração

Publicados no exchange `cashflow.events` (tipo *topic*), usando o **wire name** como routing key:

| Wire name | Quando | Efeito na consolidação |
|---|---|---|
| `cashflow.entry.registered` | Lançamento registrado e persistido | Soma ao total de créditos ou débitos do dia |
| `cashflow.entry.cancelled` | Lançamento cancelado | Compensa o valor original |

```jsonc
// cashflow.entry.registered
{
  "eventId": "01a01005-3a13-76fd-903c-2e33a04e5531",  // chave de deduplicação
  "occurredAtUtc": "2026-08-17T13:59:33.13+00:00",
  "correlationId": "0HNNSCSTQQJU2:00000001",
  "entryId": "01a01005-3a13-76fd-903c-2e33a04e5531",
  "merchantId": "3efabaf2-57c5-4563-80e4-7d39714bc5e7",
  "type": "Credit",                                    // nome, não ordinal
  "amount": 1500.00,                                   // sempre positivo
  "currency": "BRL",
  "entryDate": "2026-08-17",
  "description": "Venda no cartao"
}
```

Duas escolhas deliberadas de contrato:

- **O wire name é desacoplado do tipo CLR.** Renomear a classe, mover de namespace ou reescrever o
  produtor em outra linguagem não quebra o consumidor.
- **Enums viajam como nome, não como ordinal.** Reordenar o enum não corrompe mensagens em trânsito.

---

## 8. Onde cada requisito não funcional é atendido

```mermaid
flowchart TB
    subgraph R1["'Operante mesmo com falha na consolidação'"]
        direction LR
        A1["Outbox transacional"] --> A2["Publicação em background"]
        A2 --> A3["Backoff + jitter"]
        A3 --> A4["Circuit breaker"]
        A4 --> A5["Broker = Degraded,<br/>nunca Unhealthy"]
    end

    subgraph R2["'50 req/s no pico, até 5% de perda'"]
        direction LR
        B1["Projeção pré-agregada<br/>(custo constante)"] --> B2["OutputCache 5s"]
        B2 --> B3["Rate limit token bucket<br/>+ fila limitada"]
        B3 --> B4["429 + Retry-After<br/>(degradação graciosa)"]
    end

    subgraph R3["Consistência do saldo"]
        direction LR
        C1["Idempotency-Key<br/>na entrada"] --> C2["Dedup por event_id<br/>no consumo"]
        C2 --> C3["Concorrência otimista<br/>(version)"]
        C3 --> C4["Compensação<br/>em vez de exclusão"]
    end

    style R1 fill:#238636,color:#fff
    style R2 fill:#1f6feb,color:#fff
    style R3 fill:#8957e5,color:#fff
```

---

## 9. Evolução natural da arquitetura

O desenho de hoje é o mínimo coerente para o problema. Os pontos de crescimento já estão isolados:

| Pressão | Resposta | O que já está preparado |
|---|---|---|
| Volume de escrita | Escalar réplicas da Launches API | Stateless; outbox por linha, sem estado em memória |
| Volume de leitura | Escalar réplicas da Consolidation API + read replicas | Leitura já é `AsNoTracking` e cacheável |
| Lag de consolidação | Extrair o dispatcher para worker dedicado | `OutboxDispatcher` é uma classe isolada e testável |
| Muitos consumidores | Aumentar `prefetch` / `ConsumerConcurrency` | Configuração, sem mudança de código |
| Outro transporte (Kafka, SQS) | Novo adaptador de `IIntegrationEventPublisher` | A aplicação só conhece a abstração |
| Histórico muito grande | Particionar `daily_balances` por data | A projeção já é derivável e reconstruível |
