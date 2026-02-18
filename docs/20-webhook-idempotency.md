# 20 - Webhook Idempotency

This document explains how **stripe-webhooks-dotnet** ensures webhook processing is **idempotent** and safe under retries.

Stripe webhook delivery is **at-least-once**. That means:

- Stripe can retry an event if your endpoint times out or returns a non-2xx response
- Duplicate deliveries can occur (network issues, retries, replay tools, local testing)
- Your handler must be safe to run **multiple times** for the same event

Idempotency is the difference between:
- ✅ “safe production integration”
- ❌ “double charges / duplicated refunds / corrupted state”

---

## What does “idempotent” mean here?

Given the same Stripe event `evt_123` delivered multiple times, your system must:

- **apply side effects only once**
- return **200 OK** for duplicates
- avoid duplicate database writes

---

## The Strategy Used In This Repo

### Dedupe by Stripe Event ID (`evt_...`)

Every Stripe event includes a globally-unique event ID, for example:

- `evt_1QabcXYZ...`

This repo uses a dedicated table:

- `ProcessedEvents`

to record which event IDs have already been processed.

---

## Processing Algorithm (High Level)

On each webhook request:

1. Verify signature
2. Parse Stripe `Event`
3. Begin DB transaction
4. Check if `ProcessedEvents` contains `stripeEvent.Id`
5. If yes:
   - log “already processed”
   - return 200 OK
6. If no:
   - insert into `ProcessedEvents`
   - dispatch handler logic
   - persist simplified `PaymentEvents` rows
   - commit transaction
   - return 200 OK

This guarantees:
- duplicate deliveries do not create duplicate side effects
- processing is atomic (all-or-nothing)

---

## Why the Transaction Matters

If we **didn't** use a transaction, this failure could happen:

1. Event `evt_123` arrives
2. Handler writes business data
3. Process crashes before writing dedupe row
4. Stripe retries `evt_123`
5. Handler writes business data again

Result: duplicated side effects.

By inserting the dedupe row inside the same transaction as the handler work, we get:

- atomicity
- strong correctness under failure

---

## Database Constraints (Recommended)

For stronger protection, ensure `ProcessedEvents.EventId` is unique.

This provides an additional layer of defense if concurrency occurs.

Example conceptually:

- `UNIQUE(EventId)`

Even if two requests race, only one can insert successfully.

---

## Idempotency vs. “Retry-safe”

Idempotency ensures **duplicate deliveries** don't cause duplicated effects.

Retry-safe means the system can handle:

- network failures
- timeouts
- Stripe replays
- manual replays during incidents

This repo demonstrates the *core* capability: safe dedupe.

A more advanced production build typically adds:
- processing status (Processing / Completed / Failed)
- attempts count and backoff
- dead-letter storage
- replay tooling

(See the separate advanced repo plan.)

---

## What About Idempotency-Key Headers?

Stripe API calls (create PaymentIntent, refunds) support `Idempotency-Key` headers.

This repo uses them on demo endpoints to ensure API-side idempotency when making Stripe API requests.

Examples:
- Creating PaymentIntent
- Confirming PaymentIntent
- Creating Refund

But note:

- Stripe API idempotency keys protect outbound API calls.
- Webhook idempotency protects inbound webhook processing.

You typically need **both** in production.

---

## How To Observe Idempotency Working

### 1) Generate events

Use Stripe CLI:

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
stripe trigger payment_intent.succeeded
```

### 2) Re-send the same event (replay)

Using Stripe CLI replay features (or by triggering the same type again), you should see:

- the second delivery is detected as already processed
- no duplicate `PaymentEvents` rows are inserted

### 3) Inspect logs in Seq

Open:

http://localhost:5341

Look for logs like:

- “Webhook already processed”
- event ID and type included

---

## Tests

This repo includes tests that validate idempotency behavior, e.g.:

- same `evt_...` twice → only one record persists

These tests are what make the repo credible for hiring managers:
they prove the correctness story.

---

## Common Gotchas (and how this repo avoids them)

### ❌ Returning non-2xx for benign duplicates
If you return 400/500 for duplicates, Stripe will retry unnecessarily.

✅ This repo returns 200 OK if `evt_...` is already processed.

### ❌ Doing expensive work before dedupe check
Always dedupe early.

✅ This repo checks ProcessedEvents before dispatching handlers.

### ❌ Not storing enough data to debug
In production you usually store raw payloads for replay.

✅ This repo keeps it minimal, but persists structured logs + simplified event rows for visibility.

---

## Summary

This repo achieves safe webhook idempotency by:

- Deduping on Stripe `evt_...` IDs
- Persisting dedupe state in Postgres
- Using a transaction so processing is atomic
- Returning 200 OK for duplicate deliveries

This is the core reliability requirement for any Stripe webhook integration.

---

## Next Docs

- `docs/30-local-dev-stripe-cli.md`
- `docs/40-troubleshooting.md`
