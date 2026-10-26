# Lab 4 – Profil aplikacji, zapotrzebowanie na zasoby i testy wydajności

Aplikacja generuje mikrostrukturę materiału w 3D dwoma algorytmami: naiwnym rozrostem ziaren CA (Lab 2)
i naiwnym rozrostem ziaren Monte Carlo (Lab 3). Raport opisuje, gdzie aplikacja spędza czas, ile zużywa
zasobów i jakiego sprzętu potrzebuje.

**Najważniejsze wnioski**

- 73% czasu CA i 89% czasu MC zajmuje sam algorytm, a w nim najdroższe jest szukanie sąsiadów komórki.
- Aplikacja jest **jednowątkowa** – pracuje 1 z 8 rdzeni (użycie CPU 12,5%).
- Pamięć rośnie liniowo: ok. 60 MB samego środowiska .NET + ok. 8–9 B na komórkę siatki.
- Plik wynikowy to ok. 13 B na komórkę (100³ → 11,6 MB, 200³ → 106 MB).
- Przejście z .NET 7 na .NET 10 przyspieszyło CA o 37% i MC o 14% bez zmiany kodu.

## 1. Środowisko i metoda

| | |
|---|---|
| Komputer | Mac z procesorem Apple M2 (8 rdzeni: 4 wydajne + 4 energooszczędne), 16 GB RAM |
| System | macOS 27.0.1 |
| Środowisko | .NET 10 (SDK 10.0.403, runtime 10.0.14), build **Release** |
| Narzędzia | `dotnet-trace` 10.0 (profil CPU, podgląd w speedscope.app), `dotnet-counters` 10.0 (CPU, pamięć, GC), `/usr/bin/time -l` (szczytowa pamięć procesu), wbudowany pomiar czasu (`Stopwatch`, pliki `Time.txt` i `StepTimes*.txt`) |
| Parametry | sąsiedztwo Moore'a (26 sąsiadów), brzegi periodyczne; CA: 100 zarodków; MC: Q = 50 stanów, 20 kroków MCS, losowanie z całego zbioru (`Random`) |

Pliki profilu leżą w `OutputTXT/profile/` (`ca-net10.speedscope.json`, `mc-net10.speedscope.json`,
`mc.speedscope.json` = MC na .NET 7, `mc-counters.csv`).

## 2. Profil aplikacji – gdzie idzie czas

Siatka 100 × 100 × 100, .NET 10, udział w całym czasie działania programu:

| Część programu | CA (1,08 s) | MC (3,32 s) |
|---|---|---|
| `Generate` – sam algorytm | **73%** | **89%** |
| `WriteMorphology` – zapis pliku tekstowego | 10% | 3% |
| `CountGrains` – liczenie ziaren po symulacji | 9% | 5% |
| `SimulationConfig.Load` – wczytanie JSON-a | 3% | 1% |

.NET 10 wkleja małe funkcje w miejsce wywołania (inlining), więc w jego profilu ich czas jest wliczony
w `Generate`. Rozbicie wnętrza MC widać w profilu z .NET 7:

| Funkcja (MC, .NET 7) | Udział |
|---|---|
| `GetNeighbours` – zbieranie orientacji sąsiadów | **35%** |
| `TotalEnergy` – energia początkowa | 5% |
| `Shuffle` – losowa kolejność komórek | 5% |
| `Random.Next` – losowanie liczb | 5% |

**Czas jednego kroku CA** zależy od liczby pustych komórek: pierwsze kroki trwają ok. 78 ms (prawie wszystkie
komórki puste, każda sprawdza 26 sąsiadów), ostatnie ok. 1–2 ms.

## 3. Zapotrzebowanie na zasoby

### CPU

Aplikacja liczy na **jednym wątku**:

- profiler pokazuje jeden aktywny wątek, który 98% swojego czasu spędza na procesorze,
- `dotnet-counters` pokazuje maksymalnie 12,6% użycia CPU, czyli 1/8 procesora z 8 rdzeniami (pomiar na .NET 7, kod się nie zmienił),
- w kodzie nie ma `Parallel`, `Task` ani `Thread`.

Liczba rdzeni nie wpływa więc na czas – liczy się wydajność pojedynczego rdzenia.

### Pamięć

Teoretycznie (tablice `int`, 4 B na element):

| Tablice | Na komórkę |
|---|---|
| CA: `cells` + `next` | 8 B |
| MC: `cells` + `order` | 8 B |
| liczenie ziaren: `visited` (`bool`) | +1 B |

Zmierzona szczytowa pamięć procesu (`/usr/bin/time -l`):

| Siatka | Komórki | CA | MC |
|---|---|---|---|
| 50³ | 125 tys. | 62 MB | 62 MB |
| 100³ | 1 mln | 70 MB | 69 MB |
| 150³ | 3,4 mln | 92 MB | 87 MB |
| 200³ | 8 mln | 116 MB | – |

Ok. 60 MB to stały koszt środowiska .NET, reszta rośnie z liczbą komórek. Sterta GC przy 100³ (MC, .NET 7) wynosiła
ok. 9 MB, co zgadza się z teorią (2 × 4 B × 1 mln = 8 MB). W pętli symulacji nie powstają nowe obiekty,
więc odśmiecanie pamięci (GC) nie spowalnia programu.

### Dysk

| Siatka | `Micro.txt` |
|---|---|
| 50³ | 1,4 MB |
| 100³ | 11,6 MB |
| 150³ | 42,9 MB |
| 200³ | 106 MB |

