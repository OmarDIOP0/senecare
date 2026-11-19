# Rapport UX : test de SeneCare par « Mme Diallo »

Test réalisé le 19 novembre 2026 sur https://senecare.onrender.com (version de test), en ordinateur 1366 × 768 puis en téléphone 375 × 667. Les corrections ont été vérifiées en local (http://localhost:5199) : le site en ligne garde l'ancienne version tant que rien n'est poussé.
Comptes de test fictifs : « Aissatou Diallo », numéros 77 xxx xx xx inventés, mot de passe de test. Aucune vraie donnée personnelle.
Captures : [docs/rapport-ux/](docs/rapport-ux/) (`desktop-NN-…png`, `mobile-NN-…png`) ; journaux détaillés : `desktop-journal.txt`, `mobile-journal.txt`.

## 1. Résumé

**Note : 6/10 avant corrections, 7,5/10 après.**

1. L'inscription en 5 étapes est claire et tolère bien les erreurs : messages en français simple, numéro gardé après une erreur, double-clic et rechargement sans dégât, aucune erreur dans la console.
2. Le seul blocage grave : après une dizaine d'essais depuis le même réseau, le site affichait une page blanche « HTTP ERROR 429 ». Il est corrigé : un message explique quoi faire et la limite passe à 30.
3. Plusieurs petits pièges perdaient une débutante : « Passer » menait à la connexion, « Se déconnecter » était introuvable, la barre Appeler / WhatsApp cachait les boutons sur téléphone, le bouton final était grisé sans explication. Tous sont corrigés.
4. Il reste une décision importante pour la mise en ligne : le lien de paiement Wave n'est pas réglé sur le site de test. Mme Diallo ne peut donc pas payer, et sans abonnement actif elle ne peut pas signaler de panne.
5. Neuf corrections, un commit chacune, et 197 tests verts. Rien n'est poussé ni déployé.

## 2. Le parcours de Mme Diallo

Mme Diallo, 55 ans, habite Grand Yoff. Sa fille lui a parlé de SeneCare pour la fuite sous son évier.

**Sur l'ordinateur.** La page s'ouvre en un peu plus d'une seconde sur quatre écrans de bienvenue ([desktop-01](docs/rapport-ux/desktop-01-premiere-visite.png)). Elle les trouve jolis, mais elle veut aller vite et touche « Passer ». Surprise : elle arrive sur une page « Connexion » ([desktop-02](docs/rapport-ux/desktop-02-accueil.png)) alors qu'elle n'a pas de compte. Elle trouve heureusement « Créer mon compte » sur cette page ([desktop-03](docs/rapport-ux/desktop-03-page-connexion.png)).

L'étape 1, Maison ou Entreprise, est évidente ([desktop-04](docs/rapport-ux/desktop-04-inscription-etape1.png)). À l'étape 2, elle appuie sur « Suivant » sans rien écrire. Le site lui répond « Ce numéro n'est pas un mobile sénégalais valide », alors qu'elle n'a rien tapé ([desktop-06](docs/rapport-ux/desktop-06-etape2-vide-envoye.png)). Elle essaie « 1234 » comme mot de passe : le message « au moins 8 caractères » est clair ([desktop-07](docs/rapport-ux/desktop-07-mdp-trop-court.png)). Elle se trompe ensuite en recopiant son mot de passe, et le message est clair aussi ([desktop-08](docs/rapport-ux/desktop-08-mdp-differents.png)). Son numéro est resté rempli, c'est rassurant. Elle double-clique sur « Suivant » par habitude, sans dégât ([desktop-09](docs/rapport-ux/desktop-09-etape3.png)).

