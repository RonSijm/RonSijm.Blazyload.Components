function getShell() {
    const shell = document.querySelector('[data-testid="fluxor-shell"]');
    if (!shell) {
        throw new Error("The Fluxor application shell must be rendered before browser effects run.");
    }
    return shell;
}

export function applyTheme(isDarkMode) {
    getShell().dataset.theme = isDarkMode ? "dark" : "light";
}

export function initializeShell(isDarkMode) {
    applyTheme(isDarkMode);
    return "Browser module ready: first-render effect completed";
}
