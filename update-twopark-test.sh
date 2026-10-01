#!/bin/sh
set -e

echo "==> Parkeren: nieuwste versie ophalen"
sudo docker run --rm \
  -v "$PWD:/workspace" \
  -v "$HOME/.ssh:/root/.ssh:ro" \
  -w /workspace \
  -e GIT_SSH_COMMAND="ssh -i /root/.ssh/parkeren_deploy -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new" \
  alpine/git pull

echo "==> Parkeren: development-stack met echte 2Park-provider bouwen en starten"
sudo docker compose \
  --env-file .env \
  -p parkeren-dev \
  -f deploy/compose.yml \
  -f deploy/compose.dev.yml \
  -f deploy/compose.twopark-test.yml \
  up -d --build app db

echo "==> Status"
sudo docker compose \
  --env-file .env \
  -p parkeren-dev \
  -f deploy/compose.yml \
  -f deploy/compose.dev.yml \
  -f deploy/compose.twopark-test.yml \
  ps
