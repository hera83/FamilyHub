# Feedback: toasts, informationsbokse, dialoger og tomme tilstande

## Hvad skal jeg bruge?

| Situation | Brug | Eksempel |
|---|---|---|
| Resultatet kan ses direkte på skærmen | **Ingen besked** | Varen dukker op i listen |
| Brugeren gjorde noget, og resultatet ses ikke tydeligt | **Toast** – Success | "Navnet er gemt" |
| Brugeren fjernede/slettede noget | **Toast med Fortryd** | "Mælk er fjernet · Fortryd" |
| Noget, brugeren gjorde, fejlede | **Toast** – Error (bliver stående) | "Kunne ikke gemme · Prøv igen om lidt." |
| En tilstand, der varer (ikke forbundet, offline, sync fejler) | **InfoBox** der, hvor det gælder | "Google Kalender er ikke forbundet [Forbind]" |
| En indtastning er ugyldig | **Fejltekst under feltet** | "Skriv et navn" |
| Der skal tages stilling, og det kan ikke fortrydes | **Bekræftelsesdialog** | "Slet madplanen for uge 39?" |
| Der er ingen data endnu | **EmptyState** med én handling | "Indkøbslisten er tom [Tilføj vare]" |
| Data hentes | **Skeleton** i lister/kort, **Spinner** i små områder | – |

## Toasts

```csharp
@inject IToastService Toasts

Toasts.Success("Gemt");
Toasts.Info("Kalenderen er opdateret", "3 nye aftaler i denne uge.");
Toasts.Warning("Gemt – men ikke synkroniseret", "Prøver igen, når der er forbindelse.");
Toasts.Error("Kunne ikke gemme", "Prøv igen om lidt.");
Toasts.Undoable("Mælk er fjernet", () => RestoreAsync(item));
Toasts.Show(new ToastOptions { Level = ToastLevel.Info, Title = "Synkroniserer …", Key = "sync" });
```

Reglerne ligger ét sted (`ToastDefaults`) og gælder alle menuer:

| Type | Varighed |
|---|---|
| Success | 4 sekunder |
| Info | 5 sekunder |
| Warning | 8 sekunder |
| Med knap (fx Fortryd) | mindst 8 sekunder |
| Error | Bliver stående, til den lukkes |

- Vises øverst i midten, nyeste øverst, **højst 3** ad gangen. Den ældste (ikke-fejl) viger – aldrig den nyeste.
- Samme besked flere gange tælles op ("×3") i stedet for at stable.
- `Key` erstatter en tidligere toast med samme nøgle – godt til statusbeskeder.
- Holder man fingeren på en toast, pauser nedtællingen. Den tynde linje nederst viser tiden.
- **Tekst:** titel på 2–5 ord, evt. én kort sætning. Højst én knap.
- **Ikke til:** varige tilstande (InfoBox), valideringsfejl (feltets fejltekst), "Velkommen"-beskeder
  eller beskeder, ingen har bedt om.

## Informationsbokse (InfoBox)

```razor
<InfoBox Tone="HubTone.Warning" Title="Google Kalender er ikke forbundet">
    <ChildContent>Familiens aftaler kan ikke vises, før kalenderen er forbundet.</ChildContent>
    <Actions>
        <HubButton Variant="ButtonVariant.Primary" Size="ButtonSize.Small" OnClick="ConnectAsync">Forbind</HubButton>
    </Actions>
</InfoBox>
```

| Tone | Brug |
|---|---|
| `Neutral` | Tips og forklaringer |
| `Info` | Nyttig status ("Opdateres hvert 5. minut") |
| `Success` | Bekræfter en god tilstand ("Alt er opdateret") |
| `Warning` | Noget virker ikke helt, men kan løses |
| `Danger` | Noget virker ikke |

- Står der, hvor situationen gælder (i toppen af siden eller i det kort, det handler om).
- Forsvinder, når situationen er løst – ikke efter tid.
- Højst 1–2 knapper, der løser situationen. `OnDismiss` (kryds) kun for bokse, der er sikre at ignorere.

## Dialoger

**Foretræk "gør det + Fortryd" frem for "Er du sikker?".** Dialoger afbryder – brug dem sjældent.

```csharp
@inject DialogService Dialogs

var confirmed = await Dialogs.ConfirmAsync(new ConfirmOptions
{
    Title = "Slet madplanen for uge 39?",
    Message = "Retterne og indkøbslisten for ugen fjernes.",
    ConfirmText = "Slet",
    Destructive = true,
});
```

- Titlen er et spørgsmål. Knapperne er verber ("Slet", "Gem") – aldrig "OK" eller "Ja".
- Knaprækkefølge: stille "Annuller" først, den primære handling sidst (til højre).
- `HubDialog` til korte opgaver med få felter. `CloseOnBackdrop="false"`, når der kan være skrevet noget.

## Fejl

| Hvor | Hvad sker der |
|---|---|
| `HubButton` med async `OnClick` | Knappen deaktiveres, mens den arbejder (ingen dobbelttryk), viser spinner efter 250 ms. Fejler den: fejl-toast + log. |
| Andre handlinger (`OnCommit`, `OnEnter`, gem ved valg) | `await Toasts.TryAsync(() => SaveAsync(), "Navnet blev ikke gemt", Logger)` |
| Uventet fejl i en side | Layoutets `ErrorBoundary` viser "Siden kunne ikke vises" med "Prøv igen". Resten af appen kører videre. |
| Forbindelsen til serveren afbrydes | Skærmen viser "Et øjeblik …", prøver selv igen og genindlæser, når Family Hub svarer. |
| Baggrundsjob fejler (fx kalender-sync) | Ingen toast – vis status med en `InfoBox` der, hvor data vises. |

## Tomme tilstande og indlæsning

- `EmptyState`: ikon, en titel der siger, hvad der mangler, én sætning og **én** handling.
- Aldrig en tom flade og aldrig "Ingen data".
- Skeleton frem for spinner i lister og kort – det føles roligere. Knapper viser selv, at de arbejder.
