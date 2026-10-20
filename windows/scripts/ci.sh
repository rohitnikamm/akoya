#!/usr/bin/env bash
# Push the current branch, wait for the Windows CI run, fetch its results, print the summary.
#
#   ci.sh              push HEAD's branch, wait for the build-test check run of HEAD, print summary
#   ci.sh --wait-only  skip the push, resume waiting for HEAD's run
#
# Exit codes: 0 run succeeded, 1 run failed / no run appeared / no results / gh or git error,
# 2 run exists but is still in progress after 9 minutes.
# Results land in $SCRATCH/ci/<sha>/ (summary.md, build.log, tests/*.trx, snapshots/*.png).
# Only the Checks API and git are used: the Actions API is blocked from this workspace.
set -euo pipefail

REPO="rohitnikamm/akoya"
JOB="build-test"
SCRATCH="${SCRATCH:-/tmp/claude-0/-home-claude/9febb593-37b7-55a6-bd1f-0fcd51f28b1c/scratchpad}"
POLL_SECONDS=10
MAX_WAIT_SECONDS=$((9 * 60))
NO_RUN_GRACE_SECONDS=90

wait_only=0
case "${1:-}" in
  "") ;;
  --wait-only) wait_only=1 ;;
  *) echo "usage: ci.sh [--wait-only]" >&2; exit 64 ;;
esac

cd "$(git rev-parse --show-toplevel)"
sha="$(git rev-parse HEAD)"
branch="$(git rev-parse --abbrev-ref HEAD)"

if [[ $wait_only -eq 0 ]]; then
  if [[ "$branch" == "HEAD" ]]; then
    echo "refusing to push: HEAD is detached" >&2
    exit 1
  fi
  if [[ "$branch" == "main" ]]; then
    echo "refusing to push: current branch is main" >&2
    exit 1
  fi
  if [[ -n "$(git status --porcelain)" ]]; then
    echo "warning: working tree has uncommitted changes; CI only sees what is pushed" >&2
  fi
  echo "pushing $branch ($sha)"
  git push origin "HEAD:refs/heads/$branch"
fi

echo "waiting for check run '$JOB' on ${sha:0:9}"
started=$SECONDS
deadline=$((started + MAX_WAIT_SECONDS))
errfile="$(mktemp)"
trap 'rm -f "$errfile"' EXIT
last_err=""
seen_run=0
status=""
conclusion=""
while true; do
  if line="$(gh api "repos/$REPO/commits/$sha/check-runs" \
    --jq "[.check_runs[] | select(.name==\"$JOB\")] | sort_by(.id) | last // empty | \"\(.status) \(.conclusion)\"" \
    2>"$errfile")"; then
    last_err=""
    if [[ -n "$line" ]]; then
      seen_run=1
      status="${line%% *}"
      conclusion="${line#* }"
      if [[ "$status" == "completed" ]]; then
        break
      fi
    elif (( SECONDS - started >= NO_RUN_GRACE_SECONDS )); then
      echo "no check run appeared; did the push touch windows/** or the workflow?" >&2
      exit 1
    fi
  else
    last_err="$(cat "$errfile")"
  fi
  if (( SECONDS >= deadline )); then
    if [[ -n "$last_err" ]]; then
      echo "gh api kept failing; last error:" >&2
      echo "$last_err" >&2
      exit 1
    fi
    if [[ $seen_run -eq 1 ]]; then
      echo "still running, re-run: ci.sh --wait-only"
      exit 2
    fi
    echo "no check run appeared; did the push touch windows/** or the workflow?" >&2
    exit 1
  fi
  sleep "$POLL_SECONDS"
done
echo "check run completed: $conclusion"

dest="$SCRATCH/ci/$sha"
rm -rf "$dest"
mkdir -p "$dest"
branch="$(git rev-parse --abbrev-ref HEAD)"
if [[ "$branch" == "windows-design" ]]; then results="ci-output"; else results="ci-output-${branch//\//-}"; fi
if ! git fetch --quiet --no-tags --force origin "$results"; then
  echo "could not fetch $results from origin (the publish step may have failed); check run was: $conclusion" >&2
  exit 1
fi
if git archive FETCH_HEAD "runs/$sha" 2>/dev/null | tar -x -C "$dest" --strip-components=2; then
  echo "results: $dest"
  echo
  cat "$dest/summary.md"
else
  echo "no results for $sha on $results (run $conclusion); check annotations:" >&2
  echo "  gh api repos/$REPO/commits/$sha/check-runs" >&2
  exit 1
fi

[[ "$conclusion" == "success" ]]