À l'étape 3, elle écrit « Gran yof » avec une faute. Le site lui propose d'ajouter ce quartier comme s'il n'existait pas ([desktop-11](docs/rapport-ux/desktop-11-quartier-faute.png)). Elle corrige et trouve « Grand Yoff ». Elle oublie de toucher la carte, et un message le lui dit ([desktop-12](docs/rapport-ux/desktop-12-sans-carte.png)). Mais ce message reste affiché quelques secondes sur l'étape suivante, où il n'a plus de sens ([desktop-13](docs/rapport-ux/desktop-13-etape4-formule.png)).

À l'étape 5, le bouton « Créer mon compte » est grisé ([desktop-14](docs/rapport-ux/desktop-14-etape5.png)). Elle clique dessus, rien ne se passe. Elle finit par voir la case de confidentialité à cocher. Le texte lui promet ensuite d'être « prévenue sur WhatsApp ou par SMS », or aucun service d'envoi n'existe encore.

Le compte est créé et elle arrive sur « Abonnement » ([desktop-15](docs/rapport-ux/desktop-15-apres-inscription.png)), mais elle ne trouve pas de bouton pour payer. Un texte lui dit d'écrire sur WhatsApp, car le paiement Wave n'est pas ouvert ([desktop-16](docs/rapport-ux/desktop-16-abonnement-bas.png)). Elle veut quand même signaler sa fuite : « Activez votre abonnement » ([desktop-17](docs/rapport-ux/desktop-17-signaler.png)). Ce blocage est voulu, puisque le paiement passe avant l'espace.

Pour partir, elle cherche « Se déconnecter » et ne le trouve pas : il est caché derrière ses initiales, en haut à droite ([desktop-33](docs/rapport-ux/desktop-33-menu-deconnexion.png)). Elle revient plus tard avec un champ vide, puis un mauvais mot de passe ([desktop-30](docs/rapport-ux/desktop-30-connexion-vide.png), [desktop-31](docs/rapport-ux/desktop-31-connexion-mauvais-mdp.png)). Les messages sont corrects, sauf pour le champ vide (« numéro invalide »).

**Sur le téléphone.** Le parcours est le même et la mise en page tient bien sur 375 px ([mobile-04](docs/rapport-ux/mobile-04-inscription-etape1.png) à [mobile-14](docs/rapport-ux/mobile-14-etape5.png)). Deux choses la gênent en plus. D'abord, la barre fixe Appeler / WhatsApp en bas de l'écran recouvre le bouton « Continuer » du formulaire, et il faut faire défiler pour l'atteindre. Ensuite, après plusieurs essais, le téléphone affiche une page blanche « HTTP ERROR 429 » sans aucune explication. Sur un réseau mobile, beaucoup de clients partagent la même adresse, donc ce blocage peut toucher des gens qui n'ont rien fait. Pour se déconnecter, il faut penser à ouvrir le menu « Plus ».

**Ce qui marche bien.** Aucune erreur JavaScript ni requête en échec. Les pages s'affichent en 0,5 à 2 s une fois le serveur réveillé. Toutes les images ont un texte alternatif, le focus clavier est visible, et les messages d'erreur sont courts et en français. Le retour arrière et le rechargement ne cassent rien pendant l'inscription.

## 3. Problèmes relevés

Gravité : 🔴 bloquant · 🟠 gênant, risque d'abandon · 🟡 inconfort.

