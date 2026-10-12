// SENECARE outbox: forms saved while offline (IndexedDB), sent again when the network is back.
// Loaded by pages AND by the service worker (importScripts): no DOM access here.
(function (scope) {
  "use strict";

  const DB_NAME = "senecare";
  const STORE = "outbox";

  const open = () =>
    new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(STORE, { keyPath: "id", autoIncrement: true });
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });

  const run = async (mode, action) => {
    const db = await open();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction(STORE, mode);
        const result = action(tx.objectStore(STORE));
        tx.oncomplete = () => resolve(result.result);
        tx.onerror = () => reject(tx.error);
      });
    } finally {
      db.close();
    }
  };

  /** Saves a form submission. fields: [[name, string | Blob, fileName?], …] (Blobs are stored as-is). */
  const add = (url, fields, label) =>
    run("readwrite", (store) => store.add({ url, fields, label: label ?? "", createdAt: Date.now() }));

  const all = () => run("readonly", (store) => store.getAll());
  const count = () => run("readonly", (store) => store.count());
  const remove = (id) => run("readwrite", (store) => store.delete(id));
  const clear = () => run("readwrite", (store) => store.clear());

  /** Converts a FormData into storable entries (files keep their name). */
  const fromFormData = (formData) =>
    [...formData.entries()].map(([name, value]) =>
      value instanceof Blob ? [name, value, value.name ?? "photo"] : [name, String(value)]);

  /**
   * Sends saved submissions in order. Stops at the first network error (still offline);
   * removes an entry once the server answered (success, or a definitive 4xx refusal).
   * Returns the number of entries still waiting.
   */
  const flush = async () => {
    for (const entry of await all()) {
      const body = new FormData();
      for (const [name, value, fileName] of entry.fields) {
        if (value instanceof Blob) body.append(name, value, fileName);
        else body.append(name, value);
      }

      let response;
      try {
        response = await fetch(entry.url, { method: "POST", body, credentials: "same-origin", headers: { "X-SeneCare-Outbox": "1" } });
      } catch {
        break; // still offline
      }

      const retryLater = response.status >= 500 || response.status === 408 || response.status === 429;
      if (retryLater) break;
      await remove(entry.id);
    }
    return count();
  };

  scope.scOutbox = { add, all, count, remove, clear, flush, fromFormData };
})(self);
