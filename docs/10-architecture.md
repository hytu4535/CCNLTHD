# 10 - Architecture

This document explains the architecture of **stripe-webhooks-dotnet** and the design choices that make it safe, reproducible, and easy to evaluate.

> This repo is intentionally *minimal*.  
> It focuses on the fundamentals: webhook verification, idempotency, durable persistence, and clean operational visibility.

---

## High-level Overview

**Components** (via Docker Compose):

- **API**: .NET 8 Minimal API (port **7020**)
- **Postgres**: event persistence + idempotency store
- **Seq**: structured logs and real-time inspection
- **pgAdmin (optional)**: database inspection (tools profile)

---

## Primary Flows

### 1) Webhook Processing Flow

**Route:** `POST /webhooks/stripe`

1. Receive HTTP webhook request from Stripe (forwarded via Stripe CLI locally).
2. Validate webhook signature using the `Stripe-Signature` header + configured `whsec_...` secret.
3. Construct Stripe `Event` object.
4. Enforce **idempotency** using the Stripe `evt_...` event ID.
5. Dispatch to a small set of event handlers.
6. Persist a simplified record into `PaymentEvents` for demo visibility.
7. Return HTTP 200 on success (or a safe 400 on invalid signature / payload).

Key design goal: **safe retry behavior** without duplicated side effects.

---

### 2) PaymentIntent Demo Flow

**Routes:**
- `POST /demo/payment-intents`
- `POST /demo/payment-intents/{id}/confirm`

This flow is a demo harness for generating Stripe traffic and observing the webhook processor.

- The API creates a PaymentIntent using Stripe’s API.
- The API confirms the PaymentIntent server-side (defaults to `pm_card_visa`).
- Stripe emits webhook events (`payment_intent.*`), forwarded to this repo.
- The PaymentIntent ID is supplied when creating an order and stored on that order.
- Success and failure webhooks locate the order by PaymentIntent ID and update its payment status.
- The repo persists simplified event rows for inspection.

---

### 3) Refund Demo Flow

**Route:**
- `POST /demo/payment-intents/{id}/refund`

Creates a refund for a PaymentIntent:

- Full refund (omit amount)
- Partial refund (minor units)

Stripe emits refund-related webhook events:

- `refund.created`
- `refund.updated`
- `charge.refunded`

These get recorded into `PaymentEvents` for inspection.

---

## Persistence Model (EF Core + Postgres)

This repo uses EF Core for simplicity.

### Tables

#### `ProcessedEvents`

Purpose: **idempotency guard**

Stores Stripe event IDs (`evt_...`) that have already been processed.

- `EventId` (unique)
- `EventType`
- `ProcessedAt`

If a webhook is retried by Stripe (or replayed), we detect the event ID and short-circuit safely.

#### `PaymentEvents`

Purpose: lightweight audit for demo inspection

Stores simplified records extracted from webhook payloads:

- `StripeEventId`
- `Kind` (Stripe event type)
- `PaymentIntentId`
- `Amount`
- `Currency`
- `OccurredAt`

This is not a full payment ledger — it’s a demo visibility layer.

#### `Orders`

Each order is linked to a product and stores its Stripe `PaymentIntentId` and payment status. Orders start as `Pending`; `payment_intent.succeeded` changes the status to `Paid`, and `payment_intent.payment_failed` changes it to `Failed`.

---

## Stripe Integration Details

### Signature Validation

The webhook endpoint validates incoming payloads using:

- raw JSON body
- `Stripe-Signature` header
- webhook signing secret (`whsec_...`)

If signature verification fails, the endpoint returns **400**.

### API Version Mismatch (Local Dev)

When using `stripe listen` to forward webhooks, Stripe/CLI may emit events with an API version that differs from the version expected by the Stripe .NET SDK.

To avoid crashing during local development, event construction uses:

- `throwOnApiVersionMismatch: false`

This **does not** disable signature validation.
It only relaxes strict API version matching so the demo remains stable when Stripe updates versions.

In production, you’d typically:

- keep Stripe SDK up to date, and/or
- pin webhook endpoint API version behavior and monitor drift.

---

## Logging + Observability

### Structured Logs

The API uses structured logging (ILogger / Serilog-to-Seq config in Compose).

At minimum, log lines should include:

- Stripe Event ID (`evt_...`)
- Event Type (e.g. `payment_intent.succeeded`)
- PaymentIntentId (when present)
- Stripe RequestId (when available from Stripe exceptions)

### Seq

Seq runs at:

- http://localhost:5341

This lets reviewers “watch the system work” in real time during webhook tests.

---

## Folder Structure

```
src/StripeWebhooks.Api/
  Endpoints/                 # Minimal API endpoint mappings
  Stripe/                    # Signature verifier + webhook handler
  Persistence/
    Entities/                # EF Core entities
    AppDbContext.cs
  Program.cs                 # App wiring / DI / middleware
tests/StripeWebhooks.Tests/  # Unit + integration tests
docs/                        # Deeper docs, diagrams
scripts/                     # Convenience scripts
```

---

## Why This Repo Stays Small

This project is designed to be evaluated quickly:

- clone
- docker compose up
- stripe listen
- trigger webhooks
- see logs + DB rows

A separate “advanced” repo will demonstrate:

- background workers
- outbox
- dead-letter + replay
- DDD aggregates
- reconciliation tooling

For this repo: correctness, clarity, and reproducibility win.

---

## Next Docs

- `docs/20-webhook-idempotency.md`
- `docs/30-local-dev-stripe-cli.md`
- `docs/40-troubleshooting.md`
