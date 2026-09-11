// Plain localStorage for the visitor's own secrets (Eloverblik refresh token, Carnot API key) —
// see Components/Pages/Secrets.razor. No querying/range needs, unlike the price/tariff history in
// indexedDbInterop.js, so IndexedDB would be overkill here; a single small JSON blob is all this is.
window.secretsInterop = {
    get: () => {
        const raw = window.localStorage.getItem('wattsup.secrets');
        return raw === null ? null : JSON.parse(raw);
    },
    save: (secrets) => {
        window.localStorage.setItem('wattsup.secrets', JSON.stringify(secrets));
    },
};
