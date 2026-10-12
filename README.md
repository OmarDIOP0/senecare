# SENECARE by SENE DESIGN

Maintenance préventive et entretien des bâtiments au Sénégal.
*Vous signalez. Nous intervenons. Votre patrimoine est préservé.*

Le contexte produit et technique complet est dans [CLAUDE.md](CLAUDE.md).

## Prérequis

- [.NET SDK 10](https://dotnet.microsoft.com/download) (version figée dans `global.json`)
- [Node.js](https://nodejs.org) 20+ (compilation Tailwind CSS)
- PostgreSQL 16+ installé localement (Docker optionnel : `docker compose up -d`)

## Structure

```
src/
  SeneCare.Domain          entités, règles métier (aucune dépendance)
  SeneCare.Application     cas d'usage, interfaces            → Domain
  SeneCare.Infrastructure  EF Core, SMS, paiement, fichiers…  → Application
  SeneCare.Web             ASP.NET Core MVC + HTMX + Tailwind → Application, Infrastructure
  SeneCare.Api             API REST /api/v1 (future app)      → Application, Infrastructure
tests/
  SeneCare.Domain.Tests, SeneCare.Application.Tests
  SeneCare.Web.IntegrationTests, SeneCare.Api.IntegrationTests
  SeneCare.Infrastructure.IntegrationTests (PostgreSQL réel)
docs/
  domain-model.md          modèle de domaine (diagrammes, règles métier)
```

## Démarrer

```bash
dotnet build SeneCare.slnx
```

Le build du projet Web lance `npm ci` (la première fois) puis compile Tailwind
(`src/SeneCare.Web/Styles/app.css` → `wwwroot/css/app.css`, minifié).
Pour compiler sans Node : `dotnet build -p:SkipTailwind=true`.

```bash
dotnet run --project src/SeneCare.Web
```

Site : http://localhost:5298 · santé : http://localhost:5298/health

```bash
dotnet run --project src/SeneCare.Api
```

API : http://localhost:5141/api/v1/status · OpenAPI (dev) : http://localhost:5141/openapi/v1.json

### Styles en direct pendant le développement

```bash
cd src/SeneCare.Web
npm run css:watch
```

### Design system

Guide vivant : http://localhost:5298/design (développement uniquement).

- Tokens (couleurs clair/sombre, typo, rayons) : `src/SeneCare.Web/Styles/app.css`.
- Composants : tag helpers `<sc-icon>`, `<sc-amount>`, `<sc-status>`, `<sc-urgency>`, `<sc-sheet>`
  et partials `Views/Shared/Ui/_*.cshtml` (carte d'intervention, timeline, uploader, sélecteur de quartier…).
- Icônes Lucide, polices et HTMX sont servis par le site (aucun CDN). Après avoir ajouté une icône
  dans `Styles/icons.json` :

```bash
cd src/SeneCare.Web
npm run assets:build
```

### Tests

```bash
dotnet test SeneCare.slnx
```

### Base de données (PostgreSQL)

Une seule fois, dans `psql` (ou pgAdmin) connecté en `postgres` :

```sql
CREATE ROLE senecare LOGIN PASSWORD 'votre-mot-de-passe' CREATEDB;
CREATE DATABASE senecare OWNER senecare;
```

Puis donner la connexion au site (stockée hors du dépôt) :

```bash
dotnet user-secrets --project src/SeneCare.Web set "ConnectionStrings:Default" "Host=localhost;Database=senecare;Username=senecare;Password=votre-mot-de-passe"
```

En développement, le site applique les migrations et charge les données de référence
(régions, départements, communes, quartiers, offres, paramètres) au démarrage
(`Database:MigrateOnStartup` / `Database:SeedOnStartup`). Santé de la base : `/health/ready`.

Migrations EF Core (outil local `dotnet-ef`, restauré par `dotnet tool restore`) :

```bash
dotnet ef migrations add NomDeLaMigration --project src/SeneCare.Infrastructure --startup-project src/SeneCare.Infrastructure --output-dir Persistence/Migrations
```

```bash
dotnet ef database update --project src/SeneCare.Infrastructure --startup-project src/SeneCare.Infrastructure --connection "Host=localhost;Database=senecare;Username=senecare;Password=..."
```

### Tests de base de données

Les tests `SeneCare.Infrastructure.IntegrationTests` créent une base temporaire, la migrent,
la remplissent puis la suppriment. Ils sont ignorés si `SENECARE_TEST_DB` n'est pas défini
(compte autorisé à créer des bases, par exemple le rôle `senecare` ci-dessus) :

```powershell
$env:SENECARE_TEST_DB = "Host=localhost;Database=postgres;Username=senecare;Password=..."
dotnet test SeneCare.slnx
```

Avec Docker (optionnel) : `cp .env.example .env` puis `docker compose up -d`.

## Configuration et secrets

Aucun secret dans le dépôt. `appsettings.Example.json` liste les clés attendues ;
en développement, utiliser les user-secrets :

```bash
dotnet user-secrets --project src/SeneCare.Web set "ConnectionStrings:Default" "Host=localhost;Database=senecare;Username=senecare;Password=..."
```

Clés obligatoires pour le site et l'API (même `Security:HashKey` pour les deux) :
`Security:HashKey` (codes OTP, invitations, jetons) et `Jwt:SigningKey` (API), chacune
32 octets aléatoires en base64, à ranger avec `dotnet user-secrets set`.

En production, utiliser des variables d'environnement (`ConnectionStrings__Default`, `Security__HashKey`, …).

## Connexion (téléphone + code)

- Site : `/connexion` → code reçu → `/inscription` (nom, « Ma maison » / « Mon entreprise », consentement).
- En développement, les SMS et WhatsApp ne partent pas : le message et son code s'affichent
  dans les logs de la console (`[SMS → 77 …]`). Interdit en production.
- Entreprise : le gestionnaire invite un employé depuis `/entreprise/equipe` (lien valable 7 jours).
- API : `POST /api/v1/auth/otp`, `/auth/otp/verify` (jetons JWT + refresh), `/auth/refresh`,
  `/auth/logout`, `/auth/register`, `GET /api/v1/me`.

## Langues

Français par défaut, wolof prévu (`?culture=wo` ou cookie). Les textes de l'interface
sont dans `src/SeneCare.Web/Resources/Localization/SharedResource*.resx`.
Les clés absentes en wolof retombent sur le français.
