# SENECARE by SENE DESIGN — Contexte maître

Tu es lead developer senior .NET et product designer sur SENECARE by SENE DESIGN.
Le cahier des charges détaillé est dans `docs/cahier-des-charges.md`.

## 1. Le produit
SENECARE est un programme de maintenance préventive et d'entretien des bâtiments au Sénégal,
pour particuliers (maisons, villas), entreprises et quartiers.
Promesse : « Vous signalez. Nous intervenons. Votre patrimoine est préservé. »
Signature : « Du plan à la réalité. »
Parcours cœur : ANOMALIE → SIGNALEMENT (app, photo) → ANALYSE → RENDEZ-VOUS → INTERVENTION → SUIVI.

Domaines d'intervention : Électricité, Plomberie, Étanchéité, Maçonnerie, Menuiserie,
Sécurité (caméras), Espaces extérieurs.

Offres (montants en FCFA, entiers, sans décimales) :
- Maisons & Villas : inscription 10 000, abonnement 8 500 / mois.
- Entreprises : inscription 75 000, puis
  ESSENTIEL 1 500 000 / mois, PROFESSIONNEL 2 500 000 / mois, PREMIUM 3 500 000 / mois.
  Diagnostic technique obligatoire tous les 3 mois AVANT l'encaissement de la nouvelle période.
- Programme Quartiers : à partir de 500 adhérents dans un même quartier, opération de
  reboisement / embellissement tous les 3 mois.
Les tarifs DOIVENT être paramétrables en base par l'admin (jamais codés en dur).

## 2. Les rôles
- CLIENT (particulier) : un ou plusieurs logements, signale, suit, paie, note.
- ENTREPRISE : compte organisation multi-sites, avec sous-rôles
  (Gestionnaire = admin du compte, Responsable de site, Employé déclarant).
- TECHNICIEN : reçoit les missions, itinéraire, check-list, photos avant/après,
  signature client, rapport. (Rôle indispensable au parcours.)
- ADMIN SENECARE : sous-rôles Super-admin, Dispatcher/Superviseur, Comptabilité, Support.
Autorisation par policies ASP.NET Core, jamais par if/else dispersés dans les vues.

## 3. Réalités sénégalaises (non négociables)
- Connexion : 3G/4G instable, forfaits data chers, Android d'entrée de gamme.
  → Page initiale < 200 Ko (hors images), JS minimal, images WebP compressées,
  photos compressées côté client avant upload (max ~300 Ko), upload repris si coupure.
- Hors-ligne : un signalement doit pouvoir être rédigé sans réseau et partir
  automatiquement au retour de la connexion (Background Sync + file IndexedDB).
- Identité : connexion par NUMÉRO DE TÉLÉPHONE (+221, formats 77/78/76/70/75) + code OTP
  par SMS, WhatsApp en option. Email facultatif. Mot de passe optionnel.
- Paiement : Wave, Orange Money, Free Money (via un agrégateur type PayDunya, PayTech
  ou CinetPay derrière une interface IPaymentProvider), + paiement en espèces/virement
  saisi par l'admin. Reçus PDF. Relances avant échéance.
- Adresses : souvent imprécises. → Région / Département / Commune / Quartier
  (référentiel en base, seed avec Dakar, Thiès, Rufisque, Pikine, Guédiawaye,
  Mbour, Saint-Louis, Touba, Kaolack, Ziguinchor…) + point GPS sur carte
  + champ « point de repère » (ex : « derrière la mosquée, portail vert »).
- Communication : WhatsApp est le canal n°1, puis SMS, puis push PWA, puis email.
  Boutons « Appeler » et « WhatsApp » visibles sur chaque intervention.
- Langues : Français par défaut, Wolof prévu dès le départ (fichiers de ressources,
  aucune chaîne en dur). Anglais plus tard.
- Fuseau Africa/Dakar (UTC+0), dates au format français (« lun. 12 oct. »),
  montants « 8 500 FCFA » (espace insécable comme séparateur).
- Urgences locales : coupure / court-circuit, fuite importante, infiltration en
  saison des pluies (juillet–octobre) → niveaux d'urgence et campagnes préventives
  avant l'hivernage.
