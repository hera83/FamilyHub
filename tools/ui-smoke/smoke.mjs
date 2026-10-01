// Family Hub – UI smoke test in real Chrome/Edge, emulating the 1920x1080 kitchen touch screen.
// Checks the on-screen keyboard end-to-end (finger taps, typing, number pad, dialogs), the calendar and the meal plan
// (pick, own dish, drag and drop, swipe, undo) and saves screenshots.
//
//   cd tools/ui-smoke
//   npm install                       (first time – installs playwright-core, no browser download)
//   node smoke.mjs [screenshot-folder]
//
// The app must be running (dotnet run --project src/FamilyHub.Web). To test the calendar with appointments,
// start it on demo data first:  node demo-data.mjs; $env:FamilyHub__DataDirectory = (Resolve-Path ./demo-data)
// Environment variables:
//   FAMILYHUB_URL   default http://localhost:5080
//   BROWSER_PATH    default: installed Chrome, else Edge
import { existsSync, mkdirSync } from 'node:fs';
import { chromium } from 'playwright-core';

const BASE = process.env.FAMILYHUB_URL || 'http://localhost:5080';
const OUT = process.argv[2] || './shots';
const CHROME = process.env.BROWSER_PATH || [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/chromium',
  '/usr/bin/chromium-browser',
].find(p => existsSync(p));
mkdirSync(OUT, { recursive: true });

