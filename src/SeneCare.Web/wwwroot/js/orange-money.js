// Orange Money payment page: asks our server (never Orange directly) every 5 s whether the payment was confirmed,
// then goes back to the subscription page. Stops 10 minutes after the QR code expires, or when the page is hidden.
(() => {
  "use strict";

  document.addEventListener("DOMContentLoaded", () => {
    const box = document.querySelector("[data-om-payment]");
    if (!box) return;
    const until = Number(box.dataset.until || 0);
    let timer = 0;

    // Phones: open Max it at once (amount and merchant already filled in by Orange). Only the first time: coming
    // back from Max it, the page stays here and waits for the confirmation.
    const maxit = box.dataset.maxit;
    const phone = window.matchMedia("(pointer: coarse)").matches && window.matchMedia("(max-width: 767px)").matches;
    if (maxit && phone) {
      const key = "sc-om-opened-" + box.dataset.payment;
      let opened = false;
      try { opened = sessionStorage.getItem(key) === "1"; sessionStorage.setItem(key, "1"); } catch { opened = true; }
      if (!opened) location.href = maxit;
    }

    const check = async () => {
      if (document.hidden || (until && Date.now() > until)) return;
      try {
        const response = await fetch(box.dataset.stateUrl, { credentials: "same-origin", headers: { Accept: "application/json" } });
        if (response.ok) {
          const { state } = await response.json();
          if (state === "Paid") { location.href = box.dataset.paidUrl; return; }
          if (state === "Failed") { location.reload(); return; }
        }
      } catch { /* network down: try again later */ }
      timer = setTimeout(check, 5000);
    };

    document.addEventListener("visibilitychange", () => {
      if (!document.hidden) { clearTimeout(timer); check(); }
    });
    timer = setTimeout(check, 5000);
  });
})();
