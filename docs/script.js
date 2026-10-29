"use strict";

// Inline icons keep the page independent from third-party scripts and fonts.
const icons = {
  github: "M8 20v-4m8 4v-4M5 9 4 4l5 2h6l5-2-1 5v4c0 3-3 4-7 4s-7-1-7-4z",
  download: "M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5",
  check: "m5 12 4 4L19 6",
  leaf: "M20 3C9 2 3 7 5 14c2 7 14 5 15-11ZM4 21 15 10",
  code: "m8 7-5 5 5 5m8-10 5 5-5 5M14 3l-4 18",
  wallet:
    "M20 7H5a2 2 0 0 1 0-4h14v4M3 5v14a2 2 0 0 0 2 2h15V7m0 5h-6v5h6m-3-3h.01",
  eye: "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Zm13 0a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
  sparkles:
    "m12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5ZM3 3v4M1 5h4",
  chart: "M12 3v9h9M8 4a9 9 0 1 0 12 12",
  folder: "M3 7h6l2-3h9a1 1 0 0 1 1 1v14H3Zm0 0V4h5l3 3",
  globe:
    "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM3 12h18M12 3c-5 5-5 13 0 18 5-5 5-13 0-18",
  image: "M3 3h18v18H3Zm0 13 6-6 6 6 3-3 3 3M16 7h.01",
  copy: "M8 3h13v13H8ZM3 8v13h13v-2",
  terminal: "M3 4h18v16H3Zm3 5 3 3-3 3m6 0h5",
  shield: "M12 3 3 7v6c0 4 9 8 9 8s9-4 9-8V7ZM8 12l3 3 5-6",
  search: "M16 10a6 6 0 1 1-12 0 6 6 0 0 1 12 0Zm-1 5 6 6",
  list: "M9 6h12M9 12h12M9 18h12M3 6h.01M3 12h.01M3 18h.01",
  info: "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM12 11v6m0-10h.01",
  "arrow-right": "M3 12h18m-7-7 7 7-7 7",
  menu: "M4 6h16M4 12h16M4 18h16",
};

document.querySelectorAll("[data-icon]").forEach(function (element) {
  const path = icons[element.dataset.icon];
  if (!path) return;
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  const shape = document.createElementNS(svg.namespaceURI, "path");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("fill", "none");
  svg.setAttribute("stroke", "currentColor");
  svg.setAttribute("stroke-width", "1.6");
  svg.setAttribute("stroke-linecap", "round");
  svg.setAttribute("stroke-linejoin", "round");
  svg.setAttribute("aria-hidden", "true");
  shape.setAttribute("d", path);
  svg.appendChild(shape);
  element.replaceWith(svg);
});

const menuButton = document.querySelector(".menu-toggle");
const navigation = document.getElementById("nav-links");
function closeMenu() {
  menuButton.setAttribute("aria-expanded", "false");
  menuButton.querySelector(".sr-only").textContent = "Ouvrir le menu";
  navigation.classList.remove("is-open");
}
menuButton.addEventListener("click", function () {
  const expanded = menuButton.getAttribute("aria-expanded") !== "true";
  menuButton.setAttribute("aria-expanded", String(expanded));
  menuButton.querySelector(".sr-only").textContent = expanded
    ? "Fermer le menu"
    : "Ouvrir le menu";
  navigation.classList.toggle("is-open", expanded);
});
navigation.querySelectorAll("a").forEach(function (link) {
  link.addEventListener("click", closeMenu);
});
document.addEventListener("keydown", function (event) {
  if (
    event.key === "Escape" &&
    menuButton.getAttribute("aria-expanded") === "true"
  ) {
    closeMenu();
    menuButton.focus();
  }
});
menuButton.hidden = false;
document.documentElement.classList.add("js");

const preview = document.getElementById("app-preview");
const themeButtons = document.querySelectorAll("[data-preview-theme]");
themeButtons.forEach(function (button) {
  button.addEventListener("click", function () {
    const light = button.dataset.previewTheme === "light";
    preview.src = light
      ? "preview/readme-light.png"
      : "preview/readme-dark.png";
    document.getElementById("preview-link").href = preview.src;
    preview.alt =
      "Interface " +
      (light ? "claire" : "sombre") +
      " d’OpenCleaner : catégories de nettoyage, bouton d’analyse et liste des résultats à vérifier.";
    themeButtons.forEach(function (themeButton) {
      themeButton.setAttribute("aria-pressed", String(themeButton === button));
    });
  });
});
document.querySelector(".preview-controls").hidden = false;

// A failed or rate-limited GitHub request leaves every download link usable.
async function fetchLatestRelease() {
  const controller = new AbortController();
  const timer = setTimeout(function () {
    controller.abort();
  }, 8000);
  try {
    const response = await fetch(
      "https://api.github.com/repos/VictorCESIVA/OpenCleaner/releases/latest",
      {
        signal: controller.signal,
        headers: { Accept: "application/vnd.github+json" },
      },
    );
    if (!response.ok) return;
    const release = await response.json();
    if (
      release.draft ||
      release.prerelease ||
      typeof release.tag_name !== "string"
    )
      return;
    const releaseUrl = new URL(release.html_url);
    if (
      releaseUrl.protocol !== "https:" ||
      releaseUrl.hostname !== "github.com" ||
      !releaseUrl.pathname.startsWith("/VictorCESIVA/OpenCleaner/releases/tag/")
    )
      return;
    let downloadUrl = releaseUrl.href;
    const assets = Array.isArray(release.assets) ? release.assets : [];
    const installer = assets.find(function (asset) {
      return typeof asset.name === "string" && /-Setup\.exe$/i.test(asset.name);
    });
    if (installer) {
      const assetUrl = new URL(installer.browser_download_url);
      if (
        assetUrl.protocol === "https:" &&
        assetUrl.hostname === "github.com" &&
        assetUrl.pathname.startsWith(
          "/VictorCESIVA/OpenCleaner/releases/download/",
        )
      ) {
        downloadUrl = assetUrl.href;
      }
    }
    document.querySelectorAll("[data-download]").forEach(function (link) {
      link.href = downloadUrl;
    });
    document.getElementById("release-link").href = releaseUrl.href;
    document.getElementById("version-text").textContent =
      release.tag_name + " · Notes de version";
  } catch {
    // Direct download links stay usable when GitHub's API is unavailable.
  } finally {
    clearTimeout(timer);
  }
}
fetchLatestRelease();
