#!/usr/bin/env bash
set -euo pipefail

auth_headers=()
if [[ -n "${DOC_RAG_API_KEY:-}" ]]; then
  auth_headers=(-H "X-Api-Key: ${DOC_RAG_API_KEY}")
fi

curl -fsS http://localhost:8080/health/ready

curl -fsS -X POST http://localhost:8080/api/documents/import-folder \
  -H "Content-Type: application/json" \
  "${auth_headers[@]}" \
  -d '{"recursive":true}'

sleep 10

curl -fsS http://localhost:8080/api/documents "${auth_headers[@]}"

curl -fsS -X POST http://localhost:8080/api/rag/search \
  -H "Content-Type: application/json" \
  "${auth_headers[@]}" \
  -d '{"query":"vacation days","topK":5}'

curl -fsS -X POST http://localhost:8080/api/rag/ask \
  -H "Content-Type: application/json" \
  "${auth_headers[@]}" \
  -d '{"question":"How many vacation days do employees receive?"}'
