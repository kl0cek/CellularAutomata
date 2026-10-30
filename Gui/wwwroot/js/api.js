// Wywołania API serwera GUI (Program.cs).

async function fetchJson(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: ${response.status}`);
  return response.json();
}

export const getHealth = () => fetchJson('/api/health');
export const listRuns = () => fetchJson('/api/runs');
export const getRun = id => fetchJson(`/api/runs/${id}`);
export const getResults = () => fetchJson('/api/results');

// Wyniki uruchomienia: mikrostruktura (format binarny z MicroReader) i StepTimes.txt.
export async function getRunData(id) {
  const [micro, steps] = await Promise.all([fetch(`/api/runs/${id}/micro`), fetch(`/api/runs/${id}/steps`)]);
  return {
    micro: micro.ok ? await micro.arrayBuffer() : null,
    stepsText: steps.ok ? await steps.text() : '',
  };
}

// POST z JSON-em. Zwraca { ok, status, body }; przy braku połączenia rzuca wyjątek.
export async function postJson(url, data) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  });
  const body = await response.json().catch(() => null);
  return { ok: response.ok, status: response.status, body };
}

// Lista błędów z odpowiedzi serwera (walidacja w RunRequest / SeriesRequest).
export const errorsOf = (result, fallback) =>
  Array.isArray(result.body?.errors) ? result.body.errors : [`${fallback} (kod ${result.status}).`];
