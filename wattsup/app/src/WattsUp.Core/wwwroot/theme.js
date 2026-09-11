// Small interop helper for dark-mode detection (backlog item 8) and, for the WASM/public-demo
// host, plain localStorage persistence of the user's explicit choice (the Server/add-on host
// persists that choice server-side via ProtectedLocalStorage instead, and never calls
// getDarkMode/setDarkMode below).
window.themeInterop = {
    prefersDark: () => window.matchMedia('(prefers-color-scheme: dark)').matches,

    getDarkMode: () => {
        const stored = window.localStorage.getItem('wattsup.darkMode');
        return stored === null ? null : stored === 'true';
    },

    setDarkMode: (isDarkMode) => {
        window.localStorage.setItem('wattsup.darkMode', isDarkMode ? 'true' : 'false');
    },
};
