using System;
using System.Collections.Generic;
using DataverseDevRunner.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseDevRunner.ConsoleApp
{
    public abstract class Scenario : IScenario
    {
        public abstract string Id { get; }
        public abstract string Description { get; }
        public virtual bool SupportsOffline => false;
        public virtual bool RequiresWriteAccess => false;
        public virtual bool MayHaveExternalEffects => true;
        public virtual IReadOnlyList<ScenarioParameter> Parameters => Array.Empty<ScenarioParameter>();
        public abstract void Run(ScenarioContext context);
    }

    public sealed class NormalizeMethodScenario : Scenario
    {
        public override string Id => "sample.method";
        public override string Description => "Metodo sintetico: normalizzazione testo e verifica risultato.";
        public override bool SupportsOffline => true;
        public override bool MayHaveExternalEffects => false;
        public override IReadOnlyList<ScenarioParameter> Parameters => new[]
        {
            new ScenarioParameter("Text", "Testo da normalizzare", "  demo  "),
            new ScenarioParameter("Expected", "Risultato atteso", "DEMO")
        };

        public override void Run(ScenarioContext context)
        {
            var result = SampleNormalizePlugin.Normalize(context.Parameter("Text"));
            context.Check(result == context.Parameter("Expected"), "Il metodo restituisce il risultato atteso.");
        }
    }

    public sealed class NormalizePluginScenario : Scenario
    {
        public override string Id => "sample.plugin";
        public override string Description => "Plugin sintetico: Execute, Target e PreImage; nessuna chiamata a Dataverse.";
        public override bool SupportsOffline => true;
        public override bool MayHaveExternalEffects => false;

        public override void Run(ScenarioContext context)
        {
            var target = new Entity("account", Guid.NewGuid()) { ["name"] = "  demo  " };
            var execution = context.NewPluginContext("Update", "account");
            execution.PrimaryEntityId = target.Id;
            execution.InputParameters["Target"] = target;
            execution.PreEntityImages["Before"] = new Entity("account", target.Id) { ["name"] = "before" };
            var result = context.ExecutePlugin(new SampleNormalizePlugin(), execution);
            context.Check(target.GetAttributeValue<string>("name") == "DEMO", "Target normalizzato dal plugin locale.");
            context.Check(result.OutputParameters.Contains("Changed") && (bool)result.OutputParameters["Changed"],
                "Output Changed = true.");
        }
    }

    public sealed class WhoAmIScenario : Scenario
    {
        public override string Id => "dataverse.whoami";
        public override string Description => "Verifica identita' Dataverse, senza scritture.";
        public override bool MayHaveExternalEffects => false;
        public override void Run(ScenarioContext context)
        {
            var response = (WhoAmIResponse)context.Service.Execute(new WhoAmIRequest());
            context.Check(response.UserId == context.UserId, "Utente coerente con la connessione.");
            context.Check(response.OrganizationId == context.OrganizationId, "Organizzazione coerente con il profilo.");
        }
    }

    public sealed class ReadAccountPluginScenario : Scenario
    {
        public override string Id => "dataverse.local-plugin-read";
        public override string Description => "Plugin sintetico eseguito localmente: lettura di un account reale, senza scritture.";
        public override bool MayHaveExternalEffects => false;
        public override IReadOnlyList<ScenarioParameter> Parameters => new[]
        {
            new ScenarioParameter("AccountId", "GUID di un account nell'ambiente di sviluppo")
        };

        public override void Run(ScenarioContext context)
        {
            if (!Guid.TryParse(context.Parameter("AccountId"), out var id) || id == Guid.Empty)
                throw new ArgumentException("AccountId deve essere un GUID non vuoto.");
            var execution = context.NewPluginContext("Update", "account");
            execution.PrimaryEntityId = id;
            execution.InputParameters["Target"] = new Entity("account", id);
            var result = context.ExecutePlugin(new SampleReadAccountPlugin(), execution);
            context.Check(result.OutputParameters.Contains("AccountId") &&
                (Guid)result.OutputParameters["AccountId"] == id, "Account letto dal codice plugin locale.");
        }
    }

    public sealed class ReplayPluginScenario : Scenario
    {
        public override string Id => "plugin.replay";
        public override string Description => "DLL plugin locale e contesto da JSON. Puo' scrivere dati e chiamare servizi esterni.";
        public override bool RequiresWriteAccess => true;
        public override IReadOnlyList<ScenarioParameter> Parameters => new[]
        {
            new ScenarioParameter("DefinitionPath", "Percorso definizione replay JSON", @"local\Replays\plugin.replay.json")
        };
        public override void Run(ScenarioContext context) =>
            PluginReplayDefinition.Load(context.Parameter("DefinitionPath")).Run(context);
    }

    public sealed class SampleNormalizePlugin : IPlugin
    {
        public static string Normalize(string value) => value.Trim().ToUpperInvariant();

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            if (context.MessageName != "Update" || context.Stage != 20 ||
                !context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
                throw new InvalidPluginExecutionException("Attesi Update, stage 20 e Target Entity.");
            if (!context.PreEntityImages.Contains("Before"))
                throw new InvalidPluginExecutionException("PreImage Before obbligatoria.");
            target["name"] = Normalize(target.GetAttributeValue<string>("name") ??
                throw new InvalidPluginExecutionException("name obbligatorio."));
            context.OutputParameters["Changed"] = true;
            trace.Trace("Plugin sintetico eseguito localmente, senza persistenza del Target.");
        }
    }

    public sealed class SampleReadAccountPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var account = service.Retrieve("account", context.PrimaryEntityId, new ColumnSet("accountid"));
            context.OutputParameters["AccountId"] = account.Id;
        }
    }
}
