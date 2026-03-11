<div align="center">
  <img src="assets/logo.png" width="120" alt="OpenCleaner Logo" />
  <h1>OpenCleaner 🧹</h1>
  <p><b>L'alternative Open-Source, moderne et respectueuse de la vie privée pour nettoyer et optimiser Windows.</b></p>
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://github.com/votre-nom/OpenCleaner/actions/workflows/dotnet.yml/badge.svg)](https://github.com/votre-nom/OpenCleaner)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4.svg)]()
</div>

<hr />

## 🌟 À propos d'OpenCleaner

**OpenCleaner** est un utilitaire de nettoyage et d'optimisation conçu spécifiquement pour Windows. Contrairement aux solutions commerciales, OpenCleaner est **100% gratuit, Open-Source, sans aucune publicité**, et respecte intégralement votre vie privée (aucune télémétrie).

L'application est construite avec **.NET 8** et **WPF**, garantissant des performances exceptionnelles et une interface utilisateur fluide, dynamique et moderne.

---

## 🚀 Fonctionnalités Principales

- 🗑️ **Nettoyage Intelligent (Smart Clean) :** Analyse et supprime les fichiers temporaires de Windows, le cache des navigateurs (Chrome, Edge, Firefox), et vide la corbeille en un clic.
- 💿 **Analyseur d'Espace Disque :** Visualisez graphiquement les dossiers et fichiers qui consomment le plus d'espace sur votre disque dur.
- 🕵️ **Scanner de Confidentialité :** Détecte et neutralise les traceurs, l'historique de navigation et les fichiers de log inutiles pouvant compromettre votre vie privée.
- 👨‍💻 **Dev Cleaner :** Outil dédié aux développeurs pour nettoyer les dossiers `bin`, `obj`, `node_modules` et autres caches locaux (NPM, NuGet) qui s'accumulent au fil du temps.
- 🔁 **Détecteur de Doublons :** Identifie et vous permet de supprimer facilement les fichiers en double sur votre système (Optimisé pour gérer de très gros volumes avec un algorithme "Two-pass").
- ⏱️ **Planificateur intégré :** Automatisez vos tâches de nettoyage pour qu'elles s'exécutent en arrière-plan en toute discrétion.
- 🛡️ **Gardien (FileGuardian) :** Un système de sécurité robuste qui empêche la suppression des fichiers systèmes critiques, incluant une gestion de sauvegardes automatiques.

---

## 🖼️ Aperçus

*(Ajoutez ici des captures d'écran de l'application)*
- `Screenshot 1 : Le Tableau de bord (Smart Clean)`
- `Screenshot 2 : L'Analyseur d'espace`
- `Screenshot 3 : Le Nettoyeur Dev`

---

## 🛠️ Installation et Utilisation

### Télécharger la version prête à l'emploi (Recommandé)
1. Allez dans l'onglet [Releases](https://github.com/votre-nom/OpenCleaner/releases) de ce dépôt.
2. Téléchargez le dernier installateur complet `OpenCleaner_Installer.exe` ou la version portable.
3. Lancez l'application. Aucun prérequis n'est nécessaire (le framework .NET est inclus).

### Compiler à partir du code source
Si vous souhaitez contribuer ou compiler votre propre version :

**Prérequis :**
- [SDK .NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 ou Visual Studio Code

**Étapes :**
```cmd
# 1. Cloner le dépôt
git clone https://github.com/votre-nom/OpenCleaner.git

# 2. Se rendre dans le dossier
cd OpenCleaner

# 3. Compiler la solution
dotnet build OpenCleaner.sln -c Release

# 4. (Optionnel) Publier une version autonome
dotnet publish src/OpenCleaner.UI/OpenCleaner.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

L'exécutable se trouvera dans `src\OpenCleaner.UI\bin\Release\net8.0-windows\win-x64\publish\`.

---

## 🧩 Architecture

OpenCleaner est conçu de façon modulaire avec l'injection de dépendances (`Microsoft.Extensions.DependencyInjection`).

- `OpenCleaner.UI`: L'interface utilisateur WPF (.NET 8.0).
- `OpenCleaner.Core`: La logique centrale (Moteur de scan, Gardien de fichiers, Planificateur).
- `OpenCleaner.Contracts`: Interfaces et modèles de base, permettant de créer des plugins externes.
- `OpenCleaner.Plugins.System`: Implémentation de tous les nettoyeurs officiels (Système, Navigateurs, Doublons, Dev).

Vous pouvez facilement développer **vos propres plugins** en créant une DLL implémentant `ICleanerPlugin` et en la plaçant dans le dossier `Plugins/` de l'application.

---

## 🤝 Contribuer

Les contributions sont les bienvenues ! 
1. Forkez le projet.
2. Créez une branche pour votre fonctionnalité (`git checkout -b feature/NouvelleFonctionnalite`).
3. Commitez vos changements (`git commit -m 'Ajout d'une nouvelle fonctionnalité'`).
4. Poussez sur la branche (`git push origin feature/NouvelleFonctionnalite`).
5. Ouvrez une **Pull Request**.

Merci de respecter les conventions de nommage et d'ajouter/mettre à jour les tests unitaires via `xUnit` (`OpenCleaner.Core.Tests`).

---

## 📜 Licence

Ce projet est sous licence **MIT**. Vous êtes libre de l'utiliser, le modifier et le distribuer à la fois pour un usage commercial et privé. Voir le fichier [LICENSE](LICENSE) pour plus de détails.

---

<div align="center">
  <b>Conçu avec ❤️ pour la communauté Open-Source</b>
</div>
