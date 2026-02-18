
#!/usr/bin/env bash
set -euo pipefail

PORT="${PORT:-7020}"
URL="${URL:-http://localhost:${PORT}/webhooks/stripe}"

echo "Starting Stripe listener -> ${URL}"
echo "If you don't run this, you will get ZERO webhooks."
echo

stripe login
stripe listen --forward-to "${URL}"
