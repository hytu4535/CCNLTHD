# 40 – Troubleshooting

This guide covers common issues when running **stripe-webhooks-dotnet** locally.

If something doesn’t work, check here first.  
Most problems are configuration-related rather than code issues.

---

## 1️⃣ Invalid Stripe Signature (HTTP 400)

### Symptoms

- Webhook endpoint returns **400**
- Logs show:
  - "Stripe event construction failed"
  - "Invalid Stripe signature"
  - "Webhook secret missing or invalid"

### Cause

`Stripe__WebhookSecret` in `.env.runtime` does not match the secret printed by:

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

Each Stripe CLI session generates a new `whsec_...` value.

### Fix

1. Restart Stripe CLI:

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

2. Copy the printed `whsec_...` value.
3. Update `.env.runtime`:

```bash
Stripe__WebhookSecret=whsec_XXXXXXXX
```

4. Restart API container:

```bash
docker compose restart api
```

---

## 2️⃣ Stripe API Version Mismatch

### Symptoms

- Logs mention API version mismatch
- StripeException referencing version differences

### Cause

Stripe CLI or account API version differs from the version expected by the Stripe .NET SDK.

### Why It Still Works

This repository uses:

```
throwOnApiVersionMismatch: false
```

This tolerates API version drift during local development while still validating webhook signatures.

### Production Consideration

In production environments you would:

- Keep the Stripe SDK updated
- Monitor webhook API version compatibility
- Potentially pin webhook endpoint behavior

---

## 3️⃣ Webhook Not Triggering

### Symptoms

- No logs appear in Seq
- No rows appear in `PaymentEvents`
- Stripe CLI appears idle

### Checklist

Is API running?

```bash
curl http://localhost:7020/health
```

Is Stripe CLI forwarding correctly?

```bash
stripe listen --forward-to http://localhost:7020/webhooks/stripe
```

Check container logs:

```bash
docker compose logs -f api
```

Ensure `.env.runtime` contains the correct:

```
Stripe__WebhookSecret=whsec_...
```

Restart API after any env change.

---

## 4️⃣ Duplicate Events / Idempotency Confusion

### Symptoms

- You trigger an event twice
- Only one row appears in `PaymentEvents`

### Explanation

This is expected behavior.

Stripe may retry webhook delivery.

This repository:

- Stores processed Stripe event IDs (`evt_...`) in `ProcessedEvents`
- Skips already-processed events
- Returns HTTP 200 to prevent further retries

You should see logs like:

```
Webhook already processed: evt_123
```

This confirms idempotency is working correctly.

---

## 5️⃣ Refund Not Appearing

### Symptoms

- Refund endpoint returns 200
- No refund-related events in database

### Checklist

- Was the PaymentIntent confirmed first?
- Is Stripe CLI running and forwarding?
- Check logs in Seq → http://localhost:5341

Manually trigger refund event:

```bash
stripe trigger refund.created
```

Then verify:

```bash
curl -s "http://localhost:7020/demo/payment-events?take=20" | jq
```

---

## 6️⃣ Database Issues

### Symptoms

- App fails on startup
- Connection refused
- Migration errors

### Verify Postgres

```bash
docker compose ps
```

Restart stack:

```bash
docker compose down
docker compose up -d --build
```

### Reset Database (Destructive)

```bash
docker compose down -v
docker compose up -d --build
```

This removes volumes and recreates the database.

---

## 7️⃣ Port Already In Use

### Symptoms

- Docker fails to bind port 7020 or 5341

### Check port usage

```bash
lsof -i :7020
lsof -i :5341
```

Stop conflicting processes or modify port mapping in `docker-compose.yml`.

---

## 8️⃣ Stripe CLI Not Installed

Install from:

https://stripe.com/docs/stripe-cli

Verify installation:

```bash
stripe --version
```

---

## 9️⃣ Logs Not Appearing in Seq

Inside Docker, the API must use:

```
http://seq:80
```

Never use `localhost` inside containers.

Verify configuration in:

- `.env.runtime`
- `appsettings.json`

Check logs:

```bash
docker compose logs -f api
```

Restart if needed:

```bash
docker compose restart api
```

---

## 🔎 Recommended Debug Strategy

When something seems wrong:

1. Check `/health`
2. Verify Stripe CLI is running
3. Confirm webhook secret matches
4. Inspect API logs
5. Inspect Seq logs
6. Restart API container

99% of issues fall into:

- Wrong webhook secret
- Stripe CLI not forwarding
- Port mismatch
- API not restarted after environment change

---

## Final Note

If you've reached here and things still aren't working, it is almost always a configuration mismatch — not a flaw in the webhook processing logic.

This repository is intentionally small and deterministic to make debugging straightforward.
