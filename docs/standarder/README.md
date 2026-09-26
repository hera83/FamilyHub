# Family Hub – designstandarder

Standarderne sikrer, at alle menuer ser ens ud og er rolige og lette at bruge på køkkenskærmen.
De gælder for al ny brugerflade. Du kan se og afprøve alle byggeklodserne på skærmen under
**Indstillinger → Komponentoversigt**.

| Dokument | Indhold |
|---|---|
| [layout.md](layout.md) | Skærmens opbygning, sider, gitter, afstande, typografi, farver, touch-mål |
| [komponenter.md](komponenter.md) | Katalog over komponenter med eksempler |
| [feedback.md](feedback.md) | Toasts, informationsbokse, dialoger, tomme tilstande, fejl |
| [input-og-tastatur.md](input-og-tastatur.md) | Hvornår man vælger, tæller eller skriver – og hvordan skærmtastaturet opfører sig |
| [tekst-og-tone.md](tekst-og-tone.md) | Dansk sprog i brugerfladen |
| [nyt-modul.md](nyt-modul.md) | Trin for trin: sådan bygges en ny menu |

## De ti gyldne regler

1. **Touch først.** Alt, der kan trykkes på, er mindst 48 px (standard 56 px) med mindst 8 px luft.
   Intet må kun virke med mus (hover).
2. **Vælg frem for at skrive.** Skærmtastaturet fylder en tredjedel af skærmen. Brug steppere, valg og
   kontakter; tekstfelter kun til fri tekst.
3. **Tastaturet kommer aldrig af sig selv.** Ingen autofokus på tekstfelter – undtagen i et forløb,
   brugeren selv har startet (fx en dialog åbnet med "Tilføj").
4. **Én primær handling pr. visning.** Alle andre knapper er sekundære eller stille.
5. **Rolig feedback.** Toast for det, man lige har gjort. InfoBox for en tilstand, der varer. Dialog kun,
   når der skal tages stilling. Foretræk "Fortryd" frem for "Er du sikker?".
6. **Kun design-tokens.** Ingen hårdkodede farver, afstande eller skriftstørrelser – brug `var(--hub-…)`.
   Så virker nattilstand og visningsstørrelse af sig selv.
7. **Kun Family Hub-komponenter.** Ingen rå `<button>`, `<input>` eller hjemmelavede dialoger i moduler.
8. **Dansk, kort og venligt.** "Gemt" – ikke "Handlingen blev gennemført".
9. **Læsbart på afstand.** Brødtekst 18 px, vigtige tal store. Farve står aldrig alene – altid ikon og tekst med.
10. **En fejl vælter ikke skærmen.** `HubButton` og `Toasts.TryAsync` fanger fejl og viser en venlig besked.

## Tjekliste, før en ny menu er færdig

- [ ] Siden starter med `<HubPage Title="…">` og er delt op i `<HubSection>`.
- [ ] Ingen tekstfelter, hvor en stepper, et valg eller en kontakt kunne bruges.
- [ ] Tekstfelter har den rigtige `Kind` og en meningsfuld Enter-tast (`EnterKey` / `EnterLabel`).
- [ ] Tomme lister viser en `EmptyState` med én handling.
- [ ] Sletning sker med det samme og tilbyder `Toasts.Undoable(...)`.
- [ ] Asynkrone handlinger går gennem `HubButton` eller `Toasts.TryAsync`.
- [ ] Afprøvet i lyst og mørkt tema og med visningsstørrelse "Stor".
- [ ] Afprøvet med fingeren og skærmtastaturet (på Pi'en eller med `tools/ui-smoke`).
- [ ] Logik ligger i services med unit-tests; komponenter er tynde.
