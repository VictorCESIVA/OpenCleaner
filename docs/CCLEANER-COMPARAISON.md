# OpenCleaner et CCleaner — comparaison des fonctions

Comparaison établie à partir des fonctions publiques de CCleaner pour Windows (version 2026). OpenCleaner reste un utilitaire open source local, sans télémétrie ni catalogue propriétaire.

## Fonctions disponibles dans OpenCleaner 1.4.0

| CCleaner | OpenCleaner | Détail |
|---|---|---|
| Custom Clean | Oui | Catégories système, navigateurs, applications, registre, analyse et sélection. |
| Nettoyage des navigateurs | Oui | Chrome, Edge, Brave, Firefox, Opera, Vivaldi ; cookies et historique sur option. |
| Health Check | Oui | Score sur 100, cinq sections mesurées, constats expliqués, actions directes et historique local des 30 derniers jours. Aucune correction silencieuse : chaque action destructive reste confirmée. |
| Corbeille | Oui | Consultation du volume et vidage explicite et irréversible. |
| Prefetch | Oui | Cible experte, fichiers de plus de 30 jours. |
| Applications (Office, Thunderbird, caches dev) | Oui | Office, Thunderbird, npm, Yarn, NuGet, pip, Gradle, VS Code, Teams, Discord, Spotify, JetBrains. |
| Outils IA | Oui | Caches et journaux de Cursor, Claude et ChatGPT. |
| Startup Manager | Partiel | HKCU Run/RunOnce et dossier Démarrage avec sauvegarde, réactivation et impact estimé ; HKLM en lecture seule. |
| Apps Uninstaller | Oui | Liste, recherche, portée, taille et lancement du désinstalleur officiel. |
| Software Updater | Oui | Via `winget`, avec sortie visible, confirmation et annulation. |
| Disk Analyzer | Oui | Carte proportionnelle, navigation instantanée dans l'arborescence scannée, liste et plus gros fichiers. |
| Duplicate Finder | Oui | SHA-256 avec pré-empreinte et conservation vérifiée de l'original. |
| Registry Cleaner | Partiel | Historiques HKCU ciblés, export avant suppression, liste blanche. |
| Scheduled Cleaning | Oui | Tâche Windows dans le contexte utilisateur. |
| Smart Clean | Partiel | Analyse rapide et nettoyage des fichiers Safe ; pas de déclenchement automatique à la fermeture du navigateur. |
| Drive Wiper | Partiel | Effacement sécurisé de fichiers sélectionnés (3 passes), pas d'effacement de l'espace libre d'un disque entier. |
| Restauration | Oui | Sauvegardes chiffrées, code de récupération, quarantaine et restauration. |

## Fonctions non implémentées

| Fonction CCleaner | Raison |
|---|---|
| Driver Updater | Nécessite un catalogue propriétaire de pilotes et une chaîne de confiance adaptée ; hors périmètre d'un utilitaire local. |
| Performance Optimizer / Sleep mode | Suspension forcée de processus tiers, risque de perte de données et d'instabilité. |
| Cloud Drive Cleaner | Dépend des APIs Google Drive/OneDrive/Dropbox et de leurs quotas ; aucun accès cloud dans OpenCleaner. |
| Browser Plugin Manager | Gestion spécifique à chaque navigateur et risque de casser des extensions ; non implémenté. |
| File Recovery (Recuva) | Produit distinct ; OpenCleaner sauvegarde avant suppression mais ne récupère pas les fichiers déjà effacés. |
| Speccy | Monitoring matériel avancé ; la Santé du PC fournit les informations de base. |
| Windows Update cache | Nettoyage coordonné avec les services Windows encore suspendu par prudence. |
| Windows.old, journaux d'événements, cache de polices | Opérations système sensibles ou nécessitant l'arrêt de services ; non exposées. |
| Effacement de l'espace libre d'un disque | Très destructif et sans garantie sur SSD ; seul l'effacement de fichiers est proposé. |
| Gestion des points de restauration | Dépend de VSS et peut supprimer des points utiles ; non implémenté. |

## Principes communs

- OpenCleaner ne ferme pas les navigateurs et ne force pas les mises à jour.
- Les suppressions de fichiers passent par une sauvegarde chiffrée, sauf l'effacement sécurisé explicitement choisi.
- Les chemins critiques, les jonctions, les autres profils et les sauvegardes sont protégés.
- Aucune télémétrie, aucun compte et aucune connexion hors mise à jour Velopack ou `winget` lancé par l'utilisateur.

## Points où OpenCleaner va plus loin

- **Résultat expliqué, pas seulement chiffré.** Le score de santé détaille ses cinq sections, affiche les points perdus et laisse chaque correction à confirmer dans l'outil concerné.
- **Espace disque plus lisible.** La carte proportionnelle situe immédiatement les gros postes ; la navigation dans l'arborescence déjà scannée est instantanée et les zones protégées sont visuellement distinctes.
- **Nettoyage réversible par défaut.** Chaque suppression est sauvegardée et chiffrée, avec vérification avant suppression et restauration depuis l'application.
- **Transparence sur les estimations.** L'impact au démarrage, les tailles de caches et les limites de l'analyse disque sont présentés comme des estimations, avec leur méthode.
- **Aucune pression commerciale.** Pas de publicité, pas de compte, pas de télémétrie, pas de fonctionnalité bridée derrière une offre payante.
- **Code vérifiable.** Le code source et les tests sont publics sous licence MIT.

Ces avantages ne rendent pas OpenCleaner meilleur sur tout : CCleaner reste plus large sur les pilotes, la surveillance matérielle et la récupération de fichiers, fonctions volontairement hors périmètre.

