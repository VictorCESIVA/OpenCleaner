# OpenCleaner

Utilitaire Windows gratuit et open source pour examiner les fichiers temporaires, les caches et l'occupation du disque. Interface WPF en français avec une traduction anglaise en cours, thèmes sombre et clair, avec les outils accessibles dans une seule fenêtre.

## Utilisation

1. Choisissez une catégorie puis lancez **Analyser**. L'analyse ne supprime rien.
2. Vérifiez les résultats et leur niveau de risque. Une sélection de lignes limite le nettoyage à ces éléments ; sans sélection, le nettoyage porte sur les résultats affichés (filtre et recherche compris).
3. Confirmez **Nettoyer**. Chaque suppression de fichier passe par le gardien et une sauvegarde vérifiée.
4. Consultez **Restauration** pour restaurer ou supprimer les sauvegardes.

Au démarrage, un bandeau signale un disque sous le seuil d'alerte (10 % par défaut, réglable de 5 à 25 % dans les paramètres) avec un accès direct à **Santé du PC** ; il ne supprime rien. Le thème, la langue et ce seuil sont conservés dans `%APPDATA%/OpenCleaner/preferences.json`.

La langue se change dans **Paramètres > Langue** (français/anglais) et s'applique immédiatement. L'anglais couvre pour l'instant la fenêtre principale, les paramètres, l'espace disque, la santé du PC, le démarrage, la désinstallation et les mises à jour ; les autres écrans migrent progressivement (voir [docs/LOCALIZATION.md](docs/LOCALIZATION.md)).

Les sauvegardes sont conservées localement : elles occupent de l'espace. Un nettoyage avec une sauvegarde sur le même disque ne libère pas immédiatement le volume affiché lors du scan. Supprimez seulement les sauvegardes dont vous n'avez plus besoin pour récupérer cet espace.

### Raccourcis clavier

- `F5` : relancer l'analyse ou actualiser la page ouverte (Nettoyage, Espace disque, Santé, Démarrage).
- `Échap` : annuler l'opération en cours.
- `Ctrl+F` : placer le curseur dans le filtre des résultats.
- `Ctrl+E` : exporter le rapport d'analyse en JSON.
- Dans la carte d'espace disque : flèches pour parcourir les zones, `Entrée` pour ouvrir un dossier ou révéler un fichier.

## Fonctions disponibles

