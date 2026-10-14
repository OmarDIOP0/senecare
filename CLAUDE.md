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
    (sans Docker pour l'instant : tests d'intégration sur le PostgreSQL local via `SENECARE_TEST_DB`)
- Pourquoi MVC + HTMX et pas un SPA lourd : rendu serveur rapide sur réseau faible,
  peu de JS, SEO pour les pages publiques. L'API sert la future app native.
- Temps réel : SignalR (statut d'intervention, position technicien).
- Carte : Leaflet + OpenStreetMap (gratuit, léger).
- Logs : Serilog. Santé : HealthChecks. Configuration par environnement + secrets
  hors du code.
- Docker + docker-compose (web, api, postgres, redis éventuel).

## 5. Direction visuelle — un vrai service d'entretien et de dépannage, sobre et concret
Références validées : homeserve.fr et vbs.services (structure et ton d'un site de services qui existe
depuis des années). Objectif : jamais l'air « généré par une IA ».

Thème et tokens (Styles/app.css, noms conservés, aucune couleur en dur dans les vues) :
- Clair par défaut : fond blanc #FFFFFF, sections alternées gris très clair `--sourdine` #F5F6F8,
  bordures #E5E7EB, texte #1F2937 (jamais de noir pur), texte secondaire #4B5563.
- Une seule couleur de marque, l'orange SeneCare `--laterite` #C2410C (boutons principaux, liens actifs,
  icônes), utilisée avec parcimonie. Couleur secondaire bleu nuit `--atlantique` #1E3A5F (titres de section)
  et `--nuit` #15294A (bandeau de contact, pied de page). Statuts : `baobab`, `soleil`, `rouge`.
- Sélecteur de thème Clair / Sombre / Système dans l'en-tête (`Site/_ThemeSwitch`), mémorisé dans
  localStorage (`sc-theme`), appliqué par un script dans `Ui/_Head` avant le premier rendu (pas de flash).
  Sombre = anthracite neutre (#18191C / #222429).
- Typo : Inter auto-hébergée (400/600/700), seule famille, logo compris. h1 ≤ 44px desktop, gras 700.
- Formes : coins 6px (contrôles) / 8px (cartes), ombres légères (`shadow-xs`), bordures fines grises.
- INTERDIT : dégradés, glassmorphism, lueurs, grandes cartes très arrondies, traits colorés sous les titres,
  sur-titres « eyebrow », slogans en phrases courtes ponctuées, emojis, animations d'apparition.

Structure du site public (`_Layout`, `PublicSiteController`, `Views/PublicSite`) :
- Bandeau de contact (téléphone, WhatsApp, horaires, zones) ; en-tête blanc : logo, menu « Nos métiers »
  (sous-menu des 7 métiers), Particuliers, Entreprises, Quartiers, Tarifs, Espace client, thème,
  bouton « Demander une intervention » (`/demander` → connexion par code → `/signaler?domaine=`).
- Accueil : hero clair (titre descriptif + formulaire rapide métier/quartier/téléphone + photo de technicien),
  métiers avec photo, comment ça marche, techniciens, exemple d'intervention, avis, chiffres, conseils, FAQ.
- Pages propres : `/metiers`, `/metiers/{slug}`, `/particuliers`, `/entreprises`, `/quartiers`, `/tarifs`
  (tableaux comparatifs), `/faq`, `/mentions-legales`, `/cgu`, `/confidentialite`.
- Pied de page dense sur fond bleu nuit (métiers, quartiers, entreprise, aide, contact, mentions).
- Mobile d'abord : menu burger, barre fixe « Appeler » + « WhatsApp » en bas des pages publiques.

Contenus (`Content/site-content.json`, chargé par `SiteContent`) :
- Contact, métiers (texte, exemples, délai, prix de départ, photo), techniciens, exemple d'intervention,
  avis, chiffres, FAQ, avantages des formules par code d'offre, quartiers desservis.
- Les blocs `"sample": true` sont des CONTENUS D'EXEMPLE : affichés en développement avec l'étiquette
  « Exemple », masqués en production tant qu'ils ne sont pas remplacés. Jamais de faux avis, faux chiffres,
  certifications ou partenariats publiés.
- Montants des abonnements et délais d'intervention : toujours depuis la base (Admin › Offres).
- Photos : `wwwroot/images/` (WebP ≤ 150 Ko) ; un fichier absent affiche une tuile neutre (`Site/_Photo`).

Espace connecté : même thème et mêmes tokens ; sidebar + topbar desktop, barre d'onglets mobile,
`Ui/_PageHeader`, `.sc-table`, `Ui/_EmptyState`, KPI jamais à 0 ni « — ».
Formulaires : fond blanc, bordure grise 1px, label au-dessus, aide dessous, focus visible, erreurs en rouge.
Accessibilité : contrastes AA dans les deux thèmes, focus visible, `alt` sur les images, libellés ARIA.
Revue visuelle : `tests/SeneCare.E2E/design-review.mjs` (1440 / 390, clair / sombre) dans `docs/design-review/`.

## 6. Règles de travail
- Une phase à la fois. Avant de coder : plan court + liste des fichiers.
- Après chaque phase : build OK, tests verts, migrations EF à jour, résumé de ce qui
  a été fait + ce qu'il reste + comment tester manuellement.
- Code et noms techniques en anglais, textes UI en français via ressources.
- Pas de dépendance ajoutée sans justification.
- Ne jamais stocker de secrets dans le dépôt. Fournir appsettings.Example.json.
- Toujours demander avant une décision structurante non prévue ici.
