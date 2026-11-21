# ATAS indikátory: instalace a test

Balíček obsahuje dva indikátory pro ATAS / ATAS X (laděné na ES):

| Indikátor | Co ukazuje |
|---|---|
| **Delta Strength Bar** | barevný pruh pod cenou podle síly delty (Bid ↔ Neutral ↔ Ask) |
| **Wick Absorption** | obratové svíčky: VPOC v knotu a silná delta v knotu (absorpce / iniciativa) |
| **Footprint Exporter** | uloží historii grafu do CSV (svíčky + bid/ask/objem na každé ceně) pro analýzu |

## 1. Jednorázová příprava

Nainstalujte **.NET 10 SDK** (zdarma): https://dotnet.microsoft.com/download

ATAS X je postavený na .NET 10, indikátory se proto sestavují pro .NET 10.

Vyberte „SDK“, ne „Runtime“.

## 2. Sestavení a instalace (Windows)

1. Rozbalte zip, např. na plochu.
2. Zavřete ATAS.
3. Dvojklikem spusťte **`build-windows.bat`**. Skript:
   - najde instalaci ATAS (pokud ne, zeptá se na cestu ke složce, kde je `ATAS.Indicators.dll`),
   - sestaví oba indikátory,
   - zkopíruje DLL do `%APPDATA%\ATAS\Indicators\`.
4. Spusťte ATAS a na grafu ES přidejte přes *Indicators* **Delta Strength Bar** a **Wick Absorption**.

Na macOS (ATAS X) spusťte v Terminálu ve složce balíčku `./build-mac.sh`.

## 3. Co otestovat

- **Wick Absorption** na 1–5min ES grafu v RTH:
  - Zobrazují se zvýrazněné knoty s čárou VPOC a popiskem delty?
  - Sedí s tím, co vidíte ve footprintu?
- Pokud je signálů moc nebo málo, upravte v nastavení *Min |wick delta|* (výchozí 150), *Min wick / range* (0.35) a *Min |wick delta| / wick volume* (0.15).
- **Delta Strength Bar**: pruh u spodního okraje grafu. Vyzkoušejte všechny 3 módy a polohu „Below each candle low“.

## Export footprint dat (Footprint Exporter)

1. Otevřete **ES 1min** graf a v nastavení grafu nastavte načtení aspoň **14 dní** historie. Počkejte, než se data načtou.
2. Přidejte indikátor **Footprint Exporter**. Export proběhne sám. Vlevo nahoře na grafu se ukáže počet svíček, rozsah dat a cesta k souborům.
3. Ve složce `Dokumenty\ATAS_Export` budou 3 soubory: `…_bars.csv`, `…_levels.csv` a `…_meta.txt`. Zabalte je do zipu a nahrajte je sem do chatu.

Podrobnosti najdete ve `FootprintExporter/README.md`.

## Když něco nejde

Pošlete mi **řádky s `error`** z okna buildu, nebo screenshot. Indikátory jsem psal a testoval bez přístupu k ATAS knihovnám, takže drobná úprava názvu metody je možná. Oprava bývá na pár minut.
