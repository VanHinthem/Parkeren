#!/bin/sh
set -eu

REPO_URL="https://github.com/VanHinthem/Parkeren.git"
SOURCE_DIR="/workspace"
BRANCH="${PARKEREN_ACTIVE_BRANCH:?Set PARKEREN_ACTIVE_BRANCH}"
INTERVAL="${PARKEREN_GIT_POLL_SECONDS:-15}"

case "$INTERVAL" in
  ''|*[!0-9]*) echo "Invalid polling interval" >&2; exit 1 ;;
esac
if [ "$INTERVAL" -lt 10 ]; then
  echo "Polling interval must be at least 10 seconds" >&2
  exit 1
fi

# Validate an ordinary branch name; never pass untrusted values as command options.
case "$BRANCH" in
  ''|-*|*' '*|*'
'*) echo "Invalid branch name" >&2; exit 1 ;;
esac
git check-ref-format "refs/heads/$BRANCH" || exit 1

mkdir -p "$SOURCE_DIR"
if [ ! -d "$SOURCE_DIR/.git" ]; then
  if [ -n "$(ls -A "$SOURCE_DIR")" ]; then
    echo "Source directory is not empty and is not a Git repository" >&2
    exit 1
  fi
  git -C "$SOURCE_DIR" init
  git -C "$SOURCE_DIR" remote add origin "$REPO_URL"
fi

while :; do
  if git -C "$SOURCE_DIR" fetch --no-tags --depth=1 origin "+refs/heads/$BRANCH:refs/remotes/origin/parkeren-active"; then
    TARGET="$(git -C "$SOURCE_DIR" rev-parse refs/remotes/origin/parkeren-active)"
    CURRENT="$(git -C "$SOURCE_DIR" rev-parse HEAD 2>/dev/null || true)"
    if [ "$TARGET" != "$CURRENT" ]; then
      echo "Updating $BRANCH: ${CURRENT:-none} -> $TARGET"
      git -C "$SOURCE_DIR" reset --hard "$TARGET"
      # Remove files tracked by a previous branch but absent in the new checkout.
      # Only manage this dedicated source checkout; never mount persistent data here.
      git -C "$SOURCE_DIR" clean -fd -e src/Parkeren.Web/node_modules/ -e '**/bin/' -e '**/obj/'
      echo "Deployed commit: $TARGET"
    fi
  else
    echo "Git fetch failed; retaining current checkout and retrying" >&2
  fi
  sleep "$INTERVAL"
done
