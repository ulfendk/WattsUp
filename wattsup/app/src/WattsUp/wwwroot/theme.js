// Small interop helper for dark-mode detection (backlog item 8). Kept to the one thing JS can do
// that C# can't: read the browser's prefers-color-scheme media query on first load, before the
// user has made an explicit choice of their own (which is then persisted server-side via
// ProtectedLocalStorage, not through this file).
window.themeInterop = {
    prefersDark: () => window.matchMedia('(prefers-color-scheme: dark)').matches,
};
