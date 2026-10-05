// Inspiration (InspirationResults.razor): the category rows scroll sideways. When a category is chosen for the family
// (the course's own, e.g. "Kager, bagværk og sødt"), it may sit outside the visible part – scroll it into view.
export function revealSelected(root) {
  for (const row of root.querySelectorAll('.mp-inspiration__chips .hub-choice')) {
    const chip = row.querySelector('[aria-checked="true"]');
    if (!chip) {
      continue;
    }

    const left = chip.offsetLeft - row.offsetLeft;
    const right = left + chip.offsetWidth;
    if (left < row.scrollLeft || right > row.scrollLeft + row.clientWidth * 0.8) {
      row.scrollTo({ left: Math.max(0, left - row.clientWidth * 0.25), behavior: 'smooth' });
    }
  }
}
