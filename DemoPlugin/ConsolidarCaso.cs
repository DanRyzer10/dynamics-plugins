using System;
using System.Globalization;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CasoDemoPlugin
{
    public class ConsolidarCaso : IPlugin
    {
        private const int TopeActividades = 100;

        public void Execute(IServiceProvider serviceProvider)
        {
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            var service = factory.CreateOrganizationService(context.UserId);

            if (!context.InputParameters.Contains("CasoId")
                || !(context.InputParameters["CasoId"] is Guid casoId)
                || casoId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("El parámetro CasoId es obligatorio y debe ser un GUID válido.");
            }
            trace.Trace("ConsolidarCaso: CasoId={0}, usuario={1}", casoId, context.UserId);

            var consultaCaso = new QueryExpression("incident")
            {
                ColumnSet = new ColumnSet("title", "ticketnumber", "prioritycode", "statecode",
                                          "statuscode", "createdon", "customerid"),
                TopCount = 1
            };
            consultaCaso.Criteria.AddCondition("incidentid", ConditionOperator.Equal, casoId);
            var resultado = service.RetrieveMultiple(consultaCaso);

            if (resultado.Entities.Count == 0)
                throw new InvalidPluginExecutionException($"No existe un caso con id {casoId} (o no tienes acceso a él).");

            var caso = resultado.Entities[0];

            var consultaActividades = new QueryExpression("activitypointer")
            {
                ColumnSet = new ColumnSet("activityid"),
                TopCount = TopeActividades
            };
            consultaActividades.Criteria.AddCondition("regardingobjectid", ConditionOperator.Equal, casoId);
            var actividades = service.RetrieveMultiple(consultaActividades).Entities.Count;

            context.OutputParameters["ResumenJson"] = ConstruirResumen(caso, actividades);
        }

        public static string ConstruirResumen(Entity caso, int actividades)
        {
            var creado = caso.GetAttributeValue<DateTime?>("createdon");

            var sb = new StringBuilder();
            sb.Append('{');
            Campo(sb, "casoId", caso.Id.ToString(), true);
            Campo(sb, "numero", caso.GetAttributeValue<string>("ticketnumber"), true);
            Campo(sb, "titulo", caso.GetAttributeValue<string>("title"), true);
            Campo(sb, "prioridad", Etiqueta(caso, "prioritycode"), true);
            Campo(sb, "estado", Etiqueta(caso, "statecode"), true);
            Campo(sb, "razonEstado", Etiqueta(caso, "statuscode"), true);
            Campo(sb, "cliente", Etiqueta(caso, "customerid"), true);
            Campo(sb, "creadoUtc", creado.HasValue
                ? creado.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : null, true);
            sb.Append("\"actividades\":").Append(actividades.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"actividadesConTope\":").Append(actividades >= TopeActividades ? "true" : "false").Append(',');
            Campo(sb, "generadoUtc", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), false);
            sb.Append('}');
            return sb.ToString();
        }

        // Texto legible de un OptionSet o lookup: Dataverse lo entrega en FormattedValues.
        private static string Etiqueta(Entity e, string atributo)
        {
            if (e.FormattedValues.Contains(atributo)) return e.FormattedValues[atributo];
            if (!e.Contains(atributo)) return null;
            var valor = e[atributo];
            if (valor is OptionSetValue os) return os.Value.ToString(CultureInfo.InvariantCulture);
            if (valor is EntityReference er) return er.Name ?? er.Id.ToString();
            return valor?.ToString();
        }

        // Evita depender de librerías JSON, que en el sandbox de plug-ins son un dolor.
        private static void Campo(StringBuilder sb, string nombre, string valor, bool coma)
        {
            sb.Append('"').Append(nombre).Append("\":");
            if (valor == null) sb.Append("null");
            else sb.Append('"').Append(Escapar(valor)).Append('"');
            if (coma) sb.Append(',');
        }

        private static string Escapar(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
