# Third-party notices

NEO Player Desktop uses third-party components. Their respective licenses apply.

- FFmpeg: the Alpha portable artifact downloads the Gyan Windows release essentials build during CI. That build is distributed under GPLv3; see the FFmpeg/Gyan package for the complete notices and source references.
- Vosk API and the bundled small English (`vosk-model-small-en-us-0.15`) and Persian (`vosk-model-small-fa-0.42`) models are Apache-2.0 according to the Vosk model registry.
- NAudio, Microsoft.Data.Sqlite, TagLibSharp and other NuGet dependencies retain their own licenses.

The repository does not commit the downloaded FFmpeg or Vosk model binaries; CI fetches them when producing the portable Alpha artifact.
