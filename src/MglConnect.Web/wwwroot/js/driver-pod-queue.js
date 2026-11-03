/* POD queue. Only a matching authenticated driver may replay its own items. */
class MglIndexedPodStore {
    async open() {
        if (this.database) return this.database;
        this.database = await new Promise((resolve, reject) => {
            const request = indexedDB.open('mgl-driver-pod-v1', 1);
            request.onupgradeneeded = () => {
                const store = request.result.createObjectStore('pendingPod', { keyPath: 'id' });
                store.createIndex('owner', 'owner');
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error('Feche outras abas para abrir a fila local.'));
        });
        this.database.onversionchange = () => { this.database.close(); this.database = null; };
        return this.database;
    }
    async list(owner) {
        const db = await this.open();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('pendingPod', 'readonly');
            const request = tx.objectStore('pendingPod').index('owner').getAll(owner);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
            tx.onabort = () => reject(tx.error || new Error('Leitura local interrompida.'));
        });
    }
    async write(action) {
        const db = await this.open();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('pendingPod', 'readwrite');
            action(tx.objectStore('pendingPod'));
            // Request success alone does not mean the transaction was committed.
            tx.oncomplete = () => resolve();
            tx.onerror = tx.onabort = () => reject(tx.error || new Error('Não foi possível gravar no aparelho.'));
        });
    }
    put(item) { return this.write(store => store.put(item)); }
    remove(id) { return this.write(store => store.delete(id)); }
}

class MglDriverPodQueue {
    constructor(scope, options = {}) {
        if (!scope.userId || !scope.tenantId) throw new Error('Identidade da fila ausente.');
        this.scope = { userId: String(scope.userId), tenantId: String(scope.tenantId).toLowerCase() };
        this.owner = this.scope.userId + ':' + this.scope.tenantId;
        this.store = options.store || new MglIndexedPodStore();
        this.fetch = options.fetch || globalThis.fetch.bind(globalThis);
        this.uuid = options.uuid || (() => crypto.randomUUID());
        this.onChange = options.onChange || (() => {});
        this.inFlight = null;
    }
    async pending() { return this.store.list(this.owner); }
    async enqueue(deliveryId, input) {
        const id = Number(deliveryId);
        if (!Number.isSafeInteger(id) || id <= 0 || !input.nomeRecebedor?.trim()) throw new Error('Comprovante inválido.');
        const body = {
            nomeRecebedor: input.nomeRecebedor.trim(), documentoRecebedor: input.documentoRecebedor?.trim() || null,
            assinaturaBase64: input.assinaturaBase64 || null, localidade: input.localidade?.trim() || null
        };
        if (body.nomeRecebedor.length > 100 || body.documentoRecebedor?.length > 30 || body.assinaturaBase64?.length > 1000000)
            throw new Error('O comprovante excede os limites de tamanho.');
        const existing = (await this.pending()).find(item => item.deliveryId === id);
        if (existing) {
            if (JSON.stringify(existing.body) !== JSON.stringify(body)) throw new Error('Já há outro comprovante pendente para esta viagem. Preserve-o e procure a operação.');
            return existing;
        }
        const item = { id: this.uuid(), owner: this.owner, deliveryId: id, body, createdAt: new Date().toISOString() };
        await this.store.put(item);
        this.onChange({ state: 'pending', item });
        return item;
    }
    sync() {
        if (!this.inFlight) this.inFlight = this.syncOwned().finally(() => { this.inFlight = null; });
        return this.inFlight;
    }
    async request(url, options) {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 15000);
        try { return await this.fetch(url, { ...options, signal: controller.signal }); }
        finally { clearTimeout(timeout); }
    }
    async syncOwned() {
        // When both outboxes are present, give captured GPS a chance to commit
        // while the shift is still active. A GPS outage does not erase or block POD.
        if (typeof window !== 'undefined' && window.mglGpsOutbox?.queue) {
            try { await window.mglGpsOutbox.queue.drain(); } catch { /* POD remains independently durable. */ }
        }
        for (const item of await this.pending()) {
            let context;
            try {
                const response = await this.request('/Motorista/Offline/Contexto', { credentials: 'same-origin', cache: 'no-store' });
                if (!response.ok || response.redirected) {
                    this.onChange({ state: 'auth-required', item });
                    return;
                }
                context = await response.json();
                if (String(context.userId) !== this.scope.userId || String(context.tenantId).toLowerCase() !== this.scope.tenantId
                    || context.role !== 'Motorista' || !context.token) {
                    this.onChange({ state: 'account-mismatch', item });
                    return;
                }
                const result = await this.request(`/api/entregas/${item.deliveryId}/pod`, {
                    method: 'POST', credentials: 'same-origin', cache: 'no-store',
                    headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + context.token },
                    body: JSON.stringify(item.body)
                });
                if (!result.ok || result.redirected) {
                    this.onChange({ state: result.status === 409 ? 'conflict' : result.status === 401 || result.status === 403 ? 'auth-required' : 'pending', item });
                    return;
                }
                const receipt = await result.json();
                if (receipt.sucesso !== true || receipt.entregaId !== item.deliveryId || receipt.status !== 'Entregue') {
                    this.onChange({ state: 'pending', item });
                    return;
                }
                await this.store.remove(item.id);
                this.onChange({ state: 'confirmed', item, receipt });
            } catch {
                this.onChange({ state: 'pending', item });
                return;
            }
        }
    }
}

