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
- Identité : connexion par NUMÉRO DE TÉLÉPHONE (+221, 9 chiffres, préfixes 77/78/76/75/71/70 définis dans `PhoneNumber.MobilePrefixes`,
  contrôlés aussi pendant la saisie) + code OTP
  par SMS, WhatsApp en option. Email facultatif. Mot de passe optionnel.
  En développement seulement : `Otp:ShowCodeOnPage` (actif dans appsettings.Development.json) affiche le
  code sous les 6 cases, `Otp:SkipVerification` saute l'étape ; autorisés en Development et sur le site de
  test (Staging), l'application refuse de démarrer avec l'un de ces réglages en Production.
- Paiement : Wave, Orange Money, Free Money (via un agrégateur type PayDunya, PayTech
  ou CinetPay derrière une interface IPaymentProvider), + paiement en espèces/virement
  saisi par l'admin. Reçus PDF. Relances avant échéance.
- Adresses : souvent imprécises. → Région / Département / Commune / Quartier
  (référentiel en base, seed avec Dakar, Thiès, Rufisque, Pikine, Guédiawaye,
  Mbour, Saint-Louis, Touba, Kaolack, Ziguinchor…) + point GPS sur carte.
  Le champ « point de repère » n'est plus demandé dans les formulaires (décision utilisateur), la colonne reste en base.
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
- Déploiement de test sur Render : `Dockerfile` (racine) + Blueprint `render.yaml` (web Docker + Postgres, offre
  gratuite, région Frankfurt, `ASPNETCORE_ENVIRONMENT=Staging` → bandeau « Version de test » et code affiché).
  Derrière le proxy : en-têtes X-Forwarded-* pris en compte, port lu dans `$PORT`, adresse `postgresql://…`
  acceptée (`PostgresConnectionString`), clés Data Protection en base (table `data_protection_keys`).
  Limites de l'offre gratuite : mise en veille, photos perdues au redémarrage, base expirant après 30 jours.

## 5. Direction visuelle — chaleureuse, locale, vivante (palette Atlantique & terre cuite)
Références de mise en page : wurus.app (typographies, panneaux sombres, bandes de motifs, inscription en
étapes), homeserve.fr et vbs.services (structure d'un vrai site de services). On s'inspire, on ne copie ni
logo, ni textes, ni marque, ni couleurs : la palette est propre à SeneCare (choisie par l'utilisateur).
Objectif : séduire visuellement, jamais l'air « généré par une IA ».

Tokens (Styles/app.css, noms conservés, aucune couleur en dur dans les vues) :
- Pages sable clair `--sable` #F6F3EC, surfaces `--surface` #FFFFFF, blocs `--sourdine` #EEEAE0, bordures #E2DDD0.
- Texte bleu encre `charbon` #13222B, secondaire `charbon-doux` #5A6870 (AA).
- Terre cuite : bouton principal (dégradé `--bouton-haut` #D9622B → `--bouton-bas` #B5461A, texte blanc),
  liens, icônes, points forts (`--laterite` #B8481A).
- Soleil (`--ambre` #F2B544, `--or` #F6CB6E) : étoiles, coches, chiffres sur fond sombre, choix sélectionné
  (`--ocre-clair` #FCF0D4). Sur-titres `.sc-eyebrow` en bleu (`--ocre-texte` #1D5B75).
  Statuts : `baobab` (vert), `soleil`, `rouge`, `atlantique` (info).
- Panneaux bleu Atlantique `.sc-panneau` (`--nuit` #0F3446, grille fine claire) pour les temps forts : bandeau de
  contact, en-tête et pied de page publics, étapes, chiffres, appel final, carte de formule, panneau
  d'inscription. Bandes de motifs `.sc-bande` / `.sc-bande-fine` (`img/motifs/bande.svg`, terre cuite, soleil,
  bleu, sable) sous l'en-tête, au-dessus du pied de page, en bas des panneaux.
- Mode Clair par défaut ; sélecteur Clair / Sombre / Système (`Site/_ThemeSwitch`, localStorage `sc-theme`,
  appliqué dans `Ui/_Head` avant le premier rendu). Sombre = bleu nuit #0A1F2A.
- Typo auto-hébergée : Unbounded 700 (h1, titres de section `.sc-section-titre`, logo, grands chiffres),
  Bricolage Grotesque 400/600/700 (texte), Space Mono 700 (petites capitales `.sc-eyebrow`, `.sc-label-mono`,
  en-têtes de tableau). Drapeau du Sénégal en SVG (`Site/_FlagSn`), jamais d'emoji.
- Icônes Phosphor « duotone » (sprite `img/icons.svg`, noms via `Styles/icon-map.json`) ; la couche claire
  prend `--icon-fond` (ex. `[--icon-fond:var(--ambre)]`).
- Formes : boutons et badges en pilule, champs 12px, cartes 20px, panneaux 24–28px, ombres chaudes légères.
  Curseur « main » sur tout ce qui est cliquable.

Site public (`_Layout`, `PublicSiteController`, `Views/PublicSite`, contenus `Content/site-content.json`) :
- Bandeau de contact, en-tête bleu Atlantique (menu des métiers, Espace client, « Demander une intervention »),
  pied de page dense, barre Appeler / WhatsApp sur mobile.
- Accueil : hero + formulaire rapide (`/demander`), métiers avec photo, étapes, techniciens, exemple
  d'intervention, avis, chiffres, conseils, FAQ, appel final. Pages métiers, particuliers, entreprises,
  quartiers, tarifs (mensuel / annuel), FAQ, mentions, CGU.
- Blocs `"sample": true` = contenus d'exemple, visibles en développement (étiquette « Exemple »), masqués en
  production. Montants et délais : toujours depuis la base. Jamais de faux avis publiés.

Parcours client :
- « Créer mon compte » en 5 étapes, cadre commun `Shared/Signup/_Shell` : 1 profil (Maison / Entreprise)
  puis 2 numéro (`Registration/Start` → `/connexion` → code), toujours dans cet ordre ; puis
  (`Registration/Index`) 3 nom (+ entreprise) et logement / premier site (carte, quartier, photos),
  4 formule (mensuel / annuel « 2 mois offerts » pour les particuliers ; Essentiel / Professionnel /
  Premium, facturés au trimestre, pour les entreprises), 5 confirmation + consentements.
  Compte, logement et abonnement sont créés ensemble, puis `/abonnement` pour payer.
  D'autres logements s'ajoutent ensuite depuis l'espace.
- Paiement réel : lien marchand Wave configuré par l'admin (`/admin/paiements`), « J'ai payé » + référence,
  vérification par la Finance (notification), activation et message WhatsApp / SMS au client. Factures et
  reçus PDF. Pas de bouton de simulation dans l'interface.

Espace connecté : même palette ; sidebar + topbar desktop, barre d'onglets mobile ; « Abonnement » dans la
navigation des particuliers ; actions destructrices en rouge, en haut à droite (ex. « Annuler la demande »).
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
