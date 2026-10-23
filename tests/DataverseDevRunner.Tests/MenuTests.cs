using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataverseDevRunner.ConsoleApp;
using DataverseDevRunner.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DataverseDevRunner.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class MenuTests
    {
        [DataTestMethod]
        [DataRow(80)]
        [DataRow(120)]
        public void EnlargedHeaderFitsCommonTerminalWidths(int width)
        {
            using (var root = new MenuRoot())
            using (var terminal = new ScriptedConsole(width))
            {
                terminal.Input.Choose(2);
                RunMenu(terminal, root, Array.Empty<IScenario>());
                var header = terminal.Output.Substring(0, terminal.Output.IndexOf("Dove vuoi andare?", StringComparison.Ordinal));
                var plain = System.Text.RegularExpressions.Regex.Replace(header, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                var lines = plain.Replace("\r", "").Split('\n');
                Assert.IsTrue(lines.Length >= 12, "The enlarged banner must occupy more than the original eight lines.");
                Assert.IsTrue(lines.All(line => line.Length <= width), "The header must not overflow the terminal.");
            }
        }

        [DataTestMethod]
        [DataRow("CONNETTI", "connetti", true)]
        [DataRow("CONNETTI", "CoNnEtTi", true)]
        [DataRow("ESEGUI", "esegui", true)]
        [DataRow("ESEGUI", "EsEgUi", true)]
        [DataRow("CONNETTI", "", false)]
        [DataRow("ESEGUI", "no", false)]
        public void ConfirmationIgnoresCaseButStillRequiresTheExpectedToken(
            string token, string input, bool expected)
        {
            using (var terminal = new ScriptedConsole())
            {
                terminal.Input.PushText(input);
                terminal.Input.PushKey(ConsoleKey.Enter);
                var original = AnsiConsole.Console;
                try
                {
                    AnsiConsole.Console = terminal;
                    Assert.AreEqual(expected, Program.Confirm("Synthetic confirmation", token));
                }
                finally { AnsiConsole.Console = original; }
            }
        }

        [TestMethod]
        public void MenuExecutesOfflinePluginAndReturnsToSelection()
        {
            using (var terminal = new ScriptedConsole())
            {
                terminal.Input.Choose(); // Explore.
                terminal.Input.Choose(); // Plugin.
                terminal.Input.Choose(); // Offline.
                terminal.Input.Choose(); // Return after execution.
                terminal.Input.Choose(1); // Back.
                terminal.Input.Choose(2); // Exit.
                var original = AnsiConsole.Console;
                try
                {
                    AnsiConsole.Console = terminal;
                    Assert.AreEqual(0, Program.RunMenu(new[] { new NormalizePluginScenario() }, new RunnerSettings()));
                    Assert.IsTrue(terminal.Screens.Count > 1, "Navigation must redraw the screen.");
                    AssertSingleHeaderPerScreen(terminal);
                    StringAssert.Contains(terminal.Output, "COMPLETATO");
                    StringAssert.Contains(terminal.Output, "verifiche superate: 2");
                    StringAssert.Contains(terminal.Output, "DataverseDevRunner");
                    var executionScreen = terminal.Screens.Single(screen => screen.Contains("COMPLETATO"));
                    StringAssert.Contains(executionScreen, "Premi");
                    Assert.IsFalse(terminal.Screens.Last().Contains("COMPLETATO"),
                        "Return to a clean menu only after acknowledging the results.");
                    StringAssert.Contains(terminal.Output, "v" + typeof(Program).Assembly.GetName().Version.ToString(3));
                    Assert.IsFalse(terminal.Output.Contains("Codice locale, connessioni esplicite"));
                    Assert.IsFalse(terminal.Output.Contains("I plugin vengono eseguiti"));
                    Assert.IsFalse(terminal.Output.Contains("Nessun rollback globale"));
                    Assert.IsFalse(terminal.Output.Contains("ERRORE"));
                }
                finally { AnsiConsole.Console = original; }
            }
        }

        [TestMethod]
        public void CancellingConnectionDoesNotReadSecretsOrOpenDataverse()
        {
            using (var terminal = new ScriptedConsole())
            {
                terminal.Input.Choose();
                terminal.Input.Choose();
                terminal.Input.Choose();
                terminal.Input.Choose(); // Cancel connection.
                terminal.Input.Choose();
                terminal.Input.Choose(1);
                terminal.Input.Choose(2);
                var settings = new RunnerSettings
                {
                    Connections =
                    {
                        new ConnectionProfile
                        {
                            Name = "Synthetic development",
                            ConnectionStringEnvironmentVariable = "DVR_INTENTIONALLY_UNCONFIGURED_" + Guid.NewGuid().ToString("N"),
                            ExpectedOrganizationId = Guid.NewGuid()
                        }
                    }
                };
                var original = AnsiConsole.Console;
                try
                {
                    AnsiConsole.Console = terminal;
                    Assert.AreEqual(0, Program.RunMenu(new[] { new WhoAmIScenario() }, settings));
                    StringAssert.Contains(terminal.Output, "CONNETTI");
                    Assert.IsFalse(terminal.Output.Contains("ERRORE"));
                    Assert.IsFalse(terminal.Output.Contains("START scenario"));
                }
                finally { AnsiConsole.Console = original; }
            }
        }

        [TestMethod]
        public void TestAreaExecutesCustomScenarioWithoutShowingExamples()
        {
            using (var root = new MenuRoot())
            using (var terminal = new ScriptedConsole())
            {
                terminal.Input.Choose(1);
                terminal.Input.Choose();
                terminal.Input.Choose();
                terminal.Input.Choose();
                terminal.Input.Choose();
                terminal.Input.Choose(1);
                terminal.Input.Choose(2);
                terminal.Input.Choose(2);
                RunMenu(terminal, root, new IScenario[] { new NormalizeMethodScenario(), new LocalTestScenario(), new ReplayPluginScenario() });
                StringAssert.Contains(terminal.Output, "Custom guide [plain]");
                StringAssert.Contains(terminal.Output, "- Configure synthetic scenario");
                StringAssert.Contains(terminal.Output, "custom.local-test");
                StringAssert.Contains(terminal.Output, "COMPLETATO");
                Assert.IsFalse(terminal.Output.Contains("sample.method"));
                Assert.IsFalse(terminal.Output.Contains("plugin.replay -"));
            }
        }

        [TestMethod]
        public void ReplaySelectionSuppliesThePathWithoutPromptingOrConnecting()
        {
            using (var root = new MenuRoot())
            using (var terminal = new ScriptedConsole())
            {
                Directory.CreateDirectory(Path.Combine(root.Path, "local", "Replays", "nested"));
                File.WriteAllText(Path.Combine(root.Path, "local", "Replays", "nested", "Indietro.replay.json"), "{}");
                terminal.Input.Choose(1);
                terminal.Input.Choose(1);
                terminal.Input.Choose();
                terminal.Input.Choose();
                terminal.Input.Choose(); // Cancel before secrets are read.
                terminal.Input.Choose();
                terminal.Input.Choose(1);
                terminal.Input.Choose(2);
                terminal.Input.Choose(2);
                var settings = new RunnerSettings
                {
                    Connections =
                    {
                        new ConnectionProfile
                        {
                            Name = "Synthetic",
                            ConnectionStringEnvironmentVariable = "DVR_UNCONFIGURED_" + Guid.NewGuid().ToString("N"),
                            ExpectedOrganizationId = Guid.NewGuid()
                        }
                    }
                };
                RunMenu(terminal, root, Array.Empty<IScenario>(), settings);
                StringAssert.Contains(terminal.Output, "Replay guide");
                StringAssert.Contains(terminal.Output, @"Replay selezionato: ");
                StringAssert.Contains(terminal.Output, @"nested\Indietro.replay.json");
                StringAssert.Contains(terminal.Output, "CONNETTI");
                Assert.IsFalse(terminal.Output.Contains("Percorso definizione replay"));
                Assert.IsFalse(terminal.Output.Contains("START scenario"));
                Assert.IsFalse(terminal.Output.Contains("ERRORE"));
            }
        }

        [TestMethod]
        public void EmptyTestFoldersExplainHowToAddTestsAndAllowGoingBack()
        {
            using (var root = new MenuRoot())
            using (var terminal = new ScriptedConsole())
            {
                terminal.Input.Choose(1);
                terminal.Input.Choose();
                terminal.Input.Choose();
                terminal.Input.Choose(1);
                terminal.Input.Choose();
                terminal.Input.Choose(2);
                terminal.Input.Choose(2);
                RunMenu(terminal, root, new IScenario[] { new NormalizePluginScenario(), new ReplayPluginScenario() });
                StringAssert.Contains(terminal.Output, "Nessuno scenario disponibile");
                StringAssert.Contains(terminal.Output, "Nessun replay disponibile");
                Assert.IsFalse(terminal.Output.Contains("sample.plugin"));
                Assert.IsFalse(terminal.Output.Contains("ERRORE"));
            }
        }

        [TestMethod]
        public void ReplayDiscoveryIsRecursiveSortedAndRestrictedToTheDedicatedFolder()
        {
            using (var root = new MenuRoot())
            {
                var folder = Path.Combine(root.Path, "local", "Replays");
                Directory.CreateDirectory(Path.Combine(folder, "nested"));
                File.WriteAllText(Path.Combine(folder, "z.replay.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "A.replay.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "settings.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "nested", "A.replay.json"), "{}");
                File.WriteAllText(Path.Combine(root.Path, "local", "outside.replay.json"), "{}");
                var entries = ReplayMenuEntry.Discover(root.Path);
                CollectionAssert.AreEqual(new[] { "A", @"nested\A", "z" }, entries.Select(e => e.Label).ToArray());
                Assert.IsTrue(entries.All(e => File.Exists(e.Path)));
            }
        }

        [TestMethod]
        public void GuideEditsAreReadWithoutRecompilation()
        {
            using (var root = new MenuRoot())
            {
                var guides = TestGuides.Load(root.Path);
                guides.Scenarios.Steps.Add("An additional operation");
                root.Save(guides);
                CollectionAssert.AreEqual(guides.Scenarios.Steps, TestGuides.Load(root.Path).Scenarios.Steps);
            }
        }

        [DataTestMethod]
        [DataRow("{}")]
        [DataRow("null")]
        [DataRow("{ not valid JSON")]
        [DataRow("{\"Scenarios\":null,\"PluginReplay\":null}")]
        [DataRow("{\"Scenarios\":{\"Title\":\"Guide\",\"Steps\":[]},\"PluginReplay\":{\"Title\":\"Guide\",\"Steps\":[\"Step\"]}}")]
        [DataRow("{\"Scenarios\":{\"Title\":\"Guide\",\"Steps\":[\" \"]},\"PluginReplay\":{\"Title\":\"Guide\",\"Steps\":[\"Step\"]}}")]
        [DataRow("{\"Scenarios\":{\"Title\":\" \",\"Steps\":[\"Step\"]},\"PluginReplay\":{\"Title\":\"Guide\",\"Steps\":[\"Step\"]}}")]
        public void InvalidGuidesAreRejected(string json)
        {
            using (var root = new MenuRoot())
            {
                File.WriteAllText(root.GuidePath, json);
                try
                {
                    TestGuides.Load(root.Path);
                    Assert.Fail("Invalid guides must not be silently replaced.");
                }
                catch (JsonException) { }
                catch (InvalidDataException) { }
            }
        }

        [TestMethod]
        public void MissingGuideShowsAnActionableErrorAndKeepsNavigationAvailable()
        {
            using (var root = new MenuRoot())
            using (var terminal = new ScriptedConsole())
            {
                File.Delete(root.GuidePath);
                terminal.Input.Choose(1);
                terminal.Input.Choose();
                terminal.Input.Choose(); // Acknowledge error.
                terminal.Input.Choose(2);
                terminal.Input.Choose(2);
                RunMenu(terminal, root, Array.Empty<IScenario>());
                StringAssert.Contains(terminal.Output, "ERRORE");
                StringAssert.Contains(terminal.Output, @"configuration\test-guides.json");
            }
        }

        private static void RunMenu(ScriptedConsole terminal, MenuRoot root, IReadOnlyList<IScenario> scenarios,
            RunnerSettings? settings = null)
        {
            var original = AnsiConsole.Console;
            try
            {
                AnsiConsole.Console = terminal;
                Assert.AreEqual(0, Program.RunMenu(scenarios, settings ?? new RunnerSettings(), root.Path));
                AssertSingleHeaderPerScreen(terminal);
            }
            finally { AnsiConsole.Console = original; }
        }

        private static void AssertSingleHeaderPerScreen(ScriptedConsole terminal)
        {
            foreach (var screen in terminal.Screens)
                Assert.AreEqual(1, screen.Split(new[] { "DataverseDevRunner" },
                    StringSplitOptions.None).Length - 1, "Each screen must have exactly one header.");
        }

        private sealed class LocalTestScenario : Scenario
        {
            public override string Id => "custom.local-test";
            public override string Description => "Synthetic local scenario";
            public override bool SupportsOffline => true;
            public override bool MayHaveExternalEffects => false;
            public override void Run(ScenarioContext context) => context.Check(true, "Synthetic check");
        }

        private sealed class MenuRoot : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DvrMenu-" + Guid.NewGuid().ToString("N"));
            public string GuidePath => System.IO.Path.Combine(Path, "configuration", "test-guides.json");

            public MenuRoot()
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Path, "configuration"));
                Save(new TestGuides
                {
                    Scenarios = new TestGuide { Title = "Custom guide [plain]", Steps = { "Configure synthetic scenario" } },
                    PluginReplay = new TestGuide { Title = "Replay guide", Steps = { "Configure synthetic replay" } }
                });
            }

            public void Save(TestGuides guides) => File.WriteAllText(GuidePath, JsonConvert.SerializeObject(guides));
            public void Dispose() => Directory.Delete(Path, true);
        }

        // The upstream testing package targets modern .NET, not this runner's net48 host.
        private sealed class ScriptedConsole : IAnsiConsole, IDisposable
        {
            private readonly StringWriter _output = new StringWriter();
            private readonly IAnsiConsole _inner;
            private readonly List<int> _screenStarts = new List<int>();

            public ScriptedConsole(int width = 160)
            {
                _inner = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(_output),
                    Interactive = InteractionSupport.Yes,
                    Ansi = AnsiSupport.Yes
                });
                _inner.Profile.Width = width;
            }

            public string Output => _output.ToString();
            public IReadOnlyList<string> Screens
            {
                get
                {
                    var output = Output;
                    return _screenStarts.Select((start, index) => output.Substring(start,
                        (index + 1 < _screenStarts.Count ? _screenStarts[index + 1] : output.Length) - start)).ToArray();
                }
            }
            public ScriptedInput Input { get; } = new ScriptedInput();
            IAnsiConsoleInput IAnsiConsole.Input => Input;
            public Profile Profile => _inner.Profile;
            public IAnsiConsoleCursor Cursor => _inner.Cursor;
            public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;
            public RenderPipeline Pipeline => _inner.Pipeline;
            public void Clear(bool home)
            {
                _inner.Clear(home);
                _screenStarts.Add(_output.GetStringBuilder().Length);
            }
            public void Write(IRenderable renderable) => _inner.Write(renderable);
            public void Dispose() => _output.Dispose();
        }

        private sealed class ScriptedInput : IAnsiConsoleInput
        {
            private readonly Queue<ConsoleKeyInfo> _keys = new Queue<ConsoleKeyInfo>();
            public void PushText(string text)
            {
                foreach (var character in text)
                    _keys.Enqueue(new ConsoleKeyInfo(
                        character, (ConsoleKey)char.ToUpperInvariant(character), false, false, false));
            }
            public void PushKey(ConsoleKey key) =>
                _keys.Enqueue(new ConsoleKeyInfo(key == ConsoleKey.Enter ? '\r' : '\0', key, false, false, false));
            public void Choose(int index = 0)
            {
                for (var i = 0; i < index; i++) PushKey(ConsoleKey.DownArrow);
                PushKey(ConsoleKey.Enter);
            }
            public bool IsKeyAvailable() => _keys.Count != 0;
            public ConsoleKeyInfo? ReadKey(bool intercept) => _keys.Count != 0
                ? _keys.Dequeue()
                : throw new InvalidOperationException("Il menu ha richiesto input non previsto dal test.");
            public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(ReadKey(intercept));
            }
        }
    }
}
