// Customer side: the technician « en route » on a map, live (SignalR). The page reloads by itself
// when the technician changes the status (arrived, work started, finished).
(() => {
  "use strict";

  document.addEventListener("DOMContentLoaded", () => {
    const box = document.querySelector("[data-tracking]");
    if (!box || !window.signalR) return;

    const status = box.querySelector("[data-tracking-status]");
    const home = box.dataset.lat ? [Number(box.dataset.lat), Number(box.dataset.lng)] : null;
    let map = null;
    let marker = null;

    const show = (lat, lng) => {
      const element = box.querySelector("[data-tracking-map]");
      if (window.L && element) {
        if (!map) {
          element.hidden = false;
          map = L.map(element, { zoomControl: true, attributionControl: true });
          L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { maxZoom: 19, attribution: "© OpenStreetMap" }).addTo(map);
          if (home) L.circleMarker(home, { radius: 8, color: "#1c3d5a", fillOpacity: 0.9 }).addTo(map);
        }
        if (!marker) marker = L.marker([lat, lng]).addTo(map);
        else marker.setLatLng([lat, lng]);
        map.fitBounds(home ? [home, [lat, lng]] : [[lat, lng], [lat, lng]], { padding: [32, 32], maxZoom: 16 });
      }
      if (status) status.textContent = box.dataset.textLive;
    };

    const connection = new signalR.HubConnectionBuilder().withUrl(box.dataset.hub).withAutomaticReconnect().build();
    connection.on("position", (lat, lng) => show(lat, lng));
    connection.on("status", () => location.reload());
    connection
      .start()
      .then(() => connection.invoke("Follow", box.dataset.request))
      .then((last) => last && show(last.latitude, last.longitude))
      .catch(() => {});
  });
})();
