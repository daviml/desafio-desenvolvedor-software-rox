#!/usr/bin/env bash
# End-to-end walkthrough of the running stack (docker compose up -d).
# Registers entries, waits for the asynchronous consolidation and prints the reports.
set -euo pipefail

LAUNCHES_URL="${LAUNCHES_URL:-http://localhost:8081}"
CONSOLIDATION_URL="${CONSOLIDATION_URL:-http://localhost:8082}"
MERCHANT_ID="${MERCHANT_ID:-$(cat /proc/sys/kernel/random/uuid 2>/dev/null || powershell -NoProfile -Command "[guid]::NewGuid().ToString()" | tr -d '\r')}"
TODAY="$(date -u +%Y-%m-%d)"

say() { printf '\n\033[1;36m== %s\033[0m\n' "$1"; }

post_entry() {
  curl -sS -X POST "$LAUNCHES_URL/api/v1/entries" \
    -H 'Content-Type: application/json; charset=utf-8' \
    "${@:4}" \
    -d "{\"merchantId\":\"$MERCHANT_ID\",\"type\":\"$1\",\"amount\":$2,\"entryDate\":\"$TODAY\",\"description\":\"$3\"}"
}

wait_for_balance() {
  local expected="$1" attempts=0
  until [ "$(curl -sS "$CONSOLIDATION_URL/api/v1/merchants/$MERCHANT_ID/daily-balance/$TODAY" \
      | grep -o '"balance":[-0-9.]*' | cut -d: -f2)" = "$expected" ]; do
    attempts=$((attempts + 1))
    if [ "$attempts" -gt 60 ]; then
      echo "Timed out waiting for the consolidated balance to reach $expected" >&2
      return 1
    fi
    sleep 1
  done
}

say "Merchant $MERCHANT_ID / business day $TODAY"

say "1. Register three credits and one debit"
post_entry Credit 1500.00 "Venda no cartao" -H "Idempotency-Key: demo-credit-1"; echo
post_entry Credit 250.50 "Venda no PIX"; echo
post_entry Credit 99.90 "Venda no dinheiro"; echo
post_entry Debit 300.50 "Compra de insumos"; echo

say "2. Replay the first request with the same Idempotency-Key (must NOT duplicate)"
post_entry Credit 1500.00 "Venda no cartao" -H "Idempotency-Key: demo-credit-1"; echo

say "3. Wait for the asynchronous consolidation (1500.00 + 250.50 + 99.90 - 300.50 = 1549.90)"
wait_for_balance "1549.90"
curl -sS "$CONSOLIDATION_URL/api/v1/merchants/$MERCHANT_ID/daily-balance/$TODAY"; echo

say "4. Cancel the debit and watch the consolidated balance be compensated"
DEBIT_ID="$(curl -sS "$LAUNCHES_URL/api/v1/merchants/$MERCHANT_ID/entries?type=Debit" \
  | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)"
curl -sS -X POST "$LAUNCHES_URL/api/v1/entries/$DEBIT_ID/cancellation" \
  -H 'Content-Type: application/json' -d '{"reason":"lancamento duplicado"}'; echo

wait_for_balance "1850.40"

say "5. Final reports"
echo "-- daily balance"
curl -sS "$CONSOLIDATION_URL/api/v1/merchants/$MERCHANT_ID/daily-balance/$TODAY"; echo
echo "-- statement"
curl -sS "$CONSOLIDATION_URL/api/v1/merchants/$MERCHANT_ID/statement?from=$TODAY&to=$TODAY"; echo
echo "-- entries"
curl -sS "$LAUNCHES_URL/api/v1/merchants/$MERCHANT_ID/entries"; echo

say "Done"
