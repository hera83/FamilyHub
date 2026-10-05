// Recipe picker (RecipePickerDialog.razor): the dialog scrolls as a whole. Opening a Mambeno preview starts at the top,
// and "Tilbage" returns to where the family was in the list. Returns the position it replaced.
export function scrollDialogTo(element, top) {
  const dialog = element.closest('.hub-dialog');
  if (!dialog) {
    return 0;
  }

  const previous = dialog.scrollTop;
  dialog.scrollTop = top;
  return previous;
}