const browser = await chromium.launch({ executablePath: CHROME, headless: true });
const results = [];
const log = (name, ok, detail = '') => { results.push({ name, ok, detail }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  – ' + detail : ''}`); };

async function newPage(options = {}) {
  const context = await browser.newContext({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1, hasTouch: true, locale: 'da-DK', ...options });
  const page = await context.newPage();
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') console.log(`  [browser ${m.type()}] ${m.text()}`); });
  page.on('pageerror', e => console.log(`  [page error] ${e.message}`));
  return { context, page };
}

async function open(page, path) {
  await page.goto(BASE + path, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.hub-app', { timeout: 15000 });
  await page.waitForTimeout(700);
}

// ---------------------------------------------------------------- touch screen (kitchen)
{
  const { context, page } = await newPage();

  await open(page, '/');
  await page.screenshot({ path: `${OUT}/01-home.png` });
  log('home renders clock', await page.locator('.home-hero__time').isVisible());

  await open(page, '/indstillinger');
  await page.screenshot({ path: `${OUT}/02-settings.png`, fullPage: false });
  const status = await page.locator('.hub-infobox').first().innerText();
  log('touch screen detected', /Touchskærm fundet/.test(status), status.slice(0, 60));

  // Tap the test field with a finger -> keyboard must appear.
  const field = page.getByLabel('Prøv tastaturet');
  await field.tap();
  await page.waitForTimeout(500);
  const expanded = await page.locator('.hub-osk[data-visibility="expanded"]').count();
  log('keyboard opens on finger tap', expanded === 1);
  const inputMode = await field.getAttribute('inputmode');
  log('native keyboard suppressed (inputmode=none)', inputMode === 'none', `inputmode=${inputMode}`);
  await page.screenshot({ path: `${OUT}/03-keyboard-text.png` });

  // Type "Hej æøå 2" by tapping keys.
  const tapKey = async text => page.locator(`.hub-osk__layer[data-layer-name="letters"] [data-key="char"][data-text="${text}"]`).tap();
  for (const ch of ['h', 'e', 'j']) await tapKey(ch);
  await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="space"]').tap();
  for (const ch of ['æ', 'ø', 'å']) await tapKey(ch);
  await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="space"]').tap();
  await tapKey('2');
  await page.waitForTimeout(300);
  const typed = await field.inputValue();
  log('typing via keyboard (auto-capital first letter)', typed === 'Hej æøå 2', JSON.stringify(typed));
  log('field kept focus while typing', await field.evaluate(el => document.activeElement === el));

  // Backspace
  await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="backspace"]').tap();
  await page.waitForTimeout(200);
  log('backspace', (await field.inputValue()) === 'Hej æøå ', JSON.stringify(await field.inputValue()));

  // Symbols layer
  await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="layer"]').tap();
  await page.waitForTimeout(150);
  const layer = await page.locator('.hub-osk').getAttribute('data-layer');
  log('switch to symbols layer', layer === 'symbols');
  await page.screenshot({ path: `${OUT}/04-keyboard-symbols.png` });
  await page.locator('.hub-osk__layer[data-layer-name="symbols"] [data-key="char"][data-text="!"]').tap();
  await page.waitForTimeout(150);
  log('typing a symbol', (await field.inputValue()) === 'Hej æøå !');

  // Minimise -> pill; tap field again -> expanded
  await page.locator('.hub-osk__tool', { hasText: 'Minimér' }).tap();
  await page.waitForTimeout(400);
  log('minimise shows pill', await page.locator('.hub-osk__pill').isVisible());
  log('field still focused after minimise', await field.evaluate(el => document.activeElement === el));
  await page.screenshot({ path: `${OUT}/05-keyboard-minimized.png` });
  await field.tap();
  await page.waitForTimeout(400);
  log('tap field again re-opens keyboard', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);

  // Physical key -> keyboard steps aside
  await page.keyboard.press('a');
  await page.waitForTimeout(400);
  log('physical keyboard hides on-screen keyboard', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);

  // Tap elsewhere -> keyboard closes, change committed
  await field.tap();
  await page.waitForTimeout(400);
  await page.locator('h1.hub-page__title').tap();
  await page.waitForTimeout(700);
  log('tap outside closes keyboard', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
  log('inputmode restored after close', (await field.getAttribute('inputmode')) === 'text');

  // Number pad on the design guide
  await open(page, '/indstillinger/komponenter');
  await page.screenshot({ path: `${OUT}/06-guide-top.png` });
  const priceField = page.getByLabel('Pris');
  await priceField.scrollIntoViewIfNeeded();
  await priceField.tap();
  await page.waitForTimeout(500);
  log('decimal field opens compact number pad', (await page.locator('.hub-osk__panel--compact').count()) === 1);
  await page.locator('.hub-osk [data-key="char"][data-text="1"]').tap();
  await page.locator('.hub-osk [data-key="char"][data-text=","]').tap();
  await page.locator('.hub-osk [data-key="char"][data-text="5"]').tap();
  await page.waitForTimeout(200);
  log('number pad types decimal comma', (await priceField.inputValue()) === '1,5', await priceField.inputValue());
  await page.screenshot({ path: `${OUT}/07-numpad.png` });
  const priceBox = await priceField.boundingBox();
  const oskBox = await page.locator('.hub-osk').boundingBox();
  log('focused field is above the keyboard', priceBox.y + priceBox.height <= oskBox.y, `field bottom ${Math.round(priceBox.y + priceBox.height)} / keyboard top ${Math.round(oskBox.y)}`);

  // Enter ("Færdig") closes the keyboard
  await page.locator('.hub-osk [data-key="enter"]').tap();
  await page.waitForTimeout(700);
  log('Enter (Færdig) closes keyboard', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);

  // Quick add keeps keyboard open
  const quick = page.getByLabel('Ny vare');
  await quick.scrollIntoViewIfNeeded();
  await quick.tap();
  await page.waitForTimeout(400);
  for (const ch of ['m', 'æ', 'l', 'k']) await page.locator(`.hub-osk__layer[data-layer-name="letters"] [data-key="char"][data-text="${ch}"]`).tap();
  const enterLabel = await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="enter"]').innerText();
  log('custom Enter label', enterLabel.trim() === 'Tilføj', enterLabel);
  await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="enter"]').tap();
  await page.waitForTimeout(700);
  log('quick add: item added', (await page.locator('.hub-list__title', { hasText: 'Mælk' }).count()) >= 1);
  log('quick add: keyboard stays open', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);
  log('quick add: field cleared', (await quick.inputValue()) === '');
  await page.waitForTimeout(500);
  log('quick add: auto-capital back on for next item', (await page.locator('.hub-osk').getAttribute('data-shift')) === 'once');
  await page.screenshot({ path: `${OUT}/08-quick-add.png` });
  await page.locator('h1.hub-page__title').tap();
  await page.waitForTimeout(600);

  // Toasts
  await page.locator('.hub-btn', { hasText: 'Med »Fortryd«' }).tap();
  await page.locator('.hub-btn', { hasText: 'Fejl' }).first().tap();
  await page.waitForTimeout(500);
  log('toasts shown', (await page.locator('.hub-toast').count()) === 2);
  await page.screenshot({ path: `${OUT}/09-toasts.png` });

  // Stepper
  const stepperValue = page.locator('.hub-stepper__value').first();
  const before = await stepperValue.innerText();
  await page.locator('.hub-stepper__button[aria-label="Flere"]').first().tap();
  await page.waitForTimeout(300);
  log('stepper +1', (await stepperValue.innerText()).trim() !== before.trim(), `${before.trim()} -> ${(await stepperValue.innerText()).trim()}`);

  // Dialog with auto-focus field -> keyboard over the dialog
  await page.locator('.hub-btn', { hasText: 'Dialog med felt' }).tap();
  await page.waitForTimeout(800);
  log('dialog open', await page.locator('.hub-dialog').isVisible());
  log('keyboard opens for auto-focused field after tap', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);
  const dialogBox = await page.locator('.hub-dialog').boundingBox();
  const osk2 = await page.locator('.hub-osk').boundingBox();
  log('dialog sits above keyboard', dialogBox.y + dialogBox.height <= osk2.y + 1, `dialog bottom ${Math.round(dialogBox.y + dialogBox.height)} / keyboard top ${Math.round(osk2.y)}`);
  await page.screenshot({ path: `${OUT}/10-dialog-keyboard.png` });

  await context.close();
}

