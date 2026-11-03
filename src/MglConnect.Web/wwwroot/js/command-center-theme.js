// Apply a saved UI preference before styles load. No account data is stored.
(() => {
    let theme = 'dark';
    try {
        const saved = localStorage.getItem('mgl.ui.theme');
        if (saved === 'light' || saved === 'dark') theme = saved;
    } catch { /* Storage is optional; the default theme remains usable. */ }
    document.documentElement.dataset.mglTheme = theme;
    document.documentElement.style.colorScheme = theme;
})();
