#!/usr/bin/env bash
# Checks kept-images.jq: "latest" plus the newest three releases, compared numerically (1.10 > 1.9).
set -euo pipefail
v() { printf '{"id":%s,"name":"sha256:%s","metadata":{"container":{"tags":%s}}}' "$1" "$2" "$3"; }
versions="[$(v 1 a '["1.2.0","1.2"]'),$(v 2 b '["1.10.0","1.10","1"]'),$(v 3 c '["latest"]'),$(v 4 d '[]'),$(v 5 e '["1.9.0","1.9"]'),$(v 6 f '["0.1.0"]')]"
got=$(jq -r --argjson keep 3 -f "$(dirname "$0")/kept-images.jq" <<<"$versions" | sort | tr '\n' ' ')
expected="sha256:a sha256:b sha256:c sha256:e "
[ "$got" = "$expected" ] || { echo "expected '$expected', got '$got'"; exit 1; }
echo "kept-images.jq OK"
