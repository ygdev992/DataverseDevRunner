using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DataverseDevRunner.Core
{
    public sealed class RunnerSettings
    {
        public List<ConnectionProfile> Connections { get; set; } = new List<ConnectionProfile>();

        public static RunnerSettings Load(string path)
        {
            if (!File.Exists(path)) return new RunnerSettings();
            var result = JsonConvert.DeserializeObject<RunnerSettings>(File.ReadAllText(path),
                new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    TypeNameHandling = TypeNameHandling.None
                }) ?? throw new InvalidDataException("Configurazione locale vuota.");
            if (result.Connections == null || result.Connections.Any(p => p == null))
                throw new InvalidDataException("Connections deve essere un elenco di profili.");
            if (result.Connections.Any(p => string.IsNullOrWhiteSpace(p.Name)) ||
                result.Connections.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidDataException("I profili richiedono nomi non vuoti e univoci.");
            foreach (var profile in result.Connections)
                profile.Validate();
            return result;
        }
    }

    public sealed class ConnectionProfile
    {
        public string Name { get; set; } = "";
        public string ConnectionString { get; set; } = "";
        public string ConnectionStringEnvironmentVariable { get; set; } = "";
        public Guid ExpectedOrganizationId { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || ExpectedOrganizationId == Guid.Empty)
                throw new InvalidDataException("Profilo incompleto: servono Name ed ExpectedOrganizationId non vuoto.");
            var hasConnectionString = !string.IsNullOrWhiteSpace(ConnectionString);
            var hasEnvironmentVariable = !string.IsNullOrWhiteSpace(ConnectionStringEnvironmentVariable);
            if (!hasConnectionString && !hasEnvironmentVariable)
                throw new InvalidDataException("Inserire ConnectionString nel profilo in local\\settings.json.");
            if (hasConnectionString && hasEnvironmentVariable)
                throw new InvalidDataException(
                    "Configurare solo ConnectionString oppure ConnectionStringEnvironmentVariable, non entrambi. Per la connessione da JSON rimuovere ConnectionStringEnvironmentVariable.");
            if (hasEnvironmentVariable && ConnectionStringEnvironmentVariable.IndexOfAny(new[] { '=', ';', '\r', '\n' }) >= 0)
                throw new InvalidDataException(
                    "ConnectionStringEnvironmentVariable richiede il nome di una variabile ambiente, non la connection string. Per salvarla nel JSON usare il campo ConnectionString.");
        }

        public string ReadConnectionString()
        {
            Validate();
            if (!string.IsNullOrWhiteSpace(ConnectionString))
                return ConnectionString;
            var value = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    "La variabile ambiente indicata in ConnectionStringEnvironmentVariable non e' configurata o e' vuota. " +
                    "Per usare il file locale, sostituire ConnectionStringEnvironmentVariable con ConnectionString in local\\settings.json.");
            return value!;
        }

        public void VerifyOrganization(Guid actualOrganizationId)
        {
            if (actualOrganizationId != ExpectedOrganizationId)
                throw new InvalidOperationException("Organizzazione diversa da quella attesa: esecuzione bloccata.");
        }
    }
}
