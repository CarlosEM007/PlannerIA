using System.Globalization;
using System.Text.Json;
using PlannerIA.Core.Dominio;

namespace PlannerIA.Core.Servicos;

/// <summary>Plano devolvido pela IA, já validado, aguardando a confirmação do usuário.</summary>
public class PlanoProposto
{
    public string? Resumo { get; init; }
    public List<Periodo> Semanas { get; init; } = [];

    /// <summary>Ajustes feitos na validação (itens descartados, datas removidas...).</summary>
    public List<string> Avisos { get; init; } = [];
}

/// <summary>
/// Converte e valida o JSON da IA. Formato:
/// <code>
/// { "resumo": "...",
///   "semanas": [ { "titulo": "...", "objetivo": "...", "inicio": "2026-11-16", "fim": "2026-11-22",
///                  "tarefas": [ { "titulo": "...", "data": "2026-11-17" | null, "notas": "..." | null,
///                                 "subtarefas": [ { "titulo": "...", "data": ..., "notas": ... } ] } ] } ] }
/// </code>
/// Itens inválidos são descartados com aviso; se nenhuma semana sobrar, lança <see cref="RegraDeDominioException"/>.
/// Nada é salvo aqui.
/// </summary>
public static class LeitorPlanoIa
{
    public static PlanoProposto Ler(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new RegraDeDominioException($"A resposta da IA não é um JSON válido ({ex.Message}).");
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("semanas", out var semanasJson)
                || semanasJson.ValueKind != JsonValueKind.Array)
                throw new RegraDeDominioException("A resposta da IA não trouxe a lista de semanas.");

            var avisos = new List<string>();
            var semanas = new List<Periodo>();

            foreach (var s in semanasJson.EnumerateArray())
            {
                var semana = LerSemana(s, avisos);
                if (semana is null) continue;

                if (semanas.Any(x => x.Inicio == semana.Inicio && x.Fim == semana.Fim))
                {
                    avisos.Add($"Semana repetida na resposta ({Datas.Intervalo(semana.Inicio, semana.Fim)}) foi ignorada.");
                    continue;
                }
                semanas.Add(semana);
            }

            if (semanas.Count == 0)
                throw new RegraDeDominioException("A IA não devolveu nenhuma semana válida. Tente detalhar melhor o pedido.");

            return new PlanoProposto
            {
                Resumo = Texto(raiz, "resumo"),
                Semanas = semanas.OrderBy(s => s.Inicio).ToList(),
                Avisos = avisos
            };
        }
    }

    private static Periodo? LerSemana(JsonElement s, List<string> avisos)
    {
        var titulo = Texto(s, "titulo");
        var inicio = Data(s, "inicio");
        var fim = Data(s, "fim");

        if (inicio is null || fim is null)
        {
            avisos.Add($"Semana “{titulo ?? "sem título"}” sem datas válidas foi ignorada.");
            return null;
        }
        if (fim < inicio || fim.Value.DayNumber - inicio.Value.DayNumber > 30)
        {
            avisos.Add($"Semana “{titulo ?? "sem título"}” com intervalo inválido foi ignorada.");
            return null;
        }

        var semana = new Periodo
        {
            Tipo = TipoPeriodo.Semana,
            Titulo = string.IsNullOrWhiteSpace(titulo) ? $"Semana — {Datas.Intervalo(inicio.Value, fim.Value)}" : titulo,
            Objetivo = Texto(s, "objetivo"),
            Inicio = inicio.Value,
            Fim = fim.Value
        };

        if (s.TryGetProperty("tarefas", out var tarefas) && tarefas.ValueKind == JsonValueKind.Array)
        {
            var ordem = 0;
            foreach (var t in tarefas.EnumerateArray())
            {
                var principal = LerTarefa(t, semana, avisos);
                if (principal is null) continue;
                principal.Ordem = ordem++;
                semana.Tarefas.Add(principal);

                if (!t.TryGetProperty("subtarefas", out var subs) || subs.ValueKind != JsonValueKind.Array) continue;
                foreach (var st in subs.EnumerateArray())
                {
                    var modelo = LerTarefa(st, semana, avisos);
                    if (modelo is null) continue;
                    // Regra 1: subtarefa não tem subtarefas — qualquer nível extra é ignorado.
                    semana.Tarefas.Add(principal.AdicionarSubtarefa(modelo.Titulo, modelo.Data, modelo.Notas));
                }
            }
        }

        return semana;
    }

    private static Tarefa? LerTarefa(JsonElement t, Periodo semana, List<string> avisos)
    {
        var titulo = Texto(t, "titulo");
        if (string.IsNullOrWhiteSpace(titulo))
        {
            avisos.Add($"Uma tarefa sem título em “{semana.Titulo}” foi ignorada.");
            return null;
        }

        var data = Data(t, "data");
        if (data is { } d && !semana.Contem(d))
        {
            avisos.Add($"“{titulo}”: a data {d:dd/MM} estava fora da semana e foi removida.");
            data = null;
        }

        return new Tarefa
        {
            Titulo = titulo,
            Data = data,
            Notas = Texto(t, "notas"),
            PeriodoId = semana.Id,
            Periodo = semana
        };
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static DateOnly? Data(JsonElement e, string nome) =>
        Texto(e, nome) is { } s && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
