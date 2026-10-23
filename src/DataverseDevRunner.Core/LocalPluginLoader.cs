using System;
using System.IO;
using System.Reflection;
using Microsoft.Xrm.Sdk;

namespace DataverseDevRunner.Core
{
    public static class LocalPluginLoader
    {
        public static IPlugin Load(string assemblyPath, string typeName, string? unsecure = null, string? secure = null)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath)) throw new ArgumentException("Percorso DLL obbligatorio.", nameof(assemblyPath));
            var fullPath = Path.GetFullPath(assemblyPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("DLL plugin non trovata.", fullPath);
            var assembly = Assembly.LoadFrom(fullPath);
            var type = assembly.GetType(typeName, throwOnError: true)!;
            if (type.IsAbstract || !typeof(IPlugin).IsAssignableFrom(type))
                throw new InvalidOperationException(
                    "Il tipo non implementa l'IPlugin SDK del runner. Verificare assembly, dipendenze e packaging, senza incorporare lo SDK.");
            if (unsecure != null || secure != null)
            {
                var constructor = type.GetConstructor(new[] { typeof(string), typeof(string) });
                if (constructor != null) return (IPlugin)constructor.Invoke(new object?[] { unsecure, secure });
                if (secure == null)
                {
                    constructor = type.GetConstructor(new[] { typeof(string) });
                    if (constructor != null) return (IPlugin)constructor.Invoke(new object?[] { unsecure });
                }
                throw new MissingMethodException("Configurazione fornita ma nessun costruttore plugin compatibile.");
            }
            var parameterless = type.GetConstructor(Type.EmptyTypes);
            if (parameterless != null) return (IPlugin)parameterless.Invoke(Array.Empty<object>());
            var configured = type.GetConstructor(new[] { typeof(string), typeof(string) });
            if (configured != null) return (IPlugin)configured.Invoke(new object?[] { null, null });
            var single = type.GetConstructor(new[] { typeof(string) });
            if (single != null) return (IPlugin)single.Invoke(new object?[] { null });
            throw new MissingMethodException("Nessun costruttore plugin supportato.");
        }
    }
}