| # | Page | Problème | Gravité | Capture | Statut |
|---|------|----------|---------|---------|--------|
| 1 | Toutes (connexion, inscription) | Après environ 10 essais depuis un même réseau : page blanche « HTTP ERROR 429 » pendant 10 minutes. | 🔴 | vu au 2ᵉ passage mobile (page blanche, pas de capture) | **Corrigé** (9b01438) : message clair sur la page d'origine, limite à 30 essais |
| 2 | /bienvenue | « Passer » et « Commencer » menaient à /connexion au lieu de l'accueil. | 🟠 | desktop-02 | **Corrigé** (a916f67) |
| 3 | Bandeau « Version de test » | Le bandeau parlait encore d'un code affiché à l'écran, qui n'existe plus avec le mot de passe. | 🟠 | desktop-03 | **Corrigé** (c03778d) |
| 4 | /connexion, /inscription étape 2 | Champ vide → « numéro pas valide » au lieu de « entrez votre numéro ». | 🟠 | desktop-06, desktop-30 | **Corrigé** (984ee15) |
| 5 | Retour après erreur (sécurité) | Le retour vers la page d'origine acceptait un chemin « //autre-site » (redirection ouverte). | 🟠 | — | **Corrigé** (939676e) |
| 6 | Espace connecté (ordinateur) | « Se déconnecter » seulement dans le menu des initiales : introuvable. | 🟠 | desktop-33 → desktop-42 | **Corrigé** (3b239f1) : bouton sous « Paramètres » |
| 7 | /connexion, /inscription (téléphone) | La barre fixe Appeler / WhatsApp cachait les boutons du formulaire. | 🟠 | mobile-05 → mobile-40 | **Corrigé** (00b7752) |
| 8 | Inscription étape 5, /abonnement | Promesse d'un message WhatsApp / SMS jamais envoyé : aucun service n'est branché. | 🟠 | desktop-14 | **Corrigé** (f7001bd) : « votre espace l'indique » |
| 9 | Inscription étape 5 | Bouton final grisé sans explication. | 🟡 | desktop-14 → desktop-41 | **Corrigé** (c3465f4) : « Cochez cette case… » |
| 10 | Inscription étapes 3 → 4 | Le message « Placez le lieu sur la carte » restait affiché à l'étape suivante. | 🟡 | desktop-13 | **Corrigé** (690e855) |
| 11 | Pages publiques, page hors ligne | Si la base ne répond pas, la mise en page plantait, y compris sur la page « hors ligne ». Trouvé par les tests. | 🟠 | — | **Corrigé** (1eba376) |
| 12 | /abonnement (site de test) | Lien Wave non réglé : impossible de payer, donc de signaler une panne. | 🟠 | desktop-16, desktop-17 | **À décider** (réglage admin, pas de code) |
| 13 | Recherche de quartier | Une faute (« Gran yof ») propose d'ajouter un quartier en double. | 🟡 | desktop-11 | **À décider** |
| 14 | En-tête public, 1366 px | « Créer mon compte » n'apparaît dans l'en-tête qu'en très grand écran ; ailleurs, il faut passer par « Espace client ». | 🟡 | desktop-02 | **À décider** |
| 15 | Après déconnexion | Le bouton « retour » du navigateur affiche « Confirmer le renvoi du formulaire ». | 🟡 | desktop-34 (vu au 2ᵉ passage) | Non corrigé |
| 16 | Plusieurs pages | 9 liens ou boutons de moins de 32 px de haut (petits liens du pied de page, bandeau de contact). | 🟡 | desktop-23 | Non corrigé |
| 17 | Première connexion | La visite guidée s'ouvre par-dessus l'espace ; on peut la passer. | 🟡 | desktop-32 | Non corrigé (voulu) |
| 18 | Menu « Plus » (téléphone) | « Se déconnecter » reste dans le menu « Plus », comme dans la plupart des applications mobiles. | 🟡 | mobile-21 | Non corrigé (acceptable) |

## 4. Fichiers modifiés

