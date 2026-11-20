# Wick Absorption (ATAS / ATAS X, ES futures)

Hledá obratové svíčky, u kterých se hlavní order-flow akce odehrála v knotu:

1. **Knot** je dost velký: podíl na rozsahu svíčky a minimální počet ticků.
2. **VPOC svíčky** (cena s největším objemem) leží **v knotu**.
3. **Delta obchodovaná v knotu** je silně jednostranná, a to absolutně (kontrakty) i relativně (delta / objem v knotu).

| Situace | Dolní knot (býčí) | Horní knot (medvědí) |
|---|---|---|
| **Absorpce**: agresoři narazili na pasivní limitky | delta v knotu silně **−** | delta v knotu silně **+** |
| **Iniciativa / pohlcení**: protistrana agresivně převzala | delta v knotu silně **+** | delta v knotu silně **−** |

Na grafu se zvýrazní zóna knotu barvou podle typu signálu, čára VPOC v knotu a popisek s deltou v knotu (např. `-1.2K`).

Výchozí barvy:

| Signál | Barva |
|---|---|
| Býčí absorpce | azurová |
| Býčí iniciativa | zelená |
| Medvědí absorpce | oranžová |
| Medvědí iniciativa | červená |

## Nastavení

| Skupina | Parametr | Výchozí | Poznámka pro ES |
|---|---|---|---|
| Wick | Min wick / range | 0.35 | 35 % rozsahu svíčky |
| | Min wick (ticks) | 3 | 3 ticky = 0.75 bodu |
| | Require VPOC in wick | ano | |
| | Min wick volume share | 0.20 | objem v knotu / objem svíčky |
| Delta in wick | Detect absorption / initiative | ano / ano | |
| | Min \|wick delta\| | 150 | ES 1min RTH ~100–300. Přes noc a na MES snižte (nebo dejte 0 a spoléhejte na poměr) |
| | Min \|wick delta\| / wick volume | 0.15 | funguje v RTH i přes noc |
| Filters | Only closed bars | ano | na živé svíčce může signál ještě zmizet |

Pro jiné indikátory nebo strategie je k dispozici metoda `GetSignal(bar)`, která vrací `None`, `BullAbsorption`, `BullInitiative`, `BearAbsorption` nebo `BearInitiative`.

## Build a instalace

```bash
cd atas/WickAbsorption
```

```bash
dotnet build -c Release
```

Výsledný `bin/Release/net10.0/WickAbsorption.dll` zkopírujte do `%APPDATA%\ATAS\Indicators\` (Windows) nebo `~/Library/Application Support/ATAS/Indicators/` (macOS) a restartujte ATAS.

Indikátor potřebuje footprint data, tedy objem po cenových úrovních. Ty ATAS má u běžných grafů z tickových dat.

## Ověření

- Kód je přeložený proti náhradní kopii ATAS API.
- Logika je otestovaná na ručně postavených svíčkách: kladivo s absorpcí, kladivo s iniciativou, shooting star s absorpcí, VPOC v těle, vyrovnaná delta a svíčka bez knotu.
- Footprint API (`candle.GetAllPriceLevels()`, `PriceVolumeInfo.Ask/Bid/Volume/Price`) vychází z běžného použití, ne z oficiálních docs. Pokud build v ATAS selže, pošlete chybovou hlášku.
