# Benchmark préliminaire Windows 10 — 30 octobre 2026

## Résultat

Sur un corpus synthétique de **64 fichiers temporaires totalisant 16 Mio**, OpenCleaner, BleachBit et FluentCleaner ont chacun supprimé les 64 fichiers lors de trois essais. Un fichier témoin situé hors du corpus était encore présent après chaque essai. **Ce résultat ne classe pas les outils sur leur couverture globale, leur sûreté ou leur vitesse.**

| Produit et mode | Essais terminés | Fichiers du corpus restants | Témoin présent | Interprétation |
| --- | ---: | --- | --- | --- |
| OpenCleaner, composant de fichiers temporaires, sans sauvegarde | 3/3 | 0, 0, 0 | Oui, 3/3 | Suppression du seul corpus sélectionné par le programme de mesure. |
| BleachBit 6.0.6 portable, `--clean system.tmp` | 3/3 | 0, 0, 0 | Oui, 3/3 | Nettoyage de la catégorie temporaires, qui peut comprendre d'autres fichiers de la VM. |
| FluentCleaner Classic, release `26.09.01`, `/AUTO` avec réglages par défaut | 3/3 | 0, 0, 0 | Oui, 3/3 | D'autres règles ont été exécutées. Le journal du premier essai indique notamment des opérations sur le registre. |
| CCleaner portable 6.41.0.11567, `/AUTO` sans configuration préalable | 3/3, code de sortie 0 | 64, 64, 64 | Oui, 3/3 | Aucun fichier du corpus n'a été supprimé dans ce scénario. La cause et la détection en mode interactif n'ont pas été établies. |
| PrivaZer portable 4.0.127, `CLEAN C: /HIDE /CLOSE` | 0/1, arrêt à la limite de 120 s | 64 au moment de l'arrêt | Oui | Essai non concluant ; il ne permet pas de juger sa détection ni son nettoyage. |

Les vérifications courantes du témoin contrôlaient seulement son existence, pas son contenu. Le contenu a été vérifié en plus lors de l'essai OpenCleaner avec sauvegarde ci-dessous. Le témoin unique ne démontre pas l'absence de suppression indésirable ailleurs.

## Sauvegarde OpenCleaner

Un essai supplémentaire du même composant a été effectué avec sauvegarde activée : **64 fichiers supprimés, 64 sauvegardes inventoriées, un fichier restauré dont le SHA-256 correspond à celui enregistré avant suppression**. Le contenu du fichier témoin correspondait au texte initial. Le résultat du composant indiquait 0 octet net récupéré avec sauvegarde ; aucune mesure indépendante de l'espace libre physique n'a été réalisée. Une seule restauration a été vérifiée, sans simuler la perte de la clé ou tester le code de récupération.

## Méthode

- VMware Workstation, Windows 10 Professionnel x64 build 19045, 2 processeurs virtuels et environ 2 Gio de RAM. Tests exécutés avec le compte invité configuré pour la VM.
- Corpus créé dans `%LOCALAPPDATA%\Temp\OpenCleanerBenchmarkFixture` : 64 fichiers aléatoires de 262 144 octets, dates de modification et d'accès réglées à 35 jours avant la création. Le témoin est dans `C:\Users\Public\OpenCleanerBenchmark`.
- Les trois répétitions de chaque produit sont reparties de l'instantané `OpenCleaner - base synthétique 16 Mio`. Les résultats ont été copiés sur l'hôte avant le retour à l'instantané.
- OpenCleaner a été appelé par un **programme de mesure distinct**, qui instancie `SystemTempPlugin`, `FileGuardian` et le gestionnaire de transactions du code source présent localement. Il analyse puis ne sélectionne pour suppression que les chemins du corpus. L'interface WPF et le bouton de nettoyage rapide n'ont pas été exercés par cet essai.
- BleachBit a été appelé avec son [interface en ligne de commande documentée](https://docs.bleachbit.org/doc/command-line-interface/). FluentCleaner et CCleaner ont reçu `/AUTO` après extraction, avec leurs paramètres par défaut ; leur périmètre est plus large ou différent. Aucun paramétrage interactif n'a été vérifié.
- Avant les nettoyages, trois analyses du composant OpenCleaner ont chacune identifié 64 fichiers et 16 777 216 octets. L'aperçu BleachBit `system.tmp` a également mentionné les 64 fichiers ; il incluait d'autres éléments temporaires de la VM. L'aperçu des autres produits n'a pas été recueilli.
- Le dépôt OpenCleaner était basé sur `3b3b00313fc1254a86017d6d7af3ee46a902a0fb` et contenait les modifications non commitées de l'utilisateur. Le diff des fichiers suivis et les résultats bruts sont conservés dans `C:\Users\noela\Documents\Virtual Machines\OpenCleaner-Benchmark`. L'application autonome préparée a pour SHA-256 `7239B438F720A04B234948E213F8BA3794D3082AC52954F8FF83C7470AF236EF` ; elle n'est pas l'exécutable lancé par les essais ci-dessus.

## Durées brutes observées

Ces durées sont conservées pour la transparence. **Elles ne doivent pas être comparées entre produits** : OpenCleaner mesure l'appel du moteur après analyse, alors que les autres valeurs incluent le démarrage d'un processus et des ensembles de règles différents. Une seule VM et un petit corpus ne caractérisent pas un PC réel.

| Mode | Durées des trois essais | Médiane |
| --- | --- | ---: |
| OpenCleaner, suppression du corpus, sans sauvegarde | 1,487 s ; 1,549 s ; 1,691 s | 1,549 s |
| BleachBit, processus et catégorie `system.tmp` | 5,472 s ; 2,303 s ; 2,325 s | 2,325 s |
| FluentCleaner, processus `/AUTO` et règles par défaut | 3,637 s ; 8,527 s ; 3,671 s | 3,671 s |
| CCleaner, processus `/AUTO` ; corpus inchangé | 6,528 s ; 6,359 s ; 5,949 s | 6,359 s |

L'essai OpenCleaner avec sauvegarde a duré 4,723 s pour la suppression après analyse ; la restauration et sa vérification sont réalisées après ce chronométrage. PrivaZer a été arrêté après environ 127 s, délai d'arrêt compris.

## Fiabilité et suite

La compilation Release de l'arbre local a réussi sans avertissement et 315 tests ont réussi avant ces essais. Le programme de mesure a passé un autocontrôle de sa restriction au dossier synthétique. Les outils tiers ont été obtenus de leurs sources officielles ; les empreintes de BleachBit et PrivaZer correspondaient aux valeurs publiées par leurs éditeurs. L'archive CCleaner portable proposée par le lien officiel était la version 6.41, pas la version 7 actuelle.

À faire pour un benchmark de marché complet : profils réels ou représentatifs de plusieurs navigateurs et applications, davantage de témoins avec vérification de contenu, espace libre mesuré avant/après, modes par défaut et périmètres communs configurés dans les interfaces, essais avec et sans sauvegarde, arrêt/reprise, Windows 11 et matériel physique. CCleaner et PrivaZer nécessitent une observation interactive pour expliquer les deux résultats non concluants. Aucune licence Pro, interface utilisateur, élévation UAC réelle, télémétrie ou consommation CPU/RAM n'a été mesurée ici.
