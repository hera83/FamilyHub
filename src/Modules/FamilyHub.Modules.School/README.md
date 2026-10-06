# Modul: Skole

Børnenes skoleskemaer på køkkenskærmen – som papiret på køleskabet: mandag–fredag øverst, tiderne i venstre side,
pauser som et bånd hen over ugen. Ét faneblad pr. barn (avatar, navn, klasse og »fri 14:15« for i dag).
Visningen kan ikke redigeres; skemaerne sættes op under tandhjulet.

## Sider og ruter

| Rute | Indhold |
|---|---|
| `/skole` | Skemaet for det valgte barn. Dagens kolonne og timen lige nu er markeret. Weekend: næste uge vises. |
| `/skole/indstillinger` | Listen over skemaer og »Tilføj skema« (barn, klassetrin, klasse, tider – kun valg). |
| `/skole/indstillinger/{id}` | Ét skema: timer, tider, fag og klasse. Alt gemmes med det samme. |

Forsiden får kortet **Skole i dag** med, hvor hvert barn er i skoledagen (`ScheduleRules.StatusAt`): »Møder 08:15« før
første time, »Fri 14:15« i en time, »Pause til 10:15« mellem to timer, »Har fri« uden timer eller efter sidste time. Om
eftermiddagen/aftenen **Skole i morgen** (»08:00–14:15« eller »Har fri«). Kortet vises kun, når der er noget at sige – se
`ScheduleRules.WidgetDay`.

## Skoleformer

| | Trin | Navn | Rækker hedder | Foreslåede fag | Standardtider |
|---|---|---|---|---|---|
| Folkeskole | 0.–10. klasse + a–f | 6.a, 3. klasse | 1. time | efter klassetrin | 7 × 45 min |
| Gymnasium | 1.g–3.g + evt. a–f | 2.g, 2.b | 1. modul | gymnasiefag (religion/oldtidskundskab fra 2.g) | 4 × 90 min |
| Universitet | 1.–12. semester | 3. semester | 1. modul | ingen – kurserne tilføjes | 4 × 1¾ time |

Alt, der er forskelligt, står i `SchoolLevels`. Ældre filer uden skoleform er folkeskole. Skiftes skoleform på et skema,
ændres kun navnet (trinnet starter forfra) – timer, tider og fag bliver.

## Sådan sættes et skema op

1. **Tilføj skema**: vælg barnet (et familiemedlem), skoleform, trin og klassebogstav. Fagene foreslås ud fra skoleform og trin
   (`SubjectCatalog`), og tiderne kopieres fra en søskende med samme skoleform – ellers skoleformens standarddag (`PeriodPlanner.StandardDay`).
2. **Timer**: tryk på en time → vælg fag, evt. »Skifter hver anden uge« (ulige/lige uger) og en kort note.
   Eller vælg et fag under **Udfyld hurtigt** og tryk på timerne efter hinanden. **Ryd** tømmer timer.
3. **Tider**: steppere i 5-minutters trin. »Ryk de næste tider med« flytter rækkerne efter, så der ikke opstår huller
   eller overlap. »Tilføj pause« sætter en pause ind efter en valgt række.
4. **Fag**: navn (det eneste, der skrives) og farve. Fjernes et fag, tømmes dets timer – med »Fortryd«.

## Kalender-link (iCal) – valgfrit, gymnasium og universitet

Har skolen en kalender, man kan abonnere på, kan skemaet hentes derfra og holde sig opdateret af sig selv.
Under skemaets indstillinger: **Hent skemaet fra et link → Tilføj link**.

- **Moodle (fx AAU):** Kalender → Eksportér kalender → »Alle begivenheder« og »Seneste og næste 60 dage« → »Hent kalender-URL«.
  Linket ser ud som `https://…/calendar/export_execute.php?userid=…&authtoken=…&preset_what=all&preset_time=recentupcoming`.
  `webcal://` virker også (hentes som `https://`). Er der valgt »Denne uge«, »Næste uge« eller »Denne måned«
  (`weeknow`, `weeknext`, `monthnow`), henter Family Hub alligevel `recentupcoming`: en fast periode når aldrig næste uge,
  og AAU's Moodle giver ugen *før* ved »Denne uge« (set 6.10.2026: `weeknow` gav 28.9.–2.10.).
