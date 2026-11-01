# Catalogue des cibles de nettoyage

Ce catalogue décrit les dossiers réellement ciblés par les analyseurs intégrés, d'après le code de `src/OpenCleaner.Plugins.System/Plugins`. **Chemins vérifiés dans le code ; non validés sur toutes les installations et versions.**

Dossiers connus : `%LOCALAPPDATA%` = `C:\Users\<vous>\AppData\Local`, `%APPDATA%` = `C:\Users\<vous>\AppData\Roaming`, `%USERPROFILE%` = `C:\Users\<vous>`. Toutes les cibles sont analysées sans suivre les liens ou jonctions ; les fichiers plus récents que l'âge minimal, les fichiers verrouillés et les dossiers exclus sont ignorés.

## Navigateurs

Pour tous les profils `Default` ou `Profile *` des navigateurs Chromium, les sous-dossiers ciblés sont : `Cache`, `Code Cache`, `GPUCache` (≥ 1 h) ; `Service Worker\CacheStorage` (option, ≥ 7 j) ; `Cookies` et `Network\Cookies`, `History`, `Favicons` (options).

| Application | Racines de profils | Installation | Risque | Conservé (non supprimé) |
|---|---|---|---|---|
| Chrome | `%LOCALAPPDATA%\Google\{Chrome,Chrome Beta,Chrome Dev,Chrome SxS}\User Data` | Canaux stable, bêta, dev et canary ; seuls les profils `Default` et `Profile *` ; un `user-data-dir` personnalisé n'est pas couvert. | Faible ; « À vérifier » pour le cache hors connexion, les cookies et l'historique. | Signets, mots de passe, extensions, onglets, sessions et autres fichiers de profil. |
| Edge | `%LOCALAPPDATA%\Microsoft\{Edge,Edge Beta,Edge Dev,Edge SxS}\User Data` | Mêmes canaux et limites que Chrome. | Faible ; « À vérifier » pour les options. | Signets, mots de passe, extensions, onglets et autres fichiers de profil. |
| Brave | `%LOCALAPPDATA%\BraveSoftware\{Brave-Browser,Brave-Browser-Beta,Brave-Browser-Nightly}\User Data` | Canaux stable, bêta et nightly ; mêmes limites de profils. | Faible ; « À vérifier » pour les options. | Signets, mots de passe, extensions, onglets et autres fichiers de profil. |
| Vivaldi | `%LOCALAPPDATA%\{Vivaldi,Vivaldi Snapshot}\User Data` | Canaux stable et snapshot ; mêmes limites de profils. | Faible ; « À vérifier » pour les options. | Signets, mots de passe, extensions, onglets et autres fichiers de profil. |
| Opera, Opera GX, Opera Beta, Opera Developer | `%LOCALAPPDATA%\Opera Software\{Opera Stable,Opera GX Stable,Opera Beta,Opera Developer}` et `%APPDATA%\Opera Software\{Opera Stable,Opera GX Stable,Opera Beta,Opera Developer}` : mêmes fichiers Chromium | Profils stockés à la racine de chaque canal ; canaux listés uniquement. | Faible ; « À vérifier » pour les options. | Signets, mots de passe, extensions, onglets et autres fichiers de profil. |
| Firefox | `%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\` : `cache2\entries`, `startupCache`, `thumbnails` (≥ 1 h), `shader-cache` (option, ≥ 1 h) ; `%APPDATA%\Mozilla\Firefox\Profiles\*\` : `cookies.sqlite`, `favicons.sqlite` (options) | Tous les profils détectés ; installation personnalisée non couverte. | Faible ; « À vérifier » pour les cookies. | Signets, mots de passe, sessions, extensions et autres fichiers de profil. |

Les cookies sont supprimés par fichier complet, pour tous les profils ciblés : **aucune conservation par site**. Les navigateurs doivent être fermés : l'analyse ignore le navigateur ouvert, et le nettoyage de toute la catégorie navigateurs est refusé dès que l'un des navigateurs ciblés (Chrome, Edge, Brave, Firefox, Opera, Vivaldi) est ouvert.

## Applications

| Application | Chemins ciblés | Installation | Risque | Conservé (non supprimé) |
|---|---|---|---|---|
| Slack, Notion, WhatsApp, Signal | `%APPDATA%\<application>\` : `Cache`, `Code Cache`, `GPUCache`, `logs` (≥ 7 j) | Emplacements Roaming standard ; l'application ouverte est ignorée à l'analyse ; installation portable non couverte. | Faible | Messages, fichiers, réglages de compte et autres dossiers de données. |
| Zoom | `%APPDATA%\Zoom\logs` (≥ 30 j) | Emplacement standard ; Zoom ouvert est ignoré. | Faible | Enregistrements, réglages et données de réunion. |
| Microsoft Store | `%LOCALAPPDATA%\Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache` (≥ 7 j) | Identité de package fixe (version MSIX du Store). | Faible | Applications installées, licences et réglages. |
| OneDrive | `%LOCALAPPDATA%\Microsoft\OneDrive\logs` et `...\setup\logs` (≥ 30 j) | Journaux par utilisateur ; les journaux d'une autre session ne sont pas couverts. | Faible | Fichiers synchronisés, base de synchronisation et réglages. |
| Rapports de plantage (Crashpad) | `%LOCALAPPDATA%\Crashpad\reports` (≥ 7 j) | Dossier commun aux applications utilisant Crashpad. | Faible | Rapports récents (< 7 j) et données des applications. |
| Office | `%LOCALAPPDATA%\Microsoft\Office\16.0\Telemetry\*.log` (≥ 30 j) | Office 16.0 uniquement ; autres versions ou emplacements non couverts. | Faible | Documents, fichiers de récupération AutoRecover, cache Office et fichiers de synchronisation. |
| Thunderbird | `%LOCALAPPDATA%\Thunderbird\Profiles\*\` et `%APPDATA%\Thunderbird\Profiles\*\` : `cache2\entries`, `startupCache`, `thumbnails` (≥ 7 j) | Tous les profils détectés ; installation personnalisée non couverte. | Faible | Messages, carnets d'adresses et réglages de comptes. |
| Steam | `%ProgramFiles(x86)%\Steam`, `%LOCALAPPDATA%\Steam`, `%ProgramFiles%\Steam`, `%USERPROFILE%\Steam`, plus `\Steam` et `\Program Files (x86)\Steam` sur chaque disque fixe : `appcache\httpcache` (≥ 7 j), `logs` (≥ 30 j), `appcache\librarycache` (≥ 30 j), `steamapps\shadercache` (≥ 30 j) | Bibliothèques personnalisées et installations hors de ces dossiers non couvertes ; Steam doit être fermé, sinon aucune cible n'est analysée. | Faible ; « À vérifier » pour le cache de shaders. | Jeux, sauvegardes de jeux, téléchargements en cours et configuration Steam. |

## Développeurs et IA

Le module refuse tout nettoyage si VS Code, Discord, Spotify ou Teams est ouvert. Les cibles de l'application ouverte ne sont alors pas analysées.

| Application | Chemins ciblés | Installation | Risque | Conservé (non supprimé) |
|---|---|---|---|---|
| npm | `%LOCALAPPDATA%\npm-cache\_cacache`, `%APPDATA%\npm-cache\_cacache`, `%LOCALAPPDATA%\npm-cache\_logs` (≥ 7 j) | Emplacements par défaut ; cache déplacé non couvert. | Faible | `node_modules` des projets et configuration npm. |
| Yarn | `%LOCALAPPDATA%\Yarn\Cache` (≥ 7 j) | Cache Yarn classique à l'emplacement par défaut ; cache Yarn Berry (`.yarn/cache`) non couvert. | Faible | Dépendances des projets et configuration. |
| NuGet | `%LOCALAPPDATA%\NuGet\v3-cache` (≥ 14 j) | Cache HTTP par défaut ; cache global des paquets non ciblé. | Faible | Paquets installés et configuration. |
| pip | `%LOCALAPPDATA%\pip\cache\http` et `http-v2` (≥ 14 j) | Emplacement Windows par défaut ; `PIP_CACHE_DIR` personnalisé non couvert. | Faible | Environnements virtuels et paquets installés. |
| Gradle | `%USERPROFILE%\.gradle\daemon\*.out.log` (≥ 14 j), `%USERPROFILE%\.gradle\caches\build-cache-1` (≥ 30 j) | `GRADLE_USER_HOME` personnalisé non couvert. | Faible | Code source, dépendances et autres caches Gradle. |
| VS Code | `%APPDATA%\Code\` : `logs`, `CachedExtensionVSIXs`, `Cache`, `CachedData` (≥ 7 j) | Installation standard ; VS Code Insiders (`Code - Insiders`) non couvert. | Faible | Réglages, extensions et espaces de travail. |
| Discord | `%APPDATA%\discord\` : `Cache`, `Code Cache`, `GPUCache` (≥ 7 j) | Client standard. | Faible | Messages, réglages et autres données. |
| Spotify | `%LOCALAPPDATA%\Spotify\Browser\Cache` (≥ 7 j) | Client standard. | Faible | Téléchargements hors ligne et réglages. |
| Teams | `%APPDATA%\Microsoft\Teams\` : `Cache`, `Code Cache`, `GPUCache` (≥ 7 j) ; `%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\Logs\*.log` (≥ 7 j) | Teams classique et nouveau Teams MSIX ; autres emplacements non couverts. | Faible | Messages, réunions, fichiers et réglages. |
| JetBrains | `%LOCALAPPDATA%\JetBrains\*\log` (≥ 30 j) | Toutes les installations présentes sous ce dossier ; IDE installés ailleurs non couverts. | Faible | Projets, réglages d'IDE et plugins. |
| Cursor, Claude, ChatGPT | `%APPDATA%\Cursor\`, `%APPDATA%\Claude\` et `%APPDATA%\ChatGPT\` : `Cache`, `Code Cache`, `GPUCache`, `logs` (≥ 7 j) ; `%LOCALAPPDATA%\Cursor\` : mêmes dossiers ; `%LOCALAPPDATA%\Packages\OpenAI.ChatGPT*\LocalCache\Roaming\ChatGPT\` : mêmes dossiers | Emplacements standard ; aucune vérification d'arrêt pour ces trois outils, les fichiers verrouillés sont ignorés. | Faible | Conversations, projets, réglages et autres dossiers de données. |

## Système et fichiers temporaires

| Application | Chemins ciblés | Installation | Risque | Conservé (non supprimé) |
|---|---|---|---|---|
| Fichiers temporaires de l'utilisateur | `%LOCALAPPDATA%\Temp` (≥ 24 h) | Dossier temporaire standard de l'utilisateur ; fichiers récents et verrouillés ignorés. | Faible | Documents, profils, registre et fichiers récents (< 24 h). |
| Rapports d'erreurs Windows (WER) | `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive` et `...\ReportQueue` (≥ 7 j) | Emplacement utilisateur standard. | Faible | Documents, profils, registre et rapports récents. |
| Vidages mémoire d'application | `%LOCALAPPDATA%\CrashDumps\*.dmp` (≥ 7 j) | Fichiers `.dmp` uniquement. | À vérifier | Documents, profils, registre et autres fichiers du dossier. |
| Cache de shaders DirectX | `%LOCALAPPDATA%\D3DSCache` (≥ 7 j) | Cache DirectX de l'utilisateur, reconstruit par les applications. | Faible | Documents, profils et registre. |
| Temporaire Windows | `%WINDIR%\Temp` (≥ 7 j) | `C:\Windows\Temp` ; certains fichiers demandent des droits administrateur, les fichiers verrouillés sont ignorés. | À vérifier | Documents, profils, registre et fichiers récents. |
| Prefetch Windows | `%WINDIR%\Prefetch\*.pf` (≥ 30 j) | Windows uniquement, sans récursion. | Expert | Documents, profils et registre ; Prefetch est régénéré par Windows. |
| Miniatures et icônes de l'Explorateur | `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db` et `iconcache*.db` | Caches par utilisateur, recréés par Windows ; les fichiers verrouillés sont ignorés. | Faible | Documents, photos et réglages ; les miniatures sont régénérées. |

## Hors périmètre

- Aucun import de `winapp2.ini` ou de CleanerML : la liste des cibles est fixée dans le code.
- Aucune sélection de cookies par site : le fichier de cookies complet des profils ciblés est supprimé.
- Aucun moteur de résidus après désinstallation : OpenCleaner lance uniquement le désinstalleur officiel, sans détecter ni supprimer de restes.
- Aucun inventaire de désinstallation des applications Store/MSIX ou portables : seules les clés de désinstallation du registre sont lues.
- Aucune correspondance perceptuelle (images ou contenus similaires) : seuls les fichiers de contenu strictement identique (SHA-256) sont groupés.
- Aucune lecture de la MFT NTFS ni des tailles allouées : l'espace disque additionne des tailles logiques estimées.
- Les mises à jour winget, la mise à jour d'OpenCleaner et les thèmes ne modifient pas cette liste de cibles : ils n'ajoutent aucun emplacement de nettoyage.