| Fichier | Pourquoi |
|---------|----------|
| `src/SeneCare.Web/Infrastructure/WebAuth.cs` | Limite d'essais portée à 30, message au lieu de la page 429, `SafeReferer` (retour seulement vers une page du site). |
| `src/SeneCare.Web/Infrastructure/AntiforgeryRecoveryFilter.cs` | Utilise `SafeReferer` pour le retour après une page expirée. |
| `src/SeneCare.Web/Infrastructure/SiteContact.cs` | Les pages publiques s'affichent même si les coordonnées ne peuvent pas être lues. |
| `src/SeneCare.Web/Views/Shared/Ui/_Toasts.cshtml` | Affiche le nouveau message `flash.warning` (« trop d'essais »). |
| `src/SeneCare.Web/Controllers/SpaceController.cs` | « Passer » des écrans de bienvenue → accueil. |
| `src/SeneCare.Application/Auth/PasswordAuth.cs`, `src/SeneCare.Web/Controllers/AuthController.cs` | Messages « Entrez votre numéro » / « Entrez votre mot de passe », placés sous le bon champ. |
| `src/SeneCare.Web/Views/Shared/Ui/_Sidebar.cshtml` | Bouton « Se déconnecter » toujours visible. |
| `src/SeneCare.Web/Views/Shared/Ui/_TabBar.cshtml` | La déconnexion mobile vide aussi les pages gardées hors ligne (`data-logout`). |
| `src/SeneCare.Web/Views/Shared/_Layout.cshtml` | Pas de barre Appeler / WhatsApp sur /connexion et /inscription. |
| `src/SeneCare.Web/Views/Shared/Signup/_Consent.cshtml`, `wwwroot/js/app.js`, `wwwroot/css/app.css` | Aide sous la case de consentement ; messages effacés à chaque changement d'étape. |
| `src/SeneCare.Web/Views/Subscription/Index.cshtml` | Icône horloge au lieu de WhatsApp pour « paiement en vérification ». |
| `src/SeneCare.Web/Resources/Localization/SharedResource.resx` | Nouveaux textes (trop d'essais, champs vides, aide consentement), bandeau de test, textes sans promesse WhatsApp / SMS. |
| `tests/SeneCare.Web.IntegrationTests/PublicHomeTests.cs`, `DesignSystemTests.cs` | Suivent les nouveaux comportements (« Passer » → accueil, message du quartier inconnu). |

Tests : 197 sur 197 (Domain 102, Application 1, Infrastructure 7, API 8, Web 79).

## 5. Recommandations non appliquées : décisions à prendre

1. **Lien de paiement Wave sur le site de test** (🟠). À régler dans Admin › Paiements. Sans lui, aucun client test ne peut aller au bout. À vous de choisir : un vrai lien marchand, ou un lien de test.
2. **Recherche de quartier tolérante aux fautes** (🟡). Proposer « Grand Yoff » quand on tape « Gran yof », avant de proposer l'ajout. Il faudrait l'extension PostgreSQL `pg_trgm` (une migration) ou une comparaison maison. C'est un changement de base, donc il attend votre accord.
3. **« Créer mon compte » dans l'en-tête à 1366 px** (🟡). Il faut décider quel bouton garder quand la place manque : « Demander une intervention » ou « Créer mon compte ». Cela touche la mise en page de l'en-tête.
4. **Les écrans de bienvenue sur l'accueil public** (🟡). Ils s'intercalent entre le visiteur et la page d'accueil. Faut-il les réserver à l'application installée, ou au premier passage dans l'espace ?
5. **Seuil de la limite d'essais** (à surveiller). Il est fixé à 30 essais par 10 minutes et par adresse, et se règle dans `RateLimits:Otp:PermitLimit`. Si des clients d'un même opérateur se bloquent encore, on pourra passer à une limite par numéro.
6. **Retour arrière après déconnexion** (🟡). Il faudrait rediriger la déconnexion (POST) vers une page en GET avec des en-têtes « no-store ». C'est un petit changement, mais il touche le cache hors ligne et demande des tests sur téléphone.
7. **Cibles tactiles de moins de 32 px** (🟡). Agrandir les liens du pied de page et du bandeau de contact (44 px conseillés). C'est un ajustement de style sur plusieurs pages.
8. **Texte d'invitation d'équipe** (🟡). « Il recevra un lien par WhatsApp ou SMS » promet aussi un envoi qui n'existe pas encore. Je ne l'ai pas changé, faute d'avoir testé ce parcours entreprise.
9. **Mise en ligne.** Rien n'est poussé. Pour que le site de test profite des corrections : `git push`, puis le déploiement automatique sur Render.