- Linket hentes med det samme: virker det ikke, står der hvorfor under feltet, og intet gemmes.
- **Skemaet er bundet til linket:** timer, tider og fag følger kalenderen og kan ikke rettes – tabellen er låst, og
  sektionerne »Tider« og »Fag« er skjult. Rækkerne laves ved hver hentning ud fra hele kalenderen (`PeriodPlanner.FromEvents`
  i `SchoolWeek.For`): de tidspunkter, timerne oftest ligger på – højst 8, med »Pause« i alle huller (kalenderen siger ikke, hvad et hul er, så det gættes ikke). Et tidspunkt
  skal gå igen (mindst en fjerdedel så ofte som det mest brugte), så et enkelt møde ikke bliver en række. Er kalenderen tom
  (fx i ferien), bruges skemaets egne rækker. Det faste skemas tider og fag gemmes urørt og kommer tilbage, når linket fjernes.
- **Visningen** viser ugens rigtige timer: hver time står i de rækker, den overlapper (en forelæsning 08:15–12:00 står i begge
  moduler). Har timen sin egen tid, står den under navnet (»10:15–12:00 · Auditorium 1«). Flere timer i samme række: »+1 mere«.
  Timer, der ikke rammer nogen række, står under tabellen i »Uden for skemaets tider«.
- **Detaljer om en time:** kun i skemaer med link er hver time på skolesiden et trykmål (`SchoolTimetable.OnLessonClick`),
  ligesom timerne under »Uden for skemaets tider«. `LessonDialog` viser tid, sted og kalenderens beskrivelse – læst af
  `LessonDetails.Sections`: Moodles blokke (»COURSE«, »TEACHER«) bliver »Kursus« og »Underviser(e)«, første blok »Om timen«.
  Links, linknumre (»[1]«) og e-mailadresser udelades – skærmen kan ikke åbne dem. Flere timer i én celle vises alle; en
  ændring står øverst. Beskrivelsen gemmes i cachen (højst 2000 tegn) og tæller ikke som en ændring.
