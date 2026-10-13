// SENECARE technician app: proximity sort, offline copy of the day's missions, position sharing
// « en route », customer signature with the finger. Every part degrades gracefully without it.
(() => {
  "use strict";

  const toRad = (deg) => (deg * Math.PI) / 180;
  const distanceKm = (a, b) => {
    const dLat = toRad(b.lat - a.lat);
    const dLng = toRad(b.lng - a.lng);
    const h = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(a.lat)) * Math.cos(toRad(b.lat)) * Math.sin(dLng / 2) ** 2;
    return 6371 * 2 * Math.asin(Math.sqrt(h));
  };
  const here = (options) =>
    new Promise((resolve) => {
      if (!navigator.geolocation) return resolve(null);
      navigator.geolocation.getCurrentPosition(
        (p) => resolve({ lat: p.coords.latitude, lng: p.coords.longitude }),
        () => resolve(null),
        options,
      );
    });

  document.addEventListener("DOMContentLoaded", () => {
    // ---- Missions of the day: urgency first, then the closest (computed on the phone, works offline). ----
    const list = document.querySelector("[data-missions]");
    if (list) {
      here({ timeout: 8000, maximumAge: 300000 }).then((position) => {
        if (!position) return;
        const cards = [...list.querySelectorAll("[data-mission]")];
        for (const card of cards) {
          const lat = Number(card.dataset.lat);
          const lng = Number(card.dataset.lng);
          if (!card.dataset.lat || Number.isNaN(lat)) continue;
          card._km = distanceKm(position, { lat, lng });
          const label = card.querySelector("[data-distance]");
          if (label) {
            label.textContent = card._km.toLocaleString("fr-FR", { maximumFractionDigits: 1 }) + " km";
            label.hidden = false;
          }
        }
        cards
          .sort((a, b) =>
            Number(a.dataset.done) - Number(b.dataset.done)
            || Number(a.dataset.rank) - Number(b.dataset.rank)
            || (a._km ?? 1e9) - (b._km ?? 1e9))
          .forEach((card) => list.append(card));
      });

      // Keep every mission page of the day on the phone (the network may be gone on site).
      if (navigator.onLine && "caches" in window) {
        caches.open("sc-pages").then(async (cache) => {
          for (const link of list.querySelectorAll("a[data-prefetch]")) {
            try {
              const response = await fetch(link.href, { credentials: "same-origin" });
              if (response.ok && response.headers.get("X-SeneCare-Offline") === "page") await cache.put(link.href, response);
            } catch { /* offline again: keep what we have */ }
          }
        });
      }
    }

    // ---- « En route »: share the position with the customer (≈ every 20 s, or after 100 m). ----
    const share = document.querySelector("[data-share-position]");
    if (share && window.signalR && navigator.geolocation) {
      const status = share.querySelector("[data-share-status]");
      const connection = new signalR.HubConnectionBuilder().withUrl(share.dataset.hub).withAutomaticReconnect().build();
      let last = null;
      let lastAt = 0;
      const send = (position) => {
        const point = { lat: position.coords.latitude, lng: position.coords.longitude };
        const now = Date.now();
        if (last && now - lastAt < 20000 && distanceKm(last, point) < 0.1) return;
        if (connection.state !== "Connected") return;
        connection.invoke("Share", share.dataset.intervention, point.lat, point.lng).then(() => {
          last = point;
          lastAt = now;
          if (status) status.textContent = share.dataset.textShared;
        }).catch(() => {});
      };
      connection.start().catch(() => {});
      navigator.geolocation.watchPosition(send, () => { if (status) status.textContent = share.dataset.textDenied; }, {
        enableHighAccuracy: true,
        maximumAge: 15000,
        timeout: 30000,
      });
    }

    // ---- Customer signature drawn with the finger. ----
    document.querySelectorAll("[data-signature]").forEach((pad) => {
      const canvas = pad.querySelector("canvas");
      const input = pad.querySelector("input[type=file]");
      const form = pad.closest("form");
      const context = canvas.getContext("2d");
      let drawing = false;
      let drawn = false;

      const fit = () => {
        const ratio = window.devicePixelRatio || 1;
        canvas.width = canvas.clientWidth * ratio;
        canvas.height = canvas.clientHeight * ratio;
        context.scale(ratio, ratio);
        context.lineWidth = 2.5;
        context.lineCap = "round";
        context.strokeStyle = "#1f1b16";
      };
      fit();

      const point = (event) => {
        const box = canvas.getBoundingClientRect();
        return [event.clientX - box.left, event.clientY - box.top];
      };
      canvas.addEventListener("pointerdown", (event) => {
        drawing = true;
        canvas.setPointerCapture(event.pointerId);
        context.beginPath();
        context.moveTo(...point(event));
      });
      canvas.addEventListener("pointermove", (event) => {
        if (!drawing) return;
        context.lineTo(...point(event));
        context.stroke();
        drawn = true;
      });
      canvas.addEventListener("pointerup", () => (drawing = false));
      pad.querySelector("[data-signature-clear]")?.addEventListener("click", () => {
        context.clearRect(0, 0, canvas.width, canvas.height);
        drawn = false;
      });

      form.addEventListener("submit", (event) => {
        if (input.files.length > 0) return; // already converted: real submit
        event.preventDefault();
        if (!drawn) {
          window.scToast?.(pad.dataset.textEmpty, "erreur");
          return;
        }
        canvas.toBlob((blob) => {
          const transfer = new DataTransfer();
          transfer.items.add(new File([blob], "signature.png", { type: "image/png" }));
          input.files = transfer.files;
          form.requestSubmit();
        }, "image/png");
      });
    });
  });
})();
