using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DataverseDevRunner.Core
{
    public sealed class TestGuide
    {
        [JsonProperty(Required = Required.Always)]
        public string Title { get; set; } = "";

        [JsonProperty(Required = Required.Always)]
        public List<string> Steps { get; set; } = new List<string>();

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Title) || Steps.Count == 0 || Steps.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Ogni guida deve contenere un Title e una lista Steps di punti non vuoti.");
        }
    }

    public sealed class TestGuides
    {
        [JsonProperty(Required = Required.Always)]
        public TestGuide Scenarios { get; set; } = new TestGuide();

        [JsonProperty(Required = Required.Always)]
        public TestGuide PluginReplay { get; set; } = new TestGuide();

        public static TestGuides Load(string root)
        {
            var path = Path.Combine(root, "configuration", "test-guides.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("Guide mancanti. Ripristinare configuration\\test-guides.json.", path);
            var guides = JsonConvert.DeserializeObject<TestGuides>(File.ReadAllText(path), new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                TypeNameHandling = TypeNameHandling.None
            }) ?? throw new InvalidDataException("Il file configuration\\test-guides.json non puo' essere null.");
            guides.Scenarios.Validate();
            guides.PluginReplay.Validate();
            return guides;
        }
    }
}
