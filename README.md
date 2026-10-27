# Klembord Tool

Een klembordbeheerder voor Windows 11. Alles wat je knipt of kopieert komt in een lijst, en met één klik plak je het weer.

## Functies

- **Geschiedenis** – alle tekst die je knipt of kopieert (Ctrl+X / Ctrl+C) komt bovenaan de lijst. Klik op een regel en hij wordt direct geplakt in het programma waar je net was. Je kunt zoeken door te typen.
- **Bewaarlijst** – sleep regels naar de bewaarlijst voor dingen die je vaker nodig hebt. Volgorde aanpassen kan ook door te slepen.
- **Sneltoetsen** – koppel een toetscombinatie aan een knipsel in de bewaarlijst (rechtermuisknop › *Sneltoets instellen…*, bijv. `Ctrl+Alt+1`). Die combinatie plakt het knipsel in elk programma.
- **Reekslijst** – zet regels in een vaste volgorde. Klik op een regel: die wordt geplakt en de volgende staat meteen klaar. Elke keer dat je `Ctrl+V` drukt komt de volgende regel. Met *Regels toevoegen…* plak je een hele lijst in één keer (elke regel wordt een item). Optie *Herhalen* begint na de laatste regel weer bij de eerste.
- `Ctrl+Alt+V` opent/verbergt het venster (instelbaar via *Bestand*).
- Draait in het systeemvak; optioneel automatisch starten met Windows.
- Wachtwoorden die door wachtwoordmanagers als "niet in geschiedenis" zijn gemarkeerd worden overgeslagen.

## Installeren

**Makkelijkst:** download `KlembordTool.exe` bij de [laatste release](https://github.com/dirkplat1999/klembord-tool-/releases/latest) en start het. Waarschuwt Windows SmartScreen, kies dan *Meer informatie › Toch uitvoeren* (het programma is niet ondertekend).

**Zelf bouwen:** er is niets extra's nodig, want Windows 11 heeft de benodigde .NET Framework 4 compiler al aan boord.

1. Download of clone deze repository.
2. Dubbelklik op `build.bat`. Er verschijnt `KlembordTool.exe`.
3. Start `KlembordTool.exe`.

## Bediening

| Actie | Hoe |
| --- | --- |
| Plakken | Klik op een regel, of selecteer en druk Enter |
| Naar bewaarlijst / reeks | Slepen, of rechtermuisknop |
| Volgorde wijzigen | Slepen binnen de lijst |
| Bewerken | Dubbelklik of F2 (bewaarlijst en reeks) |
| Verwijderen | Delete |
| Zoeken | Typ in de geschiedenis, of Ctrl+F |
| Venster verbergen | Esc of het kruisje (blijft in het systeemvak) |
| Afsluiten | Bestand › Afsluiten, of rechtsklik op het pictogram |

## Gegevens

Lijsten en instellingen staan in `%APPDATA%\KlembordTool\data.xml` (op je eigen pc, niet versleuteld). Wil je de geschiedenis niet bewaren, zet dan *Opties › Geschiedenis bewaren na afsluiten* uit.

Alleen tekst wordt bijgehouden (geen afbeeldingen of bestanden).
