# Footprint Exporter (ATAS / ATAS X)

Uloží načtenou historii grafu do CSV souborů: data každé svíčky a k nim **bid, ask a objem na každé ceně** (footprint). Soubory se pak dají nahrát do chatu a analyzovat. Hlavní cíl je vyhodnotit signály Wick Absorption a Delta Strength Baru na vašich skutečných ES datech a podle výsledků nastavit prahy.

## Postup

1. Otevřete **ES 1min** graf (časový). Vyšší timeframe, volume nebo range svíčky si z 1min dat umím složit sám.
2. V nastavení grafu nastavte načtení aspoň **14 dní** historie a počkejte, než se načte.
3. Přidejte indikátor **Footprint Exporter**.
   - Export proběhne automaticky, jakmile je historie spočítaná. Ukládají se jen **uzavřené** svíčky.
   - Vlevo nahoře se ukáže stav, např. `Footprint Exporter: 19 320 bars (2026-11-02 00:00 → 2026-11-20 22:59) → C:\Users\…\ATAS_Export\ESZ6_20261121_1415_bars.csv`
   - Když se něco nepovede, stav se ukáže červeně s chybou.
4. Ve složce **`Dokumenty\ATAS_Export`** najdete 3 soubory se stejným začátkem názvu. Zabalte je do zipu a nahrajte sem.

Odhad velikosti: 14 dní 1min ES je zhruba 20 tisíc svíček, celkem řádově 10–20 MB a zazipované několik MB. Je to odhad z průměrného počtu cenových úrovní na svíčku, skutečnou velikost jsem neměřil.

## Nastavení

| Nastavení | Výchozí | Popis |
|---|---|---|
| Output folder | `Dokumenty\ATAS_Export` | kam se soubory uloží |
| Days to export | 14 | počítáno zpět od poslední svíčky, 0 = vše načtené |
| Keep appending new bars | ano | každá nově uzavřená svíčka se připíše do stejných souborů, takže graf může běžet a sbírat data dál |
| Export now (tick to run) | – | zaškrtnutím proběhne okamžitý nový export, i o víkendu se zavřeným trhem |
| Show status on chart | ano | stavový řádek vlevo nahoře |

## Formát souborů

Oddělovač je `;` a desetinná tečka.

**`…_bars.csv`**: jeden řádek na svíčku:
```
bar;time;last_time;open;high;low;close;volume;delta;max_delta;min_delta;levels
```

**`…_levels.csv`**: jeden řádek na každou obchodovanou cenu svíčky, seřazeno od high k low jako ve footprintu:
```
bar;price;bid;ask;volume
```

**`…_meta.txt`**: instrument, tick size, časová zóna počítače, rozsah dat a počet svíček.

## Ověření

Testy na simulovaných datech kontrolují:
- export jen posledních N dní a jen uzavřených svíček,
- že součet objemu z cenových úrovní = objem svíčky a součet ask − bid = delta svíčky,
- řazení úrovní,
- připisování nových svíček bez duplicit,
- „Export now“ a Days = 0.

Volitelné údaje (název instrumentu, max/min delta, last_time) se čtou tak, aby build neselhal, kdyby se v ATASu jmenovaly jinak. Pak zůstanou v CSV prázdné. V reálném ATASu se exporter zatím netestoval.
