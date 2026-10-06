// Family Hub – writes a demo data folder: a family, calendars and a few weeks of appointments around today,
// school timetables (Emma 6.a, Oliver 0.b, Mette at university) and a Moodle-like calendar file for Mette.
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

// ---------------------------------------------------------------- school timetables (Skole) – Emma 6.a and Oliver 0.b
mkdirSync(join(dir, 'skole'), { recursive: true });
const uuid = () => crypto.randomUUID();
const period = (start, end, label = null) => ({ id: uuid(), kind: label ? 'Break' : 'Lesson', start: `${start}:00`, end: `${end}:00`, label });
const subjects = list => Object.fromEntries(list.map(([name, color]) => [name, { id: uuid(), name, color }]));
const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'];
// rows: one array per lesson, one entry per weekday – a subject name, [name, note], ['odd', 'even'] (alternating) or null.
const schedule = (memberId, grade, classLetter, subjectMap, periods, rows) => {
  const lessons = periods.filter(p => p.kind === 'Lesson');
  const cells = [];
  rows.forEach((row, i) => row.forEach((entry, d) => {
    if (!entry) return;
    const base = { day: weekdays[d], periodId: lessons[i].id };
    if (Array.isArray(entry) && entry.length === 2 && subjectMap[entry[1]]) {
      cells.push({ ...base, subjectId: subjectMap[entry[0]].id, alternatesWeekly: true, evenWeekSubjectId: subjectMap[entry[1]].id });
    } else if (Array.isArray(entry)) {
      cells.push({ ...base, subjectId: subjectMap[entry[0]].id, note: entry[1] });
    } else {
      cells.push({ ...base, subjectId: subjectMap[entry].id });
    }
  }));
  return { id: uuid(), memberId, grade, classLetter, subjects: Object.values(subjectMap), periods, cells };
};

const sixth = subjects([
  ['Dansk', 'Terracotta'], ['Matematik', 'Sky'], ['Engelsk', 'Rose'], ['Tysk', 'Sand'], ['Natur/teknologi', 'Sage'],
  ['Kristendom', 'Plum'], ['Historie', 'Stone'], ['Idræt', 'Teal'], ['Musik', 'Plum'], ['Billedkunst', 'Rose'],
  ['Håndværk og design', 'Sand'], ['Madkundskab', 'Terracotta'],
]);
const emmaSchedule = schedule(members[2].id, 6, 'A', sixth, [
  period('08:00', '08:45'), period('08:45', '09:30'), period('09:30', '09:50', 'Frikvarter'),
  period('09:50', '10:35'), period('10:35', '11:20'), period('11:20', '11:50', 'Spisepause'),
  period('11:50', '12:35'), period('12:35', '13:20'), period('13:20', '13:30', 'Frikvarter'), period('13:30', '14:15'),
], [
  [['Dansk', 'Lokale 64'], 'Dansk', 'Dansk', 'Dansk', 'Natur/teknologi'],
  ['Dansk', 'Kristendom', 'Dansk', 'Dansk', 'Natur/teknologi'],
  ['Matematik', 'Matematik', 'Engelsk', 'Matematik', ['Billedkunst', 'Tysk']],
  ['Matematik', 'Matematik', 'Engelsk', 'Engelsk', ['Billedkunst', 'Tysk']],
  ['Engelsk', 'Tysk', ['Madkundskab', 'Køkkenet'], 'Kristendom', 'Historie'],
  [['Idræt', 'Idrætshal A'], 'Håndværk og design', ['Madkundskab', 'Køkkenet'], 'Musik', 'Historie'],
  [['Idræt', 'Idrætshal A'], 'Håndværk og design', null, null, null],
]);

const zero = subjects([['Bh. klasse', 'Sage'], ['Morgensang', 'Sand'], ['Bibliotek', 'Sky'], ['Idræt', 'Teal']]);
const oliverSchedule = schedule(members[3].id, 0, 'B', zero, [
  period('08:00', '08:45'), period('08:45', '09:10'), period('09:10', '09:30', 'Formiddagsmad'),
  period('09:30', '09:55', 'Pause'), period('09:55', '10:40'), period('10:40', '11:25'),
], [
  ['Bh. klasse', 'Bh. klasse', 'Morgensang', 'Bh. klasse', 'Morgensang'],
  ['Bh. klasse', 'Bh. klasse', 'Bibliotek', 'Bh. klasse', 'Bh. klasse'],
  ['Bh. klasse', 'Idræt', 'Bh. klasse', 'Bh. klasse', 'Bh. klasse'],
  ['Bh. klasse', 'Idræt', 'Bh. klasse', 'Bh. klasse', 'Bh. klasse'],
]);
// Mette studies at university: her schedule starts empty – smoke.mjs links it to the calendar below (like Moodle's export).
const uniSchedule = {
  id: uuid(), memberId: members[1].id, level: 'University', grade: 2, classLetter: null, subjects: [], cells: [],
  periods: [period('08:15', '10:00'), period('10:00', '10:15', 'Pause'), period('10:15', '12:00'), period('12:00', '12:45', 'Frokost'),
            period('12:45', '14:30'), period('14:30', '14:45', 'Pause'), period('14:45', '16:30')],
};
write('skole/skemaer.json', { schedules: [emmaSchedule, oliverSchedule, uniSchedule] });

