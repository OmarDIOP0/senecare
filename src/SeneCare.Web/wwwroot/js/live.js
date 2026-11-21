// Signed-in pages: new notifications arrive at once (SignalR, /hubs/direct). The bell lights up, a short message
// shows, and the page concerned (the request, its list, the dashboard) reloads by itself — never while the person
// is typing in a form: then the message is enough.
(() => {
  "use strict";

  const script = document.currentScript;

  document.addEventListener("DOMContentLoaded", () => {
    if (!window.signalR || !script?.dataset.hub) return;

    // Something typed and not sent: no automatic reload.
    let dirty = false;
    document.addEventListener("input", (event) => {
      if (event.target.closest?.("form")) dirty = true;
    });
    const editing = () => {
      const active = document.activeElement;
      return dirty || document.querySelector("dialog[open]") !== null
        || (active && (active.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(active.tagName)));
    };

    // Dashboards and lists change with almost everything; a detail page only with its own news.
    const dashboards = ["/espace", "/admin", "/technicien", "/entreprise", "/interventions", "/admin/dispatch"];
    const concerns = (url) => {
      if (!url) return false;
      const here = location.pathname.replace(/\/$/, "") || "/";
      const path = url.split("?")[0];
      return path === here || path.startsWith(here + "/") || dashboards.includes(here);
    };

    let pending = false;
    let timer = 0;
    const reload = () => {
      if (document.hidden) { pending = true; return; }
      clearTimeout(timer);
      timer = setTimeout(() => { if (!editing()) location.reload(); }, 800); // several at once: one reload
    };
    document.addEventListener("visibilitychange", () => {
      if (!document.hidden && pending) { pending = false; reload(); }
    });

    const lightBell = () => {
      const summary = document.querySelector("details[data-load='/notifications'] summary");
      if (!summary || summary.querySelector(".bg-laterite")) return;
      const dot = document.createElement("span");
      dot.className = "absolute top-2 right-2 size-2 rounded-full bg-laterite";
      dot.setAttribute("aria-hidden", "true");
      summary.append(dot);
    };

    const connection = new window.signalR.HubConnectionBuilder()
      .withUrl(script.dataset.hub)
      .withAutomaticReconnect([0, 2000, 10000, 30000, 60000])
      .build();

    connection.on("notify", (news) => {
      lightBell();
      if (news?.title) window.scToast?.(news.title, "succes");
      if (concerns(news?.url)) reload();
    });

    // Mobile data comes and goes: try again later, quietly.
    const start = () => connection.start().catch(() => setTimeout(start, 30000));
    connection.onclose(() => setTimeout(start, 30000));
    start();
  });
})();
