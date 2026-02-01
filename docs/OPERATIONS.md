# NetCommerce Operations Guide

> **Monitoring, observability, and operational procedures**

---

## Table of Contents

1. [Observability Stack](#observability-stack)
2. [Metrics](#metrics)
3. [Logging](#logging)
4. [Distributed Tracing](#distributed-tracing)
5. [Health Checks](#health-checks)
6. [Alerting](#alerting)
7. [Dashboards](#dashboards)
8. [Runbooks](#runbooks)
9. [Incident Response](#incident-response)
10. [Capacity Planning](#capacity-planning)

---

## Observability Stack

### Components

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    OBSERVABILITY ARCHITECTURE                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────┐       │
│  │                    NetCommerce API                               │       │
│  │                                                                  │       │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐         │       │
│  │  │   Metrics    │  │   Logging    │  │   Tracing    │         │       │
│  │  │ (OpenTelemetry)│  │ (Serilog)  │  │(OpenTelemetry)│         │       │
│  │  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘         │       │
│  └─────────┼─────────────────┼─────────────────┼────────────────────┘       │
│            │                 │                 │                            │
│            ▼                 ▼                 ▼                            │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                     │
│  │  Prometheus  │  │     Seq      │  │    Jaeger    │                     │
│  │  (Metrics)   │  │  (Logging)   │  │  (Tracing)   │                     │
│  └──────┬───────┘  └──────────────┘  └──────────────┘                     │
│         │                                                                   │
│         ▼                                                                   │
│  ┌──────────────┐                                                          │
│  │   Grafana    │  ◀── Unified dashboards                                  │
│  │ (Dashboards) │                                                          │
│  └──────────────┘                                                          │
│                                                                             │
│  PRODUCTION ALTERNATIVE:                                                    │
│  ┌──────────────────────────────────────────────────────────┐              │
│  │  Azure Monitor / Application Insights / Datadog          │              │
│  │  (All-in-one: Metrics + Logs + Traces + Alerts)         │              │
│  └──────────────────────────────────────────────────────────┘              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Development vs Production

| Aspect | Development | Production |
|--------|-------------|------------|
| Logging | Seq (localhost:5341) | Azure Monitor / Datadog |
| Metrics | Prometheus + Grafana | Azure Monitor Metrics |
| Tracing | Jaeger | Azure App Insights |
| Alerting | None | PagerDuty / OpsGenie |

---

## Metrics

### OpenTelemetry Configuration

```csharp
// src/Api/Program.cs
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        metrics.AddRuntimeInstrumentation();

        // Custom meters
        metrics.AddMeter("NetCommerce.Ordering");
        metrics.AddMeter("NetCommerce.Inventory");
        metrics.AddMeter("NetCommerce.Payments");
        metrics.AddMeter("NetCommerce.Messaging");

        // Export to Prometheus
        metrics.AddPrometheusExporter();
    });
```

### Key Business Metrics

```csharp
public class OrderingMetrics
{
    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _ordersCompleted;
    private readonly Counter<long> _ordersCancelled;
    private readonly Histogram<double> _orderValue;
    private readonly Histogram<double> _checkoutDuration;

    public OrderingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("NetCommerce.Ordering");

        _ordersCreated = meter.CreateCounter<long>(
            "orders.created",
            description: "Total orders created");

        _ordersCompleted = meter.CreateCounter<long>(
            "orders.completed",
            description: "Total orders completed successfully");

        _ordersCancelled = meter.CreateCounter<long>(
            "orders.cancelled",
            description: "Total orders cancelled");

        _orderValue = meter.CreateHistogram<double>(
            "orders.value",
            unit: "GEL",
            description: "Order value distribution");

        _checkoutDuration = meter.CreateHistogram<double>(
            "checkout.duration",
            unit: "ms",
            description: "Time from cart to order completion");
    }

    public void RecordOrderCreated() => _ordersCreated.Add(1);
    public void RecordOrderValue(decimal amount) => _orderValue.Record((double)amount);
}
```

### Infrastructure Metrics

| Metric | Description | Threshold |
|--------|-------------|-----------|
| `http_requests_duration_seconds` | Request latency | P99 < 500ms |
| `http_requests_total` | Request count | - |
| `db_connections_active` | Active DB connections | < 80% of pool |
| `redis_commands_duration_seconds` | Redis latency | P99 < 10ms |
| `wolverine_messages_processed` | Message throughput | - |
| `wolverine_messages_failed` | Failed messages | < 1% |

### Prometheus Queries

```promql
# Request rate
rate(http_requests_total[5m])

# Error rate
sum(rate(http_requests_total{status_code=~"5.."}[5m])) / sum(rate(http_requests_total[5m]))

# P99 latency
histogram_quantile(0.99, rate(http_requests_duration_seconds_bucket[5m]))

# Orders per minute
rate(orders_created_total[1m]) * 60

# Checkout success rate
sum(rate(orders_completed_total[5m])) / sum(rate(orders_created_total[5m]))
```

---

## Logging

### Structured Logging Configuration

```csharp
// src/Api/Program.cs
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProperty("Application", "NetCommerce.Api")
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341");
});
```

### Logging Conventions

```csharp
// ✅ DO: Use structured logging with named properties
_logger.LogInformation(
    "Order {OrderId} created for customer {CustomerId}. Total: {Total} {Currency}",
    order.Id,
    order.CustomerId,
    order.Total.Amount,
    order.Total.Currency);

// ✅ DO: Include correlation IDs
_logger.LogInformation(
    "Processing payment for order {OrderId}. CorrelationId: {CorrelationId}",
    orderId,
    Activity.Current?.Id);

// ❌ DON'T: Log PII directly
_logger.LogInformation("Order for {Email}", customer.Email);  // Don't do this

// ✅ DO: Use appropriate log levels
_logger.LogDebug("Entering method {Method}", nameof(ProcessPayment));
_logger.LogInformation("Order {OrderId} submitted", orderId);
_logger.LogWarning("Payment retry {Attempt} for order {OrderId}", attempt, orderId);
_logger.LogError(ex, "Payment failed for order {OrderId}", orderId);
_logger.LogCritical("Database connection lost");
```

### Log Levels

| Level | Use Case | Example |
|-------|----------|---------|
| Debug | Detailed diagnostics | Method entry/exit |
| Information | Normal operations | Order created, payment processed |
| Warning | Recoverable issues | Retry attempt, fallback used |
| Error | Operation failed | Payment failed, external service down |
| Critical | System failure | Database unreachable, data corruption |

### Seq Queries

```
# Find all errors for an order
OrderId = "550e8400-e29b-41d4-a716-446655440000" and @Level = 'Error'

# Find slow requests (> 500ms)
@Properties.ElapsedMilliseconds > 500

# Find all saga state transitions
SourceContext like '%Saga%'

# Find dead letter messages
MessageType = 'DeadLetterEnvelope'

# Error rate by endpoint
@Level = 'Error' | select count(*) group by RequestPath

# Recent authentication failures
@Level = 'Warning' and Message like '%Authentication failed%'
```

---

## Distributed Tracing

### OpenTelemetry Configuration

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddEntityFrameworkCoreInstrumentation();
        tracing.AddNpgsql();
        tracing.AddSource("Wolverine");

        // Export to Jaeger (dev) or Azure Monitor (prod)
        if (builder.Environment.IsDevelopment())
            tracing.AddJaegerExporter();
        else
            tracing.AddAzureMonitorTraceExporter();
    });
```

### Trace Propagation

```csharp
// Traces automatically propagate through:
// - HTTP requests (via W3C Trace Context headers)
// - Wolverine messages (via envelope metadata)
// - Database queries (via EF Core instrumentation)

// Access current trace
var traceId = Activity.Current?.TraceId.ToString();
var spanId = Activity.Current?.SpanId.ToString();
```

### Custom Spans

```csharp
public async Task ProcessPaymentAsync(PaymentCommand command)
{
    using var activity = ActivitySource.StartActivity("ProcessPayment");
    activity?.SetTag("order.id", command.OrderId.ToString());
    activity?.SetTag("payment.amount", command.Amount.Amount);

    try
    {
        var result = await _gateway.ChargeAsync(command);
        activity?.SetTag("payment.success", result.IsSuccess);
        activity?.SetTag("payment.transaction_id", result.TransactionId);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

---

## Health Checks

### Endpoint Configuration

```csharp
// src/Api/Program.cs
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres")
    .AddRedis(redisConnectionString, name: "redis")
    .AddUrlGroup(new Uri(keycloakUrl), name: "keycloak")
    .AddCheck<MeilisearchHealthCheck>("meilisearch")
    .AddCheck<WolverineHealthCheck>("wolverine");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // Just check if app is running
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,  // Check all dependencies
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### Health Check Response

```json
GET /health/ready

{
  "status": "Healthy",
  "totalDuration": "00:00:00.1234567",
  "entries": {
    "postgres": {
      "status": "Healthy",
      "duration": "00:00:00.0234567"
    },
    "redis": {
      "status": "Healthy",
      "duration": "00:00:00.0012345"
    },
    "keycloak": {
      "status": "Healthy",
      "duration": "00:00:00.0456789"
    },
    "meilisearch": {
      "status": "Healthy",
      "duration": "00:00:00.0123456"
    },
    "wolverine": {
      "status": "Healthy",
      "duration": "00:00:00.0001234"
    }
  }
}
```

### Kubernetes Probes

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 10
  failureThreshold: 3

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 5
  failureThreshold: 3
```

---

## Alerting

### Alert Rules

```yaml
# prometheus/alerts.yaml
groups:
- name: netcommerce
  rules:
  # High error rate
  - alert: HighErrorRate
    expr: sum(rate(http_requests_total{status_code=~"5.."}[5m])) / sum(rate(http_requests_total[5m])) > 0.01
    for: 5m
    labels:
      severity: critical
    annotations:
      summary: "High error rate detected"
      description: "Error rate is {{ $value | humanizePercentage }}"

  # High latency
  - alert: HighLatency
    expr: histogram_quantile(0.99, rate(http_requests_duration_seconds_bucket[5m])) > 0.5
    for: 5m
    labels:
      severity: warning
    annotations:
      summary: "High P99 latency"
      description: "P99 latency is {{ $value | humanizeDuration }}"

  # Database connection exhaustion
  - alert: DatabaseConnectionsHigh
    expr: pg_stat_activity_count / pg_settings_max_connections > 0.8
    for: 5m
    labels:
      severity: warning
    annotations:
      summary: "Database connections > 80%"

  # Wolverine message backlog
  - alert: MessageBacklog
    expr: wolverine_incoming_messages_queued > 1000
    for: 10m
    labels:
      severity: warning
    annotations:
      summary: "Wolverine message backlog building up"

  # Dead letter queue growth
  - alert: DeadLetterGrowth
    expr: increase(wolverine_dead_letters_total[1h]) > 10
    labels:
      severity: warning
    annotations:
      summary: "Dead letter messages increasing"

  # Saga stuck
  - alert: SagaStuck
    expr: wolverine_saga_active{state="ManualInterventionRequired"} > 0
    labels:
      severity: critical
    annotations:
      summary: "Saga requires manual intervention"
```

### Severity Levels

| Severity | Response Time | Action |
|----------|---------------|--------|
| Critical | < 15 minutes | Page on-call engineer |
| Warning | < 4 hours | Create ticket, investigate |
| Info | Next business day | Review in standup |

---

## Dashboards

### Key Dashboards

#### 1. Business Overview
- Orders per hour (trend)
- Revenue (today vs yesterday)
- Checkout conversion rate
- Payment success rate
- Active users

#### 2. API Performance
- Request rate by endpoint
- P50, P95, P99 latency
- Error rate by status code
- Slow endpoints (> 500ms)

#### 3. Infrastructure Health
- Database connections
- Redis memory usage
- CPU / Memory utilization
- Disk I/O

#### 4. Messaging
- Messages processed per minute
- Message processing latency
- Dead letter queue size
- Active sagas by state

### Sample Grafana Dashboard JSON

```json
{
  "title": "NetCommerce Overview",
  "panels": [
    {
      "title": "Orders Per Minute",
      "type": "graph",
      "targets": [
        {
          "expr": "rate(orders_created_total[1m]) * 60"
        }
      ]
    },
    {
      "title": "Request Latency (P99)",
      "type": "gauge",
      "targets": [
        {
          "expr": "histogram_quantile(0.99, rate(http_requests_duration_seconds_bucket[5m])) * 1000"
        }
      ],
      "thresholds": [
        { "value": 200, "color": "green" },
        { "value": 500, "color": "yellow" },
        { "value": 1000, "color": "red" }
      ]
    }
  ]
}
```

---

## Runbooks

### Runbook: High Error Rate

```markdown
## Symptoms
- Alert: HighErrorRate triggered
- Error rate > 1%

## Investigation Steps
1. Check Seq for error patterns:
   ```
   @Level = 'Error' | select count(*) group by @Exception.Type
   ```

2. Identify affected endpoints:
   ```
   @Level = 'Error' | select count(*) group by RequestPath
   ```

3. Check recent deployments:
   - Was there a recent deployment?
   - Rollback if necessary

4. Check infrastructure:
   - Database connectivity
   - Redis connectivity
   - Keycloak availability

## Resolution
- If database issue: See "Database Connectivity" runbook
- If external service: Enable circuit breaker fallback
- If code bug: Rollback deployment
```

### Runbook: Database Connectivity

```markdown
## Symptoms
- Health check /health/ready failing
- "Connection refused" or "timeout" errors in logs

## Investigation Steps
1. Check PostgreSQL status:
   ```bash
   kubectl get pods -l app=postgres
   ```

2. Check connection count:
   ```sql
   SELECT count(*) FROM pg_stat_activity;
   SELECT max_connections FROM pg_settings;
   ```

3. Check for long-running queries:
   ```sql
   SELECT pid, now() - query_start AS duration, query
   FROM pg_stat_activity
   WHERE state = 'active'
   ORDER BY duration DESC
   LIMIT 10;
   ```

## Resolution
- If connection exhaustion: Increase pool size or add PgBouncer
- If long queries: Kill blocking queries, add indexes
- If Pod failure: Restart or failover to replica
```

### Runbook: Saga Stuck

```markdown
## Symptoms
- Alert: SagaStuck triggered
- Orders stuck in "ManualInterventionRequired" state

## Investigation Steps
1. Query stuck sagas:
   ```sql
   SELECT id, state->>'OrderId', state->>'FailureReason'
   FROM wolverine.saga_state
   WHERE state->>'State' = 'ManualInterventionRequired';
   ```

2. Check failure reason:
   - Payment refund failed?
   - Inventory release failed?

3. Check related systems:
   - Payment gateway status
   - Inventory service health

## Resolution
1. Fix underlying issue (refund, inventory)
2. Manually complete compensation:
   ```sql
   -- Mark saga as failed after manual resolution
   UPDATE wolverine.saga_state
   SET state = jsonb_set(state, '{State}', '"Failed"')
   WHERE id = '<saga-id>';
   ```
3. Notify customer if needed
```

---

## Incident Response

### Severity Definitions

| Level | Description | Example |
|-------|-------------|---------|
| SEV1 | Complete outage | API unresponsive |
| SEV2 | Major degradation | Payments failing |
| SEV3 | Minor degradation | Search slow |
| SEV4 | Cosmetic issue | UI glitch |

### Incident Timeline

```
1. DETECT (T+0)
   └── Alert fires or customer reports issue

2. TRIAGE (T+5min)
   └── Assess severity and impact
   └── Page additional responders if needed

3. MITIGATE (T+15min)
   └── Apply immediate fix (rollback, restart, scale)
   └── Communicate status to stakeholders

4. RESOLVE (T+?)
   └── Root cause fixed
   └── Systems fully recovered

5. POSTMORTEM (T+48h)
   └── Document timeline
   └── Identify root cause
   └── Action items for prevention
```

### Communication Template

```markdown
**Incident Update - [SEV LEVEL] - [Title]**

**Status:** Investigating / Mitigating / Resolved
**Impact:** [Who is affected and how]
**Start Time:** [HH:MM UTC]
**Current Actions:** [What we're doing]
**Next Update:** [HH:MM UTC]

---
Example:

**Incident Update - SEV2 - Payment Processing Delays**

**Status:** Mitigating
**Impact:** ~15% of checkout attempts failing
**Start Time:** 14:30 UTC
**Current Actions:** Routing traffic to backup payment gateway
**Next Update:** 15:00 UTC
```

---

## Capacity Planning

### Key Capacity Metrics

| Resource | Current | Threshold | Growth Rate |
|----------|---------|-----------|-------------|
| API Pods | 4 | 10 max | Auto-scale |
| DB Connections | 200 | 500 max | 5%/month |
| Redis Memory | 2GB | 6GB max | 3%/month |
| Storage | 100GB | 256GB max | 10%/month |

### Scaling Triggers

```yaml
# Kubernetes HPA
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: netcommerce-api
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: netcommerce-api
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

### Load Testing Baseline

| Scenario | Target | Current |
|----------|--------|---------|
| Steady state | 100 RPS | ✅ 150 RPS |
| Peak (flash sale) | 500 RPS | ✅ 600 RPS |
| P99 latency | < 500ms | ✅ 350ms |
| Error rate | < 1% | ✅ 0.2% |

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce SRE Team
