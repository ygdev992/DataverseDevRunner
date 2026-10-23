# DataverseDevRunner

Banco di prova personale per eseguire **codice plugin locale contro Dataverse reale**, senza deploy, con menu console e scenari C# estendibili. Include esempi offline sintetici e replay di una DLL plugin da JSON.

Il repository standard non contiene codice, assembly, configurazioni o dati di clienti. Gli adattamenti si fanno nella cartella `local`, esclusa da Git. Non pubblicare una copia adattata senza verificare anche modifiche ai file standard, cronologia Git e autorizzazioni del cliente: `.gitignore` non e' un controllo di sicurezza.

## Prerequisiti e avvio

- Windows con .NET Framework 4.8.
- SDK .NET con supporto ai progetti SDK-style; Visual Studio puo' aprire la soluzione.
- Accesso a NuGet pubblico. I reference assembly net48 sono ripristinati come pacchetto.

```powershell
Set-Location C:\Repos\DataverseDevRunner
dotnet restore .\DataverseDevRunner.sln --source https://api.nuget.org/v3/index.json
dotnet build .\DataverseDevRunner.sln --no-restore
& .\src\DataverseDevRunner.Console\bin\Debug\net48\DataverseDevRunner.exe
```

L'header mostra il logo del runner, il nome e la versione dell'assembly. Il menu e' suddiviso in:

- **Esplora il runner**: esempi `sample.method`, `sample.plugin` e `dataverse.*`.
- **Area test > Scenari**: scenari C# personalizzati compilati da `local\Scenarios`, ordinati per ID.
- **Area test > Plugin Replay**: selezione dei file `*.replay.json` in `local\Replays`, senza digitare percorsi.

Ogni cambio di menu ridisegna la schermata, con un unico header in alto, senza accumulare loghi o menu precedenti. Durante l'esecuzione tracing e risultati restano visibili fino a quando si preme Invio per tornare al menu; a quel punto la schermata viene ripulita.

Le sottocartelle sono supportate in entrambe le aree. I replay sono ordinati per percorso relativo e visualizzati senza il suffisso `.replay.json`; la sottocartella permette di distinguere nomi uguali. L'elenco dei replay viene aggiornato a ogni visualizzazione. Se una cartella e' vuota, il menu indica come aggiungere un test.

Dopo la scelta si selezionano modalita'/ambiente e parametri; il runner mostra identita', autorizzazioni, tracing, durata e numero di verifiche superate. Nessun tracing, connection string o risultato viene persistito su disco dal runner.

### Organizzazione locale e guide

```text
local\
  settings.json
  References.props
  Scenarios\
    CustomScenario.cs
  Replays\
    example.replay.json
```

Le sezioni Scenari e Plugin Replay mostrano un riquadro con le operazioni principali. Per modificarle aprire **`configuration\test-guides.json`** nella root del repository: `Scenarios` e `PluginReplay` contengono ciascuno un `Title` e una lista ordinata `Steps`. Aggiungere, modificare o riordinare le stringhe per aggiornare i punti del riquadro. Il contenuto e' testo semplice, non markup.

Il file viene riletto entrando nella sezione e tornando all'elenco, senza ricompilazione o riavvio. Deve contenere entrambe le sezioni, titoli non vuoti e almeno un punto non vuoto per sezione; errori e file mancanti vengono segnalati esplicitamente. Le guide fanno parte del progetto standard: mantenerle generiche, senza dettagli cliente.

Non e' necessario configurare Dataverse per usare gli esempi offline:

```powershell
$runner = '.\src\DataverseDevRunner.Console\bin\Debug\net48\DataverseDevRunner.exe'
& $runner --help
& $runner --list
& $runner --run sample.plugin --offline
& $runner --run sample.method --offline --param 'Text= hello ' --param Expected=HELLO
```

I comandi non interattivi non aprono connessioni e rifiutano scenari con possibili effetti esterni o scritture. Un'asserzione fallita restituisce exit code 1. Nel menu un errore viene mostrato e si torna alla selezione; uscire dal menu restituisce 0.

## Connessione Dataverse

```powershell
New-Item -ItemType Directory -Path .\local -Force
Copy-Item .\examples\settings.example.json .\local\settings.json
```

