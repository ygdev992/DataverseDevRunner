using System;
using System.Collections.Generic;
using System.IO;
using DataverseDevRunner.ConsoleApp;
using DataverseDevRunner.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataverseDevRunner.Tests
{
    [TestClass]
    public sealed class RunnerTests
    {
        private static readonly Guid UserId = Guid.NewGuid();

        private static ScenarioContext Context(IOrganizationService? service = null) =>
            new ScenarioContext(service ?? new UnsupportedOrganizationService(), new TestTrace(), true,
                UserId, Guid.NewGuid(), "synthetic", new Dictionary<string, string>());

        [TestMethod]
        public void PluginReceivesTargetImagesServicesAndOutputsThroughExecute()
        {
            var context = Context();
            new NormalizePluginScenario().Run(context);
            Assert.AreEqual(2, context.PassedChecks);
        }

        [TestMethod]
        public void ReadOnlyGuardBlocksEveryMutationAndUnknownOrBatchRequests()
        {
            var inner = new RecordingService();
            var guard = new GuardedOrganizationService(inner, false, new TestTrace());
            var entity = new Entity("account", Guid.NewGuid());
            var related = new EntityReferenceCollection();
            var relationship = new Relationship("synthetic");
            var attempts = new Action[]
            {
                () => guard.Create(entity),
                () => guard.Update(entity),
                () => guard.Delete("account", entity.Id),
                () => guard.Associate("account", entity.Id, relationship, related),
                () => guard.Disassociate("account", entity.Id, relationship, related),
                () => guard.Execute(new OrganizationRequest("custom_Action")),
                () => guard.Execute(new OrganizationRequest("ExecuteMultiple")),
                () => guard.Execute(new OrganizationRequest("ExecuteTransaction")),
                () => guard.Execute(new OrganizationRequest("Upsert")),
                () => guard.Execute(new OrganizationRequest("SendEmail"))
            };
            foreach (var attempt in attempts)
                Assert.ThrowsException<InvalidOperationException>(attempt);
            Assert.AreEqual(0, inner.Calls);
        }

        [TestMethod]
        public void GuardDelegatesReadsAndExplicitlyAuthorizedWrites()
        {
            var inner = new RecordingService();
            var guard = new GuardedOrganizationService(inner, false, new TestTrace());
            var id = Guid.NewGuid();
            Assert.AreEqual(id, guard.Retrieve("account", id, new ColumnSet("accountid")).Id);
            guard.RetrieveMultiple(new QueryExpression("account"));
            guard.Execute(new OrganizationRequest("WhoAmI"));
            Assert.AreEqual(3, inner.Calls);
            new GuardedOrganizationService(inner, true, new TestTrace()).Update(new Entity("account", id));
            Assert.AreEqual(4, inner.Calls);
        }

        [TestMethod]
        public void PluginFactoryDoesNotSilentlyImpersonateSystemOrAnotherUser()
        {
            var context = Context();
            var execution = context.NewPluginContext("Update", "account");
            context.ExecutePlugin(new FactoryProbePlugin(UserId, shouldSucceed: true), execution);
            context.ExecutePlugin(new FactoryProbePlugin(Guid.Empty, shouldSucceed: true), execution);
            context.ExecutePlugin(new FactoryProbePlugin(null, shouldSucceed: false), execution);
            context.ExecutePlugin(new FactoryProbePlugin(Guid.NewGuid(), shouldSucceed: false), execution);
        }

        [TestMethod]
        public void HarnessRejectsFakeTransactionsAndInvalidExecutionModes()
        {
            var context = Context();
            var execution = context.NewPluginContext("Update", "account");
            execution.IsInTransaction = true;
            Assert.ThrowsException<NotSupportedException>(() => context.ExecutePlugin(new NeverRunPlugin(), execution));
            execution.IsInTransaction = false;
            execution.Stage = 99;
            Assert.ThrowsException<ArgumentException>(() => context.ExecutePlugin(new NeverRunPlugin(), execution));
            execution.Stage = 20;
            execution.Mode = 2;
            Assert.ThrowsException<ArgumentException>(() => context.ExecutePlugin(new NeverRunPlugin(), execution));
        }

        [TestMethod]
        public void ReplayConvertsSdkValuesAndPreservesImageAliases()
        {
            var id = Guid.NewGuid();
            var replay = new PluginReplayDefinition
            {
                MessageName = "Update",
                PrimaryEntityName = "account",
                PrimaryEntityId = id,
                InputParameters = new Dictionary<string, SdkValue>
                {
                    ["Target"] = new SdkValue
                    {
                        Type = "entity", LogicalName = "account", Id = id,
                        Attributes = new Dictionary<string, SdkValue>
                        {
                            ["revenue"] = new SdkValue { Type = "money", Value = new JValue(12.5m) },
                            ["statuscode"] = new SdkValue { Type = "optionset", Value = new JValue(1) },
                            ["ownerid"] = new SdkValue { Type = "entityreference", LogicalName = "systemuser", Value = new JValue(UserId.ToString("D")) },
                            ["name"] = new SdkValue { Type = "null" }
                        }
                    }
                },
                PreImages = new Dictionary<string, SdkValue>
                {
                    ["AliasFromRegistration"] = new SdkValue { Type = "entity", LogicalName = "account", Id = id }
                }
            };
            var execution = replay.CreateContext(Context());
            var target = (Entity)execution.InputParameters["Target"];
            Assert.AreEqual(id, target.Id);
            Assert.AreEqual(12.5m, target.GetAttributeValue<Money>("revenue").Value);
            Assert.AreEqual(1, target.GetAttributeValue<OptionSetValue>("statuscode").Value);
            Assert.AreEqual(UserId, target.GetAttributeValue<EntityReference>("ownerid").Id);
            Assert.IsNull(target["name"]);
            Assert.IsTrue(execution.PreEntityImages.Contains("AliasFromRegistration"));
        }

        [TestMethod]
        public void ReplayRejectsWrongValueTypesAndNonEntityImages()
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                new SdkValue { Type = "bool", Value = new JValue("false") }.ToSdk());
            Assert.ThrowsException<InvalidDataException>(() =>
                new SdkValue { Type = "unknown", Value = new JValue(1) }.ToSdk());
            var replay = new PluginReplayDefinition
            {
                PreImages = new Dictionary<string, SdkValue>
                {
                    ["Before"] = new SdkValue { Type = "string", Value = new JValue("not an entity") }
                }
            };
            Assert.ThrowsException<InvalidDataException>(() => replay.CreateContext(Context()));
        }

        [DataTestMethod]
        [DataRow("string")]
        [DataRow("guid")]
        [DataRow("bool")]
        [DataRow("int")]
        [DataRow("long")]
        [DataRow("decimal")]
        [DataRow("double")]
        [DataRow("money")]
        [DataRow("optionset")]
        [DataRow("datetime")]
        [DataRow("entityreference")]
        [DataRow("entity")]
        public void ExplicitJsonNullIsSupportedForEverySdkType(string type)
        {
            var value = JsonConvert.DeserializeObject<SdkValue>(
                "{\"Type\":\"" + type + "\",\"Value\":null}")!;
            Assert.IsNull(value.ToSdk());
        }

        [TestMethod]
        public void TypedNullAttributesRemainPresentInReplayTargetAndImages()
        {
            var definition = JsonConvert.DeserializeObject<PluginReplayDefinition>(@"{
                'InputParameters': {
                    'Target': {
                        'Type': 'entity', 'LogicalName': 'account',
                        'Attributes': {
                            'parentaccountid': { 'Type': 'entityreference', 'Value': null },
                            'accountcategorycode': { 'Type': 'optionset', 'Value': null }
                        }
                    }
                },
                'PreImages': {
                    'Before': {
                        'Type': 'entity', 'LogicalName': 'account',
                        'Attributes': { 'name': { 'Type': 'string', 'Value': null } }
                    }
                }
            }")!;
            var execution = definition.CreateContext(Context());
            var target = (Entity)execution.InputParameters["Target"];
            Assert.IsTrue(target.Contains("parentaccountid"));
            Assert.IsNull(target["parentaccountid"]);
            Assert.IsTrue(target.Contains("accountcategorycode"));
            Assert.IsNull(target["accountcategorycode"]);
            Assert.IsFalse(target.Contains("name"));
            Assert.IsTrue(execution.PreEntityImages["Before"].Contains("name"));
            Assert.IsNull(execution.PreEntityImages["Before"]["name"]);
        }

        [TestMethod]
        public void MissingValueAndInvalidTypesDoNotSilentlyBecomeNull()
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                JsonConvert.DeserializeObject<SdkValue>("{\"Type\":\"string\"}")!.ToSdk());
            Assert.ThrowsException<InvalidDataException>(() =>
                JsonConvert.DeserializeObject<SdkValue>("{\"Type\":\"entityreference\"}")!.ToSdk());
            Assert.ThrowsException<InvalidDataException>(() =>
                JsonConvert.DeserializeObject<SdkValue>("{\"Type\":\"unknown\",\"Value\":null}")!.ToSdk());
            Assert.ThrowsException<InvalidDataException>(() =>
                JsonConvert.DeserializeObject<SdkValue>("{\"Type\":\"optionset\",\"Value\":\"null\"}")!.ToSdk());
            Assert.AreEqual("null",
                JsonConvert.DeserializeObject<SdkValue>("{\"Type\":\"string\",\"Value\":\"null\"}")!.ToSdk());
            Assert.AreEqual("",
                JsonConvert.DeserializeObject<SdkValue>("{\"Type\":\"string\",\"Value\":\"\"}")!.ToSdk());
        }

        [TestMethod]
        public void ValuePresenceSurvivesSerializationForReferencesAndExplicitNull()
        {
            var reference = new SdkValue { Type = "entityreference", LogicalName = "account", Id = Guid.NewGuid() };
            var referenceJson = JsonConvert.SerializeObject(reference);
            Assert.IsNotNull(JsonConvert.DeserializeObject<SdkValue>(referenceJson)!.ToSdk());
            var explicitNull = new SdkValue { Type = "entityreference", Value = null };
            var nullJson = JObject.Parse(JsonConvert.SerializeObject(explicitNull));
            Assert.IsTrue(nullJson.ContainsKey("Value"));
            Assert.AreEqual(JTokenType.Null, nullJson["Value"]!.Type);
            Assert.IsNull(nullJson.ToObject<SdkValue>()!.ToSdk());
        }

        [TestMethod]
        public void ReferenceValueAcceptsGuidOrNullWithTheSameLogicalName()
        {
            var id = Guid.NewGuid();
            var json = new JObject
            {
                ["Type"] = "entityreference",
                ["LogicalName"] = "account",
                ["Value"] = id.ToString("D")
            };
            var reference = (EntityReference)json.ToObject<SdkValue>()!.ToSdk()!;
            Assert.AreEqual("account", reference.LogicalName);
            Assert.AreEqual(id, reference.Id);
            json["Value"] = JValue.CreateNull();
            Assert.IsNull(json.ToObject<SdkValue>()!.ToSdk());
        }

        [TestMethod]
        public void ReferenceValueRejectsInvalidGuidsAndAmbiguousIds()
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                new SdkValue { Type = "entityreference", LogicalName = "account", Value = new JValue("null") }.ToSdk());
            Assert.ThrowsException<InvalidDataException>(() =>
                new SdkValue { Type = "entityreference", LogicalName = "account", Value = new JValue(123) }.ToSdk());
            Assert.ThrowsException<InvalidDataException>(() =>
                new SdkValue
                {
                    Type = "entityreference", LogicalName = "account",
                    Value = new JValue(Guid.NewGuid().ToString("D")), Id = Guid.NewGuid()
                }.ToSdk());
            Assert.ThrowsException<InvalidDataException>(() =>
                new SdkValue { Type = "entityreference", Value = new JValue(Guid.NewGuid().ToString("D")) }.ToSdk());
        }

        [TestMethod]
        public void ReplayLoadsLocalAssemblyAndChecksActualPluginOutputs()
        {
            var replay = new PluginReplayDefinition
            {
                AssemblyPath = typeof(SampleNormalizePlugin).Assembly.Location,
                PluginType = typeof(SampleNormalizePlugin).FullName!,
                MessageName = "Update", PrimaryEntityName = "account",
                InputParameters = new Dictionary<string, SdkValue>
                {
                    ["Target"] = new SdkValue
                    {
                        Type = "entity", LogicalName = "account",
                        Attributes = new Dictionary<string, SdkValue>
                        { ["name"] = new SdkValue { Type = "string", Value = new JValue(" demo ") } }
                    }
                },
                PreImages = new Dictionary<string, SdkValue>
                { ["Before"] = new SdkValue { Type = "entity", LogicalName = "account" } },
                ExpectedOutputParameters = new Dictionary<string, SdkValue>
                { ["Changed"] = new SdkValue { Type = "bool", Value = new JValue(true) } }
            };
            var context = Context();
            replay.Run(context);
            Assert.AreEqual(2, context.PassedChecks);
            replay.ExpectedOutputParameters["Changed"].Value = new JValue(false);
            Assert.ThrowsException<ScenarioAssertionException>(() => replay.Run(Context()));
        }

        [TestMethod]
        public void LoaderRejectsNonPluginAndUnavailableConstructorConfiguration()
        {
            var path = typeof(SampleNormalizePlugin).Assembly.Location;
            Assert.ThrowsException<InvalidOperationException>(() =>
                LocalPluginLoader.Load(path, typeof(NormalizePluginScenario).FullName!));
            Assert.ThrowsException<MissingMethodException>(() =>
                LocalPluginLoader.Load(path, typeof(SampleNormalizePlugin).FullName!, "configuration"));
        }

        [TestMethod]
        public void OrganizationMismatchAndUnconfiguredProfilesFailClosed()
        {
            var profile = new ConnectionProfile
            {
                Name = "synthetic", ConnectionStringEnvironmentVariable = "DVR_TEST_UNUSED", ExpectedOrganizationId = Guid.NewGuid()
            };
            Assert.ThrowsException<InvalidOperationException>(() => profile.VerifyOrganization(Guid.NewGuid()));
            profile.VerifyOrganization(profile.ExpectedOrganizationId);
            profile.ExpectedOrganizationId = Guid.Empty;
            Assert.ThrowsException<InvalidDataException>(() => profile.Validate());
        }

        [TestMethod]
        public void ConnectionStringInVariableNameIsRejectedWithoutEchoingItsValue()
        {
            var profile = new ConnectionProfile
            {
                Name = "synthetic",
                ConnectionStringEnvironmentVariable = "AuthType=ClientSecret;ClientSecret=SYNTHETIC-TEST-VALUE;",
                ExpectedOrganizationId = Guid.NewGuid()
            };
            var error = Assert.ThrowsException<InvalidDataException>(() => profile.ReadConnectionString());
            StringAssert.Contains(error.Message, "non la connection string");
            Assert.IsFalse(error.Message.Contains("SYNTHETIC-TEST-VALUE"));
        }

        [TestMethod]
        public void MissingEnvironmentVariableExplainsLocalJsonAlternativeWithoutEchoingConfiguration()
        {
            var variable = "DVR_UNCONFIGURED_" + Guid.NewGuid().ToString("N");
            var profile = new ConnectionProfile
            {
                Name = "synthetic",
                ConnectionStringEnvironmentVariable = variable,
                ExpectedOrganizationId = Guid.NewGuid()
            };
            var error = Assert.ThrowsException<InvalidOperationException>(() => profile.ReadConnectionString());
            StringAssert.Contains(error.Message, "local\\settings.json");
            Assert.IsFalse(error.Message.Contains(variable));
        }

        [TestMethod]
        public void LocalJsonConnectionStringLoadsWithoutEnvironmentVariables()
        {
            const string connectionString = "AuthType=ClientSecret;Url=https://synthetic.invalid;ClientSecret=SYNTHETIC-TEST-VALUE;";
            var path = Path.GetTempFileName();
            try
            {
                var settings = new JObject
                {
                    ["Connections"] = new JArray(new JObject
                    {
                        ["Name"] = "synthetic",
                        ["ConnectionString"] = connectionString,
                        ["ExpectedOrganizationId"] = Guid.NewGuid()
                    })
                };
                File.WriteAllText(path, settings.ToString());
                var loaded = RunnerSettings.Load(path);
                Assert.AreEqual(connectionString, loaded.Connections[0].ReadConnectionString());
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void AmbiguousConnectionSourcesAreRejectedWithoutEchoingSecrets()
        {
            var profile = new ConnectionProfile
            {
                Name = "synthetic",
                ConnectionString = "ClientSecret=SYNTHETIC-TEST-VALUE;",
                ConnectionStringEnvironmentVariable = "DVR_OPTIONAL",
                ExpectedOrganizationId = Guid.NewGuid()
            };
            var error = Assert.ThrowsException<InvalidDataException>(() => profile.ReadConnectionString());
            StringAssert.Contains(error.Message, "non entrambi");
            Assert.IsFalse(error.Message.Contains("SYNTHETIC-TEST-VALUE"));
        }

        [TestMethod]
        public void MissingOrWhitespaceConnectionIsRejectedWithLocalConfigurationInstructions()
        {
            var profile = new ConnectionProfile
            {
                Name = "synthetic", ConnectionString = "  ", ExpectedOrganizationId = Guid.NewGuid()
            };
            var error = Assert.ThrowsException<InvalidDataException>(() => profile.ReadConnectionString());
            StringAssert.Contains(error.Message, "ConnectionString");
            StringAssert.Contains(error.Message, "local\\settings.json");
        }

        [TestMethod]
        public void ExistingEnvironmentVariableProfilesRemainSupported()
        {
            var variable = "DVR_SYNTHETIC_" + Guid.NewGuid().ToString("N");
            const string connectionString = "ClientSecret=SYNTHETIC-TEST-VALUE;";
            try
            {
                Environment.SetEnvironmentVariable(variable, connectionString);
                var profile = new ConnectionProfile
                {
                    Name = "synthetic", ConnectionStringEnvironmentVariable = variable,
                    ExpectedOrganizationId = Guid.NewGuid()
                };
                Assert.AreEqual(connectionString, profile.ReadConnectionString());
            }
            finally { Environment.SetEnvironmentVariable(variable, null); }
        }

        [TestMethod]
        public void OfflineCliFailsOnWrongResultOrConnectedScenario()
        {
            Assert.AreEqual(0, Program.Main(new[] { "--run", "sample.method", "--offline", "--param", "Text= hello ", "--param", "Expected=HELLO" }));
            Assert.AreEqual(1, Program.Main(new[] { "--run", "sample.method", "--offline", "--param", "Expected=WRONG" }));
            Assert.AreEqual(1, Program.Main(new[] { "--run", "plugin.replay", "--offline" }));
            Assert.AreEqual(1, Program.Main(new[] { "--run", "dataverse.whoami", "--offline" }));
            Assert.AreEqual(1, Program.Main(new[] { "--run", "sample.method", "--offline", "--param", "Unexpected=value" }));
        }

        [TestMethod]
        public void DuplicateScenariosAreRejectedInsteadOfRunningAmbiguousCode()
        {
            Assert.ThrowsException<InvalidOperationException>(() => ScenarioCatalog.Validate(new IScenario[]
            { new NormalizePluginScenario(), new NormalizePluginScenario() }));
        }

        private sealed class TestTrace : ITracingService
        {
            public void Trace(string format, params object[] args) { }
        }

        private sealed class RecordingService : UnsupportedOrganizationService
        {
            public int Calls { get; private set; }
            public override void Update(Entity entity) { Calls++; }
            public override Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
            { Calls++; return new Entity(entityName, id); }
            public override EntityCollection RetrieveMultiple(QueryBase query) { Calls++; return new EntityCollection(); }
            public override OrganizationResponse Execute(OrganizationRequest request) { Calls++; return new OrganizationResponse(); }
        }

        private sealed class FactoryProbePlugin : IPlugin
        {
            private readonly Guid? _userId;
            private readonly bool _shouldSucceed;
            public FactoryProbePlugin(Guid? userId, bool shouldSucceed) { _userId = userId; _shouldSucceed = shouldSucceed; }
            public void Execute(IServiceProvider serviceProvider)
            {
                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                if (_shouldSucceed) Assert.IsNotNull(factory.CreateOrganizationService(_userId));
                else Assert.ThrowsException<NotSupportedException>(() => factory.CreateOrganizationService(_userId));
            }
        }

        private sealed class NeverRunPlugin : IPlugin
        {
            public void Execute(IServiceProvider serviceProvider) => Assert.Fail("Il plugin non doveva essere eseguito.");
        }
    }
}
