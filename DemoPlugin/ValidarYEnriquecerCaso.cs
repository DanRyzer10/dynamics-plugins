using System;
using Microsoft.Xrm.Sdk;

namespace CasoDemoPlugin
{

    public class ValidarYEnriquecerCaso : IPlugin
    {
        private const int PrioridadAlta = 1; 
        private const int LongitudMinimaTitulo = 5;

        public void Execute(IServiceProvider serviceProvider)
        {
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            trace.Trace("ValidarYEnriquecerCaso: mensaje={0}, tabla={1}, etapa={2}, profundidad={3}",
                context.MessageName, context.PrimaryEntityName, context.Stage, context.Depth);

            // Protección contra bucles y contexto inesperado
            if (context.Depth > 1) return;
            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity caso)) return;
            if (caso.LogicalName != "incident") return;

            // Regla 1: validar título
            var titulo = caso.GetAttributeValue<string>("title");
            if (string.IsNullOrWhiteSpace(titulo) || titulo.Trim().Length < LongitudMinimaTitulo)
            {
                throw new InvalidPluginExecutionException(
                    $"El título del caso debe tener al menos {LongitudMinimaTitulo} caracteres.");
            }

            // Regla 2: priorizar casos urgentes
            if (titulo.IndexOf("urgente", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                caso["prioritycode"] = new OptionSetValue(PrioridadAlta);
                trace.Trace("Prioridad forzada a Alta por palabra clave 'urgente'.");
            }

            // Regla 3: marcar la descripción (en PreOperation basta con modificar Target)
            var marca = $"[Procesado por plug-in {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC]";
            var descripcion = caso.GetAttributeValue<string>("description");
            caso["description"] = string.IsNullOrEmpty(descripcion)
                ? marca
                : descripcion + Environment.NewLine + marca;
        }
    }
}