- **Nettoyage rapide** : analyse des caches à faible risque, puis confirmation avant suppression.
- **Système et navigateurs** : temporaires anciens, rapports d'erreurs Windows (WER), vidages mémoire d'applications, shaders DirectX, miniatures, caches de Chrome, Edge, Brave, Firefox, Opera et Vivaldi sur plusieurs profils, y compris les canaux bêta, dev et canary. Cookies et historique restent des options explicites ; les navigateurs doivent être fermés pour nettoyer. Prefetch Windows est proposé en mode expert (fichiers de plus de 30 jours).
- **Applications** : certains caches Steam, journaux Office, caches Thunderbird, caches npm, Yarn, NuGet, pip, Gradle, VS Code, Teams, Discord, Spotify, JetBrains et outils IA (Cursor, Claude, ChatGPT). Une catégorie dédiée couvre les caches et journaux de Slack, Notion, WhatsApp, Signal, Zoom, Microsoft Store, OneDrive et les rapports de plantage Crashpad.
- **Doublons** : comparaison SHA-256 (avec seuil de taille configurable et pré-empreinte rapide) dans les dossiers personnels courants. L'original conservé est identifié et revérifié avant la suppression des copies. Les fichiers à plusieurs liens physiques sont ignorés : leur suppression ne libère aucun octet.
- **Confidentialité** : repérage de certaines traces locales et fichiers contenant potentiellement des secrets. Ces résultats exigent une vérification manuelle ; cet outil n'est pas un antivirus.
- **Espace disque** : carte proportionnelle (treemap), navigation instantanée dans l'arborescence analysée, liste par dossier, plus gros fichiers et vue **Applications** qui associe les programmes installés aux dossiers mesurés. Une taille estimée à partir du registre est signalée lorsque le dossier n'a pas été mesuré. Un seul parcours, liens et jonctions ignorés ; les zones protégées sont signalées en ambre. Les tailles peuvent être approximatives pour les liens physiques, les fichiers compressés ou les dossiers système. L'outil est en lecture seule.
- **Santé du PC** : score sur 100 en cinq sections (stockage, mémoire, démarrage, entretien, propreté), constats expliqués et boutons d'action. Un historique des 30 derniers jours résume les nettoyages (nombre, fichiers, volume supprimé) sans afficher de chemins, un relevé quotidien local trace l'évolution de l'espace libre et le diagnostic peut être exporté en JSON sans chemins de fichiers. Les tailles de caches sont mesurées en lecture seule avec un délai maximal ; une mesure partielle est signalée. L'espace libre n'est pas un diagnostic de l'état physique du disque.
- **Désinstallation** : liste des applications installées et lancement du désinstalleur officiel après confirmation.
- **Démarrage Windows** : consultation, activation/désactivation des éléments HKCU, du dossier Démarrage et des clés machine `HKLM\...\Run` / `RunOnce` avec sauvegarde dans le registre et autorisation administrateur. Tri par impact estimé. L'impact est une heuristique fondée sur la taille de l'exécutable et son emplacement, pas une mesure du temps de démarrage. Le dossier Démarrage commun reste en lecture seule.
- **Mises à jour logicielles** : vérification et mise à jour groupée via Windows Package Manager (`winget`), avec sortie visible et annulation. La mise à jour demande une autorisation administrateur ; les applications dont la version installée est inconnue sont ignorées.
- **Effacement sécurisé** : écriture de trois passes aléatoires avant suppression de fichiers sélectionnés, sans sauvegarde OpenCleaner. Les fichiers en lecture seule ou possédant plusieurs liens physiques sont refusés. Sur SSD et en présence de copies ou instantanés, l'effacement définitif ne peut pas être garanti.
- **Planification** : tâche Windows dans le contexte utilisateur, limitée aux fichiers à faible risque des catégories autorisées (temporaires, navigateurs, miniatures, Office, Steam et caches d'applications). Les paramètres affichent la dernière exécution (fichiers, volume concerné, erreurs) et la prochaine occurrence prévue. Si OpenCleaner est déjà ouvert, Windows réessaie jusqu'à trois fois ; enregistrez à nouveau la planification pour mettre à jour une tâche existante.
- **Exclusions** : dossiers exclus du nettoyage (et de leurs sous-dossiers), conservés dans `%APPDATA%/OpenCleaner/exclusions.json`. Le gardien et les analyseurs les refusent ; les protections système restent appliquées.
- **Registre** : analyse conservatrice de traces d'historique utilisateur, en mode expert, avec exportation avant modification.

Le nettoyage direct de Windows Update reste suspendu. Les fichiers Office de récupération ou de synchronisation, node_modules et les contextes Docker sont conservés. Les extensions DLL externes et la suppression d'artefacts bin/obj ne sont pas implémentés. Le pilote de périphériques (Driver Updater), la mise en veille forcée d'applications, les APIs cloud et la récupération de fichiers supprimés restent hors périmètre.

## Compiler et tester

Prérequis : Windows 10/11 x64 et [SDK .NET 10 LTS](https://dotnet.microsoft.com/download/dotnet/10.0) à jour.

```powershell
git clone https://github.com/VictorCESIVA/OpenCleaner.git
cd OpenCleaner
dotnet restore OpenCleaner.sln
dotnet build OpenCleaner.sln -c Release --no-restore
dotnet test OpenCleaner.sln -c Release --no-build
dotnet run --project src/OpenCleaner.UI -c Release --no-build
```

Publication autonome :

```powershell
dotnet publish src/OpenCleaner.UI/OpenCleaner.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/publish
```

Contrôle des constructeurs et aperçu des fenêtres WPF, sans analyse ni nettoyage :

```powershell
dotnet run --project tools/OpenCleaner.Preview -c Release -- artifacts/ui-preview
```

Les versions distribuées se trouvent dans [GitHub Releases](https://github.com/VictorCESIVA/OpenCleaner/releases). Les anciennes versions publiées ne contiennent pas nécessairement les correctifs présents dans ce dépôt. Le workflow de release peut signer les binaires si les secrets `SIGNING_CERTIFICATE_BASE64` et `SIGNING_CERTIFICATE_PASSWORD` sont configurés ; sans certificat, un avertissement SmartScreen peut apparaître. Le caractère open source n'est pas une garantie de sûreté du fichier téléchargé.

## Sauvegardes et sécurité

Les sauvegardes de fichiers se trouvent sous %LOCALAPPDATA%/OpenCleaner/Backups. Leur contenu est chiffré en AES-256-GCM par blocs et leurs métadonnées sont authentifiées par HMAC-SHA256. La clé est protégée par DPAPI pour l'utilisateur courant dans %LOCALAPPDATA%/OpenCleaner/keys/backup.key et n'est pas transférable vers un autre utilisateur ou une autre machine. Un code de récupération à 256 bits, affiché une fois au premier démarrage et régénérable dans les paramètres, permet de reconstruire cette clé. Les sauvegardes antérieures sont migrées au démarrage ; celles qui échouent à la vérification sont isolées dans `Backups/Quarantine`. Le format chiffré n'est pas lisible par une version antérieure d'OpenCleaner. La restauration vérifie la taille, l'empreinte SHA-256 et l'authentification avant remplacement ; un fichier existant est conservé lors du remplacement puis sauvegardé. Si cette sauvegarde échoue ou si l'application est interrompue pendant l'opération, la version précédente peut rester en clair dans le dossier d'origine sous le nom .opencleaner-preserved-….tmp. Les exportations du registre sont dans %LOCALAPPDATA%/OpenCleaner/RegistryBackups ; leur importation s'effectue manuellement après vérification.

Le gardien refuse notamment les chemins système critiques, les racines, les chemins réseau et périphériques, les alias 8.3, les flux alternatifs et les liens/jonctions. Tout `C:\Windows` est protégé sauf `Windows\Temp` ; `ProgramData` et les autres profils utilisateur sont refusés. Les dossiers exclus par l'utilisateur sont refusés à l'analyse comme à la suppression, y compris leurs sous-dossiers. Les éléments machine désactivés sont sauvegardés sous `HKLM\SOFTWARE\OpenCleaner\DisabledStartup` et restaurés après une nouvelle autorisation administrateur ; le processus élevé n'accepte que des demandes `Run`/`RunOnce` validées, sans commande arbitraire. Le relevé d'espace libre reste dans `%LOCALAPPDATA%/OpenCleaner/space-history.json` : une valeur par jour et par volume, sans chemin de fichier. Le nettoyage revalide la sélection issue du dernier scan et vérifie la sauvegarde avant chaque suppression. Chaque nettoyage de l'interface ou du planificateur est enregistré dans `%LOCALAPPDATA%/OpenCleaner/transactions.db` ; une annulation ou une erreur restaure les fichiers supprimés déjà enregistrés. La page Santé en agrège les 30 derniers jours sans exposer les chemins. Les exécutions planifiées alimentent aussi `%APPDATA%/OpenCleaner/scheduled-runs.jsonl` (date, fichiers, volume concerné, erreurs), limité aux 200 dernières lignes. Au redémarrage, les transactions interrompues peuvent être récupérées sans écraser les fichiers recréés. Les modifications du registre déjà effectuées doivent être restaurées manuellement depuis leurs exports. Le journal ne contient plus les chemins locaux, est élagué après 30 jours, mais n'est pas une transaction atomique avec NTFS et n'est pas chiffré.

L'application ne force plus la fermeture des navigateurs et ne redémarre plus automatiquement pour appliquer une mise à jour. La recherche des mises à jour des installations Velopack contacte GitHub ; il n'y a pas de télémétrie applicative.

## Organisation

- **Contracts** : interfaces et modèles.
- **Core** : gardiens, sauvegardes, transactions, planification et nettoyage rapide.
- **Plugins.System** : analyseurs et nettoyeurs intégrés.
- **UI** : fenêtres WPF et coordination des opérations.
- **Tests** : régressions du moteur et des plugins, sur des fixtures temporaires.
- **docs** : site statique de présentation ; rapports d'audit du [14 octobre 2026](docs/AUDIT-2026-10-14.md), du [26 octobre 2026](docs/AUDIT-2026-10-26.md) et du [29 octobre 2026](docs/AUDIT-2026-10-29.md), [comparaison avec CCleaner](docs/CCLEANER-COMPARAISON.md) et [chantier de localisation](docs/LOCALIZATION.md).

La CI compile et teste la solution sur Windows et refuse les vulnérabilités NuGet connues. Le workflow de release valide le tag, exécute ces vérifications, construit un package Velopack et le signe si le certificat est fourni via les secrets GitHub.

## Licence

[MIT](LICENSE).
