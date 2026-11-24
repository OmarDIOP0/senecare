// Orange Money payment page: asks our server (never Orange directly) every 5 s whether the payment was confirmed,
// then goes back to the subscription page. Stops 10 minutes after the QR code expires, or when the page is hidden.
(() => {
  "use strict";

  document.addEventListener("DOMContentLoaded", () => {
    const box = document.querySelector("[data-om-payment]");
    if (!box) return;
    const until = Number(box.dataset.until || 0);
    let timer = 0;

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
