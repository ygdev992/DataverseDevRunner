using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataverseDevRunner.Core
{
    public sealed class SdkValue
    {
        private JToken? _value;
        private bool _valueSpecified;

        public string Type { get; set; } = "";
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public JToken? Value
        {
            get => _value;
            set
            {
                _value = value;
                _valueSpecified = true;
            }
        }
        public string? LogicalName { get; set; }
        public Guid? Id { get; set; }
        public Dictionary<string, SdkValue> Attributes { get; set; } = new Dictionary<string, SdkValue>();

        public bool ShouldSerializeValue() => _valueSpecified;

        public object? ToSdk()
        {
            var isNull = _valueSpecified && (Value == null || Value.Type == JTokenType.Null);
            switch (Type)
            {
                case "null": return null;
                case "string": return isNull ? null : RequiredValue(JTokenType.String).Value<string>();
                case "guid": return isNull ? null : RequiredGuidValue();
                case "bool": return isNull ? null : RequiredValue(JTokenType.Boolean).Value<bool>();
                case "int": return isNull ? null : RequiredValue(JTokenType.Integer).Value<int>();
                case "long": return isNull ? null : RequiredValue(JTokenType.Integer).Value<long>();
                case "decimal": return isNull ? null : Number().Value<decimal>();
                case "double": return isNull ? null : Number().Value<double>();
                case "money": return isNull ? null : new Money(Number().Value<decimal>());
                case "optionset": return isNull ? null : new OptionSetValue(RequiredValue(JTokenType.Integer).Value<int>());
                case "datetime":
                    return isNull ? null : DateTime.ParseExact(RequiredValue(JTokenType.String).Value<string>()!, "o",
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                case "entityreference":
                    if (isNull) return null;
                    if (_valueSpecified && Id.HasValue)
                        throw new InvalidDataException("Per entityreference usare solo Value oppure il campo legacy Id, non entrambi.");
                    var referenceId = _valueSpecified
                        ? RequiredGuidValue()
                        : Id ?? throw new InvalidDataException("Value obbligatorio per entityreference: inserire un GUID oppure null.");
                    return new EntityReference(RequiredLogicalName(), referenceId);
                case "entity":
                    if (isNull) return null;
                    var entity = new Entity(RequiredLogicalName());
                    if (Id.HasValue) entity.Id = Id.Value;
                    if (Attributes == null) throw new InvalidDataException("Attributes non puo' essere null.");
                    foreach (var attribute in Attributes)
                        entity[attribute.Key] = (attribute.Value ?? throw new InvalidDataException("Valore attributo mancante.")).ToSdk();
                    return entity;
                default:
                    throw new InvalidDataException("Tipo SDK non supportato: " + Type + ". Usare uno scenario C# per strutture piu' complesse.");
            }
        }

        private string RequiredLogicalName() => !string.IsNullOrWhiteSpace(LogicalName)
            ? LogicalName!
            : throw new InvalidDataException("LogicalName obbligatorio per " + Type);

        private Guid RequiredGuidValue()
        {
            if (!Guid.TryParse(RequiredValue(JTokenType.String).Value<string>(), out var id))
                throw new InvalidDataException("Value deve contenere un GUID valido per il tipo SDK " + Type);
            return id;
        }

        private JToken RequiredValue(JTokenType expected)
        {
            if (Value == null || Value.Type != expected)
                throw new InvalidDataException("Value non valido per il tipo SDK " + Type);
            return Value;
        }

        private JToken Number()
        {
            if (Value == null || (Value.Type != JTokenType.Integer && Value.Type != JTokenType.Float))
                throw new InvalidDataException("Value numerico obbligatorio per " + Type);
            return Value;
        }
    }

    public sealed class PluginReplayDefinition
    {
        public string AssemblyPath { get; set; } = "";
        public string PluginType { get; set; } = "";
        public string? UnsecureConfiguration { get; set; }
        public string SecureConfigurationEnvironmentVariable { get; set; } = "";
        public string MessageName { get; set; } = "";
        public string PrimaryEntityName { get; set; } = "";
        public Guid PrimaryEntityId { get; set; }
        public int Stage { get; set; } = 20;
        public int Mode { get; set; }
        public Dictionary<string, SdkValue> InputParameters { get; set; } = new Dictionary<string, SdkValue>();
        public Dictionary<string, SdkValue> SharedVariables { get; set; } = new Dictionary<string, SdkValue>();
        public Dictionary<string, SdkValue> PreImages { get; set; } = new Dictionary<string, SdkValue>();
        public Dictionary<string, SdkValue> PostImages { get; set; } = new Dictionary<string, SdkValue>();
        public Dictionary<string, SdkValue> ExpectedOutputParameters { get; set; } = new Dictionary<string, SdkValue>();

        public static PluginReplayDefinition Load(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var definition = JsonConvert.DeserializeObject<PluginReplayDefinition>(File.ReadAllText(fullPath),
                new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    TypeNameHandling = TypeNameHandling.None,
                    DateParseHandling = DateParseHandling.None
                }) ?? throw new InvalidDataException("Definizione replay vuota.");
            if (string.IsNullOrWhiteSpace(definition.AssemblyPath) || string.IsNullOrWhiteSpace(definition.PluginType) ||
                string.IsNullOrWhiteSpace(definition.MessageName))
                throw new InvalidDataException("Il replay richiede AssemblyPath, PluginType e MessageName.");
            if (!Path.IsPathRooted(definition.AssemblyPath))
                definition.AssemblyPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath)!, definition.AssemblyPath));
            if (definition.InputParameters == null || definition.SharedVariables == null || definition.PreImages == null ||
                definition.PostImages == null || definition.ExpectedOutputParameters == null)
                throw new InvalidDataException("Le collezioni del replay non possono essere null.");
            return definition;
        }

        public PluginExecutionContext CreateContext(ScenarioContext scenario)
        {
            var context = scenario.NewPluginContext(MessageName, PrimaryEntityName, Stage);
            context.Mode = Mode;
            context.PrimaryEntityId = PrimaryEntityId;
            foreach (var value in InputParameters) context.InputParameters[value.Key] = value.Value.ToSdk();
            foreach (var value in SharedVariables) context.SharedVariables[value.Key] = value.Value.ToSdk();
            AddImages(PreImages, context.PreEntityImages);
            AddImages(PostImages, context.PostEntityImages);
            return context;
        }

        public void Run(ScenarioContext scenario)
        {
            var context = CreateContext(scenario);
            string? secure = null;
            if (!string.IsNullOrWhiteSpace(SecureConfigurationEnvironmentVariable))
            {
                secure = Environment.GetEnvironmentVariable(SecureConfigurationEnvironmentVariable);
                if (secure == null)
                    throw new InvalidOperationException("Variabile ambiente secure configuration non configurata: " + SecureConfigurationEnvironmentVariable);
            }
            var plugin = LocalPluginLoader.Load(AssemblyPath, PluginType, UnsecureConfiguration, secure);
            var result = scenario.ExecutePlugin(plugin, context);
            foreach (var expected in ExpectedOutputParameters)
            {
                scenario.Check(result.OutputParameters.Contains(expected.Key), "Output presente: " + expected.Key);
                var actualValue = result.OutputParameters[expected.Key];
                var expectedValue = expected.Value.ToSdk();
                var actualJson = actualValue == null ? JValue.CreateNull() : JToken.FromObject(actualValue);
                var expectedJson = expectedValue == null ? JValue.CreateNull() : JToken.FromObject(expectedValue);
                scenario.Check(JToken.DeepEquals(actualJson, expectedJson), "Output atteso: " + expected.Key);
            }
            if (ExpectedOutputParameters.Count == 0)
                scenario.Trace.Trace("Replay completato senza verifiche automatiche sugli output.");
        }

        private static void AddImages(Dictionary<string, SdkValue> source, EntityImageCollection target)
        {
            foreach (var value in source)
            {
                if (!(value.Value.ToSdk() is Entity entity))
                    throw new InvalidDataException("Le immagini devono avere Type = entity: " + value.Key);
                target[value.Key] = entity;
            }
        }
    }
}
