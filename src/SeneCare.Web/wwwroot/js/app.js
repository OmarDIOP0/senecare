// SENECARE — small progressive enhancements. Every page works without this file.
// Event delegation only (works for HTMX-inserted content), no framework.
(() => {
  "use strict";

  // ---- Theme: follows the phone, unless the user chose (stored per device). ----
  const root = document.documentElement;
  try {
    const saved = localStorage.getItem("sc-theme");
    if (saved === "light" || saved === "dark") root.dataset.theme = saved;
  } catch { /* storage unavailable: keep the system theme */ }

  // Avatar menu / « Plus »: light, dark, or follow the phone.
  document.addEventListener("click", (event) => {
    const choice = event.target.closest("[data-theme-set]");
    if (!choice) return;
    const value = choice.dataset.themeSet;
    if (value === "system") delete root.dataset.theme;
    else root.dataset.theme = value;
    try {
      if (value === "system") localStorage.removeItem("sc-theme");
      else localStorage.setItem("sc-theme", value);
    } catch { /* ignore */ }
  });

  // ---- Phone: « 771234567 » → « 77 123 45 67 » while typing. ----
  const formatPhone = (value) => {
    let digits = value.replace(/\D/g, "");
    if (digits.startsWith("00221")) digits = digits.slice(5);
    else if (digits.length > 9 && digits.startsWith("221")) digits = digits.slice(3);
    digits = digits.slice(0, 9);
    return [digits.slice(0, 2), digits.slice(2, 5), digits.slice(5, 7), digits.slice(7, 9)].filter(Boolean).join(" ");
  };
  document.addEventListener("input", (event) => {
    const input = event.target.closest("input[data-phone]");
    if (!input) return;
    const formatted = formatPhone(input.value);
    if (formatted !== input.value) input.value = formatted;
  });

  // ---- Bottom sheets (native <dialog>). ----
  document.addEventListener("click", (event) => {
    const opener = event.target.closest("[data-sheet-open]");
    if (opener) {
      document.getElementById(opener.dataset.sheetOpen)?.showModal();
      return;
    }
    if (event.target.closest("[data-sheet-close]")) {
      event.target.closest("dialog")?.close();
      return;
    }
    // Tap on the backdrop closes.
    if (event.target instanceof HTMLDialogElement && event.target.classList.contains("sc-sheet")) {
      event.target.close();
    }
  });

  // ---- Neighborhood picker: choosing a result fills the hidden field. ----
  document.addEventListener("click", (event) => {
    const option = event.target.closest("[data-pick]");
    if (!option) return;
    const field = option.dataset.pick;
    const hidden = document.getElementById(field);
    const search = document.getElementById(field + "-recherche");
    const results = document.getElementById(field + "-resultats");
    if (hidden) hidden.value = option.dataset.pickId;
    if (search) {
      search.value = option.dataset.pickLabel;
      search.setAttribute("aria-expanded", "false");
      search.focus();
    }
    results?.replaceChildren();
  });
  document.addEventListener("htmx:afterSwap", (event) => {
    const results = event.detail.target;
    const search = document.querySelector(`[aria-controls="${results.id}"]`);
    search?.setAttribute("aria-expanded", results.children.length > 0 ? "true" : "false");
  });

  // ---- Toasts: scToast("Message", "succes" | "erreur"). Server toasts arrive as <template data-toast>. ----
  const toastRegion = () => document.getElementById("sc-toasts");
  window.scToast = (text, kind = "succes") => {
    const region = toastRegion();
    if (!region) return;
    const toast = document.createElement("div");
    toast.className =
      "pointer-events-auto flex max-w-md items-center gap-3 rounded-lg border px-3 py-2.5 text-sm font-medium shadow-sm transition-opacity duration-200 " +
      (kind === "erreur" ? "border-rouge/30 bg-rouge-clair text-rouge" : "border-ligne bg-surface text-charbon");
    toast.setAttribute("role", kind === "erreur" ? "alert" : "status");
    toast.textContent = text;
    region.append(toast);
    setTimeout(() => {
      toast.style.opacity = "0";
      setTimeout(() => toast.remove(), 250);
    }, 4500);
  };
  const flushServerToasts = () =>
    document.querySelectorAll("template[data-toast]").forEach((t) => {
      window.scToast(t.content.textContent.trim(), t.dataset.toast);
      t.remove();
    });
  document.addEventListener("DOMContentLoaded", flushServerToasts);
  document.body?.addEventListener("scToast", (event) => window.scToast(event.detail.value, event.detail.kind));

  // ---- Photo uploader: compress on the phone before sending (≈300 KB max per photo). ----
  const MAX_SIDE = 1600;

  const toBlob = (canvas, type, quality) => new Promise((resolve) => canvas.toBlob(resolve, type, quality));

  const compress = async (file, maxBytes) => {
    const bitmap = await createImageBitmap(file, { imageOrientation: "from-image" }).catch(() => null);
    if (!bitmap) return file; // not decodable here: let the server validate it
    let scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement("canvas");
    for (let attempt = 0; attempt < 6; attempt++) {
      canvas.width = Math.round(bitmap.width * scale);
      canvas.height = Math.round(bitmap.height * scale);
      canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
      for (const quality of [0.8, 0.65, 0.5]) {
        let blob = await toBlob(canvas, "image/webp", quality);
        if (!blob || blob.type !== "image/webp") blob = await toBlob(canvas, "image/jpeg", quality); // old Safari
        if (blob && blob.size <= maxBytes) {
          const ext = blob.type === "image/webp" ? "webp" : "jpg";
          return new File([blob], file.name.replace(/\.[^.]+$/, "") + "." + ext, { type: blob.type });
        }
      }
      scale *= 0.75;
    }
    return file;
  };

  const kilobytes = (bytes) => Math.round(bytes / 1024).toLocaleString("fr-FR") + " Ko";

  document.addEventListener("change", async (event) => {
    const input = event.target.closest("[data-uploader-input]");
    if (!input) return;
    const uploader = input.closest("[data-uploader]");
    const max = Number(uploader.dataset.max);
    const maxBytes = Number(uploader.dataset.maxBytes);
    const status = uploader.querySelector("[data-uploader-status]");
    const previews = uploader.querySelector("[data-previews]");
    const kept = uploader._files ?? [];

    status.textContent = uploader.dataset.workingText ?? "…";
    const incoming = [...input.files].slice(0, Math.max(0, max - kept.length));
    const compressed = await Promise.all(incoming.map((f) => compress(f, maxBytes)));
    uploader._files = [...kept, ...compressed];
    render(uploader, input, previews, status);
  });

  const render = (uploader, input, previews, status) => {
    const files = uploader._files;
    const transfer = new DataTransfer();
    files.forEach((f) => transfer.items.add(f));
    input.files = transfer.files;

    previews.replaceChildren(
      ...files.map((file, index) => {
        const item = document.createElement("li");
        item.className = "relative aspect-square overflow-hidden rounded-md border border-ligne bg-sable";
        const img = document.createElement("img");
        img.src = URL.createObjectURL(file);
        img.alt = "";
        img.className = "size-full object-cover";
        img.onload = () => URL.revokeObjectURL(img.src);
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "absolute top-1 right-1 grid size-9 place-items-center rounded-full bg-charbon/80 text-sable";
        remove.setAttribute("aria-label", (uploader.dataset.removeText ?? "Retirer") + " " + (index + 1));
        remove.textContent = "×";
        remove.onclick = () => {
          uploader._files.splice(index, 1);
          render(uploader, input, previews, status);
        };
        item.append(img, remove);
        return item;
      }),
    );

    const total = files.reduce((sum, f) => sum + f.size, 0);
    status.textContent = files.length === 0
      ? uploader.dataset.emptyText ?? ""
      : `${files.length} / ${uploader.dataset.max} · ${kilobytes(total)}`;
  };

  // ==========================================================================
  // PWA
  // ==========================================================================

  // ---- Service worker + « Nouvelle version disponible » banner. ----
  if ("serviceWorker" in navigator) {
    let reloading = false;
    navigator.serviceWorker.addEventListener("controllerchange", () => {
      if (reloading) return;
      reloading = true;
      location.reload();
    });

    const offerUpdate = (worker) => {
      const banner = document.querySelector("[data-update-banner]");
      if (!banner || !navigator.serviceWorker.controller) return; // first install: nothing to update
      banner.hidden = false;
      banner.querySelector("[data-update-apply]").onclick = () => worker.postMessage({ type: "SKIP_WAITING" });
    };

    window.addEventListener("load", async () => {
      const registration = await navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => null);
      if (!registration) return;
      if (registration.waiting) offerUpdate(registration.waiting);
      registration.addEventListener("updatefound", () => {
        const worker = registration.installing;
        worker?.addEventListener("statechange", () => {
          if (worker.state === "installed") offerUpdate(worker);
        });
      });
    });

    navigator.serviceWorker.addEventListener("message", (event) => {
      if (event.data?.type === "OUTBOX") showConnection(event.data.pending);
    });
  }

  // ---- Connection indicator: offline / waiting to send / back online. Hidden when all is well. ----
  const showConnection = async (pending) => {
    const pill = document.querySelector("[data-connection]");
    if (!pill) return;
    const waiting = pending ?? (await self.scOutbox?.count().catch(() => 0)) ?? 0;
    const set = (text, classes, icon) => {
      pill.className = "inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium " + classes;
      pill.textContent = text;
      if (icon) pill.prepend(icon);
      pill.hidden = false;
    };
    if (!navigator.onLine) {
      set(pill.dataset.textOffline, "bg-soleil-clair text-soleil");
    } else if (waiting > 0) {
      set(pill.dataset.textPending.replace("{0}", waiting), "bg-atlantique-clair text-atlantique");
    } else if (pill.dataset.wasOffline === "1") {
      set(pill.dataset.textOnline, "bg-baobab-clair text-baobab");
      setTimeout(() => (pill.hidden = true), 3000);
    } else {
      pill.hidden = true;
    }
    pill.dataset.wasOffline = navigator.onLine ? "" : "1";
  };

  const flushOutbox = async () => {
    if (!self.scOutbox || !navigator.onLine) return showConnection();
    const pending = await self.scOutbox.flush().catch(() => undefined);
    showConnection(pending);
  };

  window.addEventListener("online", flushOutbox);
  window.addEventListener("offline", () => showConnection());
  document.addEventListener("DOMContentLoaded", () => {
    flushOutbox(); // iPhone has no Background Sync: send what is waiting at every page load
    if (new URLSearchParams(location.search).has("en-attente")) {
      document.querySelector("[data-offline-pending]")?.removeAttribute("hidden");
    }
  });

  // ---- Offline forms (data-offline="queue"): saved on the phone, sent when the network is back. ----
  document.addEventListener("submit", async (event) => {
    const form = event.target.closest("form[data-offline=queue]");
    if (!form || navigator.onLine || !self.scOutbox) return; // online: normal submit (the worker catches failures)
    event.preventDefault();
    const fields = self.scOutbox.fromFormData(new FormData(form, event.submitter));
    await self.scOutbox.add(new URL(form.action, location.href).pathname, fields, form.dataset.offlineLabel ?? "");
    const registration = await navigator.serviceWorker?.ready.catch(() => null);
    await registration?.sync?.register("sc-outbox").catch(() => {});
    window.scToast(form.dataset.offlineMessage ?? "", "succes");
    form.reset();
    showConnection();
  });

  // ---- Signing out: forget cached personal pages and unsent forms on this phone. ----
  document.addEventListener("submit", (event) => {
    if (event.target.closest("form[data-logout]")) {
      navigator.serviceWorker?.controller?.postMessage({ type: "SIGNED_OUT" });
    }
  });

  document.addEventListener("click", (event) => {
    if (event.target.closest("[data-reload]")) location.reload();
  });

  // ---- « Installer l'application »: Android prompt, or the illustrated guide on iPhone. ----
  const INSTALL_SNOOZE_MS = 14 * 24 * 3600 * 1000;
  const standalone = matchMedia("(display-mode: standalone)").matches || navigator.standalone === true;
  const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent) && !window.MSStream;
  let installEvent = null;

  const snoozed = () => {
    try { return Date.now() - Number(localStorage.getItem("sc-install-later") ?? 0) < INSTALL_SNOOZE_MS; } catch { return false; }
  };
  const showInstall = () => {
    if (standalone || snoozed()) return;
    document.querySelectorAll("[data-install]").forEach((el) => (el.hidden = false));
  };

  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    installEvent = event;
    showInstall();
  });
  document.addEventListener("DOMContentLoaded", () => {
    if (isIos) showInstall();
  });
  document.addEventListener("click", async (event) => {
    if (event.target.closest("[data-install-accept]")) {
      if (installEvent) {
        installEvent.prompt();
        await installEvent.userChoice.catch(() => null);
        installEvent = null;
        document.querySelectorAll("[data-install]").forEach((el) => (el.hidden = true));
      } else if (isIos) {
        document.getElementById("installer-iphone")?.showModal();
      }
    }
    if (event.target.closest("[data-install-dismiss]")) {
      try { localStorage.setItem("sc-install-later", String(Date.now())); } catch { /* ignore */ }
      document.querySelectorAll("[data-install]").forEach((el) => (el.hidden = true));
    }
  });
  window.addEventListener("appinstalled", () => document.querySelectorAll("[data-install]").forEach((el) => (el.hidden = true)));

  // ==========================================================================
  // Welcome screens (swipe) and guided tour (coach marks)
  // ==========================================================================

  document.addEventListener("DOMContentLoaded", () => {
    const form = document.querySelector("[data-onboarding]");
    if (!form) return;
    const track = form.querySelector("[data-onboarding-track]");
    const slides = [...track.children];
    const dots = [...form.querySelectorAll("[data-onboarding-dot]")];
    const next = form.querySelector("[data-onboarding-next]");
    let current = 0;

    const update = (index) => {
      current = index;
      dots.forEach((dot, i) => (i === index ? dot.setAttribute("aria-current", "step") : dot.removeAttribute("aria-current")));
      next.textContent = index === slides.length - 1 ? next.dataset.textStart : next.dataset.textNext;
    };
    const observer = new IntersectionObserver(
      (entries) => entries.forEach((e) => e.isIntersecting && update(slides.indexOf(e.target))),
      { root: track, threshold: 0.6 },
    );
    slides.forEach((s) => observer.observe(s));
    update(0);

    const goTo = (index) => slides[index]?.scrollIntoView({ behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth", inline: "center", block: "nearest" });
    next.addEventListener("click", (event) => {
      if (current < slides.length - 1) {
        event.preventDefault();
        goTo(current + 1);
      }
    });
    dots.forEach((dot, i) => dot.addEventListener("click", (event) => {
      event.preventDefault();
      goTo(i);
    }));
  });

  // Coach marks: a spotlight on each target (data-tour) with a short explanation.
  document.addEventListener("DOMContentLoaded", () => {
    const config = document.getElementById("sc-tour");
    if (!config) return;
    const tour = JSON.parse(config.textContent);
    const visible = (el) => el && el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden";
    const steps = tour.steps
      .map((step) => ({ ...step, element: [...document.querySelectorAll(`[data-tour="${step.target}"]`)].find(visible) }))
      .filter((step) => step.element);
    if (steps.length === 0) return;

    const token = document.querySelector("input[name=__RequestVerificationToken]")?.value ?? "";
    const finish = () => {
      overlay.remove();
      spotlight.remove();
      bubble.remove();
      document.removeEventListener("keydown", onKey);
      const body = new FormData();
      body.append("__RequestVerificationToken", token);
      fetch(tour.completeUrl, { method: "POST", body, credentials: "same-origin" }).catch(() => {});
    };
    const onKey = (event) => event.key === "Escape" && finish();

    const overlay = document.createElement("div");
    overlay.className = "fixed inset-0 z-[60]";
    const spotlight = document.createElement("div");
    spotlight.className = "pointer-events-none fixed z-[61] rounded-lg ring-2 ring-laterite transition-all duration-200";
    spotlight.style.boxShadow = "0 0 0 9999px rgb(21 18 14 / 0.6)";
    const bubble = document.createElement("div");
    bubble.className = "fixed inset-x-4 z-[62] mx-auto max-w-sm rounded-lg border border-ligne bg-surface p-4 text-sm text-charbon shadow-sm";
    bubble.setAttribute("role", "dialog");
    bubble.setAttribute("aria-modal", "true");
    bubble.setAttribute("aria-labelledby", "sc-tour-title");
    bubble.innerHTML = `
      <p class="sc-surtitre" data-progress></p>
      <h2 id="sc-tour-title" class="mt-1 font-display text-xl font-bold" tabindex="-1" data-title></h2>
      <p class="mt-1" data-text></p>
      <div class="mt-4 flex items-center justify-between gap-2">
        <button type="button" class="sc-btn-discret" data-skip></button>
        <button type="button" class="sc-btn bg-laterite text-sur-accent" data-next></button>
      </div>`;
    document.body.append(overlay, spotlight, bubble);

    let index = 0;
    const show = () => {
      const step = steps[index];
      step.element.scrollIntoView({ block: "center", behavior: "auto" });
      const rect = step.element.getBoundingClientRect();
      const pad = 6;
      Object.assign(spotlight.style, {
        top: `${rect.top - pad}px`, left: `${rect.left - pad}px`,
        width: `${rect.width + pad * 2}px`, height: `${rect.height + pad * 2}px`,
      });
      const below = rect.top < innerHeight / 2;
      bubble.style.top = below ? `${Math.min(rect.bottom + 16, innerHeight - 220)}px` : "";
      bubble.style.bottom = below ? "" : `${innerHeight - rect.top + 16}px`;
      bubble.querySelector("[data-progress]").textContent = tour.labels.progress.replace("{0}", index + 1).replace("{1}", steps.length);
      bubble.querySelector("[data-title]").textContent = step.title;
      bubble.querySelector("[data-text]").textContent = step.text;
      bubble.querySelector("[data-skip]").textContent = tour.labels.skip;
      bubble.querySelector("[data-next]").textContent = index === steps.length - 1 ? tour.labels.done : tour.labels.next;
      bubble.querySelector("[data-title]").focus();
    };
    bubble.querySelector("[data-next]").addEventListener("click", () => {
      index++;
      if (index >= steps.length) finish();
      else show();
    });
    bubble.querySelector("[data-skip]").addEventListener("click", finish);
    document.addEventListener("keydown", onKey);
    addEventListener("resize", () => steps[index] && show());
    show();
  });

  // ==========================================================================
  // App shell
  // ==========================================================================

  // ---- Menus (<details data-menu>): one open at a time, Esc / outside click closes, lazy content. ----
  const menus = () => document.querySelectorAll("details[data-menu][open]");
  document.addEventListener("click", (event) => {
    menus().forEach((menu) => { if (!menu.contains(event.target)) menu.open = false; });
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") menus().forEach((menu) => { menu.open = false; menu.querySelector("summary")?.focus(); });
  });
  document.addEventListener("toggle", async (event) => {
    const menu = event.target;
    if (!(menu instanceof HTMLDetailsElement) || !menu.open || !menu.dataset.load) return;
    const target = menu.querySelector("[data-load-target]");
    try {
      const response = await fetch(menu.dataset.load, { credentials: "same-origin", headers: { "HX-Request": "true" } });
      if (response.ok && target) target.innerHTML = await response.text();
      menu.querySelector("summary .bg-laterite")?.remove(); // read now
    } catch { /* offline: keep the placeholder */ }
  }, true);

  // ---- Ctrl+K: quick navigation. ----
  const palette = () => document.getElementById("palette");
  const openPalette = () => {
    const dialog = palette();
    if (!dialog || dialog.open) return;
    dialog.showModal();
    const input = dialog.querySelector("[data-palette-input]");
    input.value = "";
    filterPalette("");
    input.focus();
  };
  const filterPalette = (query) => {
    const dialog = palette();
    const words = query.trim().toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");
    let visible = 0;
    dialog.querySelectorAll("[data-palette-item]").forEach((item, index) => {
      const text = item.textContent.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");
      const show = !words || text.includes(words);
      item.parentElement.hidden = !show;
      item.classList.toggle("bg-sourdine", show && visible === 0);
      if (show) visible++;
    });
    dialog.querySelector("[data-palette-empty]").classList.toggle("hidden", visible > 0);
  };
  document.addEventListener("keydown", (event) => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
      event.preventDefault();
      openPalette();
    }
  });
  document.addEventListener("click", (event) => {
    if (event.target.closest("[data-palette-open]")) openPalette();
    if (event.target instanceof HTMLDialogElement && event.target.id === "palette") event.target.close();
  });
  document.addEventListener("input", (event) => {
    if (event.target.matches("[data-palette-input]")) filterPalette(event.target.value);
  });
  document.addEventListener("keydown", (event) => {
    if (!event.target.matches?.("[data-palette-input]")) return;
    const items = [...palette().querySelectorAll("[data-palette-item]")].filter((i) => !i.parentElement.hidden);
    const current = items.findIndex((i) => i.classList.contains("bg-sourdine"));
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const next = items[(current + (event.key === "ArrowDown" ? 1 : items.length - 1)) % items.length];
      items.forEach((i) => i.classList.toggle("bg-sourdine", i === next));
      next?.scrollIntoView({ block: "nearest" });
    } else if (event.key === "Enter" && items[current]) {
      event.preventDefault();
      location.href = items[current].href;
    }
  });

  // ---- Tables: click a header to sort, type to filter rows, select to filter a column. ----
  document.addEventListener("click", (event) => {
    const header = event.target.closest("th[data-sort]");
    if (!header) return;
    const table = header.closest("table");
    const body = table.tBodies[0];
    const column = [...header.parentElement.children].indexOf(header);
    const ascending = header.getAttribute("aria-sort") !== "ascending";
    table.querySelectorAll("th[aria-sort]").forEach((th) => th.removeAttribute("aria-sort"));
    header.setAttribute("aria-sort", ascending ? "ascending" : "descending");
    const value = (row) => {
      const cell = row.children[column];
      return cell?.dataset.value ?? cell?.textContent.trim() ?? "";
    };
    const collator = new Intl.Collator("fr", { numeric: true, sensitivity: "base" });
    [...body.rows]
      .sort((a, b) => (ascending ? 1 : -1) * collator.compare(value(a), value(b)))
      .forEach((row) => body.append(row));
  });
  const filterTable = (scope) => {
    const table = document.getElementById(scope.dataset.tableControls);
    if (!table) return;
    const search = scope.querySelector("[data-table-search]")?.value.trim().toLowerCase() ?? "";
    const filters = [...scope.querySelectorAll("[data-table-filter]")].filter((f) => f.value);
    let shown = 0;
    table.querySelectorAll("[data-row]").forEach((row) => {
      const match = (!search || row.textContent.toLowerCase().includes(search))
        && filters.every((f) => row.dataset[f.dataset.tableFilter] === f.value);
      row.hidden = !match;
      if (match) shown++;
    });
    document.querySelectorAll(`[data-table-empty="${table.id}"]`).forEach((el) => (el.hidden = shown > 0));
  };
  document.addEventListener("input", (event) => {
    const scope = event.target.closest("[data-table-controls]");
    if (scope) filterTable(scope);
  });
})();
