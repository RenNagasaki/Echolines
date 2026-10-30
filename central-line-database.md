# Plan: Zentrale Zeilen-Datenbank (eigenes Projekt)

## Status

**Serverseitig umgesetzt (2026-08-10).** Phasen 0, 1, 2, 4, 5 und 6 stehen in diesem Repo; Phasen 3,
3b und 7 sind Client-Arbeit und gehören ins Echokraut-Repo. Was bei der Umsetzung zusätzlich
entschieden wurde, steht unten unter „Beim Bauen entschieden". Erstellt 2026-08-10 auf Wunsch des Users. Ersetzt den kollektiven Teil von
`missing-line-reports.md` — der Meldeweg über Issues/Discord wird damit zur Rückfallebene für die
Fälle, die eine Automatik nicht erkennen kann (falsche Zuordnung), siehe „Verhältnis zum anderen
Plan" unten.

Code-Aussagen sind am Repo verifiziert (Datei + Zeile); alles Übrige ist als Annahme markiert.

## Festgelegte Spezifikation (User, 2026-08-10)

1. Dialog startet ⇒ **automatische** Nachricht an den Server mit allen relevanten Infos.
2. Server prüft, ob bekannt; wenn nicht, Eintrag. Beides antwortet **200**.
3. **Kein UI**, läuft im Hintergrund.
4. **Keine Information über den Nutzer** — nur Zeile, Sprache, NPC.
5. Charaktername wird durch **Platzhalter** ersetzt.
6. Neue Zeilen gehen **erst in eine Zwischentabelle**, nach **X Anfragen** in die Haupttabelle.

Löst die heutige „eine Datei je Zeile im Google Drive"-Lösung ab.

## Entschieden: Variante A — Zufalls-GUID je Installation

