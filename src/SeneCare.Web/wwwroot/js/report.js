// « Signaler » form: 3 steps, local draft, max 3 preferred slots, optional voice note.
// Without this script the form still works (all steps visible, normal submit).
(() => {
  "use strict";

  document.addEventListener("DOMContentLoaded", () => {
    const form = document.querySelector("[data-report-wizard]");
    if (!form) return;

    // ---- Steps ----
    const steps = [...form.querySelectorAll("fieldset[data-step]")];
    const prev = form.querySelector("[data-step-prev]");
    const next = form.querySelector("[data-step-next]");
    const submit = form.querySelector("[data-step-submit]");
    const progress = form.querySelector("[data-step-progress]");
    let current = 0;

    const show = (index, scroll = true) => {
      current = index;
      steps.forEach((step, i) => (step.hidden = i !== index));
      prev.hidden = index === 0;
      next.hidden = index === steps.length - 1;
      submit.hidden = index !== steps.length - 1;
      progress.textContent = progress.dataset.text.replace("{0}", index + 1).replace("{1}", steps.length);
      steps[index].querySelector("legend")?.setAttribute("tabindex", "-1");
      if (!scroll) return;
      steps[index].querySelector("legend")?.focus({ preventScroll: true });
      form.scrollIntoView({ block: "start", behavior: "auto" });
    };

    const stepIsValid = (index) => {
      const fields = [...steps[index].querySelectorAll("[required]")];
      const invalid = fields.find((field) => !field.checkValidity());
      if (!invalid) return true;
      invalid.reportValidity?.();
      return false;
    };

    next.addEventListener("click", () => stepIsValid(current) && show(current + 1));
    prev.addEventListener("click", () => show(current - 1));
    // Choosing the trade is enough to move on.
    steps[0].addEventListener("change", () => show(1));
    show(0, false);

    // ---- At most 3 preferred slots ----
    form.querySelectorAll("[data-max-checked]").forEach((group) => {
      const max = Number(group.dataset.maxChecked);
      group.addEventListener("change", () => {
        const boxes = [...group.querySelectorAll("input[type=checkbox]")];
        const full = boxes.filter((b) => b.checked).length >= max;
        boxes.forEach((b) => (b.disabled = full && !b.checked));
      });
    });

    // ---- Local draft (text fields only; photos stay in the form) ----
    const draftKey = form.dataset.draftKey;
    const reference = form.querySelector("[data-client-reference]");
    const fields = () => [...form.elements].filter((el) => el.name && el.type !== "file" && el.type !== "hidden");

    const save = () => {
      const draft = {};
      for (const el of fields()) {
        if (el.type === "radio" || el.type === "checkbox") {
          if (el.checked) (draft[el.name] ??= []).push(el.value);
        } else {
          draft[el.name] = [el.value];
        }
      }
      try { localStorage.setItem(draftKey, JSON.stringify(draft)); } catch { /* storage full or blocked */ }
    };

    const restore = () => {
      let draft;
      try { draft = JSON.parse(localStorage.getItem(draftKey) ?? "null"); } catch { draft = null; }
      if (!draft || !draft.Description?.[0]) return;
      for (const el of fields()) {
        const values = draft[el.name];
        if (!values) continue;
        if (el.type === "radio" || el.type === "checkbox") el.checked = values.includes(el.value);
        else if (!el.value) el.value = values[0];
      }
      window.scToast?.(form.dataset.textDraft);
    };

    restore();
    form.addEventListener("input", save);
    form.addEventListener("change", save);

    form.addEventListener("submit", () => {
      try { localStorage.removeItem(draftKey); } catch { /* ignore */ }
      if (!navigator.onLine) {
        // Queued offline: the next report must get its own reference.
        setTimeout(() => {
          reference.value = crypto.randomUUID?.().replaceAll("-", "") ?? String(Date.now());
          show(0);
        }, 0);
      }
    });

    // ---- Voice note (optional): record up to 60 s with the phone's microphone ----
    const voice = form.querySelector("[data-voice]");
    if (!voice || !window.MediaRecorder || !navigator.mediaDevices?.getUserMedia) return;
    voice.hidden = false;
    const toggle = voice.querySelector("[data-voice-toggle]");
    const label = toggle.querySelector("span");
    const input = voice.querySelector("[data-voice-input]");
    const preview = voice.querySelector("[data-voice-preview]");
    let recorder = null;
    let stopTimer = 0;

    toggle.addEventListener("click", async () => {
      if (recorder?.state === "recording") {
        recorder.stop();
        return;
      }
      let stream;
      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      } catch {
        window.scToast?.(voice.dataset.textDenied, "erreur");
        return;
      }
      const chunks = [];
      recorder = new MediaRecorder(stream, { audioBitsPerSecond: 24000 });
      recorder.ondataavailable = (e) => e.data.size && chunks.push(e.data);
      recorder.onstop = () => {
        clearTimeout(stopTimer);
        stream.getTracks().forEach((t) => t.stop());
        const type = recorder.mimeType || "audio/webm";
        const file = new File(chunks, "note-vocale." + (type.includes("mp4") ? "m4a" : type.includes("ogg") ? "ogg" : "webm"), { type });
        const transfer = new DataTransfer();
        transfer.items.add(file);
        input.files = transfer.files;
        preview.src = URL.createObjectURL(file);
        preview.hidden = false;
        label.textContent = voice.dataset.textRecord;
        toggle.classList.remove("sc-btn-danger");
      };
      recorder.start();
      label.textContent = voice.dataset.textStop;
      toggle.classList.add("sc-btn-danger");
      stopTimer = setTimeout(() => recorder.state === "recording" && recorder.stop(), 60000);
    });
  });
})();
