#!/usr/bin/env bash
set -euo pipefail
docker compose -f docker-compose.dev.yml up -d --remove-orphans
