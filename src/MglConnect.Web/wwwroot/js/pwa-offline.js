/**
 * M.G.L Connect - PWA Offline Utilities
 * Módulo de apoio para armazenamento IndexedDB e sincronização em segundo plano.
 * Fornece API simples para queue de GPS coordinates e assinaturas POD quando offline.
 */
export class MglOfflineStorage {
    static dbName = 'mgl-offline-db';
    static storeName = 'gpsQueue';

    // Fila local (IndexedDB) de dados para sincronização posterior
    static pendingSync = [];

    // Inicializar e abrir a conexão com IndexedDB
    static init(cb) {
        const request = indexedDB.open(this.dbName, 1);

        request.onupgradeneeded = (event) => {
            const db = event.target.result;
            if (!db.objectStoreNames.contains(this.storeName)) {
                db.createObjectStore(this.storeName, { keyPath: 'id', autoIncrement: true });
            }
            if (!db.objectStoreNames.contains('podQueue')) {
                db.createObjectStore('podQueue', { keyPath: 'id', autoIncrement: true });
            }
        };

        request.onsuccess = (event) => {
            const db = event.target.result;
            // Carregar fila pendente existente
            const tx = db.transaction(this.storeName, 'readonly');
            const store = tx.objectStore(this.storeName);
            const getAll = store.getAll();
            getAll.onsuccess = () => {
                this.pendingSync = getAll.result || [];
                cb && cb(this.pendingSync.length);
            };
        };

        request.onerror = (event) => {
            console.error('[PWA-Error] Falha ao abrir IndexedDB:', event.target.error);
            cb && cb(0);
        };
    }

    // Adicionar coordenada GPS na fila de sincronização offline
    static addGPS(lat, lng, speedKmH = 0, heading = 0, timestamp = null) {
        const id = Date.now(); // timestamp único
        const record = {
            id: id,
            type: 'gps',
            lat: lat,
            lng: lng,
            speedKmH: speedKmH,
            heading: heading,
            timestamp: timestamp || Date.now()
        };

        // Adicionar localmente para interface responsiva imediata
        this.pendingSync.push(record);

        // Persistir no IndexedDB
        const request = indexedDB.open(this.dbName, 1);
        request.onsuccess = (event) => {
            const db = event.target.result;
            const tx = db.transaction(this.storeName, 'readwrite');
            const store = tx.objectStore(this.storeName);
            store.add(record);
            tx.oncomplete = () => {
                // console.log('[PWA-OK] GPS adicionado ao IndexedDB:', record);
            };
        };
    }

    // Adicionar assinatura POD (Base64) na fila offline
    static addPODSignature(assinaturaBase64, nomeRecebedor, documentoRecebedor, localidade) {
        const id = Date.now();
        const record = {
            id: id,
            type: 'pod',
            assinaturaBase64: assinaturaBase64,
            nomeRecebedor: nomeRecebedor,
            documentoRecebedor: documentoRecebedor,
            localidade: localidade,
            timestamp: Date.now()
        };

        this.pendingSync.push(record);

        const request = indexedDB.open(this.dbName, 1);
        request.onsuccess = (event) => {
            const db = event.target.result;
            const tx = db.transaction('podQueue', 'readwrite');
            const store = tx.objectStore('podQueue');
            store.add(record);
        };
    }

    // Obter toda a fila pendente
    static getPendingSync() {
        return this.pendingSync;
    }

    // Limpar fila sincronizada (chamar após sucesso no backend)
    static clearSynced(ids = []) {
        if (ids.length === 0) {
            this.pendingSync = [];
        } else {
            this.pendingSync = this.pendingSync.filter(item => !ids.includes(item.id));
        }

        // Remover do IndexedDB também
        const request = indexedDB.open(this.dbName, 1);
        request.onsuccess = (event) => {
            const db = event.target.result;
            const tx = db.transaction(this.storeName, 'readwrite');
            const store = tx.objectStore(this.storeName);
            ids.forEach(id => store.delete(id));
        };
    }

    // Limpar toda a fila (útil ao fazer logout ou reinicializar)
    static clearAll() {
        this.pendingSync = [];
        const request = indexedDB.open(this.dbName, 1);
        request.onsuccess = (event) => {
            const db = event.target.result;
            const tx = db.transaction(this.storeName, 'readwrite');
            tx.objectStore(this.storeName).clear();
            tx.objectStore('podQueue').clear();
        };
    }
}

// Exportar para uso global (browser)
if (typeof window !== 'undefined') {
    window.MglOfflineStorage = MglOfflineStorage;
}