# 30 – Local Development with Stripe CLI

This guide explains how to use **Stripe CLI** with `stripe-webhooks-dotnet` for realistic local development.

Stripe CLI is critical because:

- It sends **real, signed webhook requests**
- It includes a valid `Stripe-Signature` header
- It allows safe triggering of Stripe events in test mode
- It mirrors real-world webhook retry behavior

> Calling `/webhooks/stripe` manually (e.g., via Postman or curl) will fail unless you generate a valid Stripe signature header. Use Stripe CLI.

---

## ✅ Prerequisites

- Stripe account (test mode)
- Stripe CLI installed → https://stripe.com/docs/stripe-cli
- Project running via Docker (`docker compose up -d --build`)
- `.env.runtime` configured (see Quickstart)

---

## 1️⃣ Start the Application Stack

```bash
docker compose up -d --build
```

Expected services:

- API → http://localhost:7020
- Health → http://localhost:7020/health
- Seq → http://localhost:5341

Verify API health:

```bash
curl http://localhost:7020/health
```

---

## 2️⃣ Authenticate Stripe CLI

```bash
stripe login
```

This opens a browser and authenticates your CLI session.

Sanity check:

```bash
stripe --version
```

---

## 3️⃣ Start Webhook Forwarding (REQUIRED)

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

You will see output similar to:

```text
> Ready! Your webhook signing secret is whsec_XXXXXXXX
```

Copy the `whsec_...` value.

Keep this terminal running.

---

## 4️⃣ Configure the Webhook Secret

Edit `.env.runtime` and set:

```bash
Stripe__WebhookSecret=whsec_XXXXXXXX
```

Then restart the API container:

```bash
docker compose restart api
```

If the webhook secret does not match the current Stripe CLI session, signature validation will fail.

---

## 5️⃣ Trigger Stripe Events Directly

Stripe CLI can generate events without manually creating objects:

```bash
stripe trigger payment_intent.succeeded
stripe trigger payment_intent.payment_failed
stripe trigger refund.created
```

These will be delivered to your local webhook endpoint and processed transactionally.

---

## 6️⃣ Run the Full Demo Loop (Recommended)

This is the fastest way to evaluate the repository.

### A) Create a PaymentIntent

```bash
curl -s -X POST http://localhost:7020/demo/payment-intents   -H "Content-Type: application/json"   -H "Idempotency-Key: demo-pi-001"   -d '{"amount":199,"currency":"nzd"}' | jq
```

Copy the returned `pi_...` ID.

---

### B) Confirm the PaymentIntent

```bash
curl -s -X POST   http://localhost:7020/demo/payment-intents/<PI_ID>/confirm | jq
```

Stripe CLI will forward webhook events automatically.

---

### C) Inspect Persisted Events

```bash
curl -s "http://localhost:7020/demo/payment-events?take=10" | jq
```

You should see events such as:

- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `refund.created`
- `charge.refunded`

---

## 7️⃣ Refund Test Loop

### Full refund

```bash
curl -s -X POST   http://localhost:7020/demo/payment-intents/<PI_ID>/refund   -H "Idempotency-Key: demo-refund-full-001" | jq
```

### Partial refund

```bash
curl -s -X POST   http://localhost:7020/demo/payment-intents/<PI_ID>/refund   -H "Content-Type: application/json"   -H "Idempotency-Key: demo-refund-partial-001"   -d '{"amount":100,"reason":"requested_by_customer"}' | jq
```

Then verify webhook event rows:

```bash
curl -s "http://localhost:7020/demo/payment-events?take=20" | jq
```

---

## 🔎 Watching Logs in Seq

Open:

http://localhost:5341

Search for:

- `Webhook received`
- `Webhook processed`
- `Webhook already processed`
- `Refund event`

Seq provides real-time visibility into:

- Signature validation
- Idempotency detection
- Transaction boundaries
- Retry behavior

---

## 🔁 Understanding Retry & Idempotency Behavior

Stripe will retry webhook delivery if your API returns a non-200 response.

This repo:

- Returns **500** on processing failures (to allow safe retries)
- Returns **200** for already-processed events (idempotent handling)
- Stores processed `evt_...` IDs in `ProcessedEvents`

You can simulate duplicate delivery:

```bash
stripe trigger payment_intent.succeeded
stripe trigger payment_intent.succeeded
```

Second delivery should log:

```
Webhook already processed
```

But still return HTTP 200.

---

## Troubleshooting

### Webhook secret mismatch (invalid signature)

Symptoms:

- API returns 400
- Logs show signature verification failure

Fix:

1. Ensure `.env.runtime` contains the current `whsec_...`
2. Restart API:

```bash
docker compose restart api
```

---

### Stripe CLI not running

If no webhook events are inserted:

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

Must remain active.

---

### Wrong forward port

Ensure forwarding to correct port:

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

---

### API version mismatch warning

Stripe CLI may emit events using a different API version than your Stripe .NET SDK.

This repository tolerates version mismatch during local development while still validating webhook signatures.

---

## Related Documentation

- `docs/00-quickstart.md`
- `docs/40-troubleshooting.md`
- `docs/50-testing.md`
