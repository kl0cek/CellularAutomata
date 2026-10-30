export const $ = id => document.getElementById(id);

// Wypełnia listę <ul> tekstami (np. komunikatami błędów); pusta tablica czyści listę.
export function fillList(listId, texts) {
  $(listId).replaceChildren(...texts.map(text => {
    const li = document.createElement('li');
    li.textContent = text;
    return li;
  }));
}
