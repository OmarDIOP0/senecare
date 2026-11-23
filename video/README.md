# Vidéos tutoriels SENECARE

Stories verticales 1080×1920 (Remotion), voix off française, sous-titres incrustés + `.srt`.

    npm install
    npm run capture:ep -- 01   # vrais écrans de l'app (Playwright, base « senecare_video » jetable, données fictives)
    npm run render:ep -- 01    # voix (edge-tts fr-FR-DeniseNeural, ou ElevenLabs si ELEVENLABS_API_KEY), musique, .srt, MP4
    npm run studio             # aperçu interactif

- Épisodes : `episodes/0X.ts` (une phrase de voix par scène, mots en doré, taps / zooms en fraction de la scène).
  Ajouter le nouvel épisode dans `src/episodes.ts`.
- Composants : `PhoneFrame`, `TapIndicator`, `Subtitle`, `Badge`, `Intro`, `Outro`, `PhoneStage`, `Overlays`.
- Prérequis : SQL Server local, .NET 10, Node 23+, Python (edge-tts, numpy, Pillow), ffmpeg, Microsoft Edge.
- Sortie : `out/ep0X.mp4` (son normalisé à −14 LUFS) et `out/ep0X.srt`.
