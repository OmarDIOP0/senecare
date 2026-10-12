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

  document.addEventListener("click", (event) => {
    const toggle = event.target.closest("[data-theme-toggle]");
    if (!toggle) return;
    const dark = root.dataset.theme
      ? root.dataset.theme !== "dark"
      : !matchMedia("(prefers-color-scheme: dark)").matches;
    root.dataset.theme = dark ? "dark" : "light";
    try { localStorage.setItem("sc-theme", root.dataset.theme); } catch { /* ignore */ }
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
      "pointer-events-auto flex max-w-md items-center gap-3 rounded-md px-4 py-3 font-bold shadow-carte transition-opacity duration-200 " +
      (kind === "erreur" ? "bg-laterite text-sur-accent" : "bg-charbon text-sable");
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
})();
