using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using DataverseDevRunner.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Spectre.Console;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DataverseDevRunner.Tests")]

namespace DataverseDevRunner.ConsoleApp
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var scenarios = DiscoverScenarios();
                if (args.Length > 0)
                    return RunCommand(args, scenarios);
                if (System.Console.IsInputRedirected)
                    throw new InvalidOperationException("Il menu richiede un terminale interattivo. Usare --help o --list.");
                var root = FindRoot();
                Directory.SetCurrentDirectory(root);
                var settings = RunnerSettings.Load(Path.Combine(root, "local", "settings.json"));
                return RunMenu(scenarios, settings, root);
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return 1;
            }
        }

        public static IReadOnlyList<IScenario> DiscoverScenarios()
        {
            var scenarios = typeof(Program).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(IScenario).IsAssignableFrom(t))
                .Select(t => (IScenario)(Activator.CreateInstance(t) ??
                    throw new InvalidOperationException("Impossibile creare scenario " + t.FullName)));
            return ScenarioCatalog.Validate(scenarios);
        }

        private static int RunCommand(string[] args, IReadOnlyList<IScenario> scenarios)
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                System.Console.WriteLine(
                    "DataverseDevRunner (.NET Framework 4.8)\n" +
                    "Senza argomenti: menu interattivo.\n" +
                    "--list: elenco scenari, nessuna connessione.\n" +
                    "--run <id> --offline [--param Nome=Valore ...]: scenario offline sicuro.\n" +
                    "Esempio: --run sample.method --offline --param \"Text= demo \" --param Expected=DEMO\n" +
                    "Le esecuzioni connesse o con possibili effetti esterni richiedono il menu e conferma esplicita.\n" +
                    "Configurazione: ConnectionString in local\\settings.json (cartella esclusa da Git).\n" +
                    "Un processo locale non riproduce sandbox, transazioni o intera pipeline Dataverse.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--list")
            {
                foreach (var scenario in scenarios)
                    System.Console.WriteLine(scenario.Id + " | " + scenario.Description);
                return 0;
            }
            if (args.Length < 3 || args[0] != "--run" || args[2] != "--offline")
                throw new ArgumentException("Argomenti non validi. Usare --help.");
            var selected = scenarios.SingleOrDefault(s => s.Id.Equals(args[1], StringComparison.OrdinalIgnoreCase)) ??
                throw new ArgumentException("Scenario non trovato: " + args[1]);
            if (!selected.SupportsOffline || selected.RequiresWriteAccess || selected.MayHaveExternalEffects)
                throw new InvalidOperationException("Questo scenario richiede il menu interattivo e una conferma esplicita.");
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 3; index < args.Length; index += 2)
            {
                if (args[index] != "--param" || index + 1 == args.Length)
                    throw new ArgumentException("Atteso --param Nome=Valore.");
                var separator = args[index + 1].IndexOf('=');
                if (separator <= 0) throw new ArgumentException("Atteso --param Nome=Valore.");
                var name = args[index + 1].Substring(0, separator);
                if (!selected.Parameters.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || parameters.ContainsKey(name))
                    throw new ArgumentException("Parametro sconosciuto o duplicato: " + name);
                parameters.Add(name, args[index + 1].Substring(separator + 1));
            }
            foreach (var parameter in selected.Parameters)
            {
                if (!parameters.ContainsKey(parameter.Name))
                    parameters[parameter.Name] = parameter.DefaultValue ??
                        throw new ArgumentException("Parametro obbligatorio: " + parameter.Name);
            }
            RunScenario(selected, OfflineContext(new ConsoleTrace(), parameters));
            return 0;
        }

        internal static int RunMenu(IReadOnlyList<IScenario> scenarios, RunnerSettings settings, string? root = null)
        {
            root = root ?? FindRoot();
            while (true)
            {
                ShowScreen();
                var selection = Select("Dove vuoi andare?", new[] { "Esplora il runner", "Area test", "Esci" });
                if (selection == "Esci") return 0;
                try
                {
                    if (selection == "Esplora il runner")
                        RunScenarioMenu(scenarios.Where(IsExample).ToArray(), settings);
                    else
                        RunTestMenu(scenarios, settings, root);
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                    Pause();
                }
            }
        }

        private static void ShowScreen()
        {
            AnsiConsole.Clear();
            ShowHeader();
        }

        private static void ShowHeader()
        {
            var assembly = typeof(Program).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version.ToString();
            var header = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
            header.AddRow(
                new Text(
                    "    .----------.\n" +
                    "   / .--------. \\\n" +
                    "  / /          \\ \\\n" +
                    " | |   < / >    | |\n" +
                    " | |            | |\n" +
                    " | |   [====]   | |\n" +
                    "  \\ \\          / /\n" +
                    "   \\ '--------' /\n" +
                    "    '----||----'\n" +
                    "         ||\n" +
                    "       ==||==", new Style(Color.CornflowerBlue)),
                new Rows(
                    new FigletText("Dataverse").LeftJustified().Color(Color.CornflowerBlue),
                    new FigletText("DevRunner").LeftJustified().Color(Color.CornflowerBlue),
                    new Markup("[bold]DataverseDevRunner[/]  [grey]v" + Markup.Escape(version) + "[/]")));
            AnsiConsole.Write(new Panel(header).Border(BoxBorder.Rounded).BorderColor(Color.CornflowerBlue));
        }

        private static bool IsExample(IScenario scenario) =>
            scenario is NormalizeMethodScenario || scenario is NormalizePluginScenario ||
            scenario is WhoAmIScenario || scenario is ReadAccountPluginScenario;

        private static string Select(string title, IEnumerable<string> choices) =>
            AnsiConsole.Prompt(new SelectionPrompt<string>().Title(title).PageSize(12)
                .UseConverter(Markup.Escape).AddChoices(choices));

        private static void Pause() =>
            AnsiConsole.Prompt(new TextPrompt<string>("Premi [bold]Invio[/] per tornare al menu.").AllowEmpty());

        private static void RunTestMenu(IReadOnlyList<IScenario> scenarios, RunnerSettings settings, string root)
        {
            while (true)
            {
                ShowScreen();
                var selection = Select("[bold]Area test[/]", new[] { "Scenari", "Plugin Replay", "Indietro" });
                if (selection == "Indietro") return;
                try
                {
                    if (selection == "Scenari")
                        RunScenarioMenu(scenarios.Where(s => !IsExample(s) && !(s is ReplayPluginScenario)).ToArray(),
                            settings, root);
                    else
                        RunReplayMenu(settings, root);
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                    Pause();
                }
            }
        }

        private static void ShowGuide(TestGuide guide)
        {
            var points = string.Join("\n", guide.Steps.Select(step => "- " + step));
            AnsiConsole.Write(new Panel(new Text(points)).Header(Markup.Escape(guide.Title))
                .Border(BoxBorder.Rounded).BorderColor(Color.CornflowerBlue));
        }

        private static void RunScenarioMenu(IReadOnlyList<IScenario> scenarios, RunnerSettings settings, string? root = null)
        {
            var ordered = scenarios.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            while (true)
            {
                ShowScreen();
                if (root != null) ShowGuide(TestGuides.Load(root).Scenarios);
                if (ordered.Length == 0)
                    AnsiConsole.Write(new Text("Nessuno scenario disponibile. Aggiungi un file C# in local\\Scenarios e ricompila il runner.\n"));
                var choices = ordered.Select(s => s.Id + " - " + s.Description).Concat(new[] { "Indietro" }).ToArray();
                var selection = Select(root == null ? "[bold]Esplora il runner[/]" : "[bold]Scenari[/]", choices);
                if (selection == "Indietro") return;
                ExecuteInteractive(ordered[Array.IndexOf(choices, selection)], settings);
            }
        }

        private static void RunReplayMenu(RunnerSettings settings, string root)
        {
            while (true)
            {
                ShowScreen();
                ShowGuide(TestGuides.Load(root).PluginReplay);
                var replays = ReplayMenuEntry.Discover(root);
                if (replays.Count == 0)
                    AnsiConsole.Write(new Text("Nessun replay disponibile. Aggiungi un file *.replay.json in local\\Replays.\n"));
                var selection = AnsiConsole.Prompt(new SelectionPrompt<int>().Title("[bold]Plugin Replay[/]")
                    .PageSize(12).AddChoices(Enumerable.Range(0, replays.Count + 1))
                    .UseConverter(index => index == replays.Count ? "Indietro" : Markup.Escape(replays[index].Label)));
                if (selection == replays.Count) return;
                var replay = replays[selection];
                ExecuteInteractive(new ReplayPluginScenario(), settings,
                    new Dictionary<string, string> { ["DefinitionPath"] = replay.Path });
            }
        }

        private static void ExecuteInteractive(IScenario scenario, RunnerSettings settings,
            IReadOnlyDictionary<string, string>? presetParameters = null)
        {
            ShowScreen();
            AnsiConsole.Write(new Text("Scenario: " + scenario.Id + "\n"));
            if (presetParameters != null && presetParameters.TryGetValue("DefinitionPath", out var path))
                AnsiConsole.Write(new Text("Replay selezionato: " + path + "\n"));
            try { RunInteractive(scenario, settings, presetParameters); }
            catch (Exception ex) { ShowError(ex); }
            Pause();
        }

        private static void RunInteractive(IScenario scenario, RunnerSettings settings,
            IReadOnlyDictionary<string, string>? presetParameters)
        {
            var options = new List<string>();
            if (scenario.SupportsOffline) options.Add("Offline");
            options.AddRange(settings.Connections.Select(p => "Dataverse: " + p.Name));
            options.Add("Annulla");
            if (!scenario.SupportsOffline && settings.Connections.Count == 0)
                throw new InvalidOperationException(
                    "Nessun profilo Dataverse configurato. Copiare examples\\settings.example.json in local\\settings.json e adattarlo.");
            var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Modalita' / ambiente")
                .UseConverter(Markup.Escape).AddChoices(options));
            if (selected == "Annulla") return;
            var parameters = PromptParameters(scenario, presetParameters);
            var trace = new ConsoleTrace();
            if (selected == "Offline")
            {
                if ((scenario.MayHaveExternalEffects || scenario.RequiresWriteAccess) &&
                    !Confirm("Lo scenario puo' avere effetti esterni anche offline. Non e' una sandbox.", "ESEGUI"))
                    return;
                RunScenario(scenario, OfflineContext(trace, parameters));
                return;
            }

            var profile = settings.Connections.Single(p => "Dataverse: " + p.Name == selected);
            if (!Confirm("Autenticazione e verifica identita' verso il profilo: " + profile.Name +
                ". Organizzazione attesa: " + profile.ExpectedOrganizationId, "CONNETTI"))
                return;
            using (var client = Connect(profile))
            {
                var identity = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
                profile.VerifyOrganization(identity.OrganizationId);
                var table = new Table().AddColumn("Controllo").AddColumn("Valore");
                table.AddRow("Scenario", Markup.Escape(scenario.Id));
                if (parameters.TryGetValue("DefinitionPath", out var definitionPath))
                    table.AddRow("Replay", Markup.Escape(definitionPath));
                table.AddRow("Profilo", Markup.Escape(profile.Name));
                table.AddRow("Organizzazione verificata", identity.OrganizationId.ToString());
                table.AddRow("Utente autenticato", identity.UserId.ToString());
                table.AddRow("Scritture SDK", scenario.RequiresWriteAccess ? "[red]ABILITATE[/]" : "Bloccate (allowlist letture)");
                table.AddRow("Effetti esterni dichiarati", scenario.MayHaveExternalEffects ? "[red]POSSIBILI[/]" : "Non previsti dallo scenario");
                AnsiConsole.Write(table);
                if (!Confirm("L'esecuzione usa dati reali. Le scritture possono attivare altri plugin e flow.\n" +
                    "HTTP diretto e altre connessioni non sono intercettati. Nessun rollback automatico.", "ESEGUI"))
                    return;
                var guarded = new GuardedOrganizationService(client, scenario.RequiresWriteAccess, trace);
                var context = new ScenarioContext(guarded, trace, false, identity.UserId,
                    identity.OrganizationId, client.ConnectedOrgUniqueName ?? "", parameters)
                { BusinessUnitId = identity.BusinessUnitId };
                RunScenario(scenario, context);
            }
        }

        private static ServiceClient Connect(ConnectionProfile profile)
        {
            var connectionString = profile.ReadConnectionString();
            ServiceClient client;
            try
            {
                client = new ServiceClient(connectionString);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // SDK connection exceptions can contain credentials from the connection string.
                throw new InvalidOperationException("Autenticazione non riuscita (" + ex.GetType().Name +
                    "). Controllare ConnectionString in local\\settings.json, AuthType, endpoint e autorizzazioni. Dettagli SDK omessi per proteggere i segreti.");
            }
            if (!client.IsReady)
            {
                client.Dispose();
                throw new InvalidOperationException(
                    "Client Dataverse non pronto. Controllare connessione e autorizzazioni. LastError non viene mostrato per proteggere i segreti.");
            }
            return client;
        }

        private static Dictionary<string, string> PromptParameters(IScenario scenario,
            IReadOnlyDictionary<string, string>? presetParameters)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in scenario.Parameters)
            {
                if (presetParameters != null && presetParameters.TryGetValue(parameter.Name, out var value))
                {
                    values.Add(parameter.Name, value);
                    continue;
                }
                var prompt = new TextPrompt<string>(Markup.Escape(parameter.Prompt) + ":");
                if (parameter.DefaultValue != null)
                    prompt.DefaultValue(parameter.DefaultValue).HideDefaultValue();
                values.Add(parameter.Name, AnsiConsole.Prompt(prompt));
            }
            return values;
        }

        internal static bool Confirm(string warning, string token)
        {
            AnsiConsole.Write(new Panel(new Text(warning)).BorderColor(Color.Yellow));
            return AnsiConsole.Prompt(new TextPrompt<string>(
                "Digita [bold]" + token + "[/] per proseguire (Invio annulla):").AllowEmpty())
                .Equals(token, StringComparison.OrdinalIgnoreCase);
        }

        private static ScenarioContext OfflineContext(ITracingService trace, IReadOnlyDictionary<string, string> parameters) =>
            new ScenarioContext(new UnsupportedOrganizationService(), trace, true,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"), "synthetic-offline", parameters);

        private static void RunScenario(IScenario scenario, ScenarioContext context)
        {
            var timer = Stopwatch.StartNew();
            context.Trace.Trace("START scenario: {0}; modalita': {1}", scenario.Id, context.IsOffline ? "offline" : "local + Dataverse");
            try
            {
                scenario.Run(context);
                AnsiConsole.MarkupLine("[green]COMPLETATO[/] - " + timer.ElapsedMilliseconds +
                    " ms; verifiche superate: " + context.PassedChecks +
                    (context.PassedChecks == 0 ? " (esecuzione senza asserzioni)" : ""));
            }
            catch
            {
                context.Trace.Trace("FAIL scenario: {0}; durata: {1} ms. Nessun rollback automatico.", scenario.Id, timer.ElapsedMilliseconds);
                throw;
            }
        }

        private static string FindRoot()
        {
            foreach (var start in new[] { System.AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "DataverseDevRunner.sln")))
                        return directory.FullName;
            }
            throw new DirectoryNotFoundException("Root toolkit non trovata. Avviare dalla copia del repository contenente DataverseDevRunner.sln.");
        }

        private static void ShowError(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;
            AnsiConsole.Write(new Panel(new Text(exception.GetType().Name + ": " + exception.Message))
                .Header("[red]ERRORE[/]").BorderColor(Color.Red));
        }
    }

    public sealed class ConsoleTrace : ITracingService
    {
        public void Trace(string format, params object[] args)
        {
            var message = args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
            AnsiConsole.Write(new Text(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine));
        }
    }
}
