using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseDevRunner.Core
{
    public sealed class GuardedOrganizationService : IOrganizationService
    {
        private static readonly HashSet<string> ReadRequests = new HashSet<string>(StringComparer.Ordinal)
        {
            "WhoAmI", "Retrieve", "RetrieveMultiple", "RetrieveVersion", "RetrieveEntity",
            "RetrieveAttribute", "RetrieveRelationship", "RetrieveAllEntities", "RetrieveAllOptionSets",
            "RetrieveOptionSet", "RetrieveMetadataChanges", "RetrieveCurrentOrganization"
        };
        private readonly IOrganizationService _inner;
        private readonly bool _allowWrites;
        private readonly ITracingService _trace;

        public GuardedOrganizationService(IOrganizationService inner, bool allowWrites, ITracingService trace)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _allowWrites = allowWrites;
            _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        private void CheckWrite(string operation)
        {
            if (!_allowWrites)
                throw new InvalidOperationException("Operazione bloccata dal profilo di sola lettura: " + operation +
                    ". Lo scenario deve dichiarare RequiresWriteAccess e ricevere conferma.");
            _trace.Trace("SDK: {0} (scritture autorizzate)", operation);
        }

        public Guid Create(Entity entity) { CheckWrite("Create"); return _inner.Create(entity); }
        public void Update(Entity entity) { CheckWrite("Update"); _inner.Update(entity); }
        public void Delete(string entityName, Guid id) { CheckWrite("Delete"); _inner.Delete(entityName, id); }
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { CheckWrite("Associate"); _inner.Associate(entityName, entityId, relationship, relatedEntities); }
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { CheckWrite("Disassociate"); _inner.Disassociate(entityName, entityId, relationship, relatedEntities); }
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => _inner.Retrieve(entityName, id, columnSet);
        public EntityCollection RetrieveMultiple(QueryBase query) => _inner.RetrieveMultiple(query);
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!ReadRequests.Contains(request.RequestName))
                CheckWrite(request.RequestName);
            return _inner.Execute(request);
        }
    }

    public class UnsupportedOrganizationService : IOrganizationService
    {
        private static NotSupportedException Unsupported(string operation) =>
            new NotSupportedException("Servizio offline: operazione non configurata: " + operation);
        public virtual Guid Create(Entity entity) => throw Unsupported("Create");
        public virtual void Update(Entity entity) => throw Unsupported("Update");
        public virtual void Delete(string entityName, Guid id) => throw Unsupported("Delete");
        public virtual Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw Unsupported("Retrieve");
        public virtual EntityCollection RetrieveMultiple(QueryBase query) => throw Unsupported("RetrieveMultiple");
        public virtual OrganizationResponse Execute(OrganizationRequest request) => throw Unsupported(request.RequestName);
        public virtual void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw Unsupported("Associate");
        public virtual void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw Unsupported("Disassociate");
    }
}
