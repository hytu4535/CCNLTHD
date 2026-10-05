# stripe-webhooks-dotnet

A minimal, production-correct **.NET 8** Stripe webhook reference implementation.

This project demonstrates how to build a **transactional, idempotent, and operationally safe** Stripe webhook processor using:

- ASP.NET Core (Minimal APIs)
- EF Core
- PostgreSQL (Docker)
- SQLite in-memory (integration tests)
- Serilog + Seq
- WebApplicationFactory-based API testing

---

## Why this repo exists

Many Stripe examples show how to receive a webhook.

Very few demonstrate:

- ✅ Proper **signature validation**
- ✅ **Idempotent** event handling (dedupe by Stripe `evt_...` ID)
- ✅ Transactional processing (retry-safe)
- ✅ Durable persistence
- ✅ Full API-level integration tests
- ✅ Clean local dev experience (Docker + Stripe CLI)

This repository intentionally stays small while proving the important behaviors that matter in production payments systems.

---

## Architecture overview

**Core behaviors implemented:**

- Stripe webhook signature verification (`Stripe-Signature` header)
- Idempotent event handling via `ProcessedEvents` table
- Transactional event processing (single commit boundary)
- Structured event storage in `PaymentEvents`
- Retry-safe semantics (returns 500 on processing failures)
- Clean separation of signature verification and handler logic

**High-level flow:**

1. Stripe sends event
2. Signature validated using Stripe SDK
3. `ProcessedEvents` checked for existing `evt_...`
4. Transaction started
5. Event stored in `PaymentEvents`
6. Business logic executed
7. `ProcessedEvents` record inserted
8. Transaction committed
9. HTTP 200 returned

Duplicate events:

- Detected via `ProcessedEvents`
- Skipped safely
- Still return HTTP 200 (to stop Stripe retries)

Integration tests boot the real API pipeline and verify idempotency behavior end-to-end.

More detail:  
📄 [docs/10-architecture.md](docs/10-architecture.md)

---

## Quickstart

This repo supports two local development modes:

- **Option A: Full Docker stack** (API + Postgres + Seq)
- **Option B: Dev mode** (Postgres + Seq in Docker, run API in VS Code / `dotnet run`)

> Tip: start with **Option A** first to validate everything end-to-end, then switch to **Option B** when you want breakpoints and live debugging.

---

## 🚨 Stripe CLI is REQUIRED (do not skip this)

### Install Stripe CLI

If you don’t have it installed yet, follow Stripe’s official install guide:
- https://stripe.com/docs/stripe-cli

Sanity check:
```bash
stripe --version

**This webhook endpoint validates real Stripe signatures.**  
If you do **not** run the Stripe CLI listener, your app will appear healthy but you will receive **zero webhook deliveries** — meaning **no rows in `ProcessedEvents` or `PaymentEvents`**.

### Start the Stripe CLI listener (keep it running)

```bash
stripe login
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

Stripe CLI will print a signing secret like `whsec_...`.

Copy that exact value into:

- `.env.runtime` (Option A), then restart the API container:
  ```bash
  docker compose restart api
  ```
- `src/StripeWebhooks.Api/appsettings.Development.json` (Option B), then restart your local app.

### Sanity check

```bash
stripe trigger payment_intent.succeeded
```

If you do not see webhook logs immediately after triggering:  
➡️ You forgot `stripe listen`, or you configured the wrong `whsec_...`.

---

## Option A — Full Docker stack

### 1) Configure runtime environment

```bash
cp .env.runtime.example .env.runtime
```

Edit `.env.runtime`:

- `Stripe__SecretKey=sk_test_...`
- `Stripe__WebhookSecret=whsec_...`
- `ConnectionStrings__Db=Host=db;Port=5432;Database=stripe_webhooks_dotnet;Username=postgres;Password=postgres`
- `Serilog__WriteTo__1__Args__serverUrl=http://seq:80`

> `.env.runtime` is consumed via `env_file:` in `docker-compose.yml`.

### 2) Start the stack

```bash
docker compose up -d --build
```

Services:

- API: http://localhost:7020
- Swagger UI: http://localhost:7020/swagger
- Health: http://localhost:7020/health
- Seq: http://localhost:5341
- Postgres: localhost:55432