- Données personnelles : conformité loi 2008-12 et CDP (consentement explicite,
  droit d'accès / suppression, journal des accès aux données sensibles).

## 4. Stack technique
- .NET 10 (LTS), ASP.NET Core, C# à jour, nullable activé, warnings as errors.
- Solution : `SeneCare.slnx` (format .slnx, à conserver).
- Clean Architecture :
  - `src/SeneCare.Domain` : entités, value objects, enums, événements, règles métier
  - `src/SeneCare.Application` : cas d'usage, CQRS léger (MediatR ou handlers maison),
    FluentValidation, DTO, interfaces
  - `src/SeneCare.Infrastructure` : EF Core + PostgreSQL, Identity, SMS/WhatsApp, paiement,
    stockage fichiers, jobs Hangfire, PDF QuestPDF
  - `src/SeneCare.Web` : **ASP.NET Core MVC** (Controllers + Views, partial views pour HTMX)
    + HTMX + Alpine.js + Tailwind CSS, PWA.
    Projet de départ : le projet MVC existant `SeneCare/` (créé par l'utilisateur), à déplacer
    vers `src/SeneCare.Web` en Phase 0, en retirant Bootstrap et jQuery.
    Utiliser des Areas MVC par espace (Client, Entreprise, Technicien, Admin).
  - `src/SeneCare.Api` : API REST versionnée /api/v1, JWT, OpenAPI
    → prépare la future app mobile native (Flutter/MAUI)
  - `tests/` : xUnit, tests unitaires Domain/Application, tests d'intégration
    avec Testcontainers PostgreSQL, Playwright pour les parcours clés
- Pourquoi MVC + HTMX et pas un SPA lourd : rendu serveur rapide sur réseau faible,
  peu de JS, SEO pour les pages publiques. L'API sert la future app native.
- Temps réel : SignalR (statut d'intervention, position technicien).
- Carte : Leaflet + OpenStreetMap (gratuit, léger).
- Logs : Serilog. Santé : HealthChecks. Configuration par environnement + secrets
  hors du code.
- Docker + docker-compose (web, api, postgres, redis éventuel).

## 5. Direction artistique — « Made in Sénégal », jamais « fait par une IA »
INTERDIT : dégradés violet/bleu génériques, emojis comme icônes, cartes blanches
toutes identiques avec ombre floue, police Inter partout, illustrations 3D génériques,
textes marketing creux (« Révolutionnez votre expérience »), drapeau partout.

À FAIRE :
- Palette inspirée du pays, avec tokens CSS nommés :
  --latérite (terre rouge/ocre, couleur d'accent principale),
  --atlantique (bleu profond, confiance / navigation),
  --baobab (vert profond, succès / environnement / programme quartiers),
  --sable (fond chaud, remplace le blanc pur),
  --indigo-teinture, --soleil (jaune pour alertes/urgence modérée),
  --charbon (texte). Mode sombre prévu.
- Motifs graphiques discrets inspirés du wax, du tissage manjak et du bogolan,
  en SVG légers, utilisés avec parcimonie (en-têtes, onboarding, états vides).
- Typographie avec caractère : une display expressive pour les titres
  + une sans-serif très lisible pour le corps (Google Fonts, sous-ensembles latins,
  font-display: swap).
- Iconographie cohérente en ligne (Lucide ou Phosphor), une seule famille.
- Ton chaleureux et local, sans folklore : micro-copies naturelles,
  salutations contextuelles (« Nanga def, Awa 👋 » est OK UNE fois sur l'accueil,
  sinon pas d'emoji), « Teranga » dans le service client, vocabulaire du quotidien
  (« hivernage », « Senelec », « robinet », « dalle »).
- Photos réelles de maisons/équipes sénégalaises quand disponibles ; sinon
  illustrations vectorielles plates avec personnages africains, cohérentes entre elles.
- Mobile-first absolu : navigation par barre d'onglets en bas, gros boutons
  (min 48px), action principale « Signaler » toujours accessible (FAB central).
- Micro-interactions sobres (transitions 150–250ms), skeletons au lieu de spinners,
  respect de prefers-reduced-motion.
- Accessibilité : contrastes AA, focus visible, libellés ARIA, tailles de texte relatives.

## 6. Règles de travail
- Une phase à la fois. Avant de coder : plan court + liste des fichiers.
- Après chaque phase : build OK, tests verts, migrations EF à jour, résumé de ce qui
  a été fait + ce qu'il reste + comment tester manuellement.
- Code et noms techniques en anglais, textes UI en français via ressources.
- Pas de dépendance ajoutée sans justification.
- Ne jamais stocker de secrets dans le dépôt. Fournir appsettings.Example.json.
- Toujours demander avant une décision structurante non prévue ici.
