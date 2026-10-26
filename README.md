# OpenCleaner

Utilitaire Windows gratuit et open source pour examiner les fichiers temporaires, les caches et l'occupation du disque. Interface WPF en français, thèmes sombre et clair.

## Utilisation

1. Choisissez une catégorie puis lancez **Analyser**. L'analyse ne supprime rien.
2. Vérifiez les résultats et leur niveau de risque. Une sélection de lignes limite le nettoyage à ces éléments ; sans sélection, le nettoyage porte sur les résultats affichés (filtre et recherche compris).
3. Confirmez **Nettoyer**. Chaque suppression de fichier passe par le gardien et une sauvegarde vérifiée.
4. Consultez **Restauration** pour restaurer ou supprimer les sauvegardes.

Les sauvegardes sont conservées localement : elles occupent de l'espace. Un nettoyage avec une sauvegarde sur le même disque ne libère pas immédiatement le volume affiché lors du scan. Supprimez seulement les sauvegardes dont vous n'avez plus besoin pour récupérer cet espace.

## Fonctions disponibles

- **Nettoyage rapide** : analyse des caches à faible risque, puis confirmation avant suppression.
- **Système et navigateurs** : temporaires anciens, rapports d'erreurs Windows (WER), vidages mémoire d'applications, shaders DirectX, miniatures, caches de Chrome, Edge, Brave, Firefox, Opera et Vivaldi sur plusieurs profils. Cookies et historique restent des options explicites ; les navigateurs doivent être fermés pour nettoyer.
- **Applications** : certains caches Steam, journaux Office et caches npm, Yarn, NuGet, pip, Gradle, VS Code, Teams, Discord, Spotify et JetBrains.
- **Doublons** : comparaison SHA-256 (avec seuil de taille configurable et pré-empreinte rapide) dans les dossiers personnels courants. L'original conservé est identifié et revérifié avant la suppression des copies.
- **Confidentialité** : repérage de certaines traces locales et fichiers contenant potentiellement des secrets. Ces résultats exigent une vérification manuelle ; cet outil n'est pas un antivirus.
- **Espace disque** : classement des enfants d'un dossier selon leur taille accessible. Les dossiers inaccessibles, liés et certains dossiers système sont exclus ; les totaux sont donc partiels.
- **Planification** : tâche Windows dans le contexte utilisateur, limitée aux fichiers à faible risque des catégories autorisées.
- **Registre** : analyse conservatrice de traces d'historique utilisateur, en mode expert, avec exportation avant modification.

Le nettoyage direct de Windows Update est suspendu. Les fichiers Office de récupération ou de synchronisation, Prefetch, node_modules et les contextes Docker sont conservés. La corbeille, les extensions DLL externes et la suppression d'artefacts bin/obj ne sont pas implémentés.

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

Les versions distribuées se trouvent dans [GitHub Releases](https://github.com/VictorCESIVA/OpenCleaner/releases). Les anciennes versions publiées ne contiennent pas nécessairement les correctifs présents dans ce dépôt. Un avertissement SmartScreen peut apparaître si le binaire n'est pas signé ; le caractère open source n'est pas une garantie de sûreté du fichier téléchargé.

## Sauvegardes et sécurité

Les sauvegardes de fichiers se trouvent sous %LOCALAPPDATA%/OpenCleaner/Backups. La restauration vérifie leur taille et leur empreinte SHA-256. Un fichier existant est sauvegardé avant remplacement. Les exportations du registre sont dans %LOCALAPPDATA%/OpenCleaner/RegistryBackups ; leur importation s'effectue manuellement après vérification.

Les sauvegardes locales ne sont pas chiffrées et ne remplacent pas une sauvegarde indépendante. Le gardien refuse notamment les chemins système critiques, les racines, les chemins réseau et périphériques, les alias 8.3, les flux alternatifs et les liens/jonctions. Tout `C:\Windows` est protégé sauf `Windows\Temp`, et `ProgramData` est refusé. Le nettoyage revalide la sélection issue du dernier scan et vérifie la sauvegarde avant chaque suppression.

L'application ne force plus la fermeture des navigateurs et ne redémarre plus automatiquement pour appliquer une mise à jour. La recherche des mises à jour des installations Velopack contacte GitHub ; il n'y a pas de télémétrie applicative.

## Organisation

- **Contracts** : interfaces et modèles.
- **Core** : gardiens, sauvegardes, transactions, planification et nettoyage rapide.
- **Plugins.System** : analyseurs et nettoyeurs intégrés.
- **UI** : fenêtres WPF et coordination des opérations.
- **Tests** : régressions du moteur et des plugins, sur des fixtures temporaires.
- **docs** : site statique de présentation ; rapports d'audit du [14 octobre 2026](docs/AUDIT-2026-10-14.md) et du [26 octobre 2026](docs/AUDIT-2026-10-26.md).

La CI compile et teste la solution sur Windows et refuse les vulnérabilités NuGet connues. Le workflow de release valide le tag, exécute ces vérifications et construit un package Velopack.

## Licence

[MIT](LICENSE).
