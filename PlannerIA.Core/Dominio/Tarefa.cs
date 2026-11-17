namespace PlannerIA.Core.Dominio;

/// <summary>
/// Tarefa principal ou subtarefa (apenas um nível).
/// Uma principal com subtarefas é concluída automaticamente quando todas as subtarefas estão feitas.
/// </summary>
public class Tarefa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titulo { get; set; } = "";
    public string? Notas { get; set; }
    public DateOnly? Data { get; set; }
    public int Ordem { get; set; }
    public bool Concluida { get; set; }

    public Guid PeriodoId { get; set; }
    public Periodo? Periodo { get; set; }

    public Guid? TarefaPaiId { get; set; }
    public Tarefa? TarefaPai { get; set; }

    public List<Tarefa> Subtarefas { get; set; } = [];

    public bool EhSubtarefa => TarefaPaiId is not null || TarefaPai is not null;
    public bool TemSubtarefas => Subtarefas.Count > 0;

    /// <summary>Somente tarefas folha (subtarefas ou principais sem subtarefas) são marcadas à mão.</summary>
    public bool PodeMarcarManualmente => !TemSubtarefas;

    public IEnumerable<Tarefa> SubtarefasOrdenadas => Subtarefas.OrderBy(s => s.Ordem);

    public Progresso Progresso() =>
        TemSubtarefas
            ? new Progresso(Subtarefas.Count(s => s.Concluida), Subtarefas.Count)
            : new Progresso(Concluida ? 1 : 0, 1);

    public Tarefa AdicionarSubtarefa(string titulo, DateOnly? data = null, string? notas = null)
    {
        if (EhSubtarefa)
            throw new RegraDeDominioException("Uma subtarefa não pode ter subtarefas.");
        if (string.IsNullOrWhiteSpace(titulo))
            throw new RegraDeDominioException("O título da subtarefa não pode ser vazio.");

        var sub = new Tarefa
        {
            Titulo = titulo.Trim(),
            Data = data,
            Notas = notas,
            Ordem = Subtarefas.Count == 0 ? 0 : Subtarefas.Max(s => s.Ordem) + 1,
            PeriodoId = PeriodoId,
            Periodo = Periodo,
            TarefaPaiId = Id,
            TarefaPai = this
        };
        Subtarefas.Add(sub);
        RecalcularConclusao();
        return sub;
    }

    public void RemoverSubtarefa(Tarefa sub)
    {
        Subtarefas.Remove(sub);
        RecalcularConclusao();
    }

    /// <summary>Marca/desmarca uma tarefa folha e propaga para a principal.</summary>
    public void DefinirConclusao(bool concluida)
    {
        if (!PodeMarcarManualmente)
            throw new RegraDeDominioException(
                "Uma tarefa com subtarefas é concluída automaticamente quando todas as subtarefas estão feitas.");

        Concluida = concluida;
        TarefaPai?.RecalcularConclusao();
    }

    public void AlternarConclusao() => DefinirConclusao(!Concluida);

    /// <summary>Para principais com subtarefas: concluída = todas as subtarefas concluídas.</summary>
    public void RecalcularConclusao()
    {
        if (TemSubtarefas)
            Concluida = Subtarefas.All(s => s.Concluida);
    }
}
