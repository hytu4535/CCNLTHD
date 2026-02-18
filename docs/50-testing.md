# Testing

This repo uses a small, interview-friendly test suite that still proves
the important production behaviors:

-   Stripe webhook signature validation works
-   Webhook processing is idempotent (duplicate `evt_...` deliveries do
    not double-write)
-   Webhook persistence is transactional (unit-of-work behavior is
    exercised in integration tests)

## Test layout

-   `tests/StripeWebhooks.Tests/Unit/`
    -   Pure unit tests (fast)
    -   Examples:
        -   `StripeSignatureVerifierTests` (valid + invalid signatures,
            secret missing)
        -   `IdempotencyTests` (entity defaults)
-   `tests/StripeWebhooks.Tests/Integration/`
    -   API-level tests using `WebApplicationFactory`
    -   Boots the real ASP.NET pipeline in-memory
    -   Uses SQLite in-memory for the EF Core database provider

## Why SQLite in-memory (instead of EF InMemory)

The EF Core InMemory provider does **not** support transactions.\
The webhook handler uses a database transaction to ensure durable,
retry-safe processing, so integration tests run against **SQLite
in-memory** to exercise:

-   `BeginTransaction` / commit behavior
-   uniqueness and dedupe constraints in a relational store
-   realistic EF Core database semantics

## How integration tests boot the API

Integration tests use `WebApplicationFactory<Program>` to start the app
in a **Testing** environment and override infrastructure:

-   Environment:
    -   `ASPNETCORE_ENVIRONMENT=Testing`
    -   `DOTNET_ENVIRONMENT=Testing`
-   Configuration injected:
    -   `Stripe:WebhookSecret` (for signature verification)
    -   `Stripe:SecretKey` (dummy value for startup)
-   Database overridden:
    -   AppDbContext is swapped from Npgsql/Postgres → **SQLite
        in-memory**
    -   Schema is created for the test host

Logging is intentionally simplified in `Testing` to avoid Serilog
"logger frozen" issues when multiple hosts are created.

## Running tests

From repo root:

``` bash
dotnet test
```

Run only unit tests:

``` bash
dotnet test --filter "FullyQualifiedName~StripeWebhooks.Tests.Unit"
```

Run only integration tests:

``` bash
dotnet test --filter "FullyQualifiedName~StripeWebhooks.Tests.Integration"
```

## What integration tests prove

-   `Webhook_Returns400_WhenSignatureMissing`
    -   The endpoint rejects unsigned webhook requests with HTTP 400
-   `Webhook_Returns200_AndPersists_AndIsIdempotent`
    -   A valid signed event returns HTTP 200
    -   It persists:
        -   the Stripe `evt_...` into `ProcessedEvents`
        -   the event record into `PaymentEvents`
    -   A duplicate delivery of the same event still returns HTTP 200
    -   No duplicate rows are created (idempotency is preserved)