- **Farver:** en time med samme navn som et fag får fagets farve; ellers en fast farve ud fra navnet før » - «, »(« eller »:«
  (`SchoolWeek.SubjectFor`) – »Programmering (PBL)« og »Programmering - forelæsning« får samme farve.
- **Udeladt:** heldagsbegivenheder, aflyste timer og alt under 10 minutter (Moodles afleveringsfrister har ingen længde).
- **Opdatering:** `ScheduleFeedWorker` kigger hvert 5. minut; hvert link hentes hver halve time (10 min efter en fejl).
  »Opdater nu« henter med det samme. Timerne gemmes i `skole/kalender/{id}.json`, så skemaet vises, selv om skolens server er
  nede eller efter en genstart. Fejler hentningen i over 3 timer, viser siden en `InfoBox` – aldrig toasts.
- **Ændringer i ugen:** hver hentning sammenlignes med den forrige (`FeedChanges.Detect`). Ændres en time i den viste uge
  fra i dag og frem – ny, flyttet, aflyst, nyt lokale eller navn – viser siden en gul `InfoBox` »Skemaet er ændret i denne uge«
  med ændringerne i klar tekst, timen får et mærke (»Ny«, »Flyttet«, »Ændret«, »Aflyst«; aflyste står overstreget på deres
  plads), barnets faneblad og forsidekortet får et advarselstegn. **Markér som set** fjerner det på alle skærme. Ændringer gemmes
  i kalender-cachen (overlever en genstart) og forsvinder af sig selv, når dagen er gået. En time, der flyttes tilbage, er ikke
  længere en ændring. Et nyt link er udgangspunktet – intet i det tæller som ændring. Det samme gælder, når et link hentes fra en ny adresse
  (cachen husker et fingeraftryk af adressen, aldrig selve linket). Timerne genkendes på kalenderens UID
  (plus oprindelig start for gentagne timer); får en time ny UID uden andre ændringer, er det ikke en ændring.
- **Fjern link** går tilbage til det faste skema (det bliver gemt hele tiden) – med »Fortryd«.

**Linket er en nøgle** til kalenderen: det gemmes kun i `skole/skemaer.json` på serveren, vises aldrig igen på skærmen (kun
værtsnavnet, fx »www.moodle.aau.dk«), og HTTP-klienten logger ikke adresser (`RemoveAllLoggers`).

`IcsParser` læser RFC 5545: UTC-, TZID- og flydende tider, `DURATION`, foldede linjer, `\,`-escapes, ugentlige og daglige
gentagelser (`INTERVAL`, `COUNT`, `UNTIL`, `BYDAY`), `EXDATE` og flyttede forekomster (`RECURRENCE-ID`).

## Regler

| Regel | Hvor |
|---|---|
| Ulige/lige uge følger ISO-ugenummeret (uge 41 er ulige). »Uge 1/Uge 2« på papiret = ulige/lige. | `ScheduleCell.SubjectInWeek` |
| En dag slutter med sidste time, der har et fag den uge. Ingen fag = »Ingen skole«. | `ScheduleRules.DaySpan` |
| En time er »lige nu« fra start (inkl.) til slut (ekskl.). | `ScheduleRules.CurrentPeriod` |
| Forsidekortet: i dag fra 06:00, til sidste barn har fri; derefter i morgen indtil 21:00 (fra kl. 12 på en dag uden skole). | `ScheduleRules.WidgetDay` |
| Tider holdes mellem 06:00 og 23:55 og kører aldrig over midnat. | `PeriodPlanner` |
| Ét skema pr. familiemedlem. Navn og farve kommer fra familiemedlemmet; fjernes det, står der »Uden navn«. | `ScheduleLook` |
| Filen ryddes op ved indlæsning og gem: ukendte fag/rækker fjernes fra timerne, dubletter og tomme navne droppes. | `SchoolSchedule.Normalized` |

Ferier og helligdage er ikke med – skemaet er en fast uge.

## Opbygning

| Mappe | Indhold |
|---|---|
| `Feeds/` | `IcsParser` (iCalendar), `ScheduleFeedService` (hent, gem, status, ændringer, `Changed`), `ScheduleFeedWorker`, `FeedChanges` (find, flet og beskriv ændringer) |
| `Schedules/` | `SchoolModels` (skema, fag, rækker, timer), `SchoolLevels` (folkeskole/gymnasium/universitet), `SchoolWeek` (fast skema eller ugens timer fra kalenderen), `ScheduleRules`, `PeriodPlanner`, `SubjectCatalog`, `SchoolScheduleService` (singleton med `Changed`) – ren logik med tests |
| `Components/` | `SchoolTimetable` (tabellen – visning, eller trykbar med `OnCellClick`), `FeedDialog`, `ChildTabs`, `ClassFields` (skoleform, trin, klasse), `SchoolWidget`, `ScheduleLook`, dialogerne `NewScheduleDialog`, `CellDialog`, `PeriodDialog`, `SubjectDialog` |
| `Pages/` | `SchoolPage`, `SchoolSettingsPage`, `ScheduleEditorPage` |

Dialoger ligger efter `</HubPage>` (som i madplanen): `HubPage`s indgangsanimation giver siden en `transform`,
og så ville en dialog inde i siden blive centreret på hele den lange side i stedet for på skærmen.

## Data (i `skole/` i datamappen)

| Fil | Indhold |
|---|---|
| `skemaer.json` | Alle skemaer – fælles for alle skærme. Nogle få kilobyte, så en JSON-fil (`JsonFileStore`) er nok. Indeholder kalender-links. |
| `kalender/{id}.json` | Seneste timer hentet fra et skemas kalender-link (14 dage tilbage, 120 frem) og ændringer, der ikke er markeret som set. |

Demodata (`tools/ui-smoke/demo-data.mjs`) indeholder skemaerne Emma 6.a, Oliver 0.b og Mette (universitet, uden link) samt en
Moodle-lignende kalender (`skole/demo-kalender.ics`), som røgtesten serverer på http://localhost:5091/aau.ics og forbinder til Mette.
