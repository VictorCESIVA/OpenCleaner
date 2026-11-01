# Référence technique d’OpenCleaner

Pour télécharger l’application et commencer à l’utiliser, consultez le [guide utilisateur](../README.md). Ce document rassemble le détail des fonctions, les instructions de compilation et les mécanismes de protection des fichiers.

## Fonctions disponibles

- **Nettoyage rapide** : analyse des caches à faible risque, puis confirmation avant suppression.
- **Système et navigateurs** : temporaires anciens, rapports d'erreurs Windows (WER), vidages mémoire d'applications, shaders DirectX, miniatures, caches de Chrome, Edge, Brave, Firefox, Opera et Vivaldi sur plusieurs profils, y compris les canaux bêta, dev et canary. Cookies et historique restent des options explicites ; les navigateurs doivent être fermés pour nettoyer. Prefetch Windows est proposé en mode expert (fichiers de plus de 30 jours).
- **Applications** : certains caches Steam, journaux Office, caches Thunderbird, caches npm, Yarn, NuGet, pip, Gradle, VS Code, Teams, Discord, Spotify, JetBrains et outils IA (Cursor, Claude, ChatGPT). Une catégorie dédiée couvre les caches et journaux de Slack, Notion, WhatsApp, Signal, Zoom, Microsoft Store, OneDrive et les rapports de plantage Crashpad.
- **Doublons** : comparaison SHA-256 (avec seuil de taille configurable et pré-empreinte rapide) dans les dossiers personnels courants. L'utilisateur conserve la copie de son choix en la laissant décochée ; au moins une copie par groupe est exigée et une copie survivante est revérifiée avant la suppression des autres. Les fichiers à plusieurs liens physiques sont ignorés : leur suppression ne libère aucun octet.
- **Confidentialité** : repérage de certaines traces locales et fichiers contenant potentiellement des secrets. Ces résultats exigent une vérification manuelle ; cet outil n'est pas un antivirus.
- **Espace disque** : carte proportionnelle (treemap), navigation instantanée dans l'arborescence analysée, liste par dossier, plus gros fichiers et vue **Applications** qui associe les programmes installés aux dossiers mesurés. Une taille estimée à partir du registre est signalée lorsque le dossier n'a pas été mesuré. Un seul parcours, liens et jonctions ignorés ; les zones protégées sont signalées en ambre. Les tailles peuvent être approximatives pour les liens physiques, les fichiers compressés ou les dossiers système. L'outil est en lecture seule.
- **Santé du PC** : score sur 100 en cinq sections (stockage, mémoire, démarrage, entretien, propreté), constats expliqués et boutons d'action. Un historique des 30 derniers jours résume les nettoyages (nombre, fichiers, volume supprimé) sans afficher de chemins, un relevé quotidien local trace l'évolution de l'espace libre et le diagnostic peut être exporté en JSON sans chemins de fichiers. Les tailles de caches sont mesurées en lecture seule avec un délai maximal ; une mesure partielle est signalée. L'espace libre n'est pas un diagnostic de l'état physique du disque.
- **Désinstallation** : liste des applications installées d'après le registre ; seul le désinstalleur officiel est lancé, après confirmation, et aucun résidu n'est détecté ni supprimé.
- **Démarrage Windows** : consultation, activation/désactivation des éléments HKCU, du dossier Démarrage et des clés machine `HKLM\...\Run` / `RunOnce` avec sauvegarde dans le registre et autorisation administrateur. Tri par impact estimé. L'impact est une heuristique fondée sur la taille de l'exécutable et son emplacement, pas une mesure du temps de démarrage. Le dossier Démarrage commun reste en lecture seule.
- **Mises à jour logicielles** : liste par application via Windows Package Manager (`winget`), avec sélection, versions installée et disponible, source et état de chaque ligne. Les versions inconnues sont affichées mais décochées par défaut. Une seule autorisation administrateur est demandée par lot ; un échec n'interrompt pas les autres applications. La sortie brute reste consultable dans **Détails techniques** et l'annulation arrête le lot. La vérification est en lecture seule.
- **Effacement sécurisé** : écriture de trois passes aléatoires avant suppression de fichiers sélectionnés, sans sauvegarde OpenCleaner. Les fichiers en lecture seule ou possédant plusieurs liens physiques sont refusés. Sur SSD et en présence de copies ou instantanés, l'effacement définitif ne peut pas être garanti.
- **Planification** : tâche Windows dans le contexte utilisateur, limitée aux fichiers à faible risque des catégories autorisées (temporaires, navigateurs, miniatures, Office, Steam et caches d'applications). Les paramètres affichent la dernière exécution (fichiers, volume concerné, erreurs) et la prochaine occurrence prévue. Si OpenCleaner est déjà ouvert, Windows réessaie jusqu'à trois fois ; enregistrez à nouveau la planification pour mettre à jour une tâche existante.
- **Apparence** : thème clair ou sombre et six accents (menthe, azur, violet, turquoise, rose, graphite). Les couleurs dérivées sont calculées à partir de l'accent et vérifiées pour le contraste ; les couleurs de risque restent fixes. Le choix est enregistré dans les préférences locales et appliqué sans redémarrage.
- **Mise à jour d'OpenCleaner** : fenêtre dédiée avec progression, notes de version et état de l'opération. Le téléchargement en arrière-plan est optionnel ; l'installation ne démarre qu'après un clic explicite sur **Redémarrer et installer**. Aucun redémarrage automatique.
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

Publication autonome (multi-fichiers + ReadyToRun, format utilisé par les versions distribuées) :

```powershell
dotnet restore src/OpenCleaner.UI/OpenCleaner.UI.csproj -r win-x64 -p:PublishReadyToRun=true
dotnet publish src/OpenCleaner.UI/OpenCleaner.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -o artifacts/publish
```

Mesure locale du démarrage et de la mémoire :

```powershell
powershell -ExecutionPolicy Bypass -File tools/bench-startup.ps1 -ExePath artifacts/publish/OpenCleaner.UI.exe -Iterations 5
```

Contrôle des constructeurs et aperçu des fenêtres WPF, sans analyse ni nettoyage :

```powershell
dotnet run --project tools/OpenCleaner.Preview -c Release -- artifacts/ui-preview
```

Pour examiner l’interface dans une fenêtre interactive, ajoutez `--interactive` à cette commande. Cet aperçu ignore la migration des sauvegardes, les demandes de récupération et la recherche de mises à jour au démarrage. Les boutons des outils restent fonctionnels.

Les versions distribuées se trouvent dans [GitHub Releases](https://github.com/VictorCESIVA/OpenCleaner/releases). Les anciennes versions publiées ne contiennent pas nécessairement les correctifs présents dans ce dépôt. Le workflow de release peut signer les binaires si les secrets `SIGNING_CERTIFICATE_BASE64` et `SIGNING_CERTIFICATE_PASSWORD` sont configurés ; sans certificat, un avertissement SmartScreen peut apparaître. Le caractère open source n'est pas une garantie de sûreté du fichier téléchargé.

## Performance et publication

Le démarrage affiche la fenêtre principale avant les traitements non critiques. La migration des sauvegardes, l'initialisation SQLite, la lecture des transactions en attente et la purge du journal s'exécutent hors du thread d'interface ; les invites et la fenêtre de code de récupération restent sur le dispatcher. L'ordre migration → récupération → purge est conservé.

SQLite utilise un marqueur `PRAGMA user_version` pour ne migrer qu'une fois, des index sur `Transactions(State, CreatedAt)` et `Operations(TransactionId, Success)`, et une purge asynchrone. L'historique des nettoyages agrège les totaux puis lit seulement les dernières entrées.

Les icônes sont chargées à la demande pour les lignes visibles, avec un cache partagé et des requêtes en vol dédupliquées. La recherche des résultats est temporisée ; les sondes de santé indépendantes sont parallélisées ; les vues lourdes libèrent leurs données lorsqu'elles sont quittées ; la carte proportionnelle réutilise sa disposition, ses pinceaux et ses textes.

La publication distribuée est **multi-fichiers self-contained avec ReadyToRun**. Les assemblages d'infrastructure inchangés restent identiques entre deux versions, ce qui permet à Velopack de produire des deltas plus petits qu'avec un exécutable single-file. La première mise à jour depuis une version 1.6.x peut être un paquet complet. Les mesures locales et leurs limites sont documentées dans [BENCHMARK-1.7.0.md](BENCHMARK-1.7.0.md).

## Politique de suppression (depuis la version 1.6.0)

Le choix est explicite pour chaque nettoyage manuel : **Sauvegarder et supprimer**, **Supprimer sans sauvegarde**, ou **Annuler**. Il couvre les catégories de fichiers, le nettoyage rapide, les doublons, les caches développeur et la confidentialité. Sans sauvegarde, la suppression passe hors de la corbeille et ne peut pas être restaurée par OpenCleaner. Aucun échec de sauvegarde ne provoque de repli vers une suppression sans sauvegarde.

`CleanupTransactionScope` transmet ce choix au `FileGuardian` via le contexte asynchrone de l’opération. Le journal persiste `Transactions.CreateBackup` ; la migration applique `1` aux anciens enregistrements. Les chemins et les métadonnées sensibles ne sont pas ajoutés au journal. Pour une opération sans sauvegarde, une annulation, une erreur ou la reprise après redémarrage finalisent le journal sans tentative de restauration ; les suppressions déjà effectuées restent définitives.

`ScheduleConfig.CreateBackup` conserve le choix des nettoyages planifiés. La valeur par défaut reste `true` pour les configurations existantes ; choisir `false` dans les paramètres exige une confirmation explicite. Les exports du registre et la sauvegarde des éléments de démarrage sont indépendants de ce choix de nettoyage des fichiers et restent nécessaires à leur restauration ou réactivation.

## Sauvegardes et sécurité

Les sauvegardes de fichiers se trouvent sous %LOCALAPPDATA%/OpenCleaner/Backups. Leur contenu est chiffré en AES-256-GCM par blocs et leurs métadonnées sont authentifiées par HMAC-SHA256. La clé est protégée par DPAPI pour l'utilisateur courant dans %LOCALAPPDATA%/OpenCleaner/keys/backup.key et n'est pas transférable vers un autre utilisateur ou une autre machine. Un code de récupération à 256 bits, affiché une fois au premier démarrage et régénérable dans les paramètres, permet de reconstruire cette clé. Les sauvegardes antérieures sont migrées au démarrage ; celles qui échouent à la vérification sont isolées dans `Backups/Quarantine`. Le format chiffré n'est pas lisible par une version antérieure d'OpenCleaner. La restauration vérifie la taille, l'empreinte SHA-256 et l'authentification avant remplacement ; un fichier existant est conservé lors du remplacement puis sauvegardé. Si cette sauvegarde échoue ou si l'application est interrompue pendant l'opération, la version précédente peut rester en clair dans le dossier d'origine sous le nom .opencleaner-preserved-….tmp. Les exportations du registre sont dans %LOCALAPPDATA%/OpenCleaner/RegistryBackups ; leur importation s'effectue manuellement après vérification.

Le gardien refuse notamment les chemins système critiques, les racines, les chemins réseau et périphériques, les alias 8.3, les flux alternatifs et les liens/jonctions. Tout `C:\Windows` est protégé sauf `Windows\Temp` ; `ProgramData` et les autres profils utilisateur sont refusés. Les dossiers exclus par l'utilisateur sont refusés à l'analyse comme à la suppression, y compris leurs sous-dossiers. Les éléments machine désactivés sont sauvegardés sous `HKLM\SOFTWARE\OpenCleaner\DisabledStartup` et restaurés après une nouvelle autorisation administrateur ; le processus élevé n'accepte que des demandes `Run`/`RunOnce` validées, sans commande arbitraire. Le relevé d'espace libre reste dans `%LOCALAPPDATA%/OpenCleaner/space-history.json` : une valeur par jour et par volume, sans chemin de fichier. Le nettoyage revalide la sélection issue du dernier scan dans les deux modes. Si une sauvegarde est demandée, sa vérification est obligatoire avant la suppression ; sinon aucune copie du fichier n’est créée. Chaque nettoyage de l'interface ou du planificateur est enregistré dans `%LOCALAPPDATA%/OpenCleaner/transactions.db` ; avec sauvegarde, une annulation ou une erreur restaure les fichiers supprimés déjà enregistrés ; sans sauvegarde, elle arrête la suite du nettoyage sans restaurer les fichiers déjà supprimés. La page Santé en agrège les 30 derniers jours sans exposer les chemins. Les exécutions planifiées alimentent aussi `%APPDATA%/OpenCleaner/scheduled-runs.jsonl` (date, fichiers, volume concerné, erreurs), limité aux 200 dernières lignes. Au redémarrage, les transactions interrompues peuvent être récupérées sans écraser les fichiers recréés. Les modifications du registre déjà effectuées doivent être restaurées manuellement depuis leurs exports. Le journal ne contient plus les chemins locaux, est élagué après 30 jours, mais n'est pas une transaction atomique avec NTFS et n'est pas chiffré.

L'application ne force plus la fermeture des navigateurs et ne redémarre plus automatiquement pour appliquer une mise à jour. La fenêtre de mise à jour affiche la progression et les notes de version ; le téléchargement en arrière-plan est optionnel et l'installation attend un clic explicite sur **Redémarrer et installer**. La recherche et le téléchargement des mises à jour des installations Velopack contactent GitHub ; il n'y a pas de télémétrie applicative.

## Organisation

- **Contracts** : interfaces et modèles.
- **Core** : gardiens, sauvegardes, transactions, planification, nettoyage rapide, thèmes et outils winget.
- **Plugins.System** : analyseurs et nettoyeurs intégrés.
- **UI** : fenêtres WPF, thème appliqué aux ressources, fenêtre de mise à jour et coordination des opérations.
- **Tests** : régressions du moteur, des plugins et de l'interface, sur des fixtures temporaires.
- **docs** : site statique de présentation ; rapports d'audit du [14 octobre 2026](AUDIT-2026-10-14.md), du [26 octobre 2026](AUDIT-2026-10-26.md) et du [29 octobre 2026](AUDIT-2026-10-29.md), [comparaison avec CCleaner](CCLEANER-COMPARAISON.md), [chantier de localisation](LOCALIZATION.md) et [mesures 1.7.0](BENCHMARK-1.7.0.md).

La CI compile et teste la solution sur Windows et refuse les vulnérabilités NuGet connues. Le workflow de release valide le tag, exécute ces vérifications, construit un package Velopack et le signe si le certificat est fourni via les secrets GitHub.

## Licence

[MIT](../LICENSE).
