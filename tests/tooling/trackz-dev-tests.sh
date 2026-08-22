#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "$0")/../.." && pwd -P)
subject="$repo_root/scripts/trackz-dev"
fixture=$(mktemp -d "${TMPDIR:-/tmp}/trackz-dev-test.XXXXXX")
fake_bin="$fixture/bin"
state_dir="$fixture/state"
command_log="$fixture/commands.log"
docker_flag="$fixture/docker.running"
simulator_flag="$fixture/simulator.booted"
api_flag="$fixture/api.ready"
api_dir="$fixture/api"
app_bundle="$fixture/TrackZ.Mobile.app"
mkdir -p "$fake_bin" "$state_dir" "$api_dir" "$app_bundle"
: > "$api_dir/TrackZ.Api.dll"

cleanup() {
  if [[ -f "$state_dir/api.pid" ]]; then
    api_pid=$(cat "$state_dir/api.pid")
    kill "$api_pid" 2>/dev/null || true
  fi
  rm -rf "$fixture"
}
trap cleanup EXIT

write_fake() {
  local name=$1
  shift
  printf '%s\n' '#!/usr/bin/env bash' "$@" > "$fake_bin/$name"
  chmod +x "$fake_bin/$name"
}

write_fake docker \
  'printf "docker %s\n" "$*" >> "$TRACKZ_TEST_COMMAND_LOG"' \
  'if [[ "$*" == "desktop status" ]]; then [[ -f "$TRACKZ_TEST_DOCKER_FLAG" ]]; exit; fi' \
  'if [[ "$*" == "desktop start" ]]; then : > "$TRACKZ_TEST_DOCKER_FLAG"; exit 0; fi' \
  'if [[ "$*" == "desktop stop" ]]; then rm -f "$TRACKZ_TEST_DOCKER_FLAG"; exit 0; fi' \
  'exit 0'

write_fake dotnet \
  'printf "dotnet %s\n" "$*" >> "$TRACKZ_TEST_COMMAND_LOG"' \
  'if [[ "${1:-}" == "build" ]]; then exit 0; fi' \
  ': > "$TRACKZ_TEST_API_FLAG"' \
  'trap "rm -f \"$TRACKZ_TEST_API_FLAG\"; exit 0" TERM INT' \
  'while :; do /bin/sleep 1; done'

write_fake curl \
  'printf "curl %s\n" "$*" >> "$TRACKZ_TEST_COMMAND_LOG"' \
  '[[ -f "$TRACKZ_TEST_API_FLAG" ]]'

write_fake xcrun \
  'printf "xcrun %s api=%s media=%s\n" "$*" "${SIMCTL_CHILD_TRACKZ_API_ORIGIN:-}" "${SIMCTL_CHILD_TRACKZ_MEDIA_ORIGIN:-}" >> "$TRACKZ_TEST_COMMAND_LOG"' \
  'if [[ "$*" == "simctl list devices booted" ]]; then' \
  '  if [[ -s "$TRACKZ_TEST_SIMULATOR_FLAG" ]]; then printf "    iPhone 17e (%s) (Booted)\n" "$(cat "$TRACKZ_TEST_SIMULATOR_FLAG")"; fi' \
  'elif [[ "$*" == "simctl list devices available" ]]; then' \
  '  if [[ "${TRACKZ_TEST_MULTIPLE_SIMULATORS:-0}" == 1 ]]; then printf "    iPhone 17e (A4BFFA59-99E0-4658-8A2A-D3A1D625B40A) (Shutdown)\n"; fi' \
  '  printf "    iPhone 17e (54933EFF-80B2-45F8-A808-FBE79F26FAB2) (Shutdown)\n"' \
  'elif [[ "${1:-}" == "simctl" && "${2:-}" == "boot" ]]; then' \
  '  printf "%s\n" "$3" > "$TRACKZ_TEST_SIMULATOR_FLAG"' \
  'elif [[ "${1:-}" == "simctl" && "${2:-}" == "bootstatus" ]]; then' \
  '  printf "Monitoring boot status...\n"' \
  'elif [[ "${1:-}" == "simctl" && "${2:-}" == "shutdown" ]]; then' \
  '  rm -f "$TRACKZ_TEST_SIMULATOR_FLAG"' \
  'fi' \
  'exit 0'

write_fake open \
  'printf "open %s\n" "$*" >> "$TRACKZ_TEST_COMMAND_LOG"' \
  'exit 0'

write_fake ps \
  'printf "ps %s\n" "$*" >> "$TRACKZ_TEST_COMMAND_LOG"' \
  'printf "dotnet TrackZ.Api.dll --urls http://127.0.0.1:5080\n"'

export PATH="$fake_bin:/usr/bin:/bin"
export TRACKZ_TEST_COMMAND_LOG="$command_log"
export TRACKZ_TEST_DOCKER_FLAG="$docker_flag"
export TRACKZ_TEST_SIMULATOR_FLAG="$simulator_flag"
export TRACKZ_TEST_API_FLAG="$api_flag"
export TRACKZ_DEV_STATE_DIR="$state_dir"
export TRACKZ_API_DLL="$api_dir/TrackZ.Api.dll"
export TRACKZ_APP_BUNDLE="$app_bundle"
: > "$docker_flag"
printf '%s\n' '54933EFF-80B2-45F8-A808-FBE79F26FAB2' > "$simulator_flag"

"$subject" start

