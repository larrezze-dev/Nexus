#!/usr/bin/env bash
# dlq-replay.sh — Re-injects the messages of <topic>.DLQ into <topic> (conventions-events.md §7,
# step 0.7 track S). Asks for confirmation unless --yes is given.
#
# Usage: tools/dlq-replay.sh <topic>.DLQ [--yes]
#        make dlq-replay TOPIC=<topic>.DLQ [YES=1]
#
# The work is done by src/Tools/Nexus.Tools.DlqReplay (Confluent.Kafka): the Kafka console tools
# print values as text and would corrupt the Avro binary payloads. Bootstrap servers come from
# NEXUS__KAFKA__BOOTSTRAPSERVERS, default localhost:${KAFKA_PORT:-9092} (the compose broker).
set -euo pipefail

cd "$(dirname "$0")/.."

if [[ $# -lt 1 || -z "${1:-}" ]]; then
  echo "usage: tools/dlq-replay.sh <topic>.DLQ [--yes]" >&2
  exit 2
fi

if [[ "$1" != *.DLQ ]]; then
  echo "ERROR: '$1' is not a DLQ topic (expected '<topic>.DLQ')." >&2
  exit 2
fi

export NEXUS__KAFKA__BOOTSTRAPSERVERS="${NEXUS__KAFKA__BOOTSTRAPSERVERS:-localhost:${KAFKA_PORT:-9092}}"

exec dotnet run --project src/Tools/Nexus.Tools.DlqReplay --verbosity quiet -- "$@"
