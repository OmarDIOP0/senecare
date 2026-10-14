// Dispatch map: open requests coloured by urgency (Leaflet + OpenStreetMap). Data comes from the page.
(() => {
  "use strict";

  const colours = { Critical: "#b42318", High: "#d9622b", Normal: "#0f3446", Planned: "#1f7a45" };

  document.addEventListener("DOMContentLoaded", () => {
    const element = document.querySelector("[data-dispatch-map]");
    const data = document.getElementById("dispatch-points");
    if (!element || !data || !window.L) return;

    const points = JSON.parse(data.textContent);
    const map = L.map(element).setView([14.7167, -17.4677], 11); // Dakar
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { maxZoom: 19, attribution: "© OpenStreetMap" }).addTo(map);

    const bounds = [];
    for (const p of points) {
      const marker = L.circleMarker([p.lat, p.lng], {
        radius: p.urgency === "Critical" ? 10 : 8,
        color: colours[p.urgency] ?? "#0f3446",
        fillOpacity: 0.85,
      }).addTo(map);
      const link = document.createElement("a");
      link.href = p.url;
      link.textContent = p.label; // text, never HTML: labels come from customers
      marker.bindPopup(link);
      bounds.push([p.lat, p.lng]);
    }
    if (bounds.length > 0) map.fitBounds(bounds, { padding: [32, 32], maxZoom: 15 });
  });
})();
