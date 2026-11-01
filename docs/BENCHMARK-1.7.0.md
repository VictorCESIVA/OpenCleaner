# Mesures de publication et de démarrage — 1.7.0

## Objet

Comparer les variantes de publication d'OpenCleaner 1.7.0 sur une même machine, puis choisir celle qui offre le meilleur compromis entre mémoire, démarrage et facilité de mise à jour. Ces mesures ne constituent pas un benchmark comparatif du marché : un seul poste, un seul système et peu de répétitions.

## Environnement

- Windows 11 Famille, version 10.0.26200 (build 26200), x64.
- AMD Ryzen 7 4800H, 15,4 Go de RAM.
- .NET SDK 10.0.403, build Release, `win-x64`, self-contained.
- Application publiée avec `SatelliteResourceLanguages=en;fr`.

## Variantes mesurées

| Variante | Taille du dossier | Fichiers | Exécutable principal | Démarrage à chaud (3 essais) | Mémoire de travail après démarrage |
| --- | ---: | ---: | ---: | ---: | ---: |
| Référence 1.6.x, single-file | 137,2 Mo | 12 | 127,3 Mo | non remesuré | non remesuré |
| Multi-fichiers self-contained | 136,5 Mo | 278 | 0,2 Mo (apphost) | 1,303 s ; 1,326 s ; 1,951 s | 144 Mo ; 144 Mo ; 156 Mo |
| Multi-fichiers + ReadyToRun | 139,4 Mo | 278 | 0,2 Mo (apphost) | 1,245 s ; 1,257 s ; 1,479 s | 145 Mo ; 144 Mo ; 159 Mo |
| Single-file + compression | 66,3 Mo | 12 | 56,3 Mo | 1,425 s ; 1,486 s ; 1,509 s | 249 Mo ; 253 Mo ; 261 Mo |

Le premier essai de chaque série est plus lent : il inclut le chargement à froid des fichiers et du cache du système. Les essais suivants représentent mieux un lancement à chaud.

## Méthode

- Publication dans des dossiers séparés avec `dotnet publish` en Release, `win-x64`, self-contained.
- Mesure avec `tools/bench-startup.ps1` : lancement du processus, attente du premier `MainWindowHandle` visible, relevé de `WorkingSet64`, `PrivateMemorySize64` et `PeakWorkingSet64`, puis fermeture propre.
- Trois essais par variante, sans redémarrage de la machine entre les essais. Les valeurs de démarrage sont exprimées en secondes et arrondies au millième.
- Aucun nettoyage, aucune suppression, aucune modification du registre et aucune installation n'ont été exécutés pendant les mesures.

## Décision

La variante **multi-fichiers + ReadyToRun** est retenue pour l'installateur et l'archive portable :

- mémoire de travail nettement plus basse que le single-file compressé (environ 145 Mo contre 250 Mo) ;
- démarrage à chaud légèrement plus rapide ;
- fichiers d'infrastructure inchangés d'une version à l'autre, ce qui permet à Velopack de produire des deltas plus petits. Un exécutable single-file change entièrement à chaque version et rapproche chaque delta d'un téléchargement complet.

Le single-file compressé réduit fortement la taille du dossier, mais il augmente la mémoire utilisée et ne favorise pas les mises à jour différentielles. Il n'est donc pas retenu pour la version installée.

## Paquet Velopack

Un empaquetage de contrôle a été réalisé avec `vpk pack` sur le dossier multi-fichiers + ReadyToRun, sous le nom de test `OpenCleanerTest` et sans signature :

| Actif de test | Taille |
| --- | ---: |
| `OpenCleanerTest-test-Setup.exe` | 72,0 Mo |
| `OpenCleanerTest-test-Portable.zip` | 64,8 Mo |
| `OpenCleanerTest-1.7.0-test-test-full.nupkg` | 64,8 Mo |

Le téléchargement complet reste donc comparable à celui du single-file compressé, alors que la mémoire utilisée est nettement plus basse. Ce paquet de test n'a pas été publié et ne contient pas de delta, puisqu'il s'agit d'une première version de test.

## Limites

- Une seule machine et un seul système ; les valeurs varient selon le processeur, le disque, les antivirus et l'état du cache.
- Les essais sont majoritairement à chaud. Un démarrage après redémarrage complet n'a pas été mesuré séparément.
- La mémoire de travail inclut le runtime .NET et WPF ; elle ne représente pas la mémoire privée réelle de tous les composants.
- La taille du dossier publié n'est pas la taille du paquet Velopack ni celle du téléchargement : Velopack compresse les fichiers et génère des deltas.
- Aucune mesure de temps CPU, de consommation disque ou de latence d'interface n'est incluse.