if (typeof window !== 'undefined') {
    window.MglDriverPodQueue = MglDriverPodQueue;
    window.MglIndexedPodStore = MglIndexedPodStore;
    document.addEventListener('DOMContentLoaded', () => {
        const shell = document.getElementById('driverOfflineQueueState');
        if (!shell) return;
        const form = document.getElementById('formPODNavegacao');
        const status = document.getElementById('podOfflineStatus');
        const submit = document.getElementById('btnSubmitPODNav');
        const retry = document.getElementById('retryOfflinePod');
        const deliveryId = Number(form?.dataset.deliveryId);
        let queue;
        const display = (message, state = 'pending') => {
            status.hidden = false;
            status.textContent = message;
            status.className = 'alert ' + (state === 'confirmed' ? 'alert-success' : state === 'conflict' ? 'alert-danger' : 'alert-warning');
        };
        const changed = event => {
            shell.hidden = false;
            shell.dataset.state = event.state;
            shell.classList.toggle('alert-success', event.state === 'confirmed');
            shell.classList.toggle('alert-danger', event.state === 'conflict');
            shell.classList.toggle('alert-warning', event.state !== 'confirmed' && event.state !== 'conflict');
            shell.textContent = event.state === 'confirmed' ? 'Comprovante confirmado pelo servidor.'
                : event.state === 'conflict' ? 'Há comprovante preservado neste aparelho com conflito. Procure a operação.'
                : event.state === 'auth-required' || event.state === 'account-mismatch' ? 'Há comprovante pendente. Entre com o mesmo motorista e organização para sincronizar.'
                : 'Há comprovante salvo neste aparelho, ainda sem confirmação do servidor.';
            if (!form || event.item.deliveryId !== deliveryId) return;
            // Show the preserved content after a reload, rather than an empty
            // editable form that could be mistaken for the queued document.
            for (const name of ['nomeRecebedor', 'documentoRecebedor', 'localidade']) {
                if (form.elements[name]) {
                    form.elements[name].value = event.item.body[name] || '';
                    form.elements[name].readOnly = true;
                }
            }
            const signature = event.item.body.assinaturaBase64;
            if (signature?.startsWith('data:image/png;base64,')) {
                let preview = document.getElementById('queuedPodSignature');
                if (!preview) {
                    preview = document.createElement('img');
                    preview.id = 'queuedPodSignature';
                    preview.alt = 'Assinatura preservada no comprovante pendente';
                    preview.className = 'cc-signature-preview mb-3';
                    status.after(preview);
                }
                preview.src = signature;
            }
            if (event.state === 'confirmed') {
                window.dispatchEvent(new CustomEvent('mgl:delivery-confirmed', { detail: { deliveryId: event.item.deliveryId } }));
                display('Entrega e comprovante confirmados pelo servidor.', 'confirmed');
                submit.disabled = true;
                submit.textContent = 'Entrega confirmada';
                retry.hidden = true;
                return;
            }
            submit.disabled = true;
            submit.textContent = 'Comprovante pendente';
            retry.hidden = false;
            if (event.state === 'conflict') display('O servidor informou conflito com a viagem ou comprovante. O conteúdo foi preservado neste aparelho; procure a operação.', 'conflict');
            else if (event.state === 'auth-required' || event.state === 'account-mismatch') display('Comprovante preservado. Entre novamente com o mesmo motorista e organização para sincronizar.');
            else display('Comprovante salvo neste aparelho. A entrega ainda não foi confirmada pelo servidor.');
        };
        try {
            queue = new MglDriverPodQueue({ userId: shell.dataset.offlineUser, tenantId: shell.dataset.offlineTenant }, { onChange: changed });
        } catch { shell.hidden = false; shell.textContent = 'Armazenamento local indisponível. Nenhum comprovante foi salvo.'; return; }
        form?.addEventListener('submit', async event => {
            event.preventDefault();
            submit.disabled = true;
            try {
                // Synchronous preparation avoids listener/microtask ordering
                // races between signature capture and the IndexedDB transaction.
                form.dispatchEvent(new Event('mgl:prepare-pod'));
                const fields = new FormData(form);
                await queue.enqueue(deliveryId, Object.fromEntries(fields));
                if (navigator.onLine) await queue.sync();
            } catch (error) {
                display('Não foi possível salvar o comprovante. ' + error.message, 'conflict');
                submit.disabled = false;
            }
        });
        const unavailable = () => { shell.hidden = false; shell.textContent = 'Não foi possível abrir a fila local. Nenhuma confirmação foi recebida.'; };
        const resume = () => queue.sync().catch(unavailable);
        retry?.addEventListener('click', resume);
        window.addEventListener('online', resume);
        setInterval(() => { if (!document.hidden && navigator.onLine) resume(); }, 30000);
        document.addEventListener('visibilitychange', () => { if (!document.hidden && navigator.onLine) resume(); });
        navigator.serviceWorker?.addEventListener('message', event => { if (event.data?.type === 'MGL_SYNC_REQUIRED') resume(); });
        queue.pending().then(items => {
            for (const item of items) changed({ state: 'pending', item });
            if (navigator.onLine) resume();
        }).catch(unavailable);
    });
}
