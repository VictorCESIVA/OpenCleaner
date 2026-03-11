// Initialisation des icônes Lucide
lucide.createIcons();

// --- 1. SCRIPT DE RÉCUPÉRATION DE LA DERNIÈRE RELEASE GITHUB ---
const GITHUB_API_URL = 'https://api.github.com/repos/VictorCESIVA/OpenCleaner/releases/latest';
const FALLBACK_DOWNLOAD_URL = 'https://github.com/VictorCESIVA/OpenCleaner/releases/latest';

async function fetchLatestRelease() {
    const downloadBtn = document.getElementById('download-btn');
    const versionText = document.getElementById('version-text');
    const dlText = document.getElementById('dl-text');

    try {
        const response = await fetch(GITHUB_API_URL);
        if (!response.ok) throw new Error('API Rate limit ou réseau');
        
        const data = await response.json();
        
        // Trouver l'asset correct (l'archive zip)
        const zipAsset = data.assets.find(a => a.name.endsWith('.zip') || a.name.endsWith('.exe'));
        
        if (zipAsset) {
            downloadBtn.href = zipAsset.browser_download_url;
            dlText.innerText = `Télécharger ${data.tag_name}`;
            versionText.innerText = `v${data.tag_name.replace('v', '')} prête`;
        } else {
            // S'il n'y a pas d'assets, renvoyer vers la page des releases
            downloadBtn.href = data.html_url;
            dlText.innerText = `Télécharger ${data.tag_name}`;
            versionText.innerText = `${data.tag_name}`;
        }
    } catch (error) {
        console.warn("Impossible de récupérer la release:", error);
        downloadBtn.href = FALLBACK_DOWNLOAD_URL;
        versionText.innerText = "Téléchargement disponible sur Github";
    }
}

// --- 2. GESTION DU SCROLL REVEAL ANIMATIONS ---
function reveal() {
    const reveals = document.querySelectorAll('.reveal');
    const windowHeight = window.innerHeight;
    const elementVisible = 100;

    reveals.forEach(element => {
        const elementTop = element.getBoundingClientRect().top;
        if (elementTop < windowHeight - elementVisible) {
            element.classList.add('active');
        }
    });
}

// --- 3. EFFET PARALLAX LÉGER SUR LE HERO ---
function parallax() {
    const bg = document.querySelector('.parallax-bg');
    if(bg) {
        const scrolled = window.pageYOffset;
        // Déplace l'image de fond doucement vers le bas
        bg.style.transform = `translateY(calc(-10% + ${scrolled * 0.3}px))`;
    }
}

// --- 4. EFFET DE FLOU DYNAMIQUE SUR LA NAVBAR ---
function updateNavbar() {
    const navbar = document.querySelector('.navbar');
    if (window.scrollY > 50) {
        navbar.style.background = 'rgba(15, 23, 42, 0.8)';
        navbar.style.borderBottom = '1px solid rgba(255,255,255,0.1)';
        navbar.style.boxShadow = '0 10px 30px rgba(0,0,0,0.5)';
    } else {
        navbar.style.background = 'rgba(255, 255, 255, 0.03)';
        navbar.style.boxShadow = 'none';
    }
}

// Ajouter des particules douces dans le fond
function createParticles() {
    const container = document.getElementById('particles-container');
    if(!container) return;
    
    container.style.position = 'fixed';
    container.style.top = '0';
    container.style.left = '0';
    container.style.width = '100vw';
    container.style.height = '100vh';
    container.style.zIndex = '-2';
    container.style.pointerEvents = 'none';
    
    for(let i = 0; i < 30; i++) {
        const particle = document.createElement('div');
        particle.style.position = 'absolute';
        particle.style.width = Math.random() * 4 + 1 + 'px';
        particle.style.height = particle.style.width;
        particle.style.background = 'rgba(59, 130, 246, 0.3)';
        particle.style.borderRadius = '50%';
        particle.style.top = Math.random() * 100 + 'vh';
        particle.style.left = Math.random() * 100 + 'vw';
        particle.style.boxShadow = '0 0 10px rgba(59, 130, 246, 0.5)';
        
        // Animation simple inline
        particle.style.transition = 'transform 20s linear, opacity 10s alternate infinite';
        particle.style.opacity = Math.random() * 0.5 + 0.1;
        
        container.appendChild(particle);
        
        // Mouvement lent
        setTimeout(() => {
            particle.style.transform = `translate(${Math.random() * 100 - 50}px, ${Math.random() * -200 - 50}px)`;
        }, 100);
    }
}

// Initialisation au chargement de la page
window.addEventListener('load', () => {
    fetchLatestRelease();
    reveal();
    createParticles();
    updateNavbar();
});

// Écouteurs de Scroll
window.addEventListener('scroll', () => {
    reveal();
    parallax();
    updateNavbar();
});

// Smooth scroll fluide pour les ancres de navigation
document.querySelectorAll('a[href^="#"]:not([href="#"])').forEach(anchor => {
    anchor.addEventListener('click', function (e) {
        e.preventDefault();
        document.querySelector(this.getAttribute('href')).scrollIntoView({
            behavior: 'smooth'
        });
    });
});
