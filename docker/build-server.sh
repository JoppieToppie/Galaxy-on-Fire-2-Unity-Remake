#!/usr/bin/env bash
# Builds the Linux dedicated server from this checkout in Docker (ServerBuild): Build/LinuxServer/GoF2Server.x86_64.
# Needs Docker and a Unity account: UNITY_EMAIL / UNITY_PASSWORD (a Personal licence, activated for the build and returned
# afterwards). The editor image (docker/editor.Dockerfile) is built on first use for ProjectSettings/ProjectVersion.txt's
# version; the import cache is the Docker volume gof2-server-library (the first build imports every asset). The Unity log
# is Logs/server-build.log. GOF2_BUILD_VERSION, when set, is the build's shown version (BuildVersionStamp), so the image can
# match a release's other builds; else the commit's time.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${UNITY_EMAIL:?set UNITY_EMAIL and UNITY_PASSWORD}" "${UNITY_PASSWORD:?set UNITY_EMAIL and UNITY_PASSWORD}"
read -r VERSION CHANGESET < <(sed -n 's/^m_EditorVersionWithRevision: \([^ ]*\) (\(.*\))/\1 \2/p' ProjectSettings/ProjectVersion.txt)
IMAGE="gof2-editor:$VERSION"
docker image inspect "$IMAGE" >/dev/null 2>&1 ||
  docker build -t "$IMAGE" --build-arg VERSION="$VERSION" --build-arg CHANGESET="$CHANGESET" -f docker/editor.Dockerfile docker
docker volume create gof2-server-library >/dev/null
docker run --rm -v gof2-server-library:/library "$IMAGE" chown "$(id -u):$(id -g)" /library
mkdir -p Build/LinuxServer Logs
docker run --rm --shm-size 2g --user "$(id -u):$(id -g)" -e HOME=/tmp/home -e UNITY_EMAIL -e UNITY_PASSWORD -e GOF2_BUILD_VERSION \
  -v "$PWD:/project" -v gof2-server-library:/project/Library "$IMAGE" bash -c '
    U=/opt/unity/Editor/Unity
    rm -f /project/Library/*.pid   # Burst / ILPP server pids from the last container: stale here
    /opt/unity/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --activate-all --include-personal \
      --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" > /tmp/activate.log 2>&1 || { cat /tmp/activate.log; exit 1; }
    # The build runs the project'\''s Editor scripts: they never see the account (only the licensing calls get it).
    env -u UNITY_EMAIL -u UNITY_PASSWORD "$U" -batchmode -nographics -quit -projectPath /project -buildTarget Linux64 -standaloneBuildSubtarget Server \
      -executeMethod GoF2Remake.EditorTools.ServerBuild.Batch -logFile /project/Logs/server-build.log; rc=$?
    "$U" -batchmode -nographics -quit -returnlicense -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" \
      -projectPath /tmp/licence -logFile /tmp/return.log; grep -iE "returned|error" /tmp/return.log
    exit $rc'
ls -la Build/LinuxServer
