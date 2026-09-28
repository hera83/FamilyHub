// Family Hub – writes a demo data folder: a family, calendars and a few weeks of appointments around today.
// Lets you see and test the calendar without Google keys (nothing is fetched – the app shows its local copy).
//
//   node demo-data.mjs [folder]          (default ./demo-data – replaced every time)
//   $env:FamilyHub__DataDirectory = (Resolve-Path ./demo-data); dotnet run --project ../../src/FamilyHub.Web
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const dir = resolve(process.argv[2] || './demo-data');
rmSync(dir, { recursive: true, force: true });
mkdirSync(join(dir, 'kalender'), { recursive: true });
const write = (file, data) => writeFileSync(join(dir, file), JSON.stringify(data, null, 2));

// ---------------------------------------------------------------- family
const members = [
  { id: '11111111-1111-4111-8111-111111111111', name: 'Anders', color: 'Sky' },
  { id: '22222222-2222-4222-8222-222222222222', name: 'Mette', color: 'Rose' },
  { id: '33333333-3333-4333-8333-333333333333', name: 'Emma', color: 'Sand' },
  { id: '44444444-4444-4444-8444-444444444444', name: 'Oliver', color: 'Teal' },
];
write('household.json', { name: 'Familien Demo', members });

// ---------------------------------------------------------------- Google account and calendars
const account = 'demo-account';
write('kalender/google-konti.json', {
  accounts: [{ id: account, email: 'anders@example.com', protectedRefreshToken: 'demo', connectedAt: new Date().toISOString(), needsReconnect: false }],
});

const cal = (id, name, extra = {}) => ({ id, accountId: account, name, canWrite: true, isPrimary: false, selectedInGoogle: true, ...extra });
const sources = [
  cal('anders@example.com', 'anders@example.com', { isPrimary: true }),
  cal('mette@example.com', 'Mette'),
  cal('emma@group.calendar.google.com', 'Emma'),
  cal('oliver@group.calendar.google.com', 'Oliver'),
  cal('familie@group.calendar.google.com', 'Familie'),
  cal('da.danish#holiday@group.v.calendar.google.com', 'Helligdage i Danmark', { canWrite: false }),
];
write('kalender/kalendere.json', {
  calendars: [
    { calendarId: 'anders@example.com', visible: true, memberId: members[0].id, color: 'Stone' },
    { calendarId: 'mette@example.com', visible: true, memberId: members[1].id, color: 'Stone' },
    { calendarId: 'emma@group.calendar.google.com', visible: true, memberId: members[2].id, color: 'Stone' },
    { calendarId: 'oliver@group.calendar.google.com', visible: true, memberId: members[3].id, color: 'Stone' },
    { calendarId: 'familie@group.calendar.google.com', visible: true, memberId: null, color: 'Sage' },
    { calendarId: 'da.danish#holiday@group.v.calendar.google.com', visible: true, memberId: null, color: 'Stone' },
  ],
});

// ---------------------------------------------------------------- events (local time, Copenhagen)
const pad = n => String(n).padStart(2, '0');
const today = new Date();
today.setHours(0, 0, 0, 0);
const monday = new Date(today);
monday.setDate(today.getDate() - ((today.getDay() + 6) % 7));
const day = offset => { const d = new Date(monday); d.setDate(monday.getDate() + offset); return d; };
const ymd = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const offsetOf = d => { const m = -d.getTimezoneOffset(); return `${m >= 0 ? '+' : '-'}${pad(Math.floor(Math.abs(m) / 60))}:${pad(Math.abs(m) % 60)}`; };
const at = (d, time) => { const [h, m] = time.split(':').map(Number); const x = new Date(d); x.setHours(h, m, 0, 0); return `${ymd(x)}T${pad(h)}:${pad(m)}:00${offsetOf(x)}`; };

let n = 0;
const events = [];
const timed = (calendarId, offset, from, to, title, location = null, description = null) =>
  events.push({ id: `demo${++n}`, calendarId, title, start: at(day(offset), from), end: at(day(offset), to), isAllDay: false, location, description });
const allDay = (calendarId, offset, days, title) =>
  events.push({ id: `demo${++n}`, calendarId, title, start: at(day(offset), '00:00'), end: at(day(offset + days), '00:00'), isAllDay: true, location: null, description: null });

const [anders, mette, emma, oliver, familie, holidays] = sources.map(s => s.id);
for (const week of [-3, -2, -1, 0, 1, 2, 3, 4]) {
  const w = week * 7;
  timed(emma, w + 0, '16:00', '17:30', 'Fodbold', 'Kunstgræsbanen');
  timed(emma, w + 3, '16:00', '17:30', 'Fodbold', 'Kunstgræsbanen');
  timed(oliver, w + 1, '15:30', '16:30', 'Svømning', 'Svømmehallen');
  timed(oliver, w + 4, '14:00', '15:00', 'Klaver');
  timed(mette, w + 2, '17:30', '18:30', 'Yoga');
  timed(anders, w + 0, '07:00', '07:45', 'Løbetur');
  timed(familie, w + 6, '12:00', '13:30', 'Søndagsfrokost');
}

timed(anders, 0, '09:00', '10:00', 'Tandlæge', 'Tandklinikken, Hovedgaden 12', 'Husk sygesikringskortet.\nParkering bag klinikken.');
timed(mette, 0, '09:30', '11:00', 'Møde med skolen', 'Skolen');
timed(emma, 1, '08:00', '14:00', 'Skoleudflugt', 'Zoologisk Have');
timed(mette, 1, '18:00', '21:00', 'Pigeaften');
timed(anders, 2, '12:00', '13:00', 'Frokost med Jens');
timed(anders, 2, '12:30', '14:00', 'Budgetmøde');
timed(oliver, 2, '16:00', '18:00', 'Legeaftale hos Noah');
timed(familie, 3, '18:00', '19:00', 'Forældremøde 3.B', 'Skolen, lokale 12');
timed(anders, 4, '16:30', '17:00', 'Hent pakke');
timed(familie, 4, '18:30', '22:00', 'Middag hos farmor og farfar');
allDay(familie, 5, 2, 'Weekend i sommerhuset');
timed(emma, 5, '10:00', '11:00', 'Fodboldkamp', 'Stadion');
timed(oliver, 5, '13:00', '15:00', 'Børnefødselsdag – Ella');
timed(mette, 6, '10:00', '11:00', 'Løb med Sofie');
allDay(holidays, 9, 1, 'Mærkedag');
allDay(emma, 14, 5, 'Efterårsferie');
allDay(oliver, 14, 5, 'Efterårsferie');
timed(familie, 16, '10:00', '16:00', 'Tur til Legoland');
timed(mette, 8, '08:00', '16:00', 'Kursus', 'Odense');
timed(anders, 10, '19:00', '21:30', 'Bestyrelsesmøde');
allDay(anders, 11, 3, 'Konference i Aarhus');

const windowStart = new Date(today.getFullYear(), today.getMonth() - 2, 1);
const windowEnd = new Date(today.getFullYear(), today.getMonth() + 13, 1);
write('kalender/cache.json', {
  windowStart: ymd(windowStart),
  windowEnd: ymd(windowEnd),
  lastSuccess: new Date().toISOString(),
  sources,
  events,
});

console.log(`Demo data written to ${dir} (${events.length} appointments).`);
console.log(`Start the app with:  $env:FamilyHub__DataDirectory = "${dir}"; dotnet run --project src/FamilyHub.Web`);