### Orders API

Open Swagger at http://localhost:7020/swagger. Create a product first, then create a PaymentIntent and use both returned IDs when creating an order:

```json
POST /api/products
{ "name": "Coffee", "price": 4.50 }

POST /demo/payment-intents
{ "amount": 900, "currency": "nzd" }

POST /api/orders
{ "productId": 1, "quantity": 2, "paymentIntentId": "pi_..." }
```

Create the order before confirming that PaymentIntent using `POST /demo/payment-intents/{id}/confirm`; this ensures the webhook can find the order. Orders can be listed, read by ID, updated with `PUT /api/orders/{id}`, and deleted with `DELETE /api/orders/{id}`. The order stores its PaymentIntent ID and starts in `Pending`; signed `payment_intent.succeeded` and `payment_intent.payment_failed` webhooks locate the order by that ID and update its payment status to `Paid` or `Failed`. Invalid product IDs, quantities, or PaymentIntent IDs return a validation/error response.

Optional:

```bash
docker compose --profile tools up -d
```

### 3) Start Stripe CLI forwarding

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

---

## Option B — Dev mode (run API locally)

### 1) Start infrastructure only

```bash
docker compose -f docker-compose.dev.yml up -d
```

Starts:

- Postgres → localhost:55432
- Seq → http://localhost:5341

### 2) Configure local Stripe + DB settings

Edit:

```
src/StripeWebhooks.Api/appsettings.Development.json
```

Example:

```json
"Stripe": {
  "SecretKey": "sk_test_...",
  "WebhookSecret": "whsec_...",
  "SkipSignatureValidation": false
}
```

Important differences from Docker mode:

- DB host = `localhost`
- Seq URL = `http://localhost:5341`

### 3) Run the API

```bash
dotnet run --project src/StripeWebhooks.Api
```

### 4) Start Stripe CLI forwarding

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

---

## Demo API endpoints

All demo endpoints live under `/demo`.

### Create PaymentIntent

`POST /demo/payment-intents`

### Confirm PaymentIntent

`POST /demo/payment-intents/{id}/confirm`

### Refund PaymentIntent

`POST /demo/payment-intents/{id}/refund`

Supports:

- Full refund
- Partial refund
- Idempotency-Key header

### View stored events

`GET /demo/payment-events?take=50`

---

## What webhooks are handled?

Currently handled event types:

- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `refund.created`
- `refund.updated`
- `charge.refunded`

Events are persisted in:

- `ProcessedEvents` (idempotency gate)
- `PaymentEvents` (structured event log)

---

## Tests

Includes:

- Unit tests
- Full integration tests

Integration tests:

- Boot real ASP.NET Core host
- Use SQLite in-memory for real transaction semantics
- Validate duplicate webhook handling
- Verify HTTP status semantics

```bash
dotnet test
```

More detail:  
📄 [docs/50-testing.md](docs/50-testing.md)

---

## Stripe API version mismatch (local dev)

Local `stripe listen` events may use a different API version than your installed Stripe SDK.

The webhook validator tolerates this locally using:

```
throwOnApiVersionMismatch: false
```

Signature validation is still enforced.

---

## Troubleshooting

### No webhook inserts

1. Stripe CLI not running  
2. Wrong `whsec_...` configured  
3. Calling webhook endpoint manually without valid signature

### Docker logging issues

Inside Docker use:

```
http://seq:80
```

Never `localhost`.

---

## What this project signals

- Production-grade webhook correctness
- Stripe integration maturity
- Idempotency discipline
- Operational awareness
- Deterministic integration testing

It is intentionally focused and does not attempt to implement a full payment orchestration engine.

---

## Documentation

- [00-quickstart.md](docs/00-quickstart.md)
- [10-architecture.md](docs/10-architecture.md)
- [20-webhook-idempotency.md](docs/20-webhook-idempotency.md)
- [30-local-dev-stripe-cli.md](docs/30-local-dev-stripe-cli.md)
- [40-troubleshooting.md](docs/40-troubleshooting.md)
- [50-testing.md](docs/50-testing.md)

---

## License

MIT
