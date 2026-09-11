// Backs Services/BrowserPwaInstallService.cs. Captures the browser's `beforeinstallprompt` event
// (fired once the manifest + service worker + HTTPS requirements are met) so it can be replayed
// later from an explicit "Install app" button, instead of relying on visitors to notice their
// browser's own install affordance (often just a small, easy-to-miss address-bar icon).
window.wattsUpPwaInstall = (function () {
    let deferredPrompt = null;
    let dotNetRef = null;

    window.addEventListener('beforeinstallprompt', (event) => {
        // Without this the browser shows its own mini-infobar immediately; suppressing it is what
        // makes `prompt()` below able to replay it on demand instead.
        event.preventDefault();
        deferredPrompt = event;
        dotNetRef?.invokeMethodAsync('OnAvailabilityChanged', true);
    });

    // Fires on a successful install, whether triggered by our button or the browser's own UI.
    window.addEventListener('appinstalled', () => {
        deferredPrompt = null;
        dotNetRef?.invokeMethodAsync('OnAvailabilityChanged', false);
    });

    return {
        // Returns whether a prompt is already pending, in case beforeinstallprompt fired before
        // Blazor finished attaching the .NET callback.
        init(ref) {
            dotNetRef = ref;
            return deferredPrompt !== null;
        },
        async prompt() {
            if (!deferredPrompt) {
                return;
            }
            const capturedPrompt = deferredPrompt;
            // Consume it up front — the browser only lets a given deferredPrompt be shown once,
            // and clearing this first also stops a second click while the first is still awaiting
            // userChoice from re-showing it.
            deferredPrompt = null;
            capturedPrompt.prompt();
            await capturedPrompt.userChoice;
        },
    };
})();