Ok. 13 B na komórkę (linia tekstu `x y z id`).

## 4. Testy wydajności

### Rozmiar siatki

| Siatka | CA: kroki | CA: symulacja | MC (20 MCS): symulacja | Zapis pliku |
|---|---|---|---|---|
| 50³ | 11 | 0,04 s | 0,30 s | 0,02 s |
| 100³ | 22 | 0,51 s | 2,26 s | 0,08 s |
| 150³ | 34 | 2,56 s | 7,97 s | 0,22–0,28 s |
| 200³ | 41 | 7,88 s | – | 0,51 s |

- **MC rośnie liniowo** z liczbą komórek: 8 × więcej komórek (50³ → 100³) daje 7,5 × dłuższy czas.
- **CA rośnie szybciej niż liniowo**, bo liczba zarodków była stała (100): na większej siatce ziarna są większe
  i potrzeba więcej kroków (11 → 41). Czas na jedną komórkę na krok jest podobny.
- Zapis pliku rośnie liniowo z liczbą komórek.

### Wektor `int[]` a macierz `int[,,]` (CA, średnia z 5 uruchomień)

| Siatka | Wektor | Macierz | Macierz / wektor |
|---|---|---|---|
| 50³ | 34,8 ms | 42,2 ms | 1,21 |
| 100³ | 276,7 ms | 350,4 ms | 1,27 |

Obie wersje dają identyczną mikrostrukturę. Wektor jest szybszy, bo .NET lepiej optymalizuje tablice
jednowymiarowe (mniej obliczeń indeksu i sprawdzania zakresów).

### .NET 7 a .NET 10 (ten sam kod, 100³, średnia z 3 uruchomień)

| | .NET 7 | .NET 10 | Zmiana |
|---|---|---|---|
| CA – symulacja | 799 ms | 505 ms | −37% |
| MC – symulacja | 2630 ms | 2250 ms | −14% |

Wyniki symulacji są w obu wersjach identyczne co do bitu.

## 5. Wymagania sprzętowe

Na podstawie pomiarów. Wiersze 300³ i 500³ to **szacunki** z liniowego wzrostu (MC, 20 kroków MCS).

| Siatka | RAM | Dysk na wynik | Czas MC (M2) | Zalecenia |
|---|---|---|---|---|
| do 100³ | ok. 100 MB | do 12 MB | do 3 s | dowolny komputer |
| 200³ | ok. 120 MB | ok. 110 MB | ok. 18 s (szac.) | dowolny komputer |
| 300³ | ok. 300 MB | ok. 360 MB | ok. 1 min (szac.) | 4 GB RAM |
| 500³ | ok. 1,2 GB | ok. 1,7 GB | ok. 5 min (szac.) | 8 GB RAM, szybki dysk |

- **Procesor:** liczy się wydajność jednego rdzenia; więcej rdzeni nie przyspiesza obecnej wersji.
- **Oprogramowanie:** .NET 10 Runtime (macOS, Windows lub Linux).
- **Limit:** jedna tablica w .NET może mieć ok. 2,1 mld elementów, czyli siatkę do ok. 1290³.

## 6. Wąskie gardła i propozycje optymalizacji

| Wąskie gardło | Propozycja | Spodziewany efekt |
|---|---|---|
| Szukanie sąsiadów (3 × `ApplyBoundary`, modulo i mnożenia dla każdego z 26 sąsiadów) | dla komórek z dala od brzegu: sąsiad = `i + przesunięcie`, bez warunków; albo dodatkowa warstwa komórek wokół siatki | duży – to najdroższa część |
| Jeden rdzeń z ośmiu | CA: liczenie równoległe w plastrach osi Z (`SplitInZdim`); MC: podział „szachownicy” na 8 grup komórek, które się nie stykają | kilka razy szybciej |
| CA sprawdza w każdym kroku wszystkie komórki | lista frontu: tylko puste komórki stykające się z ziarnem | duży w pierwszych krokach |
| Zapis tekstowy (10% w CA) | zapis binarny | ok. 100 ms → kilkanaście ms; plik ok. 3 × mniejszy |
| `int` na orientację i ID | `ushort` (do 65 535 ziaren) | o połowę mniej pamięci, lepsze wykorzystanie pamięci podręcznej |
| Wolniejszy generator `Random` z seedem (MC) | własny szybki generator z seedem (np. xorshift) | kilka % szybciej, powtarzalność zostaje |
| Liczenie ziaren po każdej symulacji | tylko na żądanie | −5 do −9% całego czasu |

Najtańsza optymalizacja została już wykonana: aktualizacja .NET 7 → .NET 10.

## 7. Jak powtórzyć pomiary

```bash
dotnet build -c Release

# profil CPU (plik do otwarcia w speedscope.app)
dotnet-trace collect --format speedscope -o OutputTXT/profile/ca.nettrace -- dotnet bin/Release/net10.0/CellularAutomata.dll config.json

# CPU, pamięć i GC co sekundę do CSV
dotnet-counters collect --format csv -o OutputTXT/profile/counters.csv -- dotnet bin/Release/net10.0/CellularAutomata.dll config-mc.json

# szczytowa pamięć procesu
/usr/bin/time -l dotnet bin/Release/net10.0/CellularAutomata.dll config.json

# wektor vs macierz
dotnet run -c Release -- bench
```
