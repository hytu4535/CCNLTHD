# 00 – Quickstart

This guide gets the project running locally in under 5 minutes using Docker.

---

## ✅ Prerequisites

You need:

- Docker + Docker Compose
- Stripe CLI
- A Stripe test account
- (Optional) `jq` for pretty JSON output

If Stripe CLI is not installed, see:  
https://stripe.com/docs/stripe-cli

---

## 1️⃣ Clone the Repository

```bash
git clone https://github.com/nigel-dewar/stripe-webhooks-dotnet.git
cd stripe-webhooks-dotnet
```

---

## 2️⃣ Configure Runtime Environment

Copy the example runtime file:

```bash
cp .env.runtime.example .env.runtime
```

Edit `.env.runtime` and set:

```bash
Stripe__SecretKey=sk_test_...
Stripe__WebhookSecret=whsec_...
Stripe__SkipSignatureValidation=false
```

**Do not use `.env`.**  
This project uses **`.env.runtime` only** for Docker mode.

You will obtain the webhook secret from Stripe CLI in step 4.

---

## 3️⃣ Start the Application Stack

```bash
docker compose up -d --build
```

Services started:

- API → http://localhost:7020
- Health → http://localhost:7020/health
- Seq (logs) → http://localhost:5341
- Postgres (container)
- Optional pgAdmin (profile: tools)

Verify health:

```bash
curl http://localhost:7020/health
```

---

## 4️⃣ Start Stripe CLI Listener (REQUIRED)

```bash
stripe login
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

Stripe will output something like:

```text
Ready! Your webhook signing secret is whsec_XXXXXXXX
```

Copy that value into `.env.runtime`:

```bash
Stripe__WebhookSecret=whsec_XXXXXXXX
```

Then restart the API container:

```bash
docker compose restart api
```

If you do not run `stripe listen`, webhook events will not be delivered.

---

## 5️⃣ Create a PaymentIntent

```bash
curl -s -X POST http://localhost:7020/demo/payment-intents   -H "Content-Type: application/json"   -H "Idempotency-Key: demo-pi-001"   -d '{"amount":199,"currency":"nzd"}' | jq
```

You will receive a PaymentIntent ID (`pi_...`) and a client secret.

---

## 6️⃣ Confirm the PaymentIntent

```bash
curl -s -X POST   http://localhost:7020/demo/payment-intents/<PI_ID>/confirm | jq
```

Stripe CLI will forward webhook events automatically.

---

## 7️⃣ Verify Persisted Events

```bash
curl -s "http://localhost:7020/demo/payment-events?take=10" | jq
```

You should see events like:

- `payment_intent.succeeded`
- `payment_intent.payment_failed` (if triggered)
- `refund.created` / `charge.refunded` (if you test refunds)

---

## 8️⃣ Test Refund Flow

**Full refund** (omit amount):

```bash
curl -s -X POST http://localhost:7020/demo/payment-intents/<PI_ID>/refund   -H "Idempotency-Key: demo-refund-full-001" | jq
```

**Partial refund** (amount in minor units):

```bash
curl -s -X POST http://localhost:7020/demo/payment-intents/<PI_ID>/refund   -H "Content-Type: application/json"   -H "Idempotency-Key: demo-refund-partial-001"   -d '{"amount":100,"reason":"requested_by_customer"}' | jq
```

Valid reasons:

- `duplicate`
- `fraudulent`
- `requested_by_customer`

---

## 🔎 Viewing Logs in Seq

Open:

http://localhost:5341

You will see structured logs for:

- Webhook received
- Idempotency detection
- PaymentIntent success/failure
- Refund events

---

## 🧪 Running Tests

```bash
dotnet test
```

---

## 🚀 You're Ready

You now have:

- A working Stripe webhook receiver
- Idempotent processing
- Durable persistence
- Refund lifecycle support
- Local observability via Seq

For deeper explanations, see:

- [10-architecture.md](10-architecture.md)
- [20-webhook-idempotency.md](20-webhook-idempotency.md)
- [30-local-dev-stripe-cli.md](30-local-dev-stripe-cli.md)
- [40-troubleshooting.md](40-troubleshooting.md)
- [50-testing.md](50-testing.md)
