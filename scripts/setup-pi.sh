#!/usr/bin/env bash
# Richtet den Bubble-Relay auf einem Raspberry Pi (Raspberry Pi OS / Debian, arm64 oder armhf mit 64-Bit-Kernel) ein.
# Aufruf auf dem Pi:  bash setup-pi.sh
# Er installiert bei Bedarf Docker und Git, holt den Code von GitHub und startet den Relay per
# `docker compose` auf Port 5080. Die Datenbank liegt im Docker-Volume `relay-data` und übersteht Updates.
# Erneut ausführen = aktualisieren.
set -euo pipefail

REPO="https://github.com/harryneufeld/bubble.git"
DIR="$HOME/bubble"

echo "== System: $(uname -m), $(. /etc/os-release && echo "$PRETTY_NAME")"
case "$(uname -m)" in
  aarch64|arm64|x86_64) ;;
  *) echo "Achtung: 32-Bit-System ($(uname -m)). Das .NET-10-Image braucht ein 64-Bit-Betriebssystem."; exit 1 ;;
esac

if ! command -v git >/dev/null; then
  echo "== Installiere git"
  sudo apt-get update -qq
  sudo apt-get install -y -qq git
fi

if ! command -v docker >/dev/null; then
  echo "== Installiere Docker (offizielles Skript von get.docker.com)"
  curl -fsSL https://get.docker.com | sudo sh
  sudo usermod -aG docker "$USER" || true
fi

echo "== Hole Code"
if [ -d "$DIR/.git" ]; then
  git -C "$DIR" fetch -q origin main
  git -C "$DIR" reset -q --hard origin/main
else
  git clone -q --depth 1 "$REPO" "$DIR"
fi

echo "== Baue und starte den Relay (der erste Build dauert ein paar Minuten)"
cd "$DIR"
sudo docker compose up -d --build

echo "== Warte auf den Relay"
for i in $(seq 1 30); do
  if curl -fsS http://localhost:5080/ >/dev/null 2>&1; then
    echo "Relay läuft: $(curl -fsS http://localhost:5080/)"
    echo "Adresse in der App: $(hostname -I | awk '{print $1}'):5080"
    sudo docker compose ps
    exit 0
  fi
  sleep 2
done
echo "Relay antwortet nicht. Log:"
sudo docker compose logs --tail 40
exit 1
