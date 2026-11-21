# Vyhodnocení signálů na reálných ES datech

## Data

Export z Footprint Exporteru:
- ES **1M** (13 801 svíček) a **5M** (2 761 svíček), celá ETH session.
- **11 obchodních session**, 6.–20. 11. 2026.
- Kontrola dat: součty z cenových úrovní přesně odpovídají objemu i deltě každé svíčky.
- Časy jsou v **UTC**. Otevření RTH ve 14:30, závěrečná aukce (MOC) ve 20:59, pauza 22:00–23:00.
- Režim trhu: **silný pokles** 7405 → 6966 (−5,9 %). Náhodný long v RTH ztrácel v průměru −1,9 bodu za 15 minut, přes noc ≈ 0.

## Metoda

- Signály počítá **skutečný kód indikátorů** (Delta Strength Bar, Wick Absorption) přes testovací harness (`Replay.cs`), ne přepis do Pythonu.
- Pohyb ceny close → close za 5, 15 a 30 minut **ve směru signálu**, vždy v rámci jedné session.
- **edge** = výsledek signálu minus náhodný vstup ve stejném směru a stejné části dne (RTH/ETH). Tím se odečte vliv celkového trendu.
- **MFE / MAE**: největší pohyb ve prospěch / proti pozici během 15 minut.
- **t15** je hrubé t-statistické číslo. Signály se časově překrývají a variant je víc, takže |t| < 2 znamená „neprokázáno“.

## Hlavní výsledky (1M, RTH, body ES)

| Signál | n | edge 15 min | edge 30 min | úspěšnost 15 min | MFE/MAE 15 min |
|---|---|---|---|---|---|
| **Cenový knot bez order flow**, dolní (long) | 99 | **−3,7** | **−8,9** | 38 % | 14,8 / 20,6 |
| Wick Absorption, dolní knot (long) | 79 | +0,9 | +0,5 | 46 % | – |
| Cenový knot bez order flow, horní (short) | 113 | +1,1 | +5,5 | 59 % | 18,1 / 15,3 |
| Wick Absorption, horní knot (short) | 88 | +2,4 | +2,1 | 56 % | – |
| ↳ z toho Bear Initiative (prodej na VPOC v horním knotu) | 49 | +2,6 | +1,6 | **67 %** | 13,8 / 8,6 |
| Delta Strength Bar • Bear setup | 59 | **+3,3** | +0,8 | 58 % | 13,2 / 8,2 |

## Co z toho plyne

1. **Filtr VPOC + delta odstraňuje špatné obchody.** Dolní knoty podle samotné ceny (kladiva proti trendu) prodělávaly −3,7 bodu oproti náhodnému vstupu za 15 minut a −8,9 bodu za 30 minut. Se stejnou cenovou logikou, ale s podmínkou VPOC v knotu a silné delty, je to ≈ 0 až +0,9. Ziskový signál z toho ale v padajícím trhu nevznikl.
2. **Nejslibnější jsou horní knoty se silnou deltou na VPOC na 1M v RTH.** Edge je +2,4 až +3,3 bodu za 15 minut a pohyb ve prospěch (MFE) je asi 1,6× větší než proti (MAE).
   - Je to ale ve směru trendu celého období.
   - Statisticky to prokázané není (t ≈ 1,2–1,6).
   - **Na 5M se to nepotvrdilo**, výsledky tam vycházejí opačně: n = 10–35, čistý šum.
3. **Kalibrace Delta Strength Baru je špatně, bez ohledu na edge:**
   - **E (vyčerpání)** svítí na **16 % všech svíček**, je to šum. Na 1M je 10 svíček zpět příliš krátké okno.
   - **A (pohlcení)** se z 97 % objevuje přes noc. Pevný práh 20 % delta/objem je v RTH událost 1 svíčky ze 100 (99. percentil RTH = 0,20), zatímco v noci 1 ze 4 (75. percentil ETH = 0,20). Práh musí být **relativní k části dne**.
   - **▲▼ rozjetí** se objevilo jen 23× za 11 session a výhodu nemělo. Vstup už na close knotové svíčky (•) vycházel lépe než čekání na potvrzení.
4. **Citlivost na prahy:** přísnější prahy u Wick Absorption zlepšily short stranu na 1M:
   - |delta| ≥ 200: +5,8 bodu, n = 32,
   - poměr ≥ 25 %: +5,4 bodu, n = 59, t = 1,9.
   - Na 5M ale vycházejí záporně. To je typický znak přeladění na malý vzorek.

**Celkově:** 11 session v jednom režimu trhu nestačí na prokázání výhody. Data ale jasně ukázala, co je ve výpočtu špatně (A, E, rozjetí), a že filtr VPOC + delta má smysl.

## Doporučené další kroky

1. **Víc dat:** 40–60 session, ideálně i s růstovým a bočním trhem. Exporter teď počítá obchodní session, ne kalendářní dny.
2. **Přepočítat A a E na adaptivní prahy:** percentil delta/objem a objemu zvlášť pro RTH a ETH, delší okno pro E.
3. **Rozjetí** buď zmírnit, nebo nahradit vstupem na close knotové svíčky.

## Optimalizace pro 5M RTH (40 session, 28. 9. – 20. 11. 2026)

**Data:**
- Export z vašeho 5M RTH grafu: 3239 svíček, 81 na session (09:30–16:15 New York).
- Říjen byl do strany až nahoru, listopad silný pokles.

