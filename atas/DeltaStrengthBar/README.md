# Delta Strength Bar (ATAS / ATAS X)

Vodorovný barevný pruh pod cenou. Každá svíčka má jednu buňku, jejíž barva ukazuje sílu bid/ask delty přes 3bodový gradient:

```
Bid (prodávající)  ◀──  Neutral  ──▶  Ask (kupující)
     -1                   0              +1
```

## Jak se počítá síla

| Mód | Vzorec | Auto saturace |
|---|---|---|
| **Delta % of volume** (výchozí) | `Delta / Volume`, tedy jak jednostranná svíčka byla | 0.40 (40 % jednostranně = plná barva) |
| **Delta / max \|delta\|** | `Delta / max(|Delta|)` za posledních *Lookback* svíček | 0.80 |
| **Delta Z-score** | `(Delta − průměr) / směrodatná odchylka` za *Lookback* | 2.0 σ |

Výpočet pak běží v tomto pořadí:
1. Volitelné vyhlazení EMA (`Smoothing`, hodnota 1 znamená bez vyhlazení).
2. Vydělení hodnotou `Saturation` a oříznutí na ⟨-1, 1⟩.
3. **Neutral dead zone**: malé hodnoty se kreslí čistě neutrální barvou a zbytek se přeškáluje.
4. **Gamma**: tvar gradientu. Hodnota 1 je lineární, menší než 1 barví rychleji u nuly, větší než 1 zvýrazňuje hlavně extrémy.

## Nastavení

- **Calculation**: Mode, Lookback, Smoothing, Saturation (0 = auto), Dead zone, Gamma
- **Colors**: Bid / Neutral / Ask a Opacity %
- **Layout**: Position (spodek grafu / vršek grafu / pod low každé svíčky), Height, Offset, Gap, Border
- **Labels**: hodnotu delty (např. `-1.2K`) zobrazí v buňce, pokud je dost široká

## Build a instalace

Potřebujete .NET SDK 8 nebo novější. Indikátor se sestavuje pro .NET 8 a běží v ATAS na .NET 8 i .NET 10.

```bash
cd atas/DeltaStrengthBar
```

```bash
dotnet build -c Release
```

Pokud ATAS není ve výchozí složce, zadejte cestu k jeho DLL:

```bash
dotnet build -c Release -p:AtasDir="D:\ATAS Platform"
```

Výsledný `bin/Release/net8.0/DeltaStrengthBar.dll` zkopírujte do složky indikátorů:

- Windows: `%APPDATA%\ATAS\Indicators\`
- macOS (ATAS X): `~/Library/Application Support/ATAS/Indicators/`

Restartujte ATAS. Indikátor najdete pod názvem **Delta Strength Bar**.

## Poznámky

- Kreslí se ve vrstvě `Final` v hlavním cenovém panelu. Výška pruhu je v pixelech a nemění se se zoomem ceny.
- Pro jiné indikátory nebo strategie je k dispozici metoda `GetStrength(bar)`, která vrací hodnotu v ⟨-1, 1⟩.
