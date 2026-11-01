# Checklist de publication

Cette liste fixe ce qui doit être vérifié avant de publier une version. Elle ne remplace ni l'audit comparatif ni un test sur plusieurs machines : elle sert à produire des preuves datées pour chaque publication.

## 1. Avant de taguer

```powershell
dotnet restore OpenCleaner.sln -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build OpenCleaner.sln -c Release --no-restore --warnaserror
dotnet test OpenCleaner.sln -c Release --no-build
```

- La version dans `src/OpenCleaner.UI/OpenCleaner.UI.csproj` et les notes `docs/releases/<version>.md` existent.
- Les captures de `docs/preview/` correspondent à l'interface livrée.
- Les mentions de fonctionnalités du README et du site correspondent au code (voir `docs/CATALOG.md`).

## 2. Signature et distribution

- Un certificat `SIGNING_CERTIFICATE_BASE64` / `SIGNING_CERTIFICATE_PASSWORD` est configuré si la version doit être signée.
- Le workflow `Build and Release` vérifie l'Authenticode du `Setup.exe` après le packaging : une signature invalide avec un certificat configuré fait échouer la publication ; sans certificat, le statut `unsigned` est inscrit dans le résumé GitHub.
- La publication utilise le format **multi-fichiers self-contained + ReadyToRun** (`PublishSingleFile=false`). Vérifier que `publish/` contient l'apphost `OpenCleaner.UI.exe`, les DLL du runtime et le dossier de culture `fr`.
- Pour une version qui n'est pas la première, vérifier la présence d'un `*-delta.nupkg` dans `Releases/` et noter le rapport delta / paquet complet dans le résumé GitHub. Une absence de delta est un avertissement, pas un échec du premier release.
- Noter la taille du dossier `publish`, du `Setup.exe`, de l'archive portable et du paquet complet. Les mesures locales 1.7.0 sont dans [BENCHMARK-1.7.0.md](BENCHMARK-1.7.0.md).
- Après téléchargement des actifs publiés, contrôlez-les localement :

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify-release.ps1 -Path .\OpenCleaner-win-x64-Setup.exe -RequireValidSignature
powershell -ExecutionPolicy Bypass -File tools/verify-release.ps1 -Path .\OpenCleaner-win-x64-Portable.zip
```

Le script affiche le SHA-256 et le statut de signature de chaque exécutable. Il ne remplace pas un contrôle SmartScreen : lancez l'installeur sur une machine de test et notez l'avertissement éventuel.

- Une version non signée doit être annoncée comme telle. Ne pas écrire « signé » sans `Status : Valid`.

## 3. Parcours à vérifier sur une machine de test ou une VM

| Cas | Attendu |
|---|---|
| Annulation de l'invite UAC (démarrage HKLM) | Aucun changement ; message d'annulation ; liste inchangée. |
| Compte utilisateur standard | Les actions utilisateur fonctionnent ; les actions machine demandent l'élévation et échouent proprement si refusée. |
| Navigateur ouvert | L'analyse ignore le navigateur ; le nettoyage de la catégorie navigateurs est refusé avec le message prévu. |
| Interruption brutale pendant un nettoyage avec sauvegarde | Au redémarrage, la récupération est proposée ; les fichiers sauvegardés sont restaurables ; les fichiers sans sauvegarde restent supprimés. |
| Sauvegarde pleine ou échec de sauvegarde | La suppression du fichier concerné est bloquée ; aucun repli vers une suppression définitive. |
| Restauration avec le code de récupération | Les sauvegardes se déverrouillent et un fichier se restaure. |
| Disque presque plein pendant un nettoyage | L'erreur est signalée ; les fichiers déjà supprimés restent supprimés ; aucune boucle ni corruption du journal. |
| Écran à 100 %, 150 % et 200 %, thème clair et sombre | Aucune zone coupée aux dimensions minimales ; focus clavier visible. |

Spécifique à la version 1.7.0 :

| Cas | Attendu |
|---|---|
| Liste winget en français, en anglais et sortie volontairement mal formée | Les deux premières sont lues correctement ; la sortie mal formée affiche l'état « Lecture impossible » et reste consultable dans **Détails techniques**. |
| Application à version inconnue | Ligne visible, décochée par défaut, sélectionnable explicitement. |
| Refus de l'invite UAC sur un lot de mises à jour | Aucune application mise à jour ; la liste reste intacte ; message d'annulation. |
| Échec d'une application au milieu d'un lot | Les autres applications continuent ; la ligne en échec reste sélectionnée avec son code et son message. |
| Annulation pendant un lot | Le lot s'arrête, les lignes restantes sont marquées annulées ou ignorées, les boutons sont réactivés. |
| Fenêtre de mise à jour OpenCleaner | Vérification, téléchargement avec pourcentage, notes de version, état prêt, erreur et « à jour » s'affichent correctement ; aucun redémarrage sans clic explicite. |
| Téléchargement en arrière-plan désactivé | La fermeture de la fenêtre annule le téléchargement ; le bandeau ne promet pas une progression qui n'existe plus. |
| Six accents en clair et sombre, écrans à 100 %, 150 % et 200 % | Contraste des textes et des contrôles lisible ; couleurs de risque inchangées ; focus visible. |

Ces vérifications ne sont pas automatisées ici ; notez la date, la version de Windows et le résultat.

## 4. Limites connues à ne pas masquer

- Le benchmark comparatif du marché reste à produire : voir le protocole de l'audit comparatif du 30 octobre 2026 (section 7). Les mesures 1.7.0 sont locales et ne classent pas OpenCleaner face aux autres produits.
- La liste winget dépend de la langue et de la version de Windows Package Manager ; une sortie inattendue est signalée et consultable dans **Détails techniques**.
- Le téléchargement en arrière-plan d'OpenCleaner est optionnel ; une version portable n'est pas mise à jour automatiquement et l'application ne redémarre jamais sans action explicite.
- Le format multi-fichiers peut occuper plus de place sur le disque qu'un single-file compressé, mais il réduit la mémoire utilisée et la taille des deltas suivants.
- Les tests utilisent des fixtures temporaires ; certains caches d'applications et profils navigateur ne sont pas validés sur toutes les installations réelles (voir `docs/CATALOG.md`).
- Sur SSD, l'effacement sécurisé ne peut pas être garanti.

## 5. Constats datés

- **2026-10-30 — v1.6.1 publique non signée.** `OpenCleaner-win-x64-Setup.exe` téléchargé depuis GitHub Releases : `NotSigned`, SHA-256 `E0995C70DDB5386F661BA441571A32993F606AD7C1A538D4B7399FDB5D00DEA2`. `OpenCleaner-win-x64-Portable.zip` : `OpenCleaner.exe`, `Update.exe` et `OpenCleaner.UI.exe` internes `NotSigned`, SHA-256 de l'archive `92D919D88E4C38E178751D7D776CA8FD9966B3518B20DD0431146E8D6F0FA3BD`. SmartScreen non testé. Conclusion : la version publiée à cette date n'est pas signée ; ne pas la présenter comme telle.
- **2026-11-01 — v1.7.0 publique non signée.** Compilation Release stricte : 0 avertissement, 0 erreur. 474 tests réussis (362 moteur, 34 plugins, 78 interface). Aperçu WPF de toutes les fenêtres et des six accents. Vérification réelle de la page Mises à jour avec winget (deux applications listées, version inconnue décochée). Workflow `Build and Release` réussi en 2 min 44 ; actifs publiés : `OpenCleaner-win-x64-Setup.exe` 72,0 Mo, `OpenCleaner-win-x64-Portable.zip` 64,8 Mo, paquet complet 64,8 Mo, delta 58,0 Mo. `Setup.exe` téléchargé : `NotSigned`, SHA-256 `14F48CE6B2B1023AC50CEA6CA0D26888C77B2BDB89F53AD6317DC0F200E6C5B5`. Archive portable : SHA-256 `E7954988933A5428A3F954A9EA7823230D49F1F656BC49DE419961D19E64FA5C` ; `OpenCleaner.UI.exe` interne `NotSigned`, SHA-256 `C48553DEE940591566B39B6EE82A53F45012BD72CDE7B2ECB4283F7902AFD5A6`. Le premier delta est proche d'un paquet complet à cause du passage single-file → multi-fichiers ; les prochaines versions devraient produire des deltas plus petits. SmartScreen non testé. Conclusion : la version publiée à cette date n'est pas signée ; ne pas la présenter comme telle.
- **2026-11-01 — v1.7.1 publique non signée.** Compilation Release stricte : 0 avertissement, 0 erreur. 474 tests réussis (362 moteur, 34 plugins, 78 interface). Aperçus WPF de toutes les fenêtres, en clair et en sombre. Workflow `Build and Release` réussi en 2 min 41 ; actifs publiés : `OpenCleaner-win-x64-Setup.exe` 72,0 Mo, `OpenCleaner-win-x64-Portable.zip` 64,8 Mo, paquet complet 64,8 Mo, delta 0,3 Mo. `Setup.exe` téléchargé : `NotSigned`, SHA-256 `5592E1EFEC7E6A3DC89C2D7B819BDE733C29B4B22E08E16B7A3A5043B705EFBB`. Archive portable : SHA-256 `6EC4126068D66625CB51791235C8A518C04776D5BC3C7EDCC941A44BFDFE3CC4` ; `OpenCleaner.UI.exe` interne `NotSigned`, SHA-256 `CC7289AA3C80375065F5D1FB6A2E486A6C5935C7BA0E987E702639205263795F`. Le delta est petit, comme attendu après le passage au format multi-fichiers. SmartScreen non testé. Conclusion : la version publiée à cette date n'est pas signée ; ne pas la présenter comme telle.
