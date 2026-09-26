# Input og skærmtastatur

## Princippet: vælg frem for at skrive

Skærmtastaturet fylder omkring en tredjedel af skærmen og skjuler det, man arbejder med.
Derfor bruges tekstfelter kun, når brugeren skal skrive noget frit, der ikke kan vælges.

## Beslutningsguide

| Hvad skal angives? | Komponent | Tastatur |
|---|---|---|
| Ja/nej, til/fra | `HubSwitch` | Nej |
| Én af 2–5 korte muligheder | `HubChoice` (Segmented) | Nej |
| Én af 6–12 muligheder eller lange navne | `HubChoice` (Chips) | Nej |
| Mange eller skiftende muligheder (retter, varer) | Liste med seneste/favoritter øverst – søgefelt kun som sidste udvej | Kun ved søgning |
| Lille tal: portioner, antal, stk. | `NumberStepper` | Nej |
| Mængde i faste trin (0,5 kg) | `NumberStepper<decimal>` med `Step="0.5m"` | Nej |
| Klokkeslæt | `NumberStepper` med `Format` + evt. hurtige valg ("17:00", "18:00") | Nej |
| Dato | Hurtige valg ("I dag", "I morgen", "Weekend") + kalendervælger | Nej |
| Familiemedlem, farve | `HubChoice`, avatarer, `MemberColorPicker` | Nej |
| Fri tekst: navn, titel, note, ny vare | `HubTextField` (`Text`, `Name`, `Multiline`) | Ja – fuldt |
| Tal med stort spænd eller decimaler (pris, vægt) | `HubTextField` (`Number`, `Decimal`) | Ja – kompakt taltastatur |
| Telefon, e-mail, webadresse | `HubTextField` (`Phone`, `Email`, `Url`) | Ja – tilpasset |

## Sådan opfører skærmtastaturet sig

Indstilles pr. skærm under **Indstillinger → Denne skærm → Skærmtastatur**:

| Tilstand | Opførsel |
|---|---|
| **Automatisk** (standard) | Vises kun, når der trykkes i et tekstfelt **med fingeren** på en touchskærm. Mus, trackpad og fysisk tastatur får det aldrig frem – så en bærbar med touchskærm opfører sig normalt. |
| **Altid** | Vises ved ethvert tekstfelt. Til skærme, hvor touch ikke bliver genkendt. |
| **Fra** | Vises aldrig. |

Derudover, uden at nogen skal gøre noget:

- Telefoner og tablets bruger deres eget tastatur.
- Trykkes der på en fysisk tast, træder skærmtastaturet til side, indtil der igen bruges touch.
- **Minimér** gemmer tastaturet til en lille "Vis tastatur"-knap, mens feltet beholder fokus.
  Tryk i feltet igen, så kommer det frem. **Slå fra** slår det fra på skærmen (med "Fortryd").
- Tryk uden for feltet lukker tastaturet – efter at trykket er gået igennem, så knappen man ramte, virker.
- Feltet, der skrives i, holdes synligt over tastaturet. Dialoger lægger sig lige over tastaturet.
- Stort begyndelsesbogstav automatisk (sætninger eller hvert ord, efter feltets `Kind`).
  Dobbelttryk på ⇧ giver caps lock.
- Hold ⌫ for at slette hurtigt. ‹ › flytter markøren.
- Enter-tasten skifter tekst efter feltet: Færdig, Næste, Søg, Gå, Send – eller egen tekst som "Tilføj".
- Styresystemets eget skærmtastatur holdes væk, mens Family Hubs betjener feltet.

## Regler for udviklere

1. Brug altid `HubTextField` og sæt `Kind`. Den vælger layout, stort begyndelsesbogstav og Enter-tekst.
2. **Ingen `AutoFocus`** – undtagen i et forløb, brugeren selv har startet (fx første felt i en dialog åbnet med "Tilføj").
3. Formularer med flere felter: `EnterKey="EnterKey.Next"` på alle felter undtagen det sidste.
4. Flere i træk (indkøbsliste): `EnterLabel="Tilføj" KeepKeyboardOnEnter="true" OnEnter="AddAsync"` – og ryd feltet i `AddAsync`.
5. Auto-gem med `OnCommit` (Enter, "Færdig" eller når feltet forlades). Bekræft med en toast, da feltet ikke ændrer sig synligt.
6. Placér aldrig tekstfelter i faste paneler i bunden af skærmen – de ville havne bag tastaturet.
7. Sæt `MaxLength`. `Placeholder` er et eksempel ("Fx Mælk"), aldrig i stedet for `Label`.
8. Valideringsfejl vises under feltet (`Error` eller `EditForm`-validering) – aldrig som toast.
9. Et felt, der ikke må få Family Hubs tastatur: `data-osk="off"`.

## Feltets `Kind`

| Kind | Tastatur | Stort bogstav | Enter |
|---|---|---|---|
| `Text` | Bogstaver med talrække | Sætninger | Færdig |
| `Name` | Bogstaver med talrække | Hvert ord | Færdig |
| `Search` | Bogstaver med talrække | Nej | Søg |
| `Email` | Bogstaver + `@ . -` | Nej | Færdig |
| `Url` | Bogstaver + `/ - .dk` | Nej | Gå |
| `Phone` | Taltastatur med `+` | – | Færdig |
| `Number` | Taltastatur | – | Færdig |
| `Decimal` | Taltastatur med komma | – | Færdig |
| `Multiline` | Bogstaver med talrække | Sætninger | ↵ (ny linje) |

## Sådan virker det (teknik)

- `KeyboardPolicy` (Core) afgør ud fra enhedsprofil og indstilling, om tastaturet må vises, og om det kræver et fingertryk. Unit-testet.
- `DeviceService` finder skærmens egenskaber (touchpunkter, mus, mobil) via `device.js` og gemmer skærmens indstillinger i browserens localStorage.
- `OnScreenKeyboard.razor` (i layoutet) tegner tasterne og bestemmer, hvornår tastaturet er synligt.
- `keyboard.js` skriver direkte i feltet ved markøren og sender `input`/`change`-events, så Blazor-bindinger opdateres – tastetryk venter aldrig på serveren.
- Mens Family Hubs tastatur betjener et felt, sættes `inputmode="none"`, så styresystemets tastatur ikke dukker op samtidig.
- Layouts (dansk QWERTY med æøå, symboler, taltastaturer) er defineret i `KeyboardLayouts.cs`.
