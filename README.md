# ARGOS

Poste de travail BSPP du serveur **Val De Marne Simulation** : un bureau Windows 10 avec les logiciels
métier (ADAGIO ODE, ADAGIO REMISE, eFIBI, Ilias, Mail, MCO, SIGTAO), accessible uniquement aux membres
du Discord VDMS ayant le rôle BSPP.

Plateformes : **Windows**, **macOS**, **iPhone / iPad**, **Android**. Même base technique que SIMCALL
(.NET 9, Avalonia 11.3).

## Version 0.1 — le socle

- Écran de verrouillage Windows 10 (heure, date, fond d'écran BSPP).
- Écran de connexion : connexion **Discord** dans le navigateur, whitelist par rôle, avatar et pseudo Discord,
  « Utiliser un autre compte Discord ».
- « Bienvenue », puis le bureau : icônes (double-clic), fenêtres déplaçables / redimensionnables / réduites /
  agrandies, barre des tâches, menu Démarrer (liste alphabétique + vignettes), zone de notification,
  « Afficher le Bureau », notifications.
- Téléphone : écran d'accueil avec les mêmes applications, chacune en plein écran.
- iPad : bureau Windows complet.
- Serveur : connexion Discord, niveaux d'accès, revérification des rôles toutes les 5 minutes, temps réel
  (WebSocket), liste des engins, point d'entrée pour les intégrations (SIMCALL, eFIBI).

Les applications métier affichent pour l'instant une page « arrive dans une prochaine version ».

## Arborescence

```text
src/ARGOS.Shared    Contrats communs (connexion, temps réel, applications, engins)
src/ARGOS.Server    Serveur : Discord OAuth, WebSocket, API
src/ARGOS.App       Interface commune : écrans, bureau, vue mobile
src/ARGOS.Desktop   Exécutable Windows + macOS
src/ARGOS.iOS       iPhone + iPad
src/ARGOS.Android   Android (APK)
```

## Mise en route

### 1. Application Discord

Dans le [portail développeur Discord](https://discord.com/developers/applications), créer une application
(ou réutiliser celle de SIMCALL) et ajouter l'adresse de retour OAuth2 :

```text
http://46.105.78.32:8092/auth/discord/callback
```

### 2. Serveur

Créer `src/ARGOS.Server/appsettings.Local.json` (jamais versionné) :

```json
{
  "ARGOS": { "AccessKey": "une-longue-cle-aleatoire" },
  "Discord": {
    "ClientId": "…",
    "ClientSecret": "…",
    "PublicBaseUrl": "http://46.105.78.32:8092",
    "RequiredRoleId": "ID du rôle BSPP",
    "LevelRoles": {
      "operateur": "ID du rôle opérateur 18",
      "encadrement": "ID des rôles chef de garde, officier (séparés par des virgules)",
      "administration": "ID du rôle développeur"
    }
  }
}
```

Puis :

```bash
dotnet run --project src/ARGOS.Server
```

Le serveur écoute sur le port **8092** (SIMCALL reste sur 8080 ; adresse de retour Discord déjà autorisée).

### 3. Poste ordinateur

```bash
dotnet run --project src/ARGOS.Desktop
```

ARGOS s'ouvre en plein écran ; **F11** bascule en fenêtre. Pour viser un serveur de test, placer un
`argos.local.json` à côté de l'exécutable : `{ "ServerUrl": "http://127.0.0.1:8092" }`.

### 4. iPhone / iPad et Android

```bash
dotnet workload install ios android
dotnet build src/ARGOS.iOS -c Release -f net9.0-ios -r iossimulator-arm64
dotnet publish src/ARGOS.Android -c Release -f net9.0-android
```

Identifiant d'application : `fr.vdms.argos` (profil de provisionnement à créer sur le compte Apple Developer).

## Tester une notification

Avec un poste connecté :

```bash
curl -X POST http://127.0.0.1:8092/api/integrations/notify \
  -H "X-Argos-Key: une-longue-cle-aleatoire" -H "Content-Type: application/json" \
  -d '{"type":"notify","title":"ADAGIO ODE","text":"Nouvel ordre de départ","appId":"adagio-ode"}'
```

Ajouter `?userId=<ID Discord>` pour ne viser qu'un joueur. C'est la même entrée qu'utiliseront SIMCALL
(décroché d'un 18) et le script eFIBI.

## Export des ODE sur Discord

À chaque impression (ou réimpression) d'un ODE, l'image de la feuille et un résumé sont publiés dans un salon
Discord. Créer un webhook (salon › Modifier › Intégrations › Webhooks › Nouveau webhook › Copier l'URL), puis sur le VPS :

```powershell
cd C:\ARGOS
.\INSTALLER-ARGOS.cmd -OdeWebhookUrl "https://discord.com/api/webhooks/…"
```

L'adresse reste dans `appsettings.Local.json` (section `Webhooks:Ode`), jamais dans le dépôt.
