using System.Collections.Concurrent;
using System.Text.Json;


var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var claveEsperada = Environment.GetEnvironmentVariable("WEBHOOK_KEY") ?? "cambia-esta-clave";
var eventos = new ConcurrentQueue<object>();
const int MaxEventos = 50;

app.MapGet("/", () => "Receptor de webhooks de Dataverse activo");

app.MapGet("/events", () => Results.Json(eventos.Reverse().ToArray()));

app.MapPost("/webhook/dataverse", async (HttpRequest req) =>
{
    // 1) Autenticación: Dataverse envía la cabecera configurada al registrar el webhook
    if (!req.Headers.TryGetValue("x-webhook-key", out var clave) || clave != claveEsperada)
    {
        app.Logger.LogWarning("Webhook rechazado: cabecera x-webhook-key ausente o incorrecta.");
        return Results.Unauthorized();
    }

    // 2) Leer y parsear el contexto de ejecución (RemoteExecutionContext en JSON)
    string cuerpo;
    using (var lector = new StreamReader(req.Body))
        cuerpo = await lector.ReadToEndAsync();

    JsonDocument doc;
    try { doc = JsonDocument.Parse(cuerpo); }
    catch (JsonException)
    {
        app.Logger.LogWarning("Webhook con cuerpo que no es JSON válido.");
        return Results.BadRequest();
    }

    using (doc)
    {
        var raiz = doc.RootElement;

        // 3) Atributos del registro (parámetro "Target" de InputParameters)
        var atributos = new Dictionary<string, string>();
        if (raiz.TryGetProperty("InputParameters", out var parametros) && parametros.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in parametros.EnumerateArray())
            {
                if (Texto(p, "Key") != "Target") continue;
                if (!p.TryGetProperty("Value", out var target)) continue;
                if (!target.TryGetProperty("Attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array) continue;

                foreach (var kv in attrs.EnumerateArray())
                    atributos[Texto(kv, "Key") ?? "?"] = kv.TryGetProperty("Value", out var v) ? Simplificar(v) : "";
            }
        }

        var evento = new
        {
            recibidoUtc = DateTime.UtcNow,
            mensaje = Texto(raiz, "MessageName"),
            tabla = Texto(raiz, "PrimaryEntityName"),
            registroId = Texto(raiz, "PrimaryEntityId"),
            usuarioId = Texto(raiz, "InitiatingUserId"),
            correlationId = Texto(raiz, "CorrelationId"),
            etapa = Texto(raiz, "Stage"),
            profundidad = Texto(raiz, "Depth"),
            atributos,
            payloadCompleto = raiz.Clone()
        };

        eventos.Enqueue(evento);
        while (eventos.Count > MaxEventos) eventos.TryDequeue(out _);

        app.Logger.LogInformation("Evento {Mensaje} sobre {Tabla} ({Id}) | atributos: {Atributos}",
            evento.mensaje, evento.tabla, evento.registroId,
            string.Join(", ", atributos.Select(a => $"{a.Key}={a.Value}")));
    }

    // 4) Responder rápido. Si el procesamiento fuera largo, encolarlo y devolver 200 de inmediato.
    return Results.Ok(new { recibido = true });
});

app.Run();

// ---- utilidades ----

static string? Texto(JsonElement e, string nombre) =>
    e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nombre, out var v)
        ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
        : null;

// Aplana los tipos de Dataverse más comunes para que se lean bien en el log:
//  - OptionSetValue / Money  -> {"Value": n}          => n
//  - EntityReference         -> {"LogicalName","Id"}  => tabla:id
static string Simplificar(JsonElement v)
{
    if (v.ValueKind != JsonValueKind.Object) return v.ToString();
    if (v.TryGetProperty("LogicalName", out var tabla) && v.TryGetProperty("Id", out var id)) return $"{tabla}:{id}";
    if (v.TryGetProperty("Value", out var interno)) return interno.ToString();
    return v.GetRawText();
}
