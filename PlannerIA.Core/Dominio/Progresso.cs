namespace PlannerIA.Core.Dominio;

/// <summary>Tarefas folha concluídas sobre o total de tarefas folha.</summary>
public readonly record struct Progresso(int Feitas, int Total)
{
    public double Fracao => Total == 0 ? 0 : (double)Feitas / Total;
    public int Percentual => (int)Math.Round(Fracao * 100);
    public bool Completo => Total > 0 && Feitas == Total;

    public static Progresso operator +(Progresso a, Progresso b) => new(a.Feitas + b.Feitas, a.Total + b.Total);

    public static Progresso De(IEnumerable<Tarefa> principais) =>
        principais.Aggregate(new Progresso(0, 0), (acc, t) => acc + t.Progresso());

    public override string ToString() => $"{Feitas} de {Total}";
}

public class RegraDeDominioException(string mensagem) : Exception(mensagem);
