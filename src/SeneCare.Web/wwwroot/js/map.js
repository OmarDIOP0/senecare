// « Mes logements » map: tap to place the house, or use the phone's position. Leaflet + OpenStreetMap.
(() => {
  "use strict";

  const DAKAR = [14.7167, -17.4677];

  document.addEventListener("DOMContentLoaded", () => {
    const element = document.querySelector("[data-map]");
    if (!element || !window.L) return;

    const lat = document.getElementById(element.dataset.latInput);
    const lng = document.getElementById(element.dataset.lngInput);
    const start = lat.value && lng.value ? [Number(lat.value), Number(lng.value)] : null;

    const map = L.map(element, { zoomControl: true }).setView(start ?? DAKAR, start ? 17 : 12);
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: "© OpenStreetMap",
    }).addTo(map);

    let marker = start ? L.marker(start).addTo(map) : null;
    const place = (point, zoom) => {
      lat.value = point.lat.toFixed(6);
      lng.value = point.lng.toFixed(6);
      if (marker) marker.setLatLng(point);
      else marker = L.marker(point).addTo(map);
      if (zoom) map.setView(point, zoom);
    };

    map.on("click", (event) => place(event.latlng));

    // Inside the sign-up steps the map starts hidden: resize and recentre it once it is shown.
    document.addEventListener("sc:wizard-step", () => {
      if (element.offsetParent === null) return;
      map.invalidateSize();
      if (!marker) map.setView(DAKAR, 12);
    });

    const locate = document.querySelector("[data-map-locate]");
    locate?.addEventListener("click", () => {
      if (!navigator.geolocation) return;
      const label = locate.lastChild.textContent;
      locate.lastChild.textContent = locate.dataset.textLocating;
      navigator.geolocation.getCurrentPosition(
        (position) => {
          locate.lastChild.textContent = label;
          place({ lat: position.coords.latitude, lng: position.coords.longitude }, 18);
        },
        () => {
          locate.lastChild.textContent = label;
          window.scToast?.(locate.dataset.textDenied, "erreur");
        },
        { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 },
      );
    });
  });
})();
