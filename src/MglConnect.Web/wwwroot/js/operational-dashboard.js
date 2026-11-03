(() => {
    const panel = document.getElementById('operationalPanel');
    const status = document.getElementById('operationalRefreshStatus');
    if (!panel || !status) return;
    let running = false;
    let stopped = false;
    async function refresh() {
        if (running || stopped || document.hidden || !navigator.onLine) return;
        running = true;
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const url = new URL(panel.dataset.refreshUrl, location.origin);
            for (const key of ['de', 'ate']) {
                const value = new URLSearchParams(location.search).get(key);
                if (value) url.searchParams.set(key, value);
            }
            const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
            if (response.redirected || response.status === 401 || response.status === 403) {
                stopped = true;
                panel.replaceChildren();
                status.textContent = 'Sua sessão perdeu acesso. Entre novamente para consultar a operação.';
                return;
            }
            if (!response.ok) throw new Error('refresh failed');
            // Keep keyboard focus stable while the user explores an action.
            // Authorization failures above still clear content immediately.
            if (panel.contains?.(document.activeElement)) {
                status.textContent = 'Atualização consultada. A exibição será renovada ao sair da seleção atual.';
                return;
            }
            // Same-origin Razor partial; user fields are HTML-encoded by Razor.
            const parsed = new DOMParser().parseFromString(await response.text(), 'text/html');
            if (panel.dataset.fragmentId) {
                const fragment = parsed.getElementById(panel.dataset.fragmentId);
                if (!fragment) {
                    stopped = true;
                    panel.replaceChildren();
                    status.textContent = 'O acesso à operação não pôde ser confirmado. Entre novamente.';
                    return;
                }
                panel.replaceChildren(fragment);
            } else panel.replaceChildren(...parsed.body.childNodes);
            status.textContent = 'Dados atualizados. A próxima consulta será automática.';
        } catch {
            status.textContent = 'Não foi possível atualizar. Os dados abaixo são da última consulta.';
        } finally {
            clearTimeout(timeout);
            running = false;
        }
    }
    setInterval(refresh, 20000);
    panel.addEventListener?.('mgl:operation-changed', refresh);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('online', refresh);
})();
