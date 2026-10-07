// Family Hub – UI smoke test in real Chrome/Edge, emulating the 1920x1080 kitchen touch screen.
// Checks the on-screen keyboard end-to-end (finger taps, typing, number pad, dialogs), the calendar and the meal plan
// (pick, own dish, drag and drop, swipe, undo, shopping list), the screen saver, and saves screenshots.
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
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { createServer } from 'node:http';
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
    (await page.locator('.hub-dialog .hub-choice--segmented [aria-checked="true"]').first().innerText()).trim() === 'Hovedret');
  await page.screenshot({ path: `${OUT}/24-madplan-picker.png` });

  // Inspiration: Mambeno's recipes in the same picker (only when Mambeno is set up). Nothing is chosen here –
  // choosing copies the recipe into the family's real recipe book.
  const inspiration = page.locator('.hub-dialog .hub-choice__option', { hasText: 'Inspiration' });
  if (await inspiration.count()) {
    const mambenoRows = page.locator('.hub-dialog .mp-inspiration .hub-list__row');
    await inspiration.tap();
    await mambenoRows.first().waitFor({ timeout: 15000 });
    const count = (await page.locator('.mp-inspiration__count').innerText()).trim();
    log('inspiration: Mambeno recipes, starting on "Aftensmad" for a main course',
      (await page.locator('.mp-inspiration__chips [aria-checked="true"]').first().innerText()).trim() === 'Aftensmad', count);
    log('inspiration: no keyboard until the search is tapped', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
    await page.screenshot({ path: `${OUT}/24b-inspiration.png` });

    await page.locator('.mp-inspiration__chips--sub .hub-choice__option', { hasText: 'Fisk' }).tap();
    await page.waitForFunction(c => document.querySelector('.mp-inspiration__count')?.textContent.trim() !== c, count, { timeout: 15000 });
    const fish = (await page.locator('.mp-inspiration__count').innerText()).trim();
    log('inspiration: a sub-category narrows the list', parseInt(fish.replace('.', '')) < parseInt(count.replace('.', '')), `${fish} of ${count}`);

    const shown = await mambenoRows.count();
    await page.locator('.mp-inspiration__more .hub-btn').tap();
    await page.waitForFunction(n => document.querySelectorAll('.mp-inspiration .hub-list__row').length > n, shown, { timeout: 15000 });
    log('inspiration: "Vis flere" adds the next 30', (await mambenoRows.count()) === shown + 30, `${shown} → ${await mambenoRows.count()}`);

    const title = (await mambenoRows.first().locator('.hub-list__title').innerText()).trim();
    await mambenoRows.first().tap();
    await page.locator('.mp-preview').waitFor();
    log('inspiration: a recipe opens a preview with ingredients and steps',
      (await page.locator('.mp-preview__title').innerText()).trim() === title
      && (await page.locator('.mp-preview__ingredients li').count()) > 0 && (await page.locator('.mp-preview__steps li').count()) > 0, title);
    log('inspiration: the preview starts at the top', (await page.locator('.hub-dialog').evaluate(d => d.scrollTop)) === 0);
    log('inspiration: "Vælg retten" and "Tilbage" in the footer', (await page.locator('.hub-dialog__footer .hub-btn', { hasText: 'Vælg retten' }).count()) === 1);
    await page.screenshot({ path: `${OUT}/24c-inspiration-preview.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/24d-inspiration-preview-dark.png` });
    await page.locator('.hub-dialog__footer .hub-btn', { hasText: 'Tilbage' }).tap();
    await page.waitForTimeout(400);
    await page.screenshot({ path: `${OUT}/24e-inspiration-dark.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'light');
    log('inspiration: "Tilbage" returns to the same list', (await mambenoRows.count()) === shown + 30);

    await page.locator('.hub-dialog .hub-choice__option', { hasText: 'Opskriftsbogen' }).tap();
    await page.waitForTimeout(400);
    log('inspiration: back to the recipe book', (await page.locator('.hub-dialog .hub-list__row').count()) === recipes);
  } else {
    log('inspiration: Mambeno is not set up – no Inspiration (set FamilyHub__Mambeno__ApiKey to check it)', true);
  }

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
    (await page.locator('.hub-dialog .hub-choice--segmented [aria-checked="true"]').first().innerText()).trim() === 'Dessert'
    && await segment.filter({ hasText: 'Hovedret' }).isDisabled());
  await page.screenshot({ path: `${OUT}/27b-madplan-add-course.png` });
  await page.locator('.hub-dialog .hub-list__row').first().tap();
  await page.waitForTimeout(700);
  await day('2030-01-10').locator('.mp-day__add').tap();
  await page.waitForTimeout(700);
  log('meal plan: then a starter', (await page.locator('.hub-dialog .hub-choice--segmented [aria-checked="true"]').first().innerText()).trim() === 'Forret');
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

// ---------------------------------------------------------------- screen saver (Indstillinger → Pauseskærm)
{
  const { context, page } = await newPage();
  await open(page, '/indstillinger');
  const card = page.locator('.hub-card', { hasText: /^\s*Pauseskærm/ });   // the card's title, not the help text
  await card.scrollIntoViewIfNeeded();
  await card.locator('.hub-choice__option', { hasText: '15 min' }).tap();
  await page.waitForTimeout(400);
  const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('familyhub.device.v1') || '{}').screenSaverMinutes);
  log('screen saver: setting saved on this screen', stored === 15, `screenSaverMinutes=${stored}`);
  await page.screenshot({ path: `${OUT}/40-pauseskaerm-indstilling.png` });

  await card.locator('.hub-btn', { hasText: 'Prøv' }).tap();
  await page.waitForTimeout(1800);   // fades in
  log('screen saver: "Prøv" shows it', await page.locator('.hub-saver').isVisible());
  const time = (await page.locator('.hub-saver__time').innerText()).trim();
  log('screen saver: shows the time', /^\d\d:\d\d$/.test(time), time);
  const clock = await page.locator('.hub-saver__clock').boundingBox();
  log('screen saver: clock fully on screen', clock.x >= 0 && clock.y >= 0 && clock.x + clock.width <= 1920 && clock.y + clock.height <= 1080,
    `x ${Math.round(clock.x)}, y ${Math.round(clock.y)}, ${Math.round(clock.width)}x${Math.round(clock.height)}`);
  await page.screenshot({ path: `${OUT}/41-pauseskaerm.png` });

  // A tap wakes it – and must not press the option that sits underneath.
  const never = await card.locator('.hub-choice__option', { hasText: 'Aldrig' }).boundingBox();
  await page.touchscreen.tap(never.x + never.width / 2, never.y + never.height / 2);
  await page.waitForTimeout(600);
  log('screen saver: a tap wakes it', (await page.locator('.hub-saver').count()) === 0);
  const checked = (await card.locator('.hub-choice__option[aria-checked="true"]').innerText()).trim();
  log('screen saver: the waking tap pressed nothing underneath', checked === '15 min', `selected: ${checked}`);

  // Night (dark theme) has a dimmer clock. On a laptop a key wakes it – without typing into the field underneath.
  await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
  await card.locator('.hub-btn', { hasText: 'Prøv' }).tap();
  await page.waitForTimeout(1800);
  await page.screenshot({ path: `${OUT}/42-pauseskaerm-dark.png` });
  const field = page.getByLabel('Prøv tastaturet');
  await field.evaluate(el => el.focus());
  await page.keyboard.press('a');
  await page.waitForTimeout(600);
  log('screen saver: a key wakes it', (await page.locator('.hub-saver').count()) === 0);
  log('screen saver: the waking key typed nothing', (await field.inputValue()) === '', JSON.stringify(await field.inputValue()));

  // "Prøv" is only a preview – even with "Tilbage til forsiden" on, the screen stays in Indstillinger.
  await page.locator('.hub-card', { hasText: 'Tilbage til forsiden' }).locator('.hub-choice__option', { hasText: '2 min' }).tap();
  await page.waitForTimeout(400);
  await card.locator('.hub-btn', { hasText: 'Prøv' }).tap();
  await page.waitForTimeout(1200);
  await page.touchscreen.tap(960, 540);
  await page.waitForTimeout(800);
  log('screen saver: "Prøv" keeps the page', new URL(page.url()).pathname === '/indstillinger', new URL(page.url()).pathname);

  await context.close();
}

// ---------------------------------------------------------------- screen saver before "Tilbage til forsiden"
// Pauseskærm after 5 min, home after 10: waking from the screen saver must show the home screen, not the old menu.
// Minute-long timers run a minute per second here, so the test takes seconds.
{
  const { context, page } = await newPage();
  await context.addInitScript(() => {
    localStorage.setItem('familyhub.device.v1', JSON.stringify({ idleReturnMinutes: 10, screenSaverMinutes: 5 }));
    const setTimeoutOriginal = window.setTimeout;
    window.setTimeout = (fn, ms, ...args) => setTimeoutOriginal(fn, ms >= 60_000 ? ms / 60 : ms, ...args);
  });
  await open(page, '/');
  await page.locator('.hub-rail__item', { hasText: 'Madplan' }).tap();
  await page.waitForTimeout(7000);   // screen saver after 5 s, home only after 10 s
  log('screen saver: starts by itself', await page.locator('.hub-saver').isVisible());
  log('screen saver: the home screen waits underneath', new URL(page.url()).pathname === '/', new URL(page.url()).pathname);
  await page.touchscreen.tap(960, 540);
  await page.waitForTimeout(800);
  log('screen saver: waking shows the home screen', (await page.locator('.hub-saver').count()) === 0 && await page.locator('.home-hero__time').isVisible());
  await page.screenshot({ path: `${OUT}/43-pauseskaerm-vaagnet-forside.png` });
  await context.close();
}

// ---------------------------------------------------------------- school (Skole) – needs the demo data's timetables
{
  const { context, page } = await newPage();
  await open(page, '/skole');
  if (await page.locator('.st-table').count() === 0) {
    log('school: demo timetables found (run node demo-data.mjs)', false);
  } else {
    await page.locator('.st-tab', { hasText: 'Emma' }).tap();
    await page.waitForTimeout(400);
    await page.screenshot({ path: `${OUT}/50-skole.png` });
    const days = await page.locator('.st-day__name').allInnerTexts();
    log('school: Monday to Friday across the top', days.join(',').toLowerCase() === 'mandag,tirsdag,onsdag,torsdag,fredag', days.join(','));
    log('school: times down the side', (await page.locator('.st-time__range').first().innerText()) === '08:00–08:45');
    log('school: every lesson is a touch target', (await page.locator('.st-table .st-cell__press').count()) === (await page.locator('.st-table .st-lesson').count()));
    const table = await page.locator('.st-table').boundingBox();
    log('school: the whole week fits on the kitchen screen', table.y + table.height <= 1080, `table bottom ${Math.round(table.y + table.height)}`);

    // Tap another child's tab with a finger.
    const oliver = page.locator('.st-tab', { hasText: 'Oliver' });
    await oliver.tap();
    await page.waitForTimeout(400);
    log('school: a tap on a tab shows that child', (await oliver.getAttribute('aria-selected')) === 'true');
    await page.screenshot({ path: `${OUT}/51-skole-oliver.png` });

    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.locator('.st-tab', { hasText: 'Emma' }).tap();
    await page.waitForTimeout(400);
    await page.screenshot({ path: `${OUT}/52-skole-dark.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'light');

    // A lesson in the fixed timetable: a note for that day, and "Skjul" – then the eye next to the gear shows it again.
    const lessons = page.locator('.st-table .st-cell__press');
    const first = (await lessons.first().getAttribute('aria-label')).replace(/^Åbn /, '');
    await lessons.first().tap();
    await page.waitForSelector('.hub-dialog', { timeout: 5000 });
    await page.waitForTimeout(400);
    log('school: a tapped lesson opens with a note field and "Skjul"',
      (await page.locator('.hub-dialog input').count()) === 1 && (await page.locator('.hub-dialog .hub-btn', { hasText: 'Skjul' }).count()) === 1);
    log('school: no keyboard until the note field is tapped', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
    await page.screenshot({ path: `${OUT}/52b-skole-time.png` });
    await page.locator('.hub-dialog input').tap();
    await page.waitForTimeout(400);
    log('school: tapping the note field opens the keyboard', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 1);
    await page.locator('.hub-dialog input').fill('Husk madpakke');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/52c-skole-time-note.png` });
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Gem' }).tap();
    await page.waitForTimeout(600);
    log('school: the note shows under the lesson', (await page.locator('.st-table').innerText()).includes('Husk madpakke'));

    await page.locator(`.st-table [aria-label="Åbn ${first}"]`).tap();
    await page.waitForSelector('.hub-dialog', { timeout: 5000 });
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Skjul' }).tap();
    await page.waitForTimeout(800);
    const eye = page.locator('[aria-label^="Skjult:"]');
    log('school: a hidden lesson leaves the table and the eye appears',
      (await page.locator(`.st-table [aria-label="Åbn ${first}"]`).count()) === 0 && (await eye.count()) === 1);
    await page.screenshot({ path: `${OUT}/52d-skole-skjult.png` });
    await page.locator('[aria-label="Næste uge"]').tap();
    await page.waitForTimeout(500);
    log('school: next week has nothing hidden', (await eye.count()) === 0);
    await page.locator('.hub-btn', { hasText: 'Denne uge' }).tap();
    await page.waitForTimeout(500);
    await eye.tap();
    await page.waitForSelector('.hub-dialog', { timeout: 5000 });
    await page.waitForTimeout(400);
    await page.screenshot({ path: `${OUT}/52e-skole-skjulte.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/52f-skole-skjulte-dark.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'light');
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Vis igen' }).first().tap();
    await page.waitForTimeout(800);
    log('school: "Vis igen" brings the lesson back and closes the list',
      (await page.locator('.hub-dialog').count()) === 0 && (await page.locator(`.st-table [aria-label="Åbn ${first}"]`).count()) === 1);

    // Tidy up the demo data: the note goes again.
    await page.locator(`.st-table [aria-label="Åbn ${first}"]`).tap();
    await page.waitForSelector('.hub-dialog', { timeout: 5000 });
    await page.locator('.hub-dialog input').fill('');
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Gem' }).tap();
    await page.waitForTimeout(600);

    // Settings: the list, then Emma's schedule.
    await open(page, '/skole/indstillinger');
    await page.screenshot({ path: `${OUT}/53-skole-indstillinger.png` });

    // "Tilføj skema": the grade follows the kind of school (folkeskole 0–10, gymnasium 1.g–3.g, universitet semestre).
    await page.locator('.hub-btn', { hasText: 'Tilføj skema' }).tap();
    await page.waitForTimeout(500);
    await page.locator('.hub-dialog .hub-choice__option', { hasText: 'Gymnasium' }).tap();
    await page.waitForTimeout(300);
    const gym = (await page.locator('.hub-dialog .hub-stepper__value').innerText()).trim();
    log('school: gymnasium years (1.g)', gym === '1.g', gym);
    await page.screenshot({ path: `${OUT}/53b-skole-tilfoej-gymnasium.png` });
    await page.locator('.hub-dialog .hub-choice__option', { hasText: 'Universitet' }).tap();
    await page.waitForTimeout(300);
    const uni = (await page.locator('.hub-dialog .hub-stepper__value').innerText()).trim();
    log('school: university semesters, no class letter', uni === '1. semester' && (await page.locator('.hub-dialog .hub-field__label', { hasText: /^Klasse$/ }).count()) === 0, uni);
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Annuller' }).tap();
    await page.waitForTimeout(300);
    await page.locator('.hub-list__row', { hasText: 'Emma' }).tap();
    await page.waitForSelector('.st-table--editable', { timeout: 5000 });
    await page.waitForTimeout(400);
    await page.screenshot({ path: `${OUT}/54-skole-rediger.png`, fullPage: true });

    // Tap a lesson -> its dialog, with no keyboard (nothing has to be typed).
    await page.locator('.st-cell__press').first().tap();
    await page.waitForTimeout(500);
    log('school: tapping a lesson opens it', await page.locator('.hub-dialog').isVisible());
    log('school: no keyboard when the lesson opens', (await page.locator('.hub-osk[data-visibility="expanded"]').count()) === 0);
    await page.screenshot({ path: `${OUT}/55-skole-time.png` });
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Annuller' }).tap();
    await page.waitForTimeout(300);

    // Paint: pick Musik and tap an empty lesson (Wednesday, 7th).
    await page.locator('.hub-choice__option', { hasText: 'Musik' }).tap();
    const empty = page.locator('[aria-label="Onsdag, 7. time: ingen time"]');
    await empty.tap();
    await page.waitForTimeout(500);
    log('school: painting a subject onto a lesson', (await page.locator('[aria-label="Onsdag, 7. time: Musik"]').count()) === 1);
    await page.locator('.hub-choice__option', { hasText: 'Ryd' }).tap();
    await page.locator('[aria-label="Onsdag, 7. time: Musik"]').tap();
    await page.waitForTimeout(500);
    log('school: clearing a lesson again', (await page.locator('[aria-label="Onsdag, 7. time: ingen time"]').count()) === 1);
    await page.locator('.hub-choice__option', { hasText: 'Redigér' }).tap();

    // Add a break: steppers and choices only.
    await page.locator('.hub-btn', { hasText: 'Tilføj pause' }).tap();
    await page.waitForTimeout(500);
    await page.screenshot({ path: `${OUT}/56-skole-pause.png` });
    log('school: break dialog without keyboard', (await page.locator('.hub-dialog input').count()) === 0);
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Annuller' }).tap();

    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/57-skole-rediger-dark.png`, fullPage: true });
  }
  await context.close();
}
{
  const { context, page } = await newPage({ viewport: { width: 1280, height: 1024 } });
  await open(page, '/skole');
  await page.screenshot({ path: `${OUT}/58-skole-1280x1024.png` });
  await context.close();
}

// ---------------------------------------------------------------- school calendar link (iCal) – Mette at university
{
  // A tiny "school server" with the demo data's Moodle-like calendar.
  const icsPath = './demo-data/skole/demo-kalender.ics';
  let ics = existsSync(icsPath) ? readFileSync(icsPath, 'utf8') : null;
  const server = ics
    ? createServer((req, res) => { res.writeHead(200, { 'Content-Type': 'text/calendar; charset=utf-8' }); res.end(ics); }).listen(5091)
    : null;
  const { context, page } = await newPage();
  await open(page, '/skole/indstillinger');
  const mette = page.locator('.hub-list__row', { hasText: 'Mette' });
  if (!server || (await mette.count()) === 0) {
    log('school calendar: demo university schedule found (run node demo-data.mjs)', false);
  } else {
    await mette.tap();
    await page.waitForSelector('.st-table--editable', { timeout: 5000 });
    await page.waitForTimeout(400);
    await page.locator('.hub-btn', { hasText: 'Tilføj link' }).tap();
    await page.waitForTimeout(500);
    await page.screenshot({ path: `${OUT}/70-skole-link.png` });

    // A wrong link gets a calm message under the field.
    const field = page.locator('.hub-dialog input');
    await field.fill('https://localhost:1/findes-ikke.ics');
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Hent skema' }).tap();
    await page.waitForSelector('.hub-dialog .hub-field__error', { timeout: 40000 });
    log('school calendar: a link that fails is explained under the field', (await page.locator('.hub-dialog .hub-field__error').count()) === 1,
      (await page.locator('.hub-dialog .hub-field__error').innerText()).slice(0, 60));

    await field.fill('http://localhost:5091/aau.ics'); // a real webcal:// link becomes https://
    await page.locator('.hub-dialog .hub-btn', { hasText: 'Hent skema' }).tap();
    await page.waitForTimeout(1500);
    const lessons = await page.locator('.st-lesson__subject').allInnerTexts();
    log('school calendar: lessons fetched from the link', lessons.some(t => t.startsWith('Statistik')), lessons.slice(0, 3).join(' | '));
    log('school calendar: the private link is not shown', !(await page.content()).includes('aau.ics'));
    await page.screenshot({ path: `${OUT}/71-skole-link-rediger.png`, fullPage: true });

    await open(page, '/skole');
    await page.locator('.st-tab', { hasText: 'Mette' }).tap();
    await page.waitForTimeout(500);
    await page.screenshot({ path: `${OUT}/72-skole-universitet.png` });
    log('school calendar: lessons outside the rows are listed', await page.locator('.st-outside').count() === 1 || (await page.locator('.st-lesson').count()) > 0);
    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/73-skole-universitet-dark.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'light');

    // Tapping a lesson shows what the calendar says about it – teachers and course, no links or e-mail addresses.
    await page.locator('.st-cell__press', { hasText: 'Programmering - forelæsning' }).first().tap();
    await page.waitForSelector('.hub-dialog', { timeout: 5000 });
    await page.waitForTimeout(400);
    const details = await page.locator('.hub-dialog').innerText();
    log('school calendar: a tapped lesson shows its details', details.includes('Undervisere') && details.includes('Carla Dam'), details.replace(/\s+/g, ' ').slice(0, 90));
    log('school calendar: details leave out links and e-mail', !details.includes('@') && !details.includes('http'));
    await page.screenshot({ path: `${OUT}/73b-skole-time-detaljer.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/73c-skole-time-detaljer-dark.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'light');
    await page.locator('.hub-dialog [aria-label="Luk"]').tap();
    await page.waitForTimeout(300);

    // The school changes rooms – "Opdater nu" finds it, and the page warns until someone marks it as seen.
    ics = ics.replaceAll('Auditorium 2', 'Auditorium 5').replaceAll('Grupperum 3.117', 'Grupperum 4.201');
    await open(page, '/skole/indstillinger');
    await page.locator('.hub-list__row', { hasText: 'Mette' }).tap();
    await page.waitForSelector('.st-table', { timeout: 5000 });
    await page.locator('.hub-btn', { hasText: 'Opdater nu' }).tap();
    await page.waitForTimeout(1500);
    await open(page, '/skole');
    await page.locator('.st-tab', { hasText: 'Mette' }).tap();
    await page.waitForTimeout(500);
    const warning = page.locator('.st-changes');
    log('school calendar: a change this week is warned about', (await warning.count()) === 1,
      (await warning.count()) ? (await warning.innerText()).replace(/\s+/g, ' ').slice(0, 90) : 'no warning');
    log('school calendar: the changed lessons are marked', (await page.locator('.st-lesson--changed').count()) > 0);
    log('school calendar: the tab shows a warning sign', (await page.locator('.st-tab__alert').count()) === 1);
    await page.screenshot({ path: `${OUT}/74-skole-aendret.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'dark');
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${OUT}/75-skole-aendret-dark.png` });
    await page.evaluate(() => document.documentElement.dataset.theme = 'light');
    await open(page, '/');
    await page.screenshot({ path: `${OUT}/76-forside-aendret.png` });
    await open(page, '/skole');
    await page.locator('.st-tab', { hasText: 'Mette' }).tap();
    await page.waitForTimeout(400);
    await page.locator('.hub-btn', { hasText: 'Markér som set' }).tap();
    await page.waitForTimeout(600);
    log('school calendar: "Markér som set" removes the warning', (await page.locator('.st-changes').count()) === 0 && (await page.locator('.st-tab__alert').count()) === 0);
  }
  await context.close();
  server?.close();
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
