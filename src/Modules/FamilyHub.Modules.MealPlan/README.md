# Modul: Madplan

**Status:** skelet – kun modul-registrering og en "på vej"-side.

## Tanker til indhold (ikke besluttet)

- Ugeplan (mandag–søndag) med én ret pr. dag; retter vælges fra en liste over familiens retter.
- Antal personer pr. ret med `NumberStepper` – ikke tastatur.
- Indkøbsliste, der samles automatisk ud fra ugens retter, plus manuelle varer
  ("Tilføj flere i træk"-mønsteret med `KeepKeyboardOnEnter`).
- Afkrydsning i butikken fra telefonen (Family Hub kan åbnes på hjemmenettet).
- Widget på forsiden: "I aften" og antal varer på listen.
- Data i SQLite via EF Core (én `DbContext` for modulet) i `IAppDataPaths.GetDirectory("madplan")`.

Byg modulet efter [docs/standarder/nyt-modul.md](../../../docs/standarder/nyt-modul.md).
