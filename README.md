# SENECARE by SENE DESIGN

Maintenance préventive et entretien des bâtiments au Sénégal.
*Vous signalez. Nous intervenons. Votre patrimoine est préservé.*

Le contexte produit et technique complet est dans [CLAUDE.md](CLAUDE.md).

## Prérequis

- [.NET SDK 10](https://dotnet.microsoft.com/download) (version figée dans `global.json`)
- [Node.js](https://nodejs.org) 20+ (compilation Tailwind CSS)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (PostgreSQL, à partir de la Phase 1)

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

### Tests

```bash
dotnet test SeneCare.slnx
```

### Base de données (PostgreSQL)

```bash
cp .env.example .env   # puis changer POSTGRES_PASSWORD
docker compose up -d
```

## Configuration et secrets

Aucun secret dans le dépôt. `appsettings.Example.json` liste les clés attendues ;
en développement, utiliser les user-secrets :

```bash
dotnet user-secrets --project src/SeneCare.Web set "ConnectionStrings:Default" "Host=localhost;Database=senecare;Username=senecare;Password=..."
```

En production, utiliser des variables d'environnement (`ConnectionStrings__Default`, …).

## Langues

Français par défaut, wolof prévu (`?culture=wo` ou cookie). Les textes de l'interface
sont dans `src/SeneCare.Web/Resources/Localization/SharedResource*.resx`.
Les clés absentes en wolof retombent sur le français.