// ---------------------------------------------------------------- household: add, remove, undo (settings)
{
  const { context, page } = await newPage();
  await open(page, '/indstillinger');
  const letter = ch => page.locator(`.hub-osk__layer[data-layer-name="letters"] [data-key="char"][data-text="${ch}"]`);

  const addButton = page.locator('.hub-card', { hasText: 'Familiemedlemmer' }).locator('.hub-btn', { hasText: 'Tilføj' });
  await addButton.scrollIntoViewIfNeeded();
  await addButton.tap();
  await page.waitForTimeout(800);
  log('household: add dialog opens with keyboard', (await page.locator('.hub-dialog').count()) === 1 && (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);

  for (const ch of ['a', 'n', 'n', 'e']) await letter(ch).tap();
  await page.locator('.hub-dialog .hub-btn', { hasText: 'Tilføj' }).tap();
  await page.waitForTimeout(900);
  log('household: member added with capital first letter', (await page.locator('.hub-list__title', { hasText: 'Anne' }).count()) === 1);
  log('household: dialog and keyboard closed', (await page.locator('.hub-dialog').count()) === 0 && (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
  await page.screenshot({ path: `${OUT}/15-household.png` });

  await page.locator('.hub-btn[aria-label="Fjern Anne"]').tap();
  await page.waitForTimeout(600);
  log('household: remove offers undo', (await page.locator('.hub-toast', { hasText: 'Anne er fjernet' }).count()) === 1);
  await page.locator('.hub-toast__action', { hasText: 'Fortryd' }).tap();
  await page.waitForTimeout(800);
  log('household: undo restores member', (await page.locator('.hub-list__title', { hasText: 'Anne' }).count()) === 1);

  const screenFact = await page.locator('.hub-facts dt', { hasText: 'Denne skærm' }).locator('xpath=following-sibling::dd[1]').innerText();
  log('system section shows detected screen', /1920 × 1080/.test(screenFact), screenFact);

  // Clean up so the next run starts the same way.
  await page.locator('.hub-btn[aria-label="Fjern Anne"]').tap();
  await page.waitForTimeout(400);

  // Theme switch
  await page.locator('.hub-choice__option', { hasText: 'Mørk' }).tap();
  await page.waitForTimeout(500);
  log('theme: dark applied', (await page.evaluate(() => document.documentElement.dataset.theme)) === 'dark');
  await page.screenshot({ path: `${OUT}/16-settings-dark.png` });
  await page.locator('.hub-choice__option', { hasText: 'Efter tidspunkt' }).tap();
  await page.waitForTimeout(300);
  await context.close();
}

// ---------------------------------------------------------------- calendar (best with demo data: node demo-data.mjs)
{
  const { context, page } = await newPage();
  await open(page, '/kalender');
  if (await page.locator('.cal-week').count() === 0) {
    log('calendar: without accounts it offers to get started', (await page.locator('.hub-empty', { hasText: 'Forbind jeres Google-kalender' }).count()) === 1);
    await page.screenshot({ path: `${OUT}/17-calendar-empty.png` });
  } else {
    log('calendar: week view has seven days', (await page.locator('.cal-week__day').count()) === 7);
    log('calendar: today is marked', (await page.locator('.cal-week__day--today').count()) === 1);
    await page.screenshot({ path: `${OUT}/17-calendar-week.png` });

    await page.locator('.hub-choice__option', { hasText: 'Dag' }).tap();
    await page.waitForTimeout(600);
    log('calendar: day view with a column per person', (await page.locator('.cal-day__colhead').count()) >= 1, `${await page.locator('.cal-day__colhead').count()} columns`);
    log('calendar: view is kept in the address', page.url().includes('visning=dag'));
    await page.screenshot({ path: `${OUT}/18-calendar-day.png` });

    await page.locator('.hub-choice__option', { hasText: 'Måned' }).tap();
    await page.waitForTimeout(600);
    log('calendar: month view', (await page.locator('.cal-month__row').count()) >= 5);
    await page.screenshot({ path: `${OUT}/19-calendar-month.png` });

    await page.locator('.cal-month__cell--today').tap();
    await page.waitForTimeout(600);
    log('calendar: tapping a day opens it', (await page.locator('.cal-day').count()) === 1);

    await open(page, '/kalender');
    const event = page.locator('.cal-event--agenda').first();
    const title = (await event.locator('.cal-event__title').innerText()).trim();
    await event.tap();
    await page.waitForTimeout(600);
    log('calendar: tapping an event shows its details', (await page.locator('.hub-dialog__title', { hasText: title }).count()) === 1, title);
    await page.locator('.hub-dialog .hub-btn[aria-label="Luk"]').tap();
    await page.waitForTimeout(400);

    const add = page.locator('.hub-btn', { hasText: 'Tilføj aftale' });
    if (await add.count()) {
      await add.tap();
      await page.waitForTimeout(900);
      log('calendar: add dialog opens with the keyboard for the title', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);
      await page.locator('h2.hub-dialog__title').tap();
      await page.waitForTimeout(600);
      await page.locator('.hub-dialog .hub-btn', { hasText: 'Tilføj' }).last().tap();
      await page.waitForTimeout(400);
      log('calendar: a title is required', (await page.locator('.hub-field__error', { hasText: 'Skriv en titel' }).count()) === 1);
      await page.screenshot({ path: `${OUT}/20-calendar-add.png` });
      await page.locator('.hub-dialog .hub-btn', { hasText: 'Annuller' }).tap();
    }

    await open(page, '/kalender/indstillinger');
    log('calendar: settings list the calendars', (await page.locator('.hub-list__row').count()) >= 1);
    await page.screenshot({ path: `${OUT}/21-calendar-settings.png`, fullPage: true });
  }
  await context.close();
}

// ---------------------------------------------------------------- meal plan
// Plays in a week far ahead (January 2030), so a real plan is never touched – and removes what it added.
{
  const { context, page } = await newPage();
  const cdp = await context.newCDPSession(page);
  const touch = (type, x, y) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: type === 'touchEnd' ? [] : [{ x, y }] });
  const center = async locator => { const b = await locator.boundingBox(); return { x: b.x + b.width / 2, y: b.y + b.height / 2 }; };
  const day = iso => page.locator(`.mp-day[data-day="${iso}"]`);
  const dinnerTitle = async iso => (await day(iso).locator('.mp-dinner__title').count()) ? (await day(iso).locator('.mp-dinner__title').innerText()).trim() : null;

  await open(page, '/madplan');
  log('meal plan: seven days, Monday to Sunday', (await page.locator('.mp-day').count()) === 7);
  log('meal plan: today is marked', (await page.locator('.mp-day--today').count()) === 1);
  await page.screenshot({ path: `${OUT}/23-madplan-week.png` });

  // Remove anything left over from an interrupted run (and, at the end, what this run planned).
  const cleanTestWeek = async () => {
    for (const iso of ['2030-01-07', '2030-01-08', '2030-01-09', '2030-01-10', '2030-01-11', '2030-01-12', '2030-01-13']) {
      if (await dinnerTitle(iso)) {
        await day(iso).locator('.mp-day__press').tap();
        await page.waitForTimeout(600);
        await page.locator('.hub-dialog .hub-btn', { hasText: 'Fjern' }).tap();
        await page.waitForTimeout(600);
      }
    }
  };

  await open(page, '/madplan?dato=2030-01-07');
  await cleanTestWeek();
  log('meal plan: week title', /Uge 2/.test(await page.locator('.mp-toolbar__title').innerText()));

  // An empty day opens the picker with the recipe book.
  await day('2030-01-07').locator('.mp-day__press').tap();
  await page.waitForTimeout(700);
  const recipes = await page.locator('.hub-dialog .hub-list__row').count();
  log('meal plan: tapping an empty day opens the picker', (await page.locator('.hub-dialog__title', { hasText: 'Aftensmad mandag 7. januar' }).count()) === 1);
  log('meal plan: picker lists the recipe book', recipes > 1, `${recipes} rows`);
  log('meal plan: no keyboard until the search is tapped', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
  await page.screenshot({ path: `${OUT}/24-madplan-picker.png` });

  const chips = page.locator('.hub-dialog .hub-choice--chips .hub-choice__option');
  if (await chips.count() > 2) {
    await chips.nth(1).tap();
    await page.waitForTimeout(400);
    const filtered = await page.locator('.hub-dialog .hub-list__row').count();
    log('meal plan: a category narrows the list', filtered < recipes, `${filtered} of ${recipes}`);
    await chips.nth(0).tap();
    await page.waitForTimeout(400);
  }

  const first = page.locator('.hub-dialog .hub-list__row').first();
  const chosen = (await first.locator('.hub-list__title').innerText()).trim();
  await first.tap();
  await page.waitForTimeout(700);
  log('meal plan: the chosen recipe shows on the day', (await dinnerTitle('2030-01-07')) === chosen, chosen);

  // The family's own dish, typed with the on-screen keyboard.
  await day('2030-01-08').locator('.mp-day__press').tap();
  await page.waitForTimeout(700);
  await page.getByLabel('Søg').tap();
  await page.waitForTimeout(500);
  log('meal plan: the keyboard opens for the search', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);
  const key = t => page.locator(`.hub-osk__layer[data-layer-name="letters"] [data-key="char"][data-text="${t}"]`).tap();
  for (const ch of 'rester') await key(ch);
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/25-madplan-own-dish.png` });
  await page.locator('.hub-dialog .hub-list__row', { hasText: 'Brug »Rester«' }).tap();
  await page.waitForTimeout(700);
  log('meal plan: own dish from the search text', (await dinnerTitle('2030-01-08')) === 'Rester');

  // Hold a dinner with a finger and drag it onto the other planned day: they swap.
  const from = await center(day('2030-01-07').locator('.mp-dinner'));
  const to = await center(day('2030-01-08'));
  await touch('touchStart', from.x, from.y);
  await page.waitForTimeout(600);
  const lifted = await page.locator('.mp-dinner--ghost').count();
  for (let i = 1; i <= 8; i++) await touch('touchMove', from.x + (to.x - from.x) * i / 8, from.y + (to.y - from.y) * i / 8);
  await page.screenshot({ path: `${OUT}/26-madplan-drag.png` });
  await touch('touchEnd');
  await page.waitForTimeout(900);
  log('meal plan: holding a dinner lifts it', lifted === 1);
  log('meal plan: drag and drop swaps two dinners', (await dinnerTitle('2030-01-07')) === 'Rester' && (await dinnerTitle('2030-01-08')) === chosen);
  log('meal plan: the drop did not open a dialog', (await page.locator('.hub-dialog').count()) === 0);

  // Mouse drag to an empty day moves it.
  const mouseFrom = await center(day('2030-01-08').locator('.mp-dinner'));
  const mouseTo = await center(day('2030-01-10'));
  await page.mouse.move(mouseFrom.x, mouseFrom.y);
  await page.mouse.down();
  for (let i = 1; i <= 10; i++) await page.mouse.move(mouseFrom.x + (mouseTo.x - mouseFrom.x) * i / 10, mouseFrom.y + (mouseTo.y - mouseFrom.y) * i / 10);
  await page.mouse.up();
  await page.waitForTimeout(900);
  log('meal plan: mouse drag moves a dinner to an empty day', (await dinnerTitle('2030-01-10')) === chosen && (await dinnerTitle('2030-01-08')) === null);

  // A planned day shows the dish first.
  await day('2030-01-10').locator('.mp-day__press').tap();
  await page.waitForTimeout(700);
  log('meal plan: a planned day shows the dish', (await page.locator('.hub-dialog__title', { hasText: chosen }).count()) === 1);
  log('meal plan: the dish dialog offers change and move', (await page.locator('.hub-dialog .hub-btn', { hasText: 'Skift ret' }).count()) === 1 && (await page.locator('.hub-dialog .hub-choice__option').count()) === 7);
  await page.screenshot({ path: `${OUT}/27-madplan-dinner.png` });

  // "Print" (only when a printer is set up): alone on the left of the footer. Never tapped here – it would print paper.
  const printButton = page.locator('.hub-dialog__footer .hub-btn', { hasText: 'Print' });
  if (await printButton.count()) {
    const printBox = await printButton.boundingBox();
    const removeBox = await page.locator('.hub-dialog__footer .hub-btn', { hasText: 'Fjern' }).boundingBox();
    const footerBox = await page.locator('.hub-dialog__footer').boundingBox();
    log('meal plan: Print sits on the left, Fjern and Skift ret on the right',
      printBox.x - footerBox.x < 40 && removeBox.x - (printBox.x + printBox.width) > 40);
    log('meal plan: Print is big enough to tap', printBox.height >= 44);
    const theme = await page.evaluate(() => document.documentElement.dataset.theme);
    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/27-madplan-dinner-dark.png` });
    await page.evaluate(t => document.documentElement.dataset.theme = t, theme);
  } else {
    log('meal plan: no printer set up – Print is hidden (set FamilyHub__Printing__* to check it)', true);
  }

  // "Flyt til" in the dialog – the way without gestures.
  await page.locator('.hub-dialog .hub-choice__option', { hasText: 'Fre 11.' }).tap();
  await page.waitForTimeout(800);
  log('meal plan: "Flyt til" moves the dinner', (await dinnerTitle('2030-01-11')) === chosen && (await dinnerTitle('2030-01-10')) === null);

  // Remove, then undo.
  await day('2030-01-11').locator('.mp-day__press').tap();
  await page.waitForTimeout(700);
  await page.locator('.hub-dialog .hub-btn', { hasText: 'Fjern' }).tap();
  await page.waitForTimeout(700);
  log('meal plan: removing offers Fortryd', (await page.locator('.hub-toast', { hasText: 'er fjernet' }).count()) === 1 && (await dinnerTitle('2030-01-11')) === null);
  await page.locator('.hub-toast__action', { hasText: 'Fortryd' }).tap();
  await page.waitForTimeout(800);
  log('meal plan: Fortryd puts it back', (await dinnerTitle('2030-01-11')) === chosen);

  // Swipe left with a finger: next week.
  const week = await center(page.locator('.mp-week'));
  await touch('touchStart', week.x + 200, week.y);
  for (let i = 1; i <= 6; i++) await touch('touchMove', week.x + 200 - i * 60, week.y + i * 2);
  await touch('touchEnd');
  await page.waitForTimeout(900);
  log('meal plan: swiping shows the next week', page.url().includes('dato=2030-01-14'), page.url());

  await open(page, '/madplan?dato=2030-01-07');
  await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/28-madplan-dark.png` });
  await page.evaluate(() => document.documentElement.dataset.theme = 'light');

  await cleanTestWeek();
  log('meal plan: test week cleaned up', (await page.locator('.mp-dinner').count()) === 0);
  await context.close();
}
{
  const { context, page } = await newPage({ viewport: { width: 1280, height: 1024 } });
  await open(page, '/madplan');
  await page.screenshot({ path: `${OUT}/29-madplan-1280x1024.png` });
  await context.close();
}

// ---------------------------------------------------------------- laptop with mouse (no touch interaction)
{
  const { context, page } = await newPage({ hasTouch: true });
  await open(page, '/indstillinger');
  const field = page.getByLabel('Prøv tastaturet');
  await field.click();
  await page.waitForTimeout(500);
  log('mouse click does NOT open keyboard (Auto)', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
  await context.close();
}

// ---------------------------------------------------------------- no touch screen at all
{
  const { context, page } = await newPage({ hasTouch: false });
  // This laptop has a real touch screen, which Chrome reports even when not emulated – hide it.
  await context.addInitScript(() => {
    Object.defineProperty(Navigator.prototype, 'maxTouchPoints', { get: () => 0 });
    const original = window.matchMedia.bind(window);
    window.matchMedia = q => /coarse/.test(q) ? { matches: false, media: q, onchange: null, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {}, dispatchEvent() { return false; } } : original(q);
  });
  await open(page, '/indstillinger');
  const status = await page.locator('.hub-infobox').first().innerText();
  log('no touch screen reported', /Ingen touchskærm/.test(status), status.slice(0, 50));
  await context.close();
}

// ---------------------------------------------------------------- night mode + other sizes
{
  const { context, page } = await newPage();
  await open(page, '/');
  await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/11-home-dark.png` });
  await open(page, '/indstillinger/komponenter');
  await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/12-guide-dark.png` });
  await open(page, '/kalender');
  await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/22-calendar-dark.png` });
  await context.close();
}
{
  const { context, page } = await newPage({ viewport: { width: 1280, height: 1024 } });
  await open(page, '/kalender');
  await page.screenshot({ path: `${OUT}/13-calendar-1280x1024.png` });
  await context.close();
}
{
  const { context, page } = await newPage({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1' });
  await open(page, '/indstillinger');
  const status = await page.locator('.hub-infobox').first().innerText();
  log('phone: uses its own keyboard', /eget skærmtastatur/.test(status), status.slice(0, 50));
  await page.screenshot({ path: `${OUT}/14-phone-settings.png` });
  await context.close();
}

await browser.close();
const failed = results.filter(r => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length ? 1 : 0);
