// Local SVG symbols: the presentation site requires no third-party script or font.
const icons = {
    github: 'M8 20v-4m8 4v-4M5 9l-1-5 5 2h6l5-2-1 5v4c0 3-3 4-7 4s-7-1-7-4z',
    download: 'M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5',
    'shield-check': 'M12 3 3 7v6c0 4 9 8 9 8s9-4 9-8V7zM8 12l3 3 5-6',
    'chevron-down': 'm6 9 6 6 6-6',
    sparkles: 'm12 2 3 7 7 3-7 3-3 7-3-7-7-3 7-3z',
    'pie-chart': 'M12 3v9h9M8 4a9 9 0 1 0 12 12',
    'eye-off': 'm3 3 18 18M8 5c6-2 11 4 13 7l-3 4M6 6l-5 6c3 5 8 8 13 7',
    files: 'M8 3h10l3 3v13H8zM3 7v15h13M15 3v6h6',
    terminal: 'M3 4h18v16H3zm3 5 3 3-3 3m6 0h5',
    'arrow-right': 'M3 12h18m-7-7 7 7-7 7'
};
document.querySelectorAll('[data-lucide]').forEach(function (element) {
    const path = icons[element.dataset.lucide];
    if (!path) return;
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', '24');
    svg.setAttribute('height', '24');
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '1.7');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    svg.setAttribute('aria-hidden', 'true');
    if (element.className) svg.setAttribute('class', element.className);
    const shape = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    shape.setAttribute('d', path);
    svg.appendChild(shape);
    element.replaceWith(svg);
});

const releasePage = 'https://github.com/VictorCESIVA/OpenCleaner/releases/latest';
async function fetchLatestRelease() {
    const button = document.getElementById('download-btn');
    const status = document.getElementById('version-text');
    const label = document.getElementById('dl-text');
    const controller = new AbortController();
    const timer = setTimeout(function () { controller.abort(); }, 10000);
    try {
        const response = await fetch('https://api.github.com/repos/VictorCESIVA/OpenCleaner/releases/latest',
            { signal: controller.signal });
        if (!response.ok) throw new Error('Release unavailable');
        const release = await response.json();
        const assets = Array.isArray(release.assets) ? release.assets : [];
        const installer = assets.find(function (asset) { return /-Setup\.exe$/i.test(asset.name); });
        button.href = releasePage;
        if (installer) {
            const url = new URL(installer.browser_download_url);
            if (url.protocol === 'https:' && url.hostname === 'github.com' &&
                url.pathname.startsWith('/VictorCESIVA/OpenCleaner/releases/download/')) {
                button.href = url.href;
            }
        }
        label.textContent = 'Télécharger ' + String(release.tag_name || '');
        status.textContent = String(release.tag_name || 'Version disponible');
    } catch {
        button.href = releasePage;
        label.textContent = 'Voir les téléchargements';
        status.textContent = 'Versions disponibles sur GitHub';
    } finally { clearTimeout(timer); }
}

const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
const reveals = document.querySelectorAll('.reveal');
if (!reducedMotion && 'IntersectionObserver' in window) {
    const observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
            if (entry.isIntersecting) {
                entry.target.classList.add('active');
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.05 });
    reveals.forEach(function (element) { element.classList.add('pending-reveal'); observer.observe(element); });
}

let framePending = false;
window.addEventListener('scroll', function () {
    if (framePending) return;
    framePending = true;
    requestAnimationFrame(function () {
        const navbar = document.querySelector('.navbar');
        navbar.classList.toggle('scrolled', window.scrollY > 50);
        if (!reducedMotion) {
            const background = document.querySelector('.parallax-bg');
            if (background) background.style.transform = 'translateY(calc(-10% + ' + window.scrollY * 0.15 + 'px))';
        }
        framePending = false;
    });
}, { passive: true });

document.querySelectorAll('a[href^="#"]:not([href="#"])').forEach(function (anchor) {
    anchor.addEventListener('click', function (event) {
        const target = document.querySelector(anchor.getAttribute('href'));
        if (!target) return;
        event.preventDefault();
        target.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth' });
        if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
        target.focus({ preventScroll: true });
    });
});

fetchLatestRelease();
