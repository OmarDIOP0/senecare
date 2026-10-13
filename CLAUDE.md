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

## 5. Direction visuelle — produit SaaS sobre (Linear, Stripe, Vercel, Notion, Mercury, Attio, Wave)
Le « Made in Sénégal » passe par le contenu (quartiers, points de repère, Wave / Orange Money,
+221 77 123 45 67, vocabulaire du quotidien), PAS par la décoration. Ne jamais faire « généré par une IA ».

INTERDIT dans l'application :
- Bannière « hero », emojis, motifs décoratifs (wax, manjak, bogolan, cercles) dans les écrans connectés
  (tolérés uniquement sur connexion et onboarding, très discrets), illustrations clichés dans les états vides.
- rounded-2xl/3xl, grosses ombres, dégradés, glassmorphism, cartes dans des cartes,
  icônes dans des ronds colorés partout, contenu centré dans une carte géante.
- KPI à 0 ou « — » : un compte vide montre une checklist de démarrage, pas des chiffres.
- Texte de bouton en police display grasse. Fond beige.

Tokens (Styles/app.css, noms conservés) :
- Fond page `--sable` #FAFAF9, surfaces `--surface` blanc, bordures `--ligne` #E7E5E4 (1px).
- Texte #1C1917 (`charbon`), #57534E (`charbon-doux`), #A8A29E (`charbon-pale`) ; survol `--sourdine` #F5F5F4.
- Accent `--laterite` #A84B22 UNIQUEMENT : bouton primaire, élément actif, focus. Jamais en grande surface.
- `--atlantique` : liens et information. Statuts en badges discrets teintés : vert `baobab`,
  ambre `soleil`, rouge `rouge`. Mode sombre neutre froid (#0C0A09 / #1C1917).
- Typo : Inter auto-hébergée (400/500/600) pour toute l'app ; la display (Bricolage) = logo seul
  (`font-logo`). Desktop corps 14px, petits textes 13px, titres de page 20–24px semibold ;
  mobile 15–16px. Chiffres en `tabular-nums`.
- Rayons 6px (contrôles, badges) / 8px (cartes, dialogues). Aucune ombre sur les cartes ;
  `shadow-sm` seulement pour menus et popovers. Boutons 36px desktop, 44px mobile (56px en mode terrain).
- Icônes Lucide 18px, trait 1.75 (`<sc-icon>`).

Coque :
- Desktop ≥ 1024px : sidebar 240px blanche (logo, organisation, bouton « Signaler » compact,
  navigation 36px, Aide / Paramètres / carte utilisateur en bas) + topbar 56px (fil d'Ariane,
  recherche Ctrl+K, notifications, menu avatar contenant le thème). Contenu aligné à gauche, max ~1200px.
- Mobile : barre d'onglets en bas (3 onglets + « Signaler » au centre + « Plus »), header compact.
- En-tête de page standard (`Ui/_PageHeader`) : titre + description grise + action principale à droite.
- Listes : tableaux (`.sc-table`, tri, recherche, filtres, menu « … ») sur desktop, cartes compactes sur mobile.
- État vide (`Ui/_EmptyState`) dans le flux : icône 20px dans un carré gris 40px, titre, une phrase, un bouton.
- « Installer l'application » : jamais sur desktop ; fine barre refermable sur mobile.

Rédaction : microcopie courte et concrète, verbes d'action (« Ajouter un site », « Inviter », « Payer »),
pas de phrases marketing ni de points d'exclamation dans l'app, accents corrects.
Accessibilité : contrastes AA, focus visible (anneau latérite), libellés ARIA, tailles relatives,
respect de prefers-reduced-motion.
Revue visuelle : `tests/SeneCare.E2E/design-review.mjs` capture chaque écran (1440 / 390, clair / sombre)
dans `docs/design-review/` ; se demander « est-ce que ça pourrait être une page de Linear, Stripe ou Vercel ? ».

## 6. Règles de travail
- Une phase à la fois. Avant de coder : plan court + liste des fichiers.
- Après chaque phase : build OK, tests verts, migrations EF à jour, résumé de ce qui
  a été fait + ce qu'il reste + comment tester manuellement.
- Code et noms techniques en anglais, textes UI en français via ressources.
- Pas de dépendance ajoutée sans justification.
- Ne jamais stocker de secrets dans le dépôt. Fournir appsettings.Example.json.
- Toujours demander avant une décision structurante non prévue ici.
