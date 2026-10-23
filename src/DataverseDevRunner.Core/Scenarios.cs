using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace DataverseDevRunner.Core
{
    public interface IScenario
    {
        string Id { get; }
        string Description { get; }
        bool SupportsOffline { get; }
        bool RequiresWriteAccess { get; }
        bool MayHaveExternalEffects { get; }
        IReadOnlyList<ScenarioParameter> Parameters { get; }
        void Run(ScenarioContext context);
    }

    public sealed class ScenarioParameter
    {
        public ScenarioParameter(string name, string prompt, string? defaultValue = null)
        {
            Name = name;
            Prompt = prompt;
            DefaultValue = defaultValue;
        }

        public string Name { get; }
        public string Prompt { get; }
        public string? DefaultValue { get; }
    }

    public sealed class ScenarioContext
    {
        private readonly IReadOnlyDictionary<string, string> _parameters;

        public ScenarioContext(IOrganizationService service, ITracingService trace, bool isOffline,
            Guid userId, Guid organizationId, string organizationName,
            IReadOnlyDictionary<string, string> parameters)
        {
            Service = service ?? throw new ArgumentNullException(nameof(service));
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            IsOffline = isOffline;
            UserId = userId;
            OrganizationId = organizationId;
            OrganizationName = organizationName;
            _parameters = parameters;
        }

        public IOrganizationService Service { get; }
        public ITracingService Trace { get; }
        public bool IsOffline { get; }
        public Guid UserId { get; }
        public Guid OrganizationId { get; }
        public string OrganizationName { get; }
        public Guid BusinessUnitId { get; set; }
        public int PassedChecks { get; private set; }

        public string Parameter(string name)
        {
            if (!_parameters.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Parametro obbligatorio mancante: " + name);
            return value;
        }

        public PluginExecutionContext NewPluginContext(string message, string entityName, int stage = 20)
        {
            return new PluginExecutionContext
            {
                MessageName = message,
                PrimaryEntityName = entityName,
                Stage = stage,
                UserId = UserId,
                InitiatingUserId = UserId,
                OrganizationId = OrganizationId,
                OrganizationName = OrganizationName,
                BusinessUnitId = BusinessUnitId
            };
        }

        public PluginExecutionResult ExecutePlugin(IPlugin plugin, PluginExecutionContext execution)
        {
            return PluginHarness.Execute(plugin, execution, Service, Trace);
        }

        public void Check(bool condition, string description)
        {
            if (!condition)
                throw new ScenarioAssertionException(description);
            PassedChecks++;
            Trace.Trace("PASS: {0}", description);
        }
    }

    public sealed class ScenarioAssertionException : Exception
    {
        public ScenarioAssertionException(string message) : base(message) { }
    }

    public static class ScenarioCatalog
    {
        public static IReadOnlyList<IScenario> Validate(IEnumerable<IScenario> scenarios)
        {
            var result = scenarios.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();
            if (result.Any(s => string.IsNullOrWhiteSpace(s.Id)))
                throw new InvalidOperationException("Ogni scenario deve avere un Id.");
            var duplicate = result.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException("Id scenario duplicato: " + duplicate.Key);
            foreach (var scenario in result)
            {
                if (scenario.Parameters.Any(p => string.IsNullOrWhiteSpace(p.Name)) ||
                    scenario.Parameters.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("Parametri non validi nello scenario: " + scenario.Id);
            }
            return result;
        }
    }
}
