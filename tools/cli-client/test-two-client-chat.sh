#!/usr/bin/env bash
# Two-client sector-chat end-to-end test (diagnostic harness).
#
# Drives two containerised CLI clients (cli1=account c1/char chara,
# cli2=account c2/char charb -- both home sector 1015) through
# connect -> login -> enter, then each sends one sector broadcast in a
# shared time window while BOTH are in-sector. Captures each client's
# stdout and asserts each RECEIVED the other's message (the inbound
# `<-- [sector] <sender>: <msg>` echo from the always-on drain).
#
# Not a unit test -- it needs the live docker stack. Re-runnable after a
# server rebuild. Exit 0 = bidirectional chat confirmed.
set -uo pipefail
cd "$(dirname "$0")/../.."   # tools/cli-client -> repo root

STAMP="${1:-run}"
PING="PING_FROM_CHARA_${STAMP}"
PONG="PONG_FROM_CHARB_${STAMP}"
OUT1="/tmp/cli1.${STAMP}.out"
OUT2="/tmp/cli2.${STAMP}.out"
STACK_NET="${COMPOSE_PROJECT_NAME:-enb-emulator}_default"

run_client() {
  # $1=unit $2=account $3=delay-before-chat $4=message $5=outfile $6=charname
  local unit="$1" acct="$2" delay="$3" msg="$4" out="$5" char="$6"
  {
    echo "connect"
    echo "login ${acct} ${acct}"
    echo "enter ${char}"
    sleep "${delay}"
    echo "chat sector ${msg}"
    sleep 6
    echo "list"        # by now both avatars are in-space; we should see the other
    sleep 8
    echo "quit"
  } | STACK_NETWORK="${STACK_NET}" docker compose -f docker-compose.cli.yml -p "${unit}" \
        run --rm -T cli >"${out}" 2>&1
}

echo ">>> stack network: ${STACK_NET}"
echo ">>> cli1=c1/chara  cli2=c2/charb  sector 1015"
echo ">>> ping='${PING}'  pong='${PONG}'"

# cli1 chats at ~t+10s, cli2 at ~t+14s; both stay in-sector ~18s after.
run_client cli1 c1 10 "${PING}" "${OUT1}" chara &
P1=$!
run_client cli2 c2 14 "${PONG}" "${OUT2}" charb &
P2=$!
wait "${P1}"; wait "${P2}"

echo "================ cli1 (chara) output ================"; cat "${OUT1}"
echo "================ cli2 (charb) output ================"; cat "${OUT2}"
echo "===================================================="

fail=0
# --- chat: chara must see charb's PONG inbound; charb must see chara's PING ---
if grep -q -- "${PONG}" "${OUT1}"; then echo "PASS: chara received charb's broadcast"; else echo "FAIL: chara did NOT receive charb's broadcast"; fail=1; fi
if grep -q -- "${PING}" "${OUT2}"; then echo "PASS: charb received chara's broadcast"; else echo "FAIL: charb did NOT receive chara's broadcast"; fail=1; fi
# --- visibility: each `list` summary must report >=1 avatars (the other player) ---
# Match the "nearby: N objects (M avatars, K navs)" line with M>=1. Grepping
# for the char NAME is unreliable -- it also appears in the chat echo.
if grep -qE "\([1-9][0-9]* avatars," "${OUT1}"; then echo "PASS: chara sees another avatar in list"; else echo "FAIL: chara sees 0 avatars (cross-visibility broken)"; fail=1; fi
if grep -qE "\([1-9][0-9]* avatars," "${OUT2}"; then echo "PASS: charb sees another avatar in list"; else echo "FAIL: charb sees 0 avatars (cross-visibility broken)"; fail=1; fi
# sanity: each must at least have entered the sector.
grep -q "@1015" "${OUT1}" || { echo "WARN: cli1 never reached sector 1015 prompt"; }
grep -q "@1015" "${OUT2}" || { echo "WARN: cli2 never reached sector 1015 prompt"; }
exit "${fail}"
