// Family Hub – UI smoke test in real Chrome/Edge, emulating the 1920x1080 kitchen touch screen.
// Checks the on-screen keyboard end-to-end (finger taps, typing, number pad, dialogs), the calendar and the meal plan
// (pick, own dish, drag and drop, swipe, undo, shopping list) and saves screenshots.
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
  const main = iso => day(iso).locator('.mp-dinner[data-course="Main"]');
  const dinnerTitle = async iso => (await main(iso).count()) ? (await main(iso).locator('.mp-dinner__title').innerText()).trim() : null;
  const courses = async iso => (await day(iso).locator('[data-course]').evaluateAll(els => els.map(e => e.dataset.course))).join(',');

  await open(page, '/madplan');
  log('meal plan: seven days, Monday to Sunday', (await page.locator('.mp-day').count()) === 7);
  log('meal plan: today is marked', (await page.locator('.mp-day--today').count()) === 1);
  await page.screenshot({ path: `${OUT}/23-madplan-week.png` });

  // Remove anything left over from an interrupted run (and, at the end, what this run planned).
  const cleanTestWeek = async () => {
    for (const iso of ['2030-01-07', '2030-01-08', '2030-01-09', '2030-01-10', '2030-01-11', '2030-01-12', '2030-01-13']) {
      while (await day(iso).locator('.mp-dish').count()) {
        await day(iso).locator('.mp-dish').first().tap();
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
  await day('2030-01-07').locator('.mp-day__empty').tap();
  await page.waitForTimeout(700);
  const recipes = await page.locator('.hub-dialog .hub-list__row').count();
  log('meal plan: tapping an empty day opens the picker', (await page.locator('.hub-dialog__title', { hasText: 'Aftensmad mandag 7. januar' }).count()) === 1);
  log('meal plan: picker lists the recipe book', recipes > 1, `${recipes} rows`);
  log('meal plan: no keyboard until the search is tapped', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
  log('meal plan: an empty day suggests the main course',
    (await page.locator('.hub-dialog .hub-choice--segmented [aria-checked="true"]').innerText()).trim() === 'Hovedret');
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
  await day('2030-01-08').locator('.mp-day__empty').tap();
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
  const from = await center(main('2030-01-07'));
  const to = await center(day('2030-01-08'));
  await touch('touchStart', from.x, from.y);
  await page.waitForTimeout(600);
  const lifted = await page.locator('.mp-menu--ghost').count();
  for (let i = 1; i <= 8; i++) await touch('touchMove', from.x + (to.x - from.x) * i / 8, from.y + (to.y - from.y) * i / 8);
  await page.screenshot({ path: `${OUT}/26-madplan-drag.png` });
  await touch('touchEnd');
  await page.waitForTimeout(900);
  log('meal plan: holding a dinner lifts it', lifted === 1);
  log('meal plan: drag and drop swaps two dinners', (await dinnerTitle('2030-01-07')) === 'Rester' && (await dinnerTitle('2030-01-08')) === chosen);
  log('meal plan: the drop did not open a dialog', (await page.locator('.hub-dialog').count()) === 0);

  // Mouse drag to an empty day moves it.
  const mouseFrom = await center(main('2030-01-08'));
  const mouseTo = await center(day('2030-01-10'));
  await page.mouse.move(mouseFrom.x, mouseFrom.y);
  await page.mouse.down();
  for (let i = 1; i <= 10; i++) await page.mouse.move(mouseFrom.x + (mouseTo.x - mouseFrom.x) * i / 10, mouseFrom.y + (mouseTo.y - mouseFrom.y) * i / 10);
  await page.mouse.up();
  await page.waitForTimeout(900);
  log('meal plan: mouse drag moves a dinner to an empty day', (await dinnerTitle('2030-01-10')) === chosen && (await dinnerTitle('2030-01-08')) === null);

  // A planned day shows the dish first.
  await main('2030-01-10').tap();
  await page.waitForTimeout(700);
  log('meal plan: a planned day shows the dish', (await page.locator('.hub-dialog__title', { hasText: chosen }).count()) === 1);
  log('meal plan: the dish dialog offers change, no move', (await page.locator('.hub-dialog .hub-btn', { hasText: 'Skift ret' }).count()) === 1 && (await page.locator('.hub-dialog .hub-choice__option').count()) === 0);
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

  await page.locator('.hub-dialog .hub-btn[aria-label="Luk"]').tap();
  await page.waitForTimeout(500);

  // Remove, then undo.
  await main('2030-01-10').tap();
  await page.waitForTimeout(700);
  await page.locator('.hub-dialog .hub-btn', { hasText: 'Fjern' }).tap();
  await page.waitForTimeout(700);
  log('meal plan: removing offers Fortryd', (await page.locator('.hub-toast', { hasText: 'er fjernet' }).count()) === 1 && (await dinnerTitle('2030-01-10')) === null);
  await page.locator('.hub-toast__action', { hasText: 'Fortryd' }).tap();
  await page.waitForTimeout(800);
  log('meal plan: Fortryd puts it back', (await dinnerTitle('2030-01-10')) === chosen);

  // One more dish on the day: the small "Tilføj" under the main course suggests a dessert, then a starter.
  log('meal plan: a planned day offers "Tilføj" for one more dish', (await day('2030-01-10').locator('.mp-day__add').count()) === 1);
  await day('2030-01-10').locator('.mp-day__add').tap();
  await page.waitForTimeout(700);
  const segment = page.locator('.hub-dialog .hub-choice--segmented .hub-choice__option');
  log('meal plan: "Tilføj" suggests a dessert and greys out the main course',
    (await page.locator('.hub-dialog .hub-choice--segmented [aria-checked="true"]').innerText()).trim() === 'Dessert'
    && await segment.filter({ hasText: 'Hovedret' }).isDisabled());
  await page.screenshot({ path: `${OUT}/27b-madplan-add-course.png` });
  await page.locator('.hub-dialog .hub-list__row').first().tap();
  await page.waitForTimeout(700);
  await day('2030-01-10').locator('.mp-day__add').tap();
  await page.waitForTimeout(700);
  log('meal plan: then a starter', (await page.locator('.hub-dialog .hub-choice--segmented [aria-checked="true"]').innerText()).trim() === 'Forret');
  await page.getByLabel('Søg').tap();
  await page.waitForTimeout(400);
  for (const ch of 'suppe') await key(ch);
  await page.waitForTimeout(300);
  await page.locator('.hub-dialog .hub-list__row', { hasText: 'Brug »Suppe«' }).tap();
  await page.waitForTimeout(800);
  log('meal plan: the day shows starter, main course and dessert in that order', (await courses('2030-01-10')) === 'Starter,Main,Dessert', await courses('2030-01-10'));
  log('meal plan: a full day has no "Tilføj"', (await day('2030-01-10').locator('.mp-day__add').count()) === 0);
  await page.screenshot({ path: `${OUT}/27c-madplan-courses.png` });

  // A day with only a main course, dragged onto the full day: the two days swap whole menus.
  await day('2030-01-13').locator('.mp-day__empty').tap();
  await page.waitForTimeout(700);
  await page.locator('.hub-dialog .hub-list__row').first().tap();
  await page.waitForTimeout(700);
  const dayFrom = await center(main('2030-01-13'));
  const dayTo = await center(day('2030-01-10'));
  await touch('touchStart', dayFrom.x, dayFrom.y);
  await page.waitForTimeout(600);
  for (let i = 1; i <= 8; i++) await touch('touchMove', dayFrom.x + (dayTo.x - dayFrom.x) * i / 8, dayFrom.y + (dayTo.y - dayFrom.y) * i / 8);
  await touch('touchEnd');
  await page.waitForTimeout(900);
  log('meal plan: dragging a day onto a full day swaps the whole menus',
    (await courses('2030-01-10')) === 'Main' && (await courses('2030-01-13')) === 'Starter,Main,Dessert',
    `${await courses('2030-01-10')} | ${await courses('2030-01-13')}`);

  // Drag the full day (holding its dessert) to an empty day: every course goes along.
  const fullFrom = await center(day('2030-01-13').locator('.mp-dinner[data-course="Dessert"]'));
  const emptyTo = await center(day('2030-01-12'));
  await touch('touchStart', fullFrom.x, fullFrom.y);
  await page.waitForTimeout(600);
  const ghostCourses = await page.locator('.mp-menu--ghost [data-course]').count();
  for (let i = 1; i <= 8; i++) await touch('touchMove', fullFrom.x + (emptyTo.x - fullFrom.x) * i / 8, fullFrom.y + (emptyTo.y - fullFrom.y) * i / 8);
  await page.screenshot({ path: `${OUT}/27d-madplan-drag-menu.png` });
  await touch('touchEnd');
  await page.waitForTimeout(900);
  log('meal plan: holding a dish lifts the whole menu', ghostCourses === 3, `${ghostCourses} dishes in the ghost`);
  log('meal plan: dragging a full day to an empty day moves every course',
    (await courses('2030-01-12')) === 'Starter,Main,Dessert' && (await courses('2030-01-13')) === '',
    `${await courses('2030-01-12')} | ${await courses('2030-01-13')}`);

  // The shopping list: the week's ingredients by section, crossing out, staples, how a grocery is bought and fixed items.
  // Everything it changes is undone again (the fixed item is for the test week only and removed at the end).
  await page.locator('.hub-page__actions .hub-btn', { hasText: 'Indkøbsliste' }).tap();
  await page.waitForTimeout(1000);
  const lastDialog = () => page.locator('.hub-dialog').last();
  const groceries = page.locator('.sl-main .sl-row');
  log('shopping list: opens wide for the week', (await page.locator('.hub-dialog--extralarge .hub-dialog__title', { hasText: 'Indkøbsliste · uge 2' }).count()) === 1);
  log('shopping list: groceries by section of the shop', (await groceries.count()) > 0 && (await page.locator('.sl-section').count()) > 0,
    `${await groceries.count()} groceries, ${await page.locator('.sl-section').count()} sections`);
  log('shopping list: no keyboard', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
  await page.screenshot({ path: `${OUT}/30-indkobsliste.png` });

  // Rows not touched before (an interrupted run may have left marks in the test week).
  const grocery = page.locator(`.sl-main .sl-row[data-key="${await page.locator('.sl-main .sl-row:not(.sl-row--checked)').first().getAttribute('data-key')}"]`);
  await grocery.locator('.sl-row__main').tap();
  await page.waitForTimeout(600);
  log('shopping list: tapping a grocery crosses it out', /sl-row--checked/.test(await grocery.getAttribute('class')));
  await page.screenshot({ path: `${OUT}/30b-indkobsliste-streget-ud.png` });
  await grocery.locator('.sl-row__main').tap();
  await page.waitForTimeout(600);
  log('shopping list: tapping it again puts it back', !/sl-row--checked/.test(await grocery.getAttribute('class')));

  const staple = page.locator('.sl-side .sl-row:not(.sl-row--checked)').first();
  if (await staple.count()) {
    const key = await staple.getAttribute('data-key');
    await staple.locator('.sl-row__main').tap();
    await page.waitForTimeout(700);
    log('shopping list: a missing staple goes on the list', (await page.locator(`.sl-main .sl-row[data-key="${key}"]`).count()) === 1, key);
    await page.locator(`.sl-side .sl-row[data-key="${key}"] .sl-row__main`).tap();
    await page.waitForTimeout(700);
    log('shopping list: and off it again', (await page.locator(`.sl-main .sl-row[data-key="${key}"]`).count()) === 0);
  }

  await groceries.first().locator('.hub-btn').tap();
  await page.waitForTimeout(600);
  log('shopping list: the pencil says how a grocery is bought', (await lastDialog().locator('.hub-switch', { hasText: 'Basisvare' }).count()) === 1);
  const packs = lastDialog().locator('.hub-choice__option', { hasText: 'Hele pakker' });
  if (await packs.count()) {
    await packs.tap();
    await page.waitForTimeout(400);
    log('shopping list: packs show what the list will say', (await lastDialog().locator('.gr-preview').count()) === 1);
  }
  await page.screenshot({ path: `${OUT}/31-indkobsliste-vare.png` });
  await lastDialog().locator('.hub-btn', { hasText: 'Annuller' }).tap();
  await page.waitForTimeout(500);

  // A fixed item for the test week only, typed with the on-screen keyboard; Enter ("Tilføj") adds it and stays.
  await page.locator('.sl-group__head .hub-btn', { hasText: 'Tilføj' }).tap();
  await page.waitForTimeout(700);
  log('shopping list: "Tilføj" opens the keyboard (started by the user)', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);
  await lastDialog().locator('.hub-choice__option', { hasText: 'Kun uge 2' }).tap(); // a tap outside the field closes the keyboard …
  await page.waitForTimeout(500);
  await lastDialog().locator('input').tap();                                        // … and a tap in it brings it back
  await page.waitForTimeout(500);
  for (const ch of ['s', 'k', 'y', 'r']) await page.locator(`.hub-osk__layer[data-layer-name="letters"] [data-key="char"][data-text="${ch}"]`).tap();
  log('shopping list: Enter says Tilføj', (await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="enter"]').innerText()).trim() === 'Tilføj');
  await page.locator('.hub-osk__layer[data-layer-name="letters"] [data-key="enter"]').tap();
  await page.waitForTimeout(800);
  log('shopping list: Enter adds the item and makes room for the next', /Tilføjet: Skyr/.test(await lastDialog().innerText()) && (await lastDialog().locator('input').inputValue()) === '');
  log('shopping list: the keyboard stays for the next item', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);
  await page.screenshot({ path: `${OUT}/32-indkobsliste-fast-vare.png` });
  await lastDialog().locator('.hub-btn', { hasText: 'Færdig' }).tap();
  await page.waitForTimeout(700);
  const skyr = page.locator('.sl-side .hub-list__row', { hasText: 'Skyr' });
  log('shopping list: the fixed item is on the list', (await skyr.count()) === 1 && (await page.locator('.sl-main .sl-row[data-key="skyr"]').count()) === 1);

  // Remove it – with Fortryd – and remove it for good.
  const removeSkyr = async () => {
    await page.locator('.sl-side .hub-list__row', { hasText: 'Skyr' }).tap();
    await page.waitForTimeout(600);
    await lastDialog().locator('.hub-btn', { hasText: 'Fjern' }).tap();
    await page.waitForTimeout(700);
  };
  await removeSkyr();
  log('shopping list: removing a fixed item offers Fortryd', (await page.locator('.hub-toast', { hasText: 'Skyr er fjernet' }).count()) === 1 && (await skyr.count()) === 0);
  await page.locator('.hub-toast__action', { hasText: 'Fortryd' }).tap();
  await page.waitForTimeout(800);
  log('shopping list: Fortryd puts it back', (await skyr.count()) === 1);
  await removeSkyr();

  await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/33-indkobsliste-dark.png` });
  await page.evaluate(() => document.documentElement.dataset.theme = 'light');
  await page.locator('.hub-dialog .hub-btn', { hasText: 'Færdig' }).tap();
  await page.waitForTimeout(500);
  log('shopping list: Færdig closes it', (await page.locator('.hub-dialog').count()) === 0);

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
  await page.locator('.hub-page__actions .hub-btn', { hasText: 'Indkøbsliste' }).tap();
  await page.waitForTimeout(1000);
  await page.screenshot({ path: `${OUT}/34-indkobsliste-1280x1024.png` });
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
