#!/bin/bash
# =====================================================================
# ARGOS : ajoute les 6 secrets iPhone (TestFlight) au dépôt GitHub.
# À lancer sur le Mac, dans le Terminal :
#   bash secrets-ios.sh
# Prérequis : GitHub CLI connecté (brew install gh && gh auth login).
# Aucun secret n'est écrit dans ce fichier ni dans le dépôt : tout est lu sur le Mac
# puis envoyé, chiffré, dans GitHub › Settings › Secrets and variables › Actions.
#
# Les 6 secrets :
#   ARGOS_IOS_CERTIFICATE_P12       certificat « Apple Distribution » (.p12) en base64 — le même que SIMCALL
#   ARGOS_IOS_CERTIFICATE_PASSWORD  mot de passe de ce .p12
#   ARGOS_IOS_PROFILE               profil « App Store » de fr.vdms.argos (.mobileprovision) en base64
#   ARGOS_ASC_KEY_ID                identifiant de la clé API App Store Connect — la même que SIMCALL
#   ARGOS_ASC_ISSUER_ID             « Issuer ID » de la clé API
#   ARGOS_ASC_KEY_P8                contenu du fichier AuthKey_XXXX.p8
# =====================================================================
set -e
REPO="${REPO:-Plouuuf/ARGOS}"

command -v gh >/dev/null || { echo "Installez GitHub CLI : brew install gh, puis gh auth login"; exit 1; }
gh auth status >/dev/null 2>&1 || gh auth login

ask_file() {
  local prompt="$1" f
  while true; do
    read -r -p "$prompt (glissez le fichier ici puis Entrée) : " f
    f="${f%\"}"; f="${f#\"}"; f="${f%\'}"; f="${f#\'}"; f="${f%% }"; f="${f//\\ / }"
    [ -f "$f" ] && { printf '%s' "$f"; return; }
    echo "  Fichier introuvable : $f" >&2
  done
}

echo
echo "1/4  Certificat Apple Distribution (.p12)"
echo "     Trousseau d'accès › Mes certificats › « Apple Distribution: … » › clic droit › Exporter (.p12)"
P12=$(ask_file "     Fichier .p12")
read -r -s -p "     Mot de passe du .p12 : " P12_PASSWORD; echo
openssl pkcs12 -in "$P12" -nokeys -passin pass:"$P12_PASSWORD" -legacy >/dev/null 2>&1 \
  || openssl pkcs12 -in "$P12" -nokeys -passin pass:"$P12_PASSWORD" >/dev/null 2>&1 \
  || { echo "     Mot de passe incorrect pour ce .p12."; exit 1; }

echo
echo "2/4  Profil App Store de fr.vdms.argos (.mobileprovision)"
echo "     developer.apple.com › Certificates, IDs & Profiles › Profiles › + › App Store Connect"
echo "     › App ID fr.vdms.argos › certificat Apple Distribution › nom « ARGOS App Store » › Download"
PROFILE=$(ask_file "     Fichier .mobileprovision")
APPID=$(security cms -D -i "$PROFILE" 2>/dev/null | plutil -extract Entitlements.application-identifier raw - 2>/dev/null || true)
case "$APPID" in
  *fr.vdms.argos) echo "     Profil OK ($APPID)";;
  *) echo "     Attention : ce profil est pour « $APPID », pas pour fr.vdms.argos."; read -r -p "     Continuer quand même ? (o/N) " r; [ "$r" = "o" ] || exit 1;;
esac

echo
echo "3/4  Clé API App Store Connect (la même que SIMCALL)"
echo "     appstoreconnect.apple.com › Utilisateurs et accès › Intégrations › Clés API App Store Connect"
P8=$(ask_file "     Fichier AuthKey_XXXXXXXXXX.p8")
DEFAULT_KEY_ID=$(basename "$P8" | sed -n 's/^AuthKey_\(.*\)\.p8$/\1/p')
read -r -p "     Key ID [${DEFAULT_KEY_ID}] : " ASC_KEY_ID; ASC_KEY_ID="${ASC_KEY_ID:-$DEFAULT_KEY_ID}"
read -r -p "     Issuer ID (en haut de la page des clés) : " ASC_ISSUER_ID
[ -n "$ASC_KEY_ID" ] && [ -n "$ASC_ISSUER_ID" ] || { echo "     Key ID et Issuer ID sont obligatoires."; exit 1; }

echo
echo "4/4  Envoi des secrets vers $REPO…"
base64 -i "$P12" | tr -d '\n' | gh secret set ARGOS_IOS_CERTIFICATE_P12 --repo "$REPO"
printf '%s' "$P12_PASSWORD"    | gh secret set ARGOS_IOS_CERTIFICATE_PASSWORD --repo "$REPO"
base64 -i "$PROFILE" | tr -d '\n' | gh secret set ARGOS_IOS_PROFILE --repo "$REPO"
printf '%s' "$ASC_KEY_ID"      | gh secret set ARGOS_ASC_KEY_ID --repo "$REPO"
printf '%s' "$ASC_ISSUER_ID"   | gh secret set ARGOS_ASC_ISSUER_ID --repo "$REPO"
gh secret set ARGOS_ASC_KEY_P8 --repo "$REPO" < "$P8"

echo
gh secret list --repo "$REPO" | grep -E "ARGOS_(IOS|ASC)" || true
echo
echo "Terminé : les 6 secrets iPhone sont dans GitHub. Le prochain build enverra ARGOS sur TestFlight."
