using System;
using Microsoft.Xrm.Sdk;

namespace DataverseDevRunner.Core
{
    public sealed class PluginExecutionContext : IPluginExecutionContext
    {
        public int Stage { get; set; } = 20;
        public int Mode { get; set; }
        public int IsolationMode { get; set; } = 2;
        public int Depth { get; set; } = 1;
        public string MessageName { get; set; } = "Update";
        public string PrimaryEntityName { get; set; } = "";
        public string SecondaryEntityName { get; set; } = "";
        public Guid? RequestId { get; set; } = Guid.NewGuid();
        public ParameterCollection InputParameters { get; } = new ParameterCollection();
        public ParameterCollection OutputParameters { get; } = new ParameterCollection();
        public ParameterCollection SharedVariables { get; } = new ParameterCollection();
        public Guid UserId { get; set; }
        public Guid InitiatingUserId { get; set; }
        public Guid BusinessUnitId { get; set; }
        public Guid OrganizationId { get; set; }
        public string OrganizationName { get; set; } = "";
        public Guid PrimaryEntityId { get; set; }
        public EntityImageCollection PreEntityImages { get; } = new EntityImageCollection();
        public EntityImageCollection PostEntityImages { get; } = new EntityImageCollection();
        public EntityReference OwningExtension { get; set; } = new EntityReference("sdkmessageprocessingstep", Guid.NewGuid());
        public Guid CorrelationId { get; set; } = Guid.NewGuid();
        public bool IsExecutingOffline { get; set; }
        public bool IsOfflinePlayback { get; set; }
        public bool IsInTransaction { get; set; }
        public Guid OperationId { get; set; } = Guid.NewGuid();
        public DateTime OperationCreatedOn { get; set; } = DateTime.UtcNow;
        public IPluginExecutionContext ParentContext { get; set; } = null!;
    }

    public sealed class PluginExecutionResult
    {
        internal PluginExecutionResult(PluginExecutionContext context) { Context = context; }
        public PluginExecutionContext Context { get; }
        public ParameterCollection OutputParameters => Context.OutputParameters;
    }

    public static class PluginHarness
    {
        public static PluginExecutionResult Execute(IPlugin plugin, PluginExecutionContext context,
            IOrganizationService service, ITracingService trace)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Stage != 10 && context.Stage != 20 && context.Stage != 30 && context.Stage != 40)
                throw new ArgumentException("Stage supportati: 10, 20, 30, 40.", nameof(context));
            if (context.Mode != 0 && context.Mode != 1)
                throw new ArgumentException("Mode deve essere 0 (sync) o 1 (async).", nameof(context));
            if (context.IsInTransaction)
                throw new NotSupportedException("Il runner locale non crea una transazione Dataverse.");

            plugin.Execute(new PluginServices(context, service, trace));
            return new PluginExecutionResult(context);
        }

        private sealed class PluginServices : IServiceProvider, IOrganizationServiceFactory
        {
            private readonly PluginExecutionContext _context;
            private readonly IOrganizationService _service;
            private readonly ITracingService _trace;

            public PluginServices(PluginExecutionContext context, IOrganizationService service, ITracingService trace)
            {
                _context = context;
                _service = service ?? throw new ArgumentNullException(nameof(service));
                _trace = trace ?? throw new ArgumentNullException(nameof(trace));
            }

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(IPluginExecutionContext) || serviceType == typeof(IExecutionContext)) return _context;
                if (serviceType == typeof(IOrganizationServiceFactory)) return this;
                if (serviceType == typeof(ITracingService)) return _trace;
                if (serviceType == typeof(IOrganizationService)) return _service;
                throw new NotSupportedException("Servizio non simulato dal runner: " + serviceType.FullName);
            }

            public IOrganizationService CreateOrganizationService(Guid? userId)
            {
                // A null id means SYSTEM in the real pipeline; it must not silently become the connected user.
                if (!userId.HasValue || (userId.Value != Guid.Empty && userId.Value != _context.UserId))
                    throw new NotSupportedException(
                        "Impersonation/SYSTEM non simulati. Usare context.UserId (o Guid.Empty per l'utente corrente).");
                return _service;
            }
        }
    }
}