grep -Fq 'docker desktop status' "$command_log"
if grep -Fq 'docker desktop start' "$command_log"; then
  echo 'start restarted Docker Desktop even though it was already running' >&2
  exit 1
fi
grep -Fq 'docker compose up -d postgres minio minio-bootstrap' "$command_log"
grep -Fq 'xcrun simctl launch 54933EFF-80B2-45F8-A808-FBE79F26FAB2 com.trackz.app api=http://127.0.0.1:5080 media=http://127.0.0.1:5080' "$command_log"
api_pid=$(cat "$state_dir/api.pid")
/bin/sleep 1
if ! kill -0 "$api_pid" 2>/dev/null; then
  echo 'start did not leave the TrackZ API running after the command returned' >&2
  exit 1
fi

"$subject" stop

if kill -0 "$api_pid" 2>/dev/null; then
  echo 'stop left the recorded TrackZ API process running' >&2
  exit 1
fi

grep -Fq 'xcrun simctl terminate 54933EFF-80B2-45F8-A808-FBE79F26FAB2 com.trackz.app' "$command_log"
grep -Fq 'xcrun simctl shutdown 54933EFF-80B2-45F8-A808-FBE79F26FAB2' "$command_log"
grep -Fq 'docker compose stop postgres minio minio-bootstrap' "$command_log"
if grep -Fq 'docker desktop stop' "$command_log"; then
  echo 'stop closed a pre-existing Docker Desktop session' >&2
  exit 1
fi

echo 'trackz-dev start/stop pre-existing Docker contract: PASS'

rm -rf "$state_dir"
mkdir -p "$state_dir"
: > "$command_log"
rm -f "$docker_flag"

"$subject" start
if [[ $(cat "$state_dir/simulator.udid") != '54933EFF-80B2-45F8-A808-FBE79F26FAB2' ]]; then
  echo 'simulator boot progress polluted the persisted device UDID' >&2
  exit 1
fi
"$subject" start
api_pid=$(cat "$state_dir/api.pid")
"$subject" stop

if [[ $(grep -Fc 'docker desktop start' "$command_log") -ne 1 ]]; then
  echo 'repeated start did not start Docker Desktop exactly once' >&2
  exit 1
fi
if [[ $(grep -Fc 'docker desktop stop' "$command_log") -ne 1 ]]; then
  echo 'stop did not close Docker Desktop owned by the first start' >&2
  exit 1
fi
if [[ -f "$docker_flag" ]]; then
  echo 'owned Docker Desktop remained running after stop' >&2
  exit 1
fi

echo 'trackz-dev repeated-start ownership contract: PASS'

status_output=$("$subject" status)
grep -Fq 'Docker Desktop: stopped' <<< "$status_output"
grep -Fq 'TrackZ API: stopped' <<< "$status_output"
grep -Fq 'iOS Simulator: stopped' <<< "$status_output"

echo 'trackz-dev status contract: PASS'

: > "$command_log"
"$subject" stop
if grep -Fq 'xcrun simctl boot ' "$command_log"; then
  echo 'idempotent stop booted a shutdown simulator before stopping it' >&2
  exit 1
fi

echo 'trackz-dev idempotent stop contract: PASS'

rm -rf "$state_dir"
mkdir -p "$state_dir"
: > "$command_log"
: > "$docker_flag"
printf '%s\n' '54933EFF-80B2-45F8-A808-FBE79F26FAB2' > "$simulator_flag"
export TRACKZ_TEST_MULTIPLE_SIMULATORS=1

"$subject" start
"$subject" restart
api_pid=$(cat "$state_dir/api.pid")

if grep -Fq 'xcrun simctl launch A4BFFA59-99E0-4658-8A2A-D3A1D625B40A com.trackz.app' "$command_log"; then
  echo 'restart selected a different same-name simulator and lost the existing device data' >&2
  exit 1
fi

if [[ $(grep -Fc 'docker compose up -d postgres minio minio-bootstrap' "$command_log") -ne 2 ]]; then
  echo 'restart did not bring the TrackZ containers back up' >&2
  exit 1
fi
grep -Fq 'docker compose stop postgres minio minio-bootstrap' "$command_log"
if grep -Eq 'docker desktop (start|stop)' "$command_log"; then
  echo 'restart changed a pre-existing Docker Desktop session' >&2
  exit 1
fi

"$subject" stop
unset TRACKZ_TEST_MULTIPLE_SIMULATORS
echo 'trackz-dev restart contract: PASS'

rm -rf "$state_dir"
mkdir -p "$state_dir"
: > "$command_log"
: > "$docker_flag"
printf '%s\n' '54933EFF-80B2-45F8-A808-FBE79F26FAB2' > "$simulator_flag"
: > "$api_flag"

"$subject" start

if grep -Fq 'dotnet TrackZ.Api.dll' "$command_log"; then
  echo 'start launched a duplicate API even though port 5080 was already reachable' >&2
  exit 1
fi
if [[ -f "$state_dir/api.pid" ]]; then
  echo 'start claimed ownership of an API process it did not launch' >&2
  exit 1
fi
status_output=$("$subject" status)
grep -Fq 'TrackZ API: reachable (pre-existing)' <<< "$status_output"

"$subject" stop
if [[ ! -f "$api_flag" ]]; then
  echo 'stop terminated or disturbed a pre-existing API session' >&2
  exit 1
fi

echo 'trackz-dev pre-existing API contract: PASS'
