// Reconnect handling for a kiosk: nobody is there to press "Retry", so we keep trying on our own.
// A reload only happens once /health answers – otherwise Chromium would show its own error page.

const RECOVERY_INTERVAL_MS = 5000;

const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);
document.getElementById("components-reconnect-button").addEventListener("click", retry);
document.getElementById("components-resume-button").addEventListener("click", resume);

let recoveryTimer = 0;

function handleReconnectStateChanged(event) {
    const state = event.detail.state;
    if (state === "show") {
        reconnectModal.showModal();
    } else if (state === "hide") {
        stopRecovery();
        reconnectModal.close();
    } else if (state === "failed") {
        startRecovery();
    } else if (state === "rejected") {
        reloadWhenServerIsUp();
    }
}

async function retry() {
    try {
        // true = reconnected; false = server is up but the old session is gone.
        const successful = await Blazor.reconnect();
        if (!successful) {
            const resumed = await Blazor.resumeCircuit();
            if (resumed) {
                reconnectModal.close();
            } else {
                await reloadWhenServerIsUp();
            }
        }
    } catch {
        // Server still unreachable – the recovery loop tries again.
        startRecovery();
    }
}

async function resume() {
    try {
        if (!(await Blazor.resumeCircuit())) {
            await reloadWhenServerIsUp();
        }
    } catch {
        reconnectModal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

function startRecovery() {
    if (recoveryTimer) return;
    recoveryTimer = setInterval(async () => {
        if (await serverIsUp()) {
            stopRecovery();
            await retry();
        }
    }, RECOVERY_INTERVAL_MS);
}

function stopRecovery() {
    clearInterval(recoveryTimer);
    recoveryTimer = 0;
}

async function reloadWhenServerIsUp() {
    while (!(await serverIsUp())) {
        await new Promise(resolve => setTimeout(resolve, RECOVERY_INTERVAL_MS));
    }
    location.reload();
}

async function serverIsUp() {
    try {
        const response = await fetch("health", { cache: "no-store" });
        return response.ok;
    } catch {
        return false;
    }
}

// If the connection dies for good (Blazor's error bar appears), recover the same way.
const errorUi = document.getElementById("blazor-error-ui");
if (errorUi) {
    new MutationObserver(() => {
        if (getComputedStyle(errorUi).display !== "none") {
            setTimeout(reloadWhenServerIsUp, RECOVERY_INTERVAL_MS);
        }
    }).observe(errorUi, { attributes: true, attributeFilter: ["style"] });
}