// An iCal file like Moodle's, with this week's and next week's lectures – served by smoke.mjs on http://localhost:5091/aau.ics.
const icsTime = (offset, time) => { const d = day(offset); return `${ymd(d).replaceAll('-', '')}T${time.replace(':', '')}00`; };
// Moodle's description: what the lesson is about, COURSE, TEACHER and a list of links – folded at 75 characters with a tab.
const teachers = {
  statistik: ['Anne Hansen (ah@math.example)'],
  programmering: ['Bo Berg (bb@cs.example)', 'Carla Dam (cd@cs.example)'],
  'lineær algebra': ['Dorte Kjær (dk@math.example)'],
};
const fold = line => line.length <= 75 ? line : line.slice(0, 75) + '\r\n\t' + fold(line.slice(75));
const description = (n, title) => {
  const course = title.split(/ - | \(/)[0];
  const who = teachers[course.toLowerCase()];
  if (!who) return null;
  const text = [`${title} (Hold X)`, '', 'COURSE', `2. semester / ${course} - E26 [1]`, '', 'TEACHER', ...who, '', ' ', '',
    'Links:', '------', `[1] https://moodle.example/local/planning/eventcourselink.php?c=${n}&amp;e=1`, ''].join('\n');
  const escaped = text.replaceAll('\\', '\\\\').replaceAll(';', '\\;').replaceAll(',', '\\,').replaceAll('\n', '\\n');
  return fold(`DESCRIPTION:${escaped}`) + '\r\n\t';
};
const lecture = (n, offset, from, to, title, place) => [
  'BEGIN:VEVENT', `UID:demo-${n}@moodle.example`, `SUMMARY:${title}`, description(n, title), place ?`LOCATION:${place.replaceAll(',', '\\,')}` : null,
  `DTSTART;TZID=Europe/Copenhagen:${icsTime(offset, from)}`, `DTEND;TZID=Europe/Copenhagen:${icsTime(offset, to)}`, 'END:VEVENT',
].filter(Boolean).join('\r\n');
let lectureNo = 0;
const lectures = [];
for (const w of [0, 7]) {
  lectures.push(lecture(++lectureNo, w + 0, '08:15', '12:00', 'Statistik - forelæsning', 'Fib 14, lokale 2.1'));
  lectures.push(lecture(++lectureNo, w + 1, '08:15', '12:00', 'Statistik - øvelser', 'Grupperum 3.117'));
  lectures.push(lecture(++lectureNo, w + 1, '12:45', '16:15', 'Programmering (PBL)', 'Selma Lagerløfs Vej 300'));
  lectures.push(lecture(++lectureNo, w + 2, '10:15', '12:00', 'Lineær algebra', 'Auditorium 1'));
  lectures.push(lecture(++lectureNo, w + 3, '08:15', '12:00', 'Programmering - forelæsning', 'Auditorium 2'));
  lectures.push(lecture(++lectureNo, w + 3, '17:00', '19:00', 'Studiegruppe', null));
  lectures.push(lecture(++lectureNo, w + 4, '08:15', '10:00', 'Lineær algebra - øvelser', 'Grupperum 3.117'));
}
const deadline = ['BEGIN:VEVENT', 'UID:demo-deadline@moodle.example', 'SUMMARY:Aflevering 2 skal afleveres',
  `DTSTART;TZID=Europe/Copenhagen:${icsTime(4, '23:59')}`, `DTEND;TZID=Europe/Copenhagen:${icsTime(4, '23:59')}`, 'END:VEVENT'].join('\r\n');
writeFileSync(join(dir, 'skole', 'demo-kalender.ics'),
  ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//Family Hub demo//Moodle-lignende//DA', ...lectures, deadline, 'END:VCALENDAR'].join('\r\n'));

console.log(`Demo data written to ${dir} (${events.length} appointments).`);
console.log(`Start the app with:  $env:FamilyHub__DataDirectory = "${dir}"; dotnet run --project src/FamilyHub.Web`);