**Metoda:**
- Session jsou rozdělené střídavě na **A** (liché) a **B** (sudé), takže obě poloviny obsahují oba měsíce.
- Prahy se ladily na A a ověřovaly na B. Kontroluje se i zvlášť září/říjen a listopad.
- Logika Wick Absorption je přepsaná do Pythonu (`wa_model.py`) a ověřená na **100 % shodu** s C# kódem. Pak se prošlo **720 kombinací** prahů (`opt5m_sweep.py`).

**Zjištění:**
1. **Klasické čtení absorpce na 5M nefunguje, výsledky mají opačné znaménko.**
   - Dolní knot se silným prodejem na VPOC (Bull Absorption): −2,7 bodu za 15 minut, úspěšnost 30 %, záporné v A, B, říjnu i listopadu. 68 % kombinací prahů je záporných v obou polovinách.
   - Horní knot se silným nákupem (Bear Absorption): 79 % kombinací záporných v obou polovinách.
   - **Strana, která byla agresivní na VPOC v knotu, měla většinou pravdu.**
2. **„Jdi se směrem delty na VPOC v knotu“** (`opt5m_follow.py`):

| Varianta | n | výhoda 15 min | A / B | září+říjen / listopad |
|---|---|---|---|---|
| zóna VPOC ±1 tick, \|delta\| ≥ 200, ≥ 15 % (**nové výchozí**) | 70 | **+1,8** | +3,0 / +0,6 | +0,9 / +2,6 |
| VPOC úroveň, \|delta\| ≥ 100 (staré prahy) | 138 | +1,2 | +1,0 / +1,3 | +0,4 / +1,8 |
| kontrola: jít s deltou celé svíčky (5 % nejsilnějších) | 148 | +0,7 | +0,4 / +0,9 | +0,8 / 0,0 |
| kontrola: jít s deltou VPOC libovolné svíčky | 494 | +0,4 | −0,1 / +0,7 | 0,0 / +0,7 |

   Podmínka „VPOC v knotu“ přidává oproti kontrolám asi +1 bod. **Statisticky to prokázané není** (t ≈ 1,6). Na polovině B, která se k ladění nepoužila, je výhoda jen +0,6.
3. **Delta Strength Bar** (`opt5m_ae.py`):
   - **E** nemá výhodu v žádné variantě, výsledek je kolem nuly. Označovalo 24 % svíček, proto je ve výchozím stavu vypnuté.
   - **A s relativními prahy** (percentil za 200 svíček) vychází jako obrat +3,9 bodu a kladné ve všech částech. Je to ale jen 10 signálů.
   - **Rozjetí** se objevilo jen 2× za 40 session.

**Výsledné výchozí nastavení**, ověřené replayem C# kódu (`opt5m_verify.py`):

| | n | výhoda 15 min | úspěšnost | MFE / MAE 15 min |
|---|---|---|---|---|
| Wick Absorption (nové) | 70 | +1,8 | 57 % | 8,7 / 7,0 |
| Wick Absorption (staré klasické) | 138 | −1,0 | 44 % | 8,1 / 8,9 |
| DSB knotové setupy • | 68 | +2,0 | 59 % | 8,9 / 7,0 |
| DSB A | 10 | +3,9 | 70 % | 12,5 / 7,7 |

**Realisticky:** očekávejte menší výhodu než v tabulce. Nastavení bylo vybrané na stejných datech a na ES je potřeba odečíst náklady, zhruba 0,25–0,5 bodu na obchod. Jde o lépe nastavený filtr, ne o hotovou strategii.

## Data pro optimalizaci 5M grafu

- **Stačí 1M export.** 5M svíčky z něj skládá `resample_footprint.py`. Ověřeno na listopadovém exportu:
  - 2758 z 2759 5M svíček je shodných s 5M exportem z ATASu (OHLC, objem, delta, max/min delta),
  - 0,03 % cenových úrovní se liší,
  - **100 % signálů** obou indikátorů je shodných.
- **RTH vs. celá session:** exportujte ze stejně nastaveného grafu, na jakém obchodujete. Vyhodnocují se vždy jen signály v RTH.
  - Wick Absorption na nočních svíčkách nezávisí (0 rozdílů).
  - Delta Strength Bar se bez noci liší jen v prvních 30 min RTH. Na 5M je to 36 z 270 signálů, téměř vše E (vyčerpání), protože nové high/low po otevření se pak porovnává se včerejším závěrem místo s nocí.
- **Velikost:** 11 session 1M s nocí má zazipovaně 1,6 MB, samotné RTH 0,7 MB. 60 session vyjde zhruba na 9 MB, samotné RTH na 4 MB.

## Jak analýzu zopakovat

```bash
cd atas/analysis
```

Exportované soubory dejte do `data/` jako `ES1M_bars.csv`, `ES1M_levels.csv` a `ES5M_…`. Složka `data/` se do gitu necommituje. Pak spusťte replay indikátorů:

```bash
dotnet run -c Release -- data/ES1M data/ES1M_signals.csv
```

```bash
dotnet run -c Release -- data/ES5M data/ES5M_signals.csv
```

5M z 1M exportu (volitelně `--rth`):

```bash
python3 -I ../../.claude/skills/atas-indicator/scripts/resample_footprint.py data/ES1M_bars.csv 5 data/ES5M
```

5M optimalizace:

```bash
dotnet run -c Release -- data/R5M data/R5M_signals.csv
```

```bash
python3 -I opt5m_parity.py
```

```bash
python3 -I opt5m_sweep.py
```

```bash
python3 -I opt5m_follow.py
```

```bash
python3 -I opt5m_ae.py
```

```bash
python3 -I opt5m_verify.py
```

Vyhodnocení 1M/5M ETH:

```bash
python3 -I evaluate.py data/ES1M:1 data/ES5M:5
```