Modificare il profilo locale:

- `Name`: etichetta visibile nel menu.
- `ConnectionString`: connection string completa, salvata direttamente nel JSON locale.
- `ExpectedOrganizationId`: Organization ID effettivo dell'ambiente di sviluppo, ricavato da informazioni amministrative affidabili. Il GUID vuoto dell'esempio viene rifiutato. Non e' Tenant ID, Environment ID o User ID.

Esempio **solo schematico** di `local\settings.json`:

```json
{
  "Connections": [
    {
      "Name": "Development",
      "ConnectionString": "AuthType=ClientSecret;Url=https://YOUR-ORG.crm.dynamics.com;ClientId=YOUR-APP-ID;ClientSecret=YOUR-SECRET;",
      "ExpectedOrganizationId": "INSERIRE-ORGANIZATION-ID-REALE"
    }
  ]
}
```

Sostituire i segnaposto con i propri valori e avviare la console, anche con F5 da Visual Studio. **Non servono variabili ambiente o comandi PowerShell per la connessione.** Il runner legge il file all'avvio: dopo una modifica chiudere e riaprire l'applicazione. In JSON gli eventuali caratteri `\` e `"` nella stringa vanno scritti rispettivamente come `\\` e `\"`.

La cartella `local\` e' esclusa da Git, ma il file contiene credenziali **in chiaro**: proteggerne l'accesso e non condividerlo, allegarlo o copiarlo nel repository standard. Il runner non mostra la connection string nei propri messaggi d'errore.

Per la prima prova selezionare **Esplora il runner > dataverse.whoami**, quindi il profilo configurato, confermare `CONNETTI` e, dopo la verifica dell'organizzazione, `ESEGUI`. Questo scenario non scrive dati e non esegue plugin locali.

Se il file esistente usa `ConnectionStringEnvironmentVariable`, sostituire **il nome del campo** con `ConnectionString` e inserire la stringa completa come valore. Conservare Name ed ExpectedOrganizationId.

Per compatibilita', `ConnectionStringEnvironmentVariable` rimane un'alternativa facoltativa per chi gia' la utilizza; non configurare entrambi i campi nello stesso profilo. Sono utilizzabili gli AuthType supportati da `ServiceClient`, configurando anche application user e ruoli minimi necessari.

Prima di autenticarsi il menu richiede `CONNETTI`. Esegue quindi WhoAmI e blocca l'avvio se Organization ID non corrisponde al profilo. Prima dello scenario richiede `ESEGUI`, mostrando l'utente autenticato e se le scritture SDK sono abilitate.

Il runner non contatta automaticamente alcun ambiente all'avvio. Per gli scenari connessi la connessione dura una sola esecuzione.

## Scenari inclusi

| ID | Comportamento |
|---|---|
| `sample.method` | Metodo sintetico di normalizzazione e confronto con il risultato atteso; disponibile offline. |
| `sample.plugin` | `IPlugin.Execute` con Target, PreImage e output; disponibile offline, nessuna persistenza. |
| `dataverse.whoami` | Verifica identita' sulla connessione reale, senza scritture. |
| `dataverse.local-plugin-read` | Plugin sintetico **locale** che legge solo `accountid` di un account reale e verifica il risultato. |
| `plugin.replay` | Carica una DLL locale e ricostruisce il contesto da JSON; richiede conferma di scritture e possibili effetti esterni. |

Il completamento senza eccezioni non equivale alla verifica di un requisito: l'interfaccia distingue il numero di asserzioni superate e segnala le esecuzioni senza asserzioni.

## Eseguire un proprio plugin senza deploy

1. Compilare il plugin nel suo repository originale con DLL, PDB e dipendenze compatibili. Non cambiare ILMerge o il packaging del plugin per aggirare problemi del runner.
2. Creare `local\Replays` e copiare `examples\plugin.replay.example.json` in `local\Replays\nome.replay.json`.
3. Adattare DLL, tipo completo, messaggio, stage, mode, entita', ID, parametri e alias delle immagini alla registrazione effettiva del plugin. Gli ID dell'esempio sono sintetici e non individuano record utilizzabili.
4. Avviare il menu, selezionare **Area test > Plugin Replay**, scegliere il replay nell'elenco e poi il profilo di sviluppo.

I replay precedentemente nella root di `local` devono essere spostati in `local\Replays`. Se `AssemblyPath` e' relativo, aggiornarlo per mantenere il riferimento alla stessa DLL dopo lo spostamento; i percorsi assoluti restano invariati. Gli altri JSON, inclusi i profili di connessione, non vengono elencati.

`AssemblyPath` assoluto e' consigliato; un percorso relativo viene risolto rispetto al JSON. I file JSON sono letti a ogni esecuzione. Le DLL vengono caricate nel processo e **non sono scaricabili singolarmente**: chiudere il runner prima di ricompilare o cambiare versione/cliente, quindi riavviarlo. Dipendenze incompatibili o DLL omonime richiedono processi separati, non modifiche al plugin per forzare il caricamento.

Il loader supporta costruttori pubblici senza parametri, `(string unsecure)` e `(string unsecure, string secure)`. `UnsecureConfiguration` puo' essere definita nel JSON locale; per configurazioni riservate usare `SecureConfigurationEnvironmentVariable`, che contiene **solo il nome** della variabile ambiente.

Valori SDK JSON supportati: `null`, `string`, `guid`, `bool`, `int`, `long`, `decimal`, `double`, `money`, `optionset`, `datetime`, `entityreference`, `entity`. `datetime` usa formato ISO round-trip, ad esempio `2026-01-01T00:00:00.0000000Z`. Lookup, Money e OptionSet sono espliciti, non dedotti dai nomi degli attributi:

```json
{
  "ownerid": { "Type": "entityreference", "LogicalName": "systemuser", "Value": "11111111-1111-1111-1111-111111111111" },
  "revenue": { "Type": "money", "Value": 12.50 },
  "statuscode": { "Type": "optionset", "Value": 1 }
}
```

Per rappresentare un attributo nullo, mantenere il suo tipo e usare sempre **`"Value": null`**, senza virgolette intorno a `null`:

```json
{
  "name": { "Type": "string", "Value": null },
  "parentaccountid": { "Type": "entityreference", "LogicalName": "account", "Value": null },
  "accountcategorycode": { "Type": "optionset", "Value": null },
  "revenue": { "Type": "money", "Value": null }
}
```

La stessa convenzione vale per tutti i tipi SDK supportati, in Target, immagini e output attesi. Per i lookup usare sempre `Value`: una stringa GUID se valorizzati, oppure `null` se vuoti. `LogicalName` identifica la tabella referenziata: e' obbligatorio per i lookup valorizzati e puo' rimanere anche per quelli nulli, dove viene ignorato. Non scrivere `"Value": "null"`, che non e' un GUID valido.

`"Value": null` indica un attributo presente con valore nullo. Omettere interamente l'attributo significa invece non trasmetterlo: la distinzione e' importante negli Update. Per le stringhe, `"Value": ""` resta una stringa vuota e `"Value": "null"` resta il testo letterale `null`.

Il vecchio formato `{ "Type": "null" }` e il campo `Id` dei lookup restano supportati per compatibilita'. Nei nuovi lookup usare `Value`; non configurare contemporaneamente un `Value` non nullo e un `Id`. La mancanza di `Value` non diventa automaticamente un null.

`Id` rimane invece l'identita' delle Entity, ad esempio il record `Target` e le immagini: non va confuso con il valore di un attributo lookup.

`ExpectedOutputParameters` permette verifiche automatiche sugli output usando lo stesso formato. Per EntityCollection, query, tipi early-bound, contesti annidati o logica di verifica articolata usare uno scenario C#.

## Aggiungere scenari e testare metodi

```powershell
New-Item -ItemType Directory -Path .\local\Scenarios -Force
Copy-Item .\examples\CustomScenario.cs.example .\local\Scenarios\CustomScenario.cs
```

Le classi concrete che implementano `IScenario`, con costruttore pubblico senza parametri, vengono scoperte automaticamente nell'assembly console. Per gli scenari locali basta estendere `Scenario`; un Id duplicato viene rifiutato. I file `local\Scenarios\**\*.cs` sono inclusi dalla compilazione, senza modificare progetti o menu.

Dopo aggiunte o modifiche agli scenari/riferimenti, chiudere e ricompilare il runner, quindi scegliere **Area test > Scenari**. Non serve ricompilare per modificare un replay JSON.

Se occorre un riferimento tipizzato al plugin o alle sue dipendenze, copiare `examples\References.props.example` in `local\References.props` e adattare i percorsi. Per un metodo pubblico richiamabile direttamente, `Run` puo' istanziare il servizio applicativo del plugin, fornire `context.Service` / `context.Trace` e verificare l'esito con `context.Check(...)`. Non duplicare la logica produttiva nello scenario.

Se il metodo dipende da campi inizializzati da una base plugin, usare `context.ExecutePlugin(...)` e l'entry point normale. Non vengono usati reflection su metodi privati, iniezioni di campi privati o refactoring automatici del repository cliente.

Dichiarare esplicitamente `RequiresWriteAccess` e `MayHaveExternalEffects`. La base considera possibili gli effetti esterni; l'accesso SDK resta read-only finche' lo scenario non dichiara le scritture e l'operatore non conferma. Gli scenari devono usare `context.Service` per beneficiare del blocco; una connessione autonoma lo aggira.

## Limiti importanti

- Esegue codice fidato sul PC: **non e' una sandbox di sicurezza**. Una DLL o uno scenario possono accedere a file, rete e variabili ambiente.
- Il contesto simula l'invocazione, non l'intera pipeline Dataverse. Non verifica step registrati, filtering attributes, ordine, security sandbox, timeout, retry di piattaforma o isolamento transazionale.
- Stage, mode e immagini descrivono lo scenario; impostare mode async non avvia un servizio asincrono reale.
- Non viene eseguita automaticamente l'operazione principale: un Target modificato in PreOperation **non viene salvato**. In PostOperation le modifiche al solo Target non persistono nemmeno nella piattaforma.
- Le chiamate esplicite a `context.Service` sono reali. Le scritture possono innescare plugin e flow **gia' distribuiti**, anche quelli dello stesso tipo che si sta provando. Nessun bypass e' applicato automaticamente.
- Non esiste una transazione attorno all'intera esecuzione: errori, interruzioni o asserzioni fallite non annullano scritture gia' effettuate. Cleanup e dati di prova vanno progettati per scenario.
- Il guard SDK blocca Create/Update/Delete/Associate/Disassociate e tutte le richieste Execute fuori da un'allowlist di lettura, incluse Custom API, batch e azioni non classificate. Non intercetta HTTP diretto, SQL, filesystem o SDK creati separatamente.
- `CreateOrganizationService(context.UserId)` e `Guid.Empty` usano il servizio corrente. Altri utenti e `null` (SYSTEM nella pipeline) sono rifiutati: nessuna impersonation simulata silenziosamente.
- Le interfacce avanzate del contesto e i servizi non implementati generano errori espliciti. Il contesto reale di transazione non e' simulato; `IsInTransaction = true` viene rifiutato.
- Tracing ed errori possono includere dati sensibili se prodotti dal plugin. Restano visibili nel terminale: evitare screen recording, redirezioni o condivisioni non autorizzate.
- .NET Framework 4.8 e' scelto per supportare plugin Framework esistenti su Windows. Non garantisce compatibilita' con qualunque dipendenza. Un futuro host .NET moderno va aggiunto separatamente, non ottenuto cambiando alla cieca il target del runner.

## Debug e verifiche

Aprire `DataverseDevRunner.sln`, impostare la console come startup project, compilare in Debug e avviare con F5. Usare la DLL e il PDB corrispondenti alla build locale del plugin. Quando i simboli sono caricati, i breakpoint nel sorgente originale possono essere raggiunti anche se il plugin appartiene a un'altra soluzione. E' possibile anche usare Attach to Process sul runner.

```powershell
dotnet test .\tests\DataverseDevRunner.Tests\DataverseDevRunner.Tests.csproj --no-restore
```

La suite standard usa solo dati sintetici e non legge connessioni o ambienti cliente. Le prove connesse devono essere autorizzate e svolte in un ambiente di sviluppo con privilegi minimi e dati appropriati. La modalita' remota che attiva il plugin distribuito non e' inclusa in questa prima versione.
