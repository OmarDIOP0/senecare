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

## 5. Direction visuelle — chaleureuse et grand public (structure HomeServe, finition WURUS)
Références validées par l'utilisateur : homeserve.fr (structure du site vitrine : menu par univers,
bloc d'accueil, cartes métiers, étapes, offres, engagements, conseils) et wurus.app (palette crème /
brun / ocre, typographies, formes en pilule). On s'inspire, on ne copie ni logo, ni textes, ni marque.
Le « Made in Sénégal » passe par le contenu (quartiers, points de repère, Wave, +221 77 123 45 67,
hivernage, Senelec), pas par des motifs décoratifs.

Tokens (Styles/app.css, noms conservés) :
- Fond page `--sable` #F7F1E2 (crème), surfaces `--surface` #FFFDF7, blocs atténués `--sourdine` #F3EBD6,
  bordures `--ligne` #E9DDC9 (1px).
- Texte brun : `charbon` #160A04, `charbon-doux` #6B5C4A (texte secondaire, AA), `charbon-pale` #9A8A74
  (icônes et séparateurs seulement, jamais du texte), placeholder #7A6C5B.
- Action `--laterite` #A84B22 : bouton primaire, élément actif, focus, petit trait sous les titres publics.
- Touches `--ocre` #C7932D / `--ocre-clair` #F3E6CF / `--ocre-texte` #7D5A12 : offre mise en avant,
  sur-titres (`.sc-eyebrow`), tuiles d'icônes publiques. Texte sur fond ocre = brun (`text-sur-ocre`), jamais blanc.
- `--atlantique` : liens et information. Statuts en badges pilules teintés : `baobab`, `soleil`, `rouge`.
- AUCUNE section sombre en mode clair. Le brun expresso (#100804 / #20140D / #2A1A10) n'existe qu'en mode sombre.
- Typo auto-hébergée : **Unbounded 700** pour h1, `.sc-titre`, titres de sections publiques et logo
  (`font-display` / `font-logo`, tracking -0.02em) ; **Bricolage Grotesque 400/600/700** pour tout le reste
  (`font-medium` = 600). Corps 16px mobile, 15px desktop. Chiffres comparés en `tabular-nums`.
- Formes : boutons et badges en pilule (`rounded-full`), contrôles 12px, cartes 22px (`rounded-lg`),
  blocs de section publics 28–32px (`rounded-2xl/3xl`). Pas d'ombre sur les cartes ; `shadow-sm` chaude
  pour menus et popovers. Boutons 44px mobile, 40px desktop, 56px en mode terrain.
- Icônes Lucide 18px, trait 1.75 (`<sc-icon>`).

Site public (`_Layout`, `Home/Index`) :
- En-tête : logo, Particuliers · Entreprises · Quartiers · Nos métiers · Conseils (ancres de l'accueil),
  « Se connecter » + « Créer mon compte » ; menu repliable sur mobile. Pied de page à colonnes.
- Accueil : bloc d'accueil ocre clair avec un aperçu de suivi construit avec de vrais composants (pas de photo),
  3 promesses, cartes métiers, 4 étapes numérotées, offres lues en base (jamais de prix en dur),
  Programme Quartiers (seuil lu en base), engagements, conseils, dernier appel.
- Chiffres publics : uniquement réels (`PublicFigures.HasEnough`), sinon les engagements de service.
  Jamais de faux avis ni de faux chiffres ; pas d'avis clients publiés sans consentement explicite.

Espace connecté :
- Même palette et mêmes polices. Desktop ≥ 1024px : sidebar 240px (logo, organisation, « Signaler »,
  navigation, Aide / Paramètres / carte utilisateur) + topbar (fil d'Ariane, Ctrl+K, notifications, avatar).
  Mobile : barre d'onglets (3 onglets + « Signaler » au centre + « Plus »).
- En-tête de page `Ui/_PageHeader`, listes en `.sc-table` sur desktop et cartes sur mobile,
  état vide `Ui/_EmptyState`, KPI jamais à 0 ni « — » (checklist de démarrage sur un compte vide).
- Pas de motifs décoratifs ni d'emojis dans l'app (motifs tolérés sur connexion et onboarding, très discrets).

Rédaction : microcopie courte et concrète, verbes d'action, accents corrects, pas de point d'exclamation
dans l'app. Accessibilité : contrastes AA, focus visible (anneau latérite), libellés ARIA, prefers-reduced-motion.
Revue visuelle : `tests/SeneCare.E2E/design-review.mjs` capture chaque écran (1440 / 390, clair / sombre)
dans `docs/design-review/`.

## 6. Règles de travail
- Une phase à la fois. Avant de coder : plan court + liste des fichiers.
- Après chaque phase : build OK, tests verts, migrations EF à jour, résumé de ce qui
  a été fait + ce qu'il reste + comment tester manuellement.
- Code et noms techniques en anglais, textes UI en français via ressources.
- Pas de dépendance ajoutée sans justification.
- Ne jamais stocker de secrets dans le dépôt. Fournir appsettings.Example.json.
- Toujours demander avant une décision structurante non prévue ici.