Punkt 6 („nach X Anfragen befördern") braucht unterscheidbare Absender, sonst befördert **ein**
Absender mit X POSTs beliebigen Text und die Zwischentabelle wäre Verzögerung statt Schutz. **Der
User hat Variante A gewählt** (2026-08-10):

- **`installId` = einmalig erzeugte Zufalls-GUID**, in der Plugin-Konfiguration abgelegt.
- Aus **nichts** abgeleitet: nicht aus Charakter, Account, Hardware, IP oder Spielpfad. Sie enthält
  keine Aussage über die Person und ist nicht rückführbar.
- **Einziger Zweck:** eine Stimme je Installation je Zeile (`UNIQUE(pending_id, voter_key)`, wobei
  `voter_key` = `installId`).
- Nicht mit Spielzeit, Welt oder Ähnlichem verknüpft gespeichert.

Damit bedeutet X **X verschiedene Installationen** — die Bedingung dafür, dass Punkt 6 überhaupt
schützt.

Für die **Ratenbegrenzung** kommt zusätzlich die IP zum Einsatz — flüchtig, nicht mit Zeilen
verknüpft gespeichert. Ohne irgendeine Begrenzung ist der Endpunkt sonst ein offenes Scheunentor.

## Zwei Befunde vorab, die die Ausgangslage verändern

### 1. Der Google-Drive-Upload ist bereits totes Holz

`IGoogleDriveSyncService.UploadVoiceLine` hat **keinen einzigen Aufrufer** im Plugin — die einzigen
Vorkommen sind die Deklaration (`IGoogleDriveSyncService.cs:15`), die Weiterleitung
(`GoogleDriveSyncService.cs:94`) und die Implementierung (`DriveUploadService.cs:34`). Es gibt also
**nichts zu migrieren**: das neue System ist grüne Wiese, und der alte Pfad kann ersatzlos raus.

⚠ **Aber nur der Upload-Pfad.** `IGoogleDriveSyncService` wird weiterhin von `Plugin.cs`,
`AlltalkInstanceService` und `AudioFileService` injiziert, und `DriveLinkHelper` wandelt
Drive-Freigabelinks in Direktdownloads (für Custom-Model/Voices-URLs). Der **Download**-Teil bleibt.

### 2. Die Zeilen-Identität existiert schon dreifach

`VoiceLine.GetFileName` (`DataClasses/VoiceLine.cs`) baut heute
`{Language}_{Gender}_{Race}_{Name}_{VoiceMessageToFileName(RemovePlayerNameInText(Text))}.json`.
Derselbe Hash ist auch der **WAV-Dateiname** und die Melde-ID aus dem Paket-Plan. Wenn der Server
denselben Schlüssel verwendet, hängen zentrale DB, `*.ekpack` und lokale DB **ohne Übersetzungsschicht**
zusammen. Das ist die wichtigste Einzelentscheidung des Entwurfs.

⚠ Voraussetzung: **vor** dem Hashen tokenisieren (`Helper/Functional/PlayerNameTokenizer`), sonst
trägt jeder Nutzer seinen Charakternamen in den Schlüssel und dieselbe Zeile zerfällt in tausend
Varianten. `RemovePlayerNameInText` normalisiert bereits auf `<PLAYERNAME>`.

## Der harte Teil: ein offener Schreib-Endpunkt wird vergiftet

Das ist das zentrale Risiko, nicht die Technik. Ein unauthentifizierter Endpunkt, der beliebigen Text
einem beliebigen NPC zuordnet, wird früher oder später mit Müll oder Beleidigungen gefüllt — und weil
die DB später Sprachausgabe für alle speist, ist der Schaden hoch und öffentlich.

Ein API-Schlüssel im Plugin hilft **nicht**: er steht in der DLL und ist in Minuten extrahiert. Er
hält Zufallsverkehr ab, keinen Angreifer. Das sollte man wissen, bevor man sich darauf verlässt.

**Vorschlag: Konsens statt Vertrauen — dreistufig.**

1. **Der eigene Bestand ist die Referenz** (korrigiert 2026-08-10). Ursprünglich stand hier, der
   Server halte selbst den aus den Spieldateien extrahierten Harvest-Bestand. **Das ist gestrichen:
   der Server hält keinen eigenen Stand und hat keine Korrelation zu einer Spielversion** — es kommt
   einfach an, was ankommt, egal woher. Die Prüfung läuft stattdessen gegen `lines`: Was dort schon
   steht, ist bekannt, wird stillschweigend geschluckt und ist der Großteil des Verkehrs. Alles
   andere geht in Quarantäne.
   Dazu Formgrenzen, die keinen Bestand brauchen: Längen- und Zeichensatzgrenzen je Sprache,
   Höchstzahl Zeilen je Anfrage, Pflichtfelder.

   ⚠ **Am Anfang ist diese Referenz leer** — der erste Massenimport ist die Zündung. Bis drei
   Installationen ihren Harvest eingeliefert haben, ist praktisch alles Quarantäne. Das ist kein
   Fehler, aber es heißt: **die Web-UI und die manuelle Freigabe müssen ab Phase 4 stehen, sonst
   passiert in den ersten Wochen nichts Sichtbares.** Danach ist `lines` faktisch der
   Harvest-Bestand — ohne dass der Server ihn je selbst pflegen musste, und ohne dass er beim
   nächsten Patch veraltet.
2. **Quarantäne für alles Unbekannte.** Neue Zeilen landen in `pending`, nicht im öffentlichen
   Bestand. Genau diese Zeilen sind der eigentliche Gewinn — sie zeigen die Harvest-Lücken.
3. **Beförderung durch Übereinstimmung.** `pending` → öffentlich, sobald **N verschiedene
   Installationen** exakt denselben `(sprache, npc, texthash)` melden (Vorschlag N=3). Ein Einzelner
   kann damit nichts einschleusen, ohne N Installationen zu fälschen. Das passt exakt zum
   Wunsch „kein Feedback, läuft einfach mit" — der Konsens entsteht von selbst.

Dazu Ratenbegrenzung je Installations-ID und je IP, plus eine Sperrliste.

## Datenschutz — nicht verhandelbar, weil es automatisch läuft

Der Client sendet ohne Zutun des Nutzers. Damit gilt:

- **Kein Charaktername, keine `content_id`, keine Pfade.** Text wird tokenisiert, bevor er den
  Rechner verlässt. Präzedenzfall im Repo: `AudioPackageRules.RelativeEntryPath` hält bewusst den
  Windows-Benutzernamen aus dem Manifest.
- **Installations-ID = zufällige GUID**, aus nichts abgeleitet. Zweck ausschließlich: Konsenszählung
  und Ratenbegrenzung.
- **IP nur flüchtig** für die Ratenbegrenzung, nicht mit Zeilen verknüpft gespeichert.
### Einwilligung — entschieden (User, 2026-08-10)

- **Ein Schalter, zwei Orte, ein Konfigurationswert** (z. B. `Configuration.ShareLinesWithCommunityDb`,
  Standard **`true`**):
  - **Ersteinrichtungsassistent** (`NativeFirstTimeWindow`) — mit einem erklärenden Satz daneben.
  - **Settings → General** (`NativeConfigWindow.BuildGeneralPanel`) — derselbe Wert, damit man ihn
    jederzeit wiederfindet.
- **Kein UI im Betrieb**, wie gewünscht: keine Meldungen, keine Fortschrittsanzeige, keine Fehler im
  Spiel.
- **Changelog erklärt es ausdrücklich**: Standard an, und dass **keine Nutzerdaten** übertragen
  werden.

⚠ **Wichtiges Detail, das leicht übersehen wird: Bestandsnutzer sehen den Assistenten nie.**
Ein neuer Konfigurationswert mit Standard `true` schaltet sich bei jedem bestehenden Nutzer still
ein — die Ersteinrichtung läuft bei denen nicht noch einmal. Der einzige Kanal, der sie zuverlässig
erreicht, ist **der Changelog-Popup**, der nach jedem Update automatisch aufgeht
(`IChangelogService` + `NativeChangelogWindow`, gesteuert aus `Plugin.HandleStartup`/`OnLogin`).

⇒ **Der Changelog-Eintrag ist für Bestandsnutzer nicht Doku, sondern die Aufklärung selbst.** Er muss
deshalb in der Liste weit oben stehen und den Schalter benennen, nicht nur das Feature loben.

### Entwurf für den Changelog-Eintrag

**EN**
```
[Community] Echokraut helps complete the dialogue database - and you can turn it off
  When you talk to an NPC, Echokraut now reports that dialogue line to a central
  database, so lines missing from the plugin's own data can be found and added.
  This is ON by default and runs quietly in the background.

  - Only three things are sent: the dialogue line, its language, and which NPC
    says it. Your character name is replaced by a placeholder before sending.
  - Nothing about you is sent - no character name, no world, no account, no
    play time, no file paths.
  - You can switch it off any time under Settings -> General.
```

**DE** (ASCII-Umschrift wie im Rest der Datei)
```
[Gemeinschaft] Echokraut vervollstaendigt die Dialog-Datenbank - abschaltbar
  Wenn du mit einem NPC sprichst, meldet Echokraut diese Dialogzeile jetzt an eine
  zentrale Datenbank, damit Zeilen gefunden werden koennen, die in den Daten des
  Plugins fehlen. Das ist standardmaessig AN und laeuft still im Hintergrund.

  - Gesendet werden nur drei Dinge: die Dialogzeile, ihre Sprache und welcher NPC
    sie spricht. Dein Charaktername wird vorher durch einen Platzhalter ersetzt.
  - Ueber dich wird nichts gesendet - kein Charaktername, keine Welt, kein Account,
    keine Spielzeit, keine Dateipfade.
  - Du kannst es jederzeit unter Einstellungen -> Allgemein abschalten.
```

## Schnittstelle

Ein Endpunkt, gestapelt, immer 200 — wie gewünscht:

```
POST /v1/lines
{
  "installId": "<guid>",          // Zufalls-GUID, Variante A
  "pluginVersion": "0.19.3.1",
  "origin": "live",               // Gegenstück zu "harvest", siehe Datenmodell
  "lines": [
    { "lang": "de", "npc": "Alphinaud Leveilleur", "gender": "Male",
      "race": "Elezen", "npcBaseId": 1000123,
      "text": "Nun denn, -PlayerFirstName-, ...",   // Platzhalter, NIE der echte Name
      "hash": "<VoiceMessageToFileName-Hash>",
      "source": "AddonTalk" }
  ]
}
→ 200 {}   (immer, auch wenn alles schon bekannt war)
```

**Was NICHT im Payload steht** (Punkt 4): kein Charaktername, keine Welt, keine `content_id`, keine
Spielzeit, keine Pfade, keine Hardware-Kennung. `pluginVersion` ist drin, weil ein Formatfehler sonst
nicht zuzuordnen ist — falls auch das zu viel ist, kann es in einen HTTP-Header wandern oder ganz
entfallen.

- **Stapel statt Einzelrequest.** Eine Zeile je Request wäre bei mehreren hundert Zeilen pro
  Spielstunde verschwenderisch und bricht bei jedem Netzwerkhänger. Der Client puffert und sendet
  alle ~60 s oder ab 50 Zeilen.
- **Client-seitige Unterdrückung ist der größte Hebel:** eine lokal persistierte Menge bereits
  gesendeter Hashes. Nach dem ersten Durchspielen sendet ein Nutzer fast nichts mehr. Ohne das
  schickt jeder Nutzer bei jedem Dialog erneut dieselben Zeilen.
- `400` nur bei kaputtem oder zu großem Payload, `429` bei Ratenbegrenzung — der Client ignoriert
  beides und versucht es später erneut. **Nie eine Fehlermeldung im Spiel.**
- Versionierter Pfad (`/v1/`), damit ein Formatwechsel alte Clients nicht bricht.

### Zweiter Endpunkt: Massenimport aus dem Harvest (User, 2026-08-10)

Ein Harvest im Plugin liefert sein **komplettes** Ergebnis hier ein. Das ist nicht dasselbe wie der
Live-Meldeweg und braucht einen eigenen Endpunkt, sonst kollidiert es mit der Ratenbegrenzung des
Live-Pfades.

**Größenordnung (geklärt, User 2026-08-10): der Harvest läuft je Sprache.** Eine Einlieferung umfasst
also rund **288.000 Zeilen** (232.000 Quest + 26.790 Cutscene + ~29.000 Skript), nicht 1,15 Millionen.
Bei 1000 Zeilen je Anfrage sind das ~288 Stücke — daher `"chunkCount": 288` im Beispiel oben.

Zwei Folgen daraus:

- **`lang` gehört in den Fortsetzungszustand des Clients**, nicht nur in die Zeile. Ein Nutzer, der
  in mehreren Sprachen spielt, löst mehrere getrennte Einlieferungen aus; der Client muss je Sprache
  wissen, wie weit er gekommen ist. Serverseitig kollidiert nichts, weil `lang` Teil von
  `UNIQUE(lang, npc_name, text_hash)` ist.
- **X=3 gilt je Sprache getrennt.** Eine deutsche Zeile wird nicht dadurch befördert, dass drei
  Installationen die englische Fassung geliefert haben. Für die selteneren Sprachen (FR, JP) heißt
  das: drei harvestende Installationen **dieser** Sprache sind die Hürde — realistisch der Fall, in
  dem die manuelle Freigabe in der Web-UI dauerhaft gebraucht wird, nicht nur zur Anlaufzeit.

```
POST /v1/lines/import
{
  "installId": "<guid>",
  "pluginVersion": "0.19.3.1",
  "origin": "harvest",            // unterscheidet die Herkunft, siehe Datenmodell
  "chunk": 7, "chunkCount": 288,  // fortlaufend, damit ein Abbruch fortsetzbar ist
  "lines": [ /* … wie bei /v1/lines, bis 1000 je Anfrage … */ ]
}
→ 200 {}
```

- **Jede Installation darf importieren, anonym wie der Live-Pfad — aber gedrosselt und ausnahmslos
  in die Quarantäne** (entschieden). Kein Sonderweg, kein Betreiber-Token: Harvest-Zeilen
  unterliegen derselben X=3-Regel wie live gemeldete. Ein einzelner Absender kann damit auch mit
  288.000 Zeilen nichts in den öffentlichen Bestand drücken.
- **Gestückelt, nicht in einem Schwung.** Bis 1000 Zeilen je Anfrage, der Client arbeitet die Stücke
  nacheinander im Hintergrund ab. `chunk`/`chunkCount` dienen nur der Fortsetzbarkeit — der Server
  darf sie ignorieren, weil jede Zeile ohnehin idempotent über `UNIQUE(lang, npc_name, text_hash)`
  und `UNIQUE(pending_id, voter_key)` einläuft.
- **Eigenes Ratenbudget je `installId`**, großzügiger als der Live-Pfad, aber gedeckelt: ein Harvest
  darf über Stunden laufen, nicht in Minuten. `429` bremst, der Client wartet und macht weiter.
- **Wiederholter Harvest derselben Installation kostet nichts.** `UNIQUE(pending_id, voter_key)`
  lässt die zweite Stimme derselben Installation fallen — ein Nutzer, der nach jedem Patch neu
  harvestet, treibt keinen Zähler hoch.

⚠ **Folge für die Anlaufphase, die man einplanen muss:** Bis drei Installationen ihren Harvest
geschickt haben, liegen mehrere hunderttausend Zeilen in `pending_lines`. Zusammen mit dem
Entschluss, dort **nie** etwas zu verwerfen, ist das der eigentliche Größentreiber der Datenbank —
die Zwischentabelle ist auf Dauer größer als die Haupttabelle, nicht kleiner. Indizes und Pagination
sind deshalb keine Kür.

## Datenmodell (Skizze)

Zwischentabelle und Haupttabelle wie in Punkt 6 verlangt, dazu die Stimmen als eigene Tabelle —
drei insgesamt:

```
-- ZWISCHENTABELLE: alles Neue landet hier zuerst
pending_lines   id, lang, npc_name, gender, race, npc_base_id, text, text_hash,
                first_seen_utc, last_seen_utc, hit_count, state
                UNIQUE(lang, npc_name, text_hash)
                -- state: pending | rejected | promoted   (nichts wird gelöscht, s. u.)

-- Stimmen je Zwischeneintrag
pending_votes   pending_id, voter_key, origin, seen_utc
                UNIQUE(pending_id, voter_key)   -- eine Stimme je Installation
                -- voter_key = installId (Variante A)
                -- origin: live | harvest

-- HAUPTTABELLE: befördert ab X
lines           id, lang, npc_name, gender, race, npc_base_id, text, text_hash,
                first_seen_utc, last_seen_utc, promoted_utc, source_count,
                seen_live, seen_harvest, origin_operator
                UNIQUE(lang, npc_name, text_hash)
                -- origin_operator: aus dem Drive-Massenimport der Web-UI (weder live noch harvest)
```

**`origin` bzw. `seen_live`/`seen_harvest` sind der Ersatz für die gestrichene Harvest-Referenz.**
Die Frage aus Phase 6 — *was kennt der Harvest nicht?* — war ursprünglich ein Abgleich gegen einen
Bestand, den der Server selbst hält. Den gibt es nicht mehr. Sie ist aber **innerhalb** der Datenbank
beantwortbar, sobald jede Stimme mitbringt, woher sie kam:

- `seen_live = true AND seen_harvest = false` ⇒ **genau die Harvest-Lücke.** Ein Spieler hat die
  Zeile im Spiel gesehen, kein einziger Harvest hat sie je geliefert.
- `seen_harvest = true AND seen_live = false` ⇒ existiert in den Spieldateien, ist aber noch
  niemandem begegnet (toter Inhalt, ungenutzte Zeile, Vorab-Daten eines Patches).
- beides `true` ⇒ der Normalfall, uninteressant.

Das ist strenger als der alte Entwurf und kostet nichts: zwei Spalten und ein Feld je Stimme. Und es
**veraltet nicht mit dem Spielpatch**, weil es keinen eingefrorenen Referenzstand mehr gibt.

**Beförderung:** `COUNT(pending_votes)` erreicht X ⇒ Zeile wandert nach `lines`, Zwischeneintrag
bekommt `state = promoted` (**nicht gelöscht**, siehe Verfallsregel unten).
`UNIQUE(pending_id, voter_key)` ist die ganze Konsenslogik — mehr braucht es nicht.

### ⚠ Lücke im ursprünglichen Ablauf: „schon bekannt ⇒ nichts tun" bricht Phase 6

Hier stand: *existiert die Zeile in `lines` ⇒ nichts tun*. Zusammen mit `seen_live`/`seen_harvest`
ist das **falsch** und macht genau die Auswertung kaputt, für die die beiden Spalten da sind:

> Drei Harvests befördern eine Zeile ⇒ `lines` mit `seen_harvest = true, seen_live = false`.
> Ein halbes Jahr später sieht ein Spieler die Zeile im Dialog. Der Server sagt „schon bekannt,
> nichts tun" ⇒ **`seen_live` bleibt für immer `false`.** Die Zeile steht dauerhaft als „existiert
> in den Spieldateien, ist aber noch niemandem begegnet" da, obwohl sie längst begegnet ist.

Dasselbe in der anderen Richtung: eine live gefundene Zeile, die ein späterer Harvest nachliefert,
bliebe ewig als „Harvest-Lücke" markiert, obwohl der Harvest sie inzwischen kennt — **genau der
Befund, den Phase 6 liefern soll, wäre systematisch veraltet.**

**Richtiger Ablauf beim Eintreffen einer Zeile** — in allen Fällen `200`:

1. Steht sie in `lines`: **nicht „nichts tun".** `seen_live` bzw. `seen_harvest` gemäß `origin`
   auf `true` setzen, falls noch nicht gesetzt, und `last_seen_utc` fortschreiben. Sonst nichts.
   Ein Schreibvorgang nur bei tatsächlicher Änderung, damit der Normalfall (alles längst bekannt)
   ein reiner Lesevorgang bleibt.
2. Sonst `pending_lines` anlegen oder die Stimme ergänzen (`UNIQUE(pending_id, voter_key)` schluckt
   Wiederholungen) und ggf. befördern. Beim Befördern werden `seen_live`/`seen_harvest` aus den
   `origin`-Werten der vorhandenen Stimmen aufgebaut.
3. Steht sie in `pending_lines` mit `state = rejected`: Stimme still verwerfen, `200`. Sonst holt
   ein abgelehnter Eintrag über neue Meldungen doch wieder Stimmen und würde erneut befördert.

**Keine Verfallsregel — `pending_lines` wird nie verworfen** (User, 2026-08-10). Weil die Web-UI
manuelle Freigabe unterhalb von X erlaubt, ist jeder Einzelfund potenziell wertvoll: genau die
Einträge, die X=3 nie erreichen, sind das interessante Material für Phase 4 (was kennt der Harvest
nicht?). Ein Aufräumjob würde sie löschen, bevor jemand sie angesehen hat.

Folgen, die der Entwurf tragen muss:

- Die Tabelle wächst unbegrenzt. Für Postgres unkritisch, aber die Admin-Liste **muss von Anfang an
  paginiert und indiziert** sein (`(lang, npc_name)`, `text_hash`, `last_seen_utc`) — eine
  ungebremste „alles anzeigen"-Ansicht wird sonst nach einem Jahr unbenutzbar.
- Der `rejected`-Zustand aus der Web-UI wird damit **noch wichtiger**: er ist die einzige Art, wie
  ein Eintrag aus der Arbeitsliste verschwindet. Abgelehnte Einträge bleiben in der Tabelle stehen,
  aber außerhalb der Standardansicht.
- **Auch beförderte Einträge bleiben stehen** (`state = promoted`) statt gelöscht zu werden. Sonst
  wäre „nie verworfen" nur für Ladenhüter wahr, und die Stimmen mit ihren `origin`-Werten — die
  Herkunftsspur einer Zeile — verschwänden ausgerechnet bei den Zeilen, die es geschafft haben.
  Die Standardansicht der Web-UI filtert auf `state = pending`.

## Web-UI (Admin) — entschieden

Der Server bekommt eine kleine Weboberfläche für den Betreiber. Drei Aufgaben:

### 1. Manuelles Freigeben unterhalb von X

Zeilen, die X=3 nie erreichen (Randinhalte, alte Saison-Events, seltene NPCs), müssen von Hand
befördert werden können. Liste über `pending_lines`, sortier- und filterbar nach Sprache, NPC,
Stimmenzahl, Alter; je Eintrag **Freigeben** / **Ablehnen**, dazu Mehrfachauswahl.

Abgelehnte Zeilen brauchen einen `rejected`-Zustand statt einer Löschung — sonst meldet der nächste
Client sie sofort wieder und sie stehen erneut in der Liste.

### 2. Massenimport der bisherigen Google-Drive-Dateien

⚠ **Präzisierung zum Format** (am Code geprüft, `DriveUploadService.cs:34-60`): die Dateien heißen
**`.json`**, nicht `.txt` — nur der MIME-Typ beim Upload war `text/plain`. Inhalt ist
`JsonSerializer.Serialize(voiceLine)`, also ein Objekt mit `Gender`, `Race`, `Language`, `Name`,
`Text`. Der Dateiname ist
`{Language}_{Gender}_{Race}_{Name}_{VoiceMessageToFileName(RemovePlayerNameInText(Text))}.json`.

⚠ **Und ein Fallstrick, der den Import sonst still verfälscht:** `Gender`, `Race` und `Language` sind
Enums und wurden von `System.Text.Json` **als Zahlen** serialisiert (kein `JsonStringEnumConverter`
im Pfad). Ändert sich eine Enum-Reihenfolge, zeigen alte Zahlen auf falsche Werte. **Der Dateiname
enthält dieselben Angaben als Klartext** (`Gender.ToString()` usw.) — der Importer sollte deshalb
**den Dateinamen als führend** behandeln und aus dem JSON nur `Text` übernehmen. Bei Abweichung
zwischen beiden: Eintrag in einen Konfliktbericht, nicht raten.

Bedienung: Mehrfachauswahl im Datei-Dialog (viele Dateien auf einmal) oder ein Zip. Ergebnis als
Bericht — importiert / schon bekannt / fehlerhaft, mit Beispielen. Import geht **direkt in `lines`**
(Haupttabelle), nicht in die Quarantäne: das Material stammt vom Betreiber selbst.

⚠ Diese Zeilen sind **weder `live` noch `harvest`** — sie kommen aus dem alten Drive-Bestand und
haben keine Stimme. Sie bekommen deshalb `origin_operator = true` bei `seen_live = seen_harvest =
false`. Ohne diese dritte Herkunft würden sie in der Phase-6-Auswertung als „existiert in den
Spieldateien, ist aber noch niemandem begegnet" auftauchen, obwohl über sie gar nichts bekannt ist.

### 3. Einblick für Phase 4

Ansicht „gemeldet, aber im Harvest unbekannt" — der eigentliche Nutzen des ganzen Aufbaus. Konkret
ist das der Filter `seen_live AND NOT seen_harvest` (siehe Datenmodell), kein Abgleich gegen einen
extern gepflegten Bestand.

Dazu die Gegenrichtung `seen_harvest AND NOT seen_live` — existiert in den Spieldateien, ist aber
noch niemandem begegnet.

### ⚠ Damit kommt Authentifizierung ins Spiel

Bis hierhin war der Dienst schreibend-anonym und ansonsten harmlos. **Eine Admin-Oberfläche im
offenen Netz ohne Anmeldung wäre der größte Einzelfehler des Entwurfs** — wer sie findet, kann
beliebige Zeilen freigeben und damit genau die Vergiftung auslösen, gegen die die Quarantäne gebaut
wurde.

**Entschieden (User, 2026-08-10): ein einzelnes Betreiberkonto, Passwort aus einem Secret.**
Eigener Pfad (`/admin`), Passwort-Hash aus einer Umgebungsvariablen/Secret, Cookie-Session, keine
Registrierung, Ratenbegrenzung auf dem Login. Kein Nutzerverwaltungs-System — es gibt genau einen
Benutzer. Kein OAuth, keine Abhängigkeit von GitLabs Verfügbarkeit.

## Technik

- **ASP.NET Core, containerisiert.** Vorbild im Nachbarprojekt: `Echocrowd.Server` (ASP.NET, EF Core,
  `Models`/`Services`/`Migrations`/`Middleware`) — gleicher Autor, gleicher Stack.
- **Datenbank: PostgreSQL** (entschieden). EF Core mit `Npgsql`; Migrationen wie im Nachbarprojekt.
  Datenbank als eigener Container, Volume für die Daten, Zugangsdaten aus Secrets.
- **Repo-Name: `echolines`** (entschieden), unter `gitlab.echotools.cloud/echotools/echolines`,
  GitHub-Spiegel `RenNagasaki/Echolines`.
- **Eigene `Testing/checklist.json`** — Server-Komponenten ohne eigene Tag-Struktur hängen sonst als
  zusätzliche `component` an der Plugin-Checkliste (Regelwerk im übergeordneten `CLAUDE.md`).
- **xUnit-Testprojekt `Echolines.Tests`** und **`.claude/verify.ps1`** (Build + Tests, `exit 0`/`!= 0`)
  — vom Regelwerk verlangt und hier besonders angebracht, siehe eigenen Abschnitt unten.

### Tests — was tatsächlich geprüft werden muss

Die Konsens- und Beförderungslogik ist genau die Art Code, die man von Hand nicht nachprüft: Sie
schlägt still fehl, das Ergebnis sieht in der Web-UI plausibel aus, und der Fehler fällt erst
Monate später auf. Die Fälle, die einen Test verdienen:

**Konsens und Beförderung**
- Dieselbe `installId` meldet dieselbe Zeile zehnmal ⇒ **eine** Stimme, keine Beförderung.
- Drei verschiedene `installId` ⇒ Beförderung bei genau der dritten, nicht bei der zweiten.
- Beförderung setzt `state = promoted`, **löscht den Zwischeneintrag nicht** und lässt die Stimmen
  samt `origin` stehen.
- Eine Zeile mit `state = rejected` sammelt neue Meldungen ⇒ Stimme wird verworfen, keine erneute
  Beförderung, Antwort trotzdem `200`.

**Herkunft — der Fall, der den Entwurf einmal gekippt hat**
- Zeile durch drei Harvests befördert (`seen_harvest`), danach Live-Meldung ⇒ **`seen_live` wird
  auf `true` nachgezogen**, obwohl die Zeile längst in `lines` steht.
- Umgekehrt: live befördert, später vom Harvest geliefert ⇒ `seen_harvest` wird nachgezogen.
- Wiederholte Meldung ohne Änderung ⇒ **kein** Schreibvorgang (der Normalfall bleibt lesend).
- Drive-Massenimport ⇒ `origin_operator`, und `seen_live`/`seen_harvest` bleiben `false`.

**Sprachtrennung**
- Drei Installationen melden dieselbe Zeile auf Englisch, eine auf Deutsch ⇒ die englische wird
  befördert, die deutsche bleibt in der Quarantäne.

**Schnittstelle**
- Beide Endpunkte antworten `200`, auch wenn alles bereits bekannt ist.
- Zu großer Stapel ⇒ `400`; Ratenbegrenzung ⇒ `429`; nie ein `500` an den Client.
- Stück wird doppelt geschickt (Client hat nach Abbruch neu angesetzt) ⇒ idempotent, keine
  Doppelzählung.

**Datenschutz als Test, nicht als Vorsatz**
- Ein Payload, dessen `text` einen Klarnamen enthält, darf so nicht entstehen: Test gegen die
  Tokenisierung im Client (`RemovePlayerNameInText` vor dem Hashen), damit die Zusage aus dem
  Changelog nachweisbar ist statt nur behauptet.

### Bau und Auslieferung (entschieden, analog Echokraut)

- **GitLab CI baut das Docker-Image** (seit 2026-10-30, vorher GitHub Action → GHCR). Job
  `build:image` (Kaniko) nach `registry.echotools.cloud/echotools/echolines`, getaggt mit Commit-SHA
  und `latest` (main) bzw. Release-Tag — ein `latest`-only-Image macht Rückrollen unmöglich.
  `deploy:cluster` stößt danach Renovate im Cluster-Repo an (Sofort-Deploy). Ein öffentliches Image
  gibt es nicht mehr; Self-Hoster bauen per `docker compose` aus dem Quellcode.
- **GitLab CI analog zu `Echokraut/.gitlab-ci.yml`**, dessen Aufbau übernommen wird:
  - Stages `security` → `analysis` → `mirror`
  - `gitleaks` (Secret-Scan, `alpine:latest`)
  - `sonarqube` (`mcr.microsoft.com/dotnet/sdk:10.0`, `dotnet-sonarscanner`, eigener
    `SONAR_PROJECT_KEY`)
  - `mirror-to-github` nur auf dem Hauptbranch, **additiv** mit `git push --all --force` +
    `git push --tags` — **nie `--mirror`**: das löscht auf GitHub Tags, die dort über die Release-UI
    entstanden sind, und zerstört die zugehörigen Releases.
  - ⚠ Die GitHub-Zieladresse wird **fest eingetragen** (`RenNagasaki/Echolines`), nicht aus
    `${GITHUB_USER}` gebaut — der CI-Anmeldebenutzer ist nicht der Repo-Eigentümer, das hat bei
    Echokraut schon einmal auf das falsche Repo gezeigt und 403 geliefert.
- **Reihenfolge der beiden CI-Systeme:** GitLab ist der Ursprung, GitHub der Spiegel. Die
  GitHub-Action läuft also auf dem gespiegelten Stand. **Entschieden (User, 2026-08-10): so bleibt
  es.** Bewusst in Kauf genommene Folge: **ein Bau startet erst, nachdem der Spiegel-Job durch ist** —
  zwischen Push auf GitLab und fertigem Image liegt immer die Spiegel-Laufzeit.

## Client-Seite (Plugin)

### Umfang: JEDE unvertonte Zeile, ausnahmslos (User, 2026-08-10)

Gemeldet wird **jede Dialogzeile, die das Spiel nicht selbst vertont** — **unabhängig davon**, ob
lokal Audio existiert, ob die Zeile in der lokalen DB steht, ob sie im `*.ekpack` enthalten war oder
ob der Nutzer den NPC stummgeschaltet hat. Nur „was mir fehlt" zu melden wäre wertlos: die zentrale
DB soll den vollständigen Bestand abbilden, nicht die Lücken einer einzelnen Installation.

**Vertonte Zeilen kommen dabei von selbst nicht vor**, ohne dass man dafür etwas prüfen müsste:
`AddonTalkHelper.HandleChange` verlässt sich bei einer vertonten Zeile früh (`voiceNext` ⇒
„Skipping voice-acted line") und ruft den Prozessor gar nicht erst auf. Was `ProcessSpeechAsync`
erreicht, ist damit per Konstruktion unvertont.

### ⚠ Der Aufrufpunkt ist NICHT `LogVoiceClip` — dort gehen Zeilen verloren

Ursprünglich hatte ich `VoiceMessageProcessor.LogVoiceClip` vorgeschlagen. **Das ist falsch.** Am
Code geprüft (`VoiceMessageProcessor.cs`): `LogVoiceClip` steht in Zeile 235, **hinter vier Early
Returns**:

| Zeile | Abbruch | greift im None-Modus? |
|---|---|---|
| 193 | NPC stummgeschaltet (`IsNpcMuted`) | **ja** — keine `HasLiveGeneration`-Bedingung |
| 207 | keine Stimme gesetzt | nein (nur bei Live-Generierung) |
| 214 | Stimmen-Lautstärke 0 | nein (nur bei Live-Generierung) |
| 226 | Endlautstärke ≤ 0 | **ja** — keine Bedingung |

⚠ **Der Kommentar direkt über `LogVoiceClip` behauptet „regardless of mute/volume state" — das
stimmt nicht.** Ein stummgeschalteter NPC oder eine Endlautstärke von 0 lässt die Zeile fallen,
bevor sie überhaupt geloggt wird. (Nebenbefund: damit fehlen diese Zeilen auch der **lokalen** DB —
ein eigenständiger Altfehler, hier nur notiert, nicht Teil dieses Plans.)

⇒ **Der Meldeaufruf gehört an Schritt 3.5, `VoiceMessageProcessor.cs:187`**, direkt nachdem der
NPC aufgelöst und `voiceMessage` gebaut ist und `DialogState.CurrentVoiceMessage` gesetzt wird —
**vor** allen Stumm-, Stimmen- und Lautstärkeprüfungen. Dort liegen NPC, Sprache, Geschlecht, Rasse
und Text bereits vor, und die Zeile ist bereits als sprechbar erkannt (die Textprüfung sitzt
davor, Zeile ~150).

Kleiner Restfall, bewusst in Kauf genommen: **Schritt 1** (`IsBackendAvailable`, Zeile ~122) bricht
ab, wenn ein Nutzer *Local* ohne Installation oder *Remote* ohne URL eingestellt hat. Im
**None-Modus liefert `IsBackendAvailable()` true** (`BackendService.cs:227`) — Paket-Nutzer, also die
wichtigste Gruppe, sind nicht betroffen. Nur fehlkonfigurierte Installationen melden nichts.

**Auch nicht auflösbare Sprecher werden gemeldet.** Zeilen mit `???` oder einem Fakenamen sind
sogar die wertvollsten — genau sie zeigen die Zuordnungslücken. Gesendet wird dann der angezeigte
Sprechername.

### Übrige Regeln

- Neuer `ILineSubmissionService`: puffert, dedupliziert lokal, sendet im Hintergrund.
  **Nie auf dem Frame-Thread** — im Repo mehrfach schmerzhaft gelernt (`Windows/CLAUDE.md`
  → „Backend calls must not run on the frame thread").
- **Die lokale „schon gesendet"-Menge bedeutet NICHT „lokal bekannt".** Sie verhindert nur, dass
  dieselbe Installation dieselbe Zeile mehrfach schickt. Ob die Zeile lokal in der DB steht oder
  Audio hat, spielt für den Versand **keine** Rolle.
- **Statischer `HttpClient`**, kein `new` je Aufruf. Im Repo zweimal als Socket-Erschöpfung
  aufgetreten (`BackendService`-Probe, AllTalk-Streaming-Client).
- Der alte Upload-Pfad (`UploadVoiceLine`, `DriveUploadService`) fliegt raus; `DriveLinkHelper` und
  der Download-Teil bleiben.

## Phasen

| Phase | Inhalt | Aufwand (Schätzung) |
|---|---|---|
| **0** | `Echolines.Tests` + `.claude/verify.ps1` aufsetzen, damit ab Phase 1 jede Änderung geprüft ist | 0,5 Tag |
| **1** | Server-Grundgerüst: Projekt, Postgres-Modell, `POST /v1/lines`, Ratenbegrenzung, Docker + beide CI-Systeme | 2,5 Tage |
| **2** | `POST /v1/lines/import`: Stückelung, eigenes Ratenbudget, Quarantänelogik, Beförderung ab X=3, `origin`-Herkunft | 1,5 Tage |
| **3** | Client: `ILineSubmissionService`, lokale Unterdrückung, Stapelversand, `installId`, Schalter an beiden Stellen, Changelog | 1,5 Tage |
| **3b** | Client: Harvest löst den Massenimport aus — gestückelt, im Hintergrund, nach Abbruch fortsetzbar | 0,5 Tag |
| **4** | Web-UI: Admin-Login, Freigabe-/Ablehnliste, Phase-4-Ansicht | 2 Tage |
| **5** | Web-UI: Massenimport der Drive-Dateien (Dateiname führend, Konfliktbericht) | 1 Tag |
| **6** | Auswertung „welche gemeldeten Zeilen kennt der Harvest nicht?" — Abfrage über `seen_live`/`seen_harvest`, kein Referenzstand nötig | 0,5 Tag |
| **7** | Alten Drive-Upload aus dem Plugin entfernen | 0,5 Tag |
| **quer** | Tests zu den Fällen aus „Tests" oben, mitlaufend je Phase (Schwerpunkt 1, 2, 3b) | 1,5 Tage |

Summe grob **12 Tage** (vorher 9,5 — die Differenz sind Phase 0, die Tests und der
Massenimport-Client aus 3b). Phase 5 kann vorgezogen werden, wenn der Drive-Bestand zuerst gerettet
werden soll — sie hängt nur an Phase 1 und dem Admin-Login aus Phase 4.

**Phase 0 steht bewusst vor Phase 1.** Ein Testprojekt, das nach der Logik entsteht, prüft die
Logik gegen sich selbst — es schreibt fest, was der Code tut, nicht was er soll. Die vier Fälle aus
„Herkunft" oben sind genau die, die dabei durchgerutscht wären.

⚠ **Neue Reihenfolge-Abhängigkeit durch die gestrichene Harvest-Referenz:** Solange `lines` leer
ist, landet alles in der Quarantäne und nichts wird sichtbar. **Phase 4 (Web-UI mit manueller
Freigabe) rückt damit von „nützlich" auf „nötig, bevor der Betrieb Sinn ergibt"** — vorher gibt es
keine Möglichkeit, die ersten Einträge zu bewerten, und ohne drei harvestende Installationen
befördert sich nichts von selbst.

**Phase 4 ist der Zweck der Übung.** Sie beantwortet dauerhaft und automatisch die Frage, an der
diese Session gearbeitet hat: was fehlt dem Extract noch?

## Beim Bauen entschieden (2026-08-10)

Vier Punkte, die der Plan offen ließ und die die Umsetzung beantworten musste:

### 1. Der `hash` wird serverseitig neu berechnet, nicht geglaubt

`VoiceMessageToFileName` (`TalkTextHelper.cs:572`) ist **kein Hash**, sondern ein deterministischer,
verlustbehafteter Slug: Kleinschreibung, ungültige Dateinamenzeichen und `.!,-_ ` raus, bei 120
Zeichen abgeschnitten. Weil er rein aus dem Text folgt, **kann der Server ihn selbst bilden** — und
sollte es auch: ein Client mit einem falschen oder erfundenen `hash` würde eine Zeile sonst in
beliebig viele Schlüssel zersplittern, also Vergiftung per Tippfehler. Der Wert des Clients wird
weiterhin entgegengenommen, aber nur verglichen; eine Abweichung ist ein Gesundheitssignal.

⚠ **Fallstrick, der das fast lautlos kaputtgemacht hätte:** das Plugin ruft
`Path.GetInvalidFileNameChars()` — das liefert unter **Windows 41 Zeichen, unter Linux zwei** (`/` und
`NUL`). Das Plugin läuft unter Windows, der Server im Linux-Container. Ein Doppelpunkt ist in
FFXIV-Dialogen häufig; der Server hätte für solche Zeilen einen anderen Schlüssel gebildet als das
Plugin. Der Windows-Zeichensatz steht deshalb fest verdrahtet in `LineKey`, mit Test dagegen.

### 2. Herkunft entscheidet der Endpunkt, nicht das Feld `origin`

Ein Client, der auf `/v1/lines` `"origin": "harvest"` behauptet, bekäme seine Meldung sonst als
Spieldatei-Beleg verbucht — genau die Unterscheidung, auf der Phase 6 ruht. Das Feld bleibt im
Format (Doku, Debugging), wird serverseitig aber ignoriert.

### 3. `last_seen_utc` in `lines` wird nur tageweise fortgeschrieben

Der Plan verlangt beides: `last_seen_utc` fortschreiben **und** „ein Schreibvorgang nur bei
tatsächlicher Änderung". Wörtlich genommen widerspricht sich das, denn jede Meldung ändert
`last_seen`. Aufgelöst über eine Auflösung von einem Tag (`IngestOptions.LastSeenResolution`): das
Feld bedeutet weiterhin, was es sagt, und der Normalfall — eine längst bekannte Zeile — bleibt ein
reiner Lesevorgang. Ohne das wäre jeder Dialog im Spiel ein Schreibzugriff.

### 4. Die Ratenbegrenzung braucht `X-Install-Id` als Header

Der Limiter läuft, **bevor** der Body gelesen ist; die `installId` aus dem Payload steht ihm also
nicht zur Verfügung. Der Client muss sie deshalb zusätzlich als Header `X-Install-Id` mitschicken.
Fehlt er, greift die Begrenzung nur je IP — was alle hinter einem NAT in denselben Topf wirft.
**Das ist eine Anforderung an Phase 3**, sonst ist das Budget je Installation wirkungslos.

## Naheliegende Erweiterung (bewusst NICHT eingeplant)

Dieselbe DB kann später auch **ausliefern** — Zeilen und Zuordnungen abrufbar machen und damit die
`*.ekpack`-Verteilung ergänzen oder ersetzen. Das ist ein eigener Entwurf (Auslieferung, Caching,
Urheberrecht an den generierten Audios) und gehört nicht in diesen Plan. Erwähnt, damit das
Datenmodell es nicht verbaut — deshalb `state`, `first_seen_utc` und die Herkunftsspalten von
Anfang an.

## Verhältnis zum anderen Plan

`missing-line-reports.md` deckt drei Klassen ab: **L1** Zeile fehlt in der DB, **L2** Audio fehlt,
**L3** Zuordnung falsch. Die zentrale DB erledigt **L1 vollautomatisch und laufend** — dafür braucht
es dann keinen Melde-Knopf mehr. **L2 und L3 bleiben** beim Meldeweg: dass eine Zuordnung falsch ist,
kann nur ein Mensch feststellen, und welches Paket welche Audios enthält, weiß der Server nicht.

Empfehlung: den Paket-Plan auf L2/L3 eindampfen, sobald dieser hier steht.

## Repo-Beschreibung (für GitLab/GitHub)

Englisch, weil GitHub der öffentliche Spiegel ist und die Nutzerschaft international — dieselbe
Begründung wie bei der Tester-Checkliste.

**Kurzbeschreibung (das einzeilige Feld):**

```
Community-fed database of FINAL FANTASY XIV dialogue lines in all four game languages,
collecting what Echokraut's own extraction misses. No user data is ever sent or stored.
```

**Kürzere Fassung, falls das Feld eng ist:**

```
Community-fed database of FFXIV dialogue lines in all four languages — the missing-line
backstop for Echokraut. No user data.
```

**Deutsch, falls irgendwo gebraucht:**

```
Von der Gemeinschaft gespeiste Datenbank der FFXIV-Dialogzeilen in allen vier Spielsprachen —
sie fängt auf, was Echokrauts eigene Extraktion nicht findet. Es werden keine Nutzerdaten übertragen.
```

**Einleitung für die README:**

```
Echolines collects FINAL FANTASY XIV dialogue lines reported by Echokraut installations and
keeps them in one place, in all four game languages.

Echokraut extracts dialogue from the game's own data files, but that extraction is never quite
complete: speakers that cannot be resolved, content added by a patch, sheet families nobody has
looked at yet. Echolines closes that gap from the other end — every line a player actually sees
is reported once, and what the extraction does not know shows up here.

Three things are sent per line: the text, its language, and which NPC speaks it. The player's
character name is replaced by a placeholder before sending. Nothing about the player is
transmitted or stored: no character name, no world, no account, no play time, no file paths.
Reporting is on by default and can be switched off in Echokraut under Settings -> General.

New lines land in a staging table first and are only promoted once three separate installations
have reported them, so a single source cannot inject anything.
```

## Offene Fragen an den User

_Keine mehr — alle drei am 2026-08-10 beantwortet, siehe unten._

**Nicht in diesem Repo zu klären:** unter welcher Adresse der Dienst erreichbar ist. Der Client
verdrahtet sie fest, aber die Entscheidung gehört auf die Plugin-Seite (Echokraut), nicht hierher.

## Entschieden (User, 2026-08-10)

- **Variante A** — Zufalls-GUID je Installation als `installId`.
- **X = 3** für die automatische Beförderung, **plus manuelle Freigabe** in der Web-UI für alles
  darunter.
- **Schalter mit Standard „an"** im Ersteinrichtungsassistenten **und** unter Settings → General,
  ein gemeinsamer Konfigurationswert.
- **Changelog erklärt ausdrücklich**: Standard an, keine Nutzerdaten. Entwurf EN/DE steht oben.
- **PostgreSQL**.
- **`npc_base_id` wird mitgespeichert.**
- **Repo-Name `echolines`.**
- **Web-UI** mit manueller Freigabe, Massenimport der Drive-Dateien und Phase-4-Ansicht.
- **Docker-Image über GitLab CI** (2026-10-30, ursprünglich GitHub Action auf dem Spiegel), GitHub
  ist ein Snapshot-Spiegel analog Echokraut.
- **Admin-Login: ein Betreiberkonto, Passwort-Hash aus einem Secret.** Kein OAuth.
- **`pending_lines` wird nie verworfen** — keine Verfallsregel; dafür Pagination, Indizes und der
  `rejected`-Zustand als einzige Art, Einträge aus der Arbeitsliste zu bekommen.
- **Der Server hält KEINEN eigenen Harvest-Bestand und keine Korrelation zu einer Spielversion.**
  Es kommt an, was ankommt, egal woher. Referenz für „schon bekannt" ist ausschließlich der eigene
  Bestand in `lines`.
- **Zweiter Endpunkt `POST /v1/lines/import`** für den Massenimport, den ein Harvest auf
  Plugin-Seite auslöst: jede Installation darf, anonym, aber **gestückelt, gedrosselt und
  ausnahmslos in die Quarantäne** — dieselbe X=3-Regel wie live gemeldete Zeilen.
- **Herkunft je Stimme (`origin: live | harvest`)** plus `seen_live`/`seen_harvest` an der Zeile.
  Damit bleibt Phase 6 beantwortbar, ohne dass irgendwo ein Referenzstand gepflegt wird.
- **Umfang: JEDE unvertonte Zeile wird gemeldet**, nicht nur die lokal fehlenden. Aufrufpunkt
  deshalb `VoiceMessageProcessor.cs:187` (Schritt 3.5) statt `LogVoiceClip`.
- **Der Harvest läuft je Sprache** — eine Einlieferung ≈ 288.000 Zeilen, nicht 1,15 Mio. X=3 gilt
  damit je Sprache getrennt; der Fortsetzungszustand des Clients ist je Sprache zu führen.
- **Testprojekt `Echolines.Tests` + `.claude/verify.ps1` sind Teil des Plans** (Phase 0, vor der
  Logik) mit den Fällen aus dem Abschnitt „Tests". Summe dadurch 12 statt 9,5 Tage.
