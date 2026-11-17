namespace PlannerIA.Core.Dominio;

public enum TipoPeriodo
{
    Mes = 0,
    Semana = 1,
    Dia = 2
}

/// <summary>
/// Um mês, uma semana ou um dia. Semanas pertencem a um mês (o mês em que começam)
/// e dias pertencem a uma semana.
/// </summary>
public class Periodo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TipoPeriodo Tipo { get; set; }
    public string Titulo { get; set; } = "";
    public string? Objetivo { get; set; }
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    public int Ordem { get; set; }

    public Guid? PeriodoPaiId { get; set; }
    public Periodo? PeriodoPai { get; set; }

    public List<Periodo> Filhos { get; set; } = [];
    public List<Tarefa> Tarefas { get; set; } = [];

    public bool Contem(DateOnly data) => data >= Inicio && data <= Fim;

    public int Dias => Fim.DayNumber - Inicio.DayNumber + 1;

    /// <summary>Tarefas principais (sem pai), na ordem definida.</summary>
    public IEnumerable<Tarefa> TarefasPrincipais =>
        Tarefas.Where(t => t.TarefaPaiId is null && t.TarefaPai is null).OrderBy(t => t.Ordem);

    /// <summary>Progresso das tarefas deste período e de todos os períodos filhos.</summary>
    public Progresso Progresso()
    {
        var proprio = Core.Dominio.Progresso.De(TarefasPrincipais);
        return Filhos.Aggregate(proprio, (acc, f) => acc + f.Progresso());
    }
}
