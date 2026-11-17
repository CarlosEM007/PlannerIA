using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Servicos;
using PlannerIA.Servicos;

namespace PlannerIA.ViewModels;

public partial class DiaCalendarioViewModel(DateOnly data, bool noMes, Periodo? semana, IReadOnlyList<Tarefa> tarefas, MainViewModel shell)
    : ObservableObject
{
    public DateOnly Data => data;
    public string Numero => data.Day.ToString();
    public bool NoMes => noMes;
    public bool EhHoje => data == Datas.Hoje;
    public bool NoPlano => semana is not null;
    public int Total => tarefas.Count;
    public int Feitas => tarefas.Count(t => t.Concluida);
    public bool TemTarefas => Total > 0;
    public double Fracao => Total == 0 ? 0 : (double)Feitas / Total;
    public string Texto => Total == 0 ? "" : $"{Feitas}/{Total}";
    public bool Completo => Total > 0 && Feitas == Total;

    /// <summary>Até 3 marcadores coloridos (categoria) para o dia.</summary>
    public IEnumerable<object> Marcadores =>
        tarefas.Take(4).Select(t => new { Cor = Paleta.CorPara(t.TarefaPai?.Titulo ?? t.Titulo), Feita = t.Concluida });

    public string Dica => Total == 0
        ? Datas.DiaCurto(data)
        : $"{Datas.DiaCurto(data)}\n" + string.Join("\n", tarefas.Select(t => (t.Concluida ? "✔ " : "○ ") + t.Titulo));

    [RelayCommand]
    private void Abrir() => shell.AbrirDia(data);
}

public partial class SemanaResumoViewModel(Periodo semana, MainViewModel shell) : ObservableObject
{
    public Periodo Semana => semana;
    public string Titulo => semana.Titulo;
    public string Intervalo => Datas.Intervalo(semana.Inicio, semana.Fim);
    public string? Objetivo => semana.Objetivo;
    public double Fracao => semana.Progresso().Fracao;
    public string Texto => $"{semana.Progresso()} · {semana.Progresso().Percentual}%";
    public bool EhAtual => semana.Contem(Datas.Hoje);
    public bool Completa => semana.Progresso().Completo;

    [RelayCommand]
    private void Abrir() => shell.AbrirSemana(semana);
}

/// <summary>Visão Mês: calendário com marcadores por dia e lista das semanas do mês.</summary>
public partial class MesViewModel(MainViewModel shell) : PaginaViewModel(shell)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private DateOnly _referencia = new(Datas.Hoje.Year, Datas.Hoje.Month, 1);

    public string Titulo => Datas.NomeDoMes(Referencia.Year, Referencia.Month);

    public string[] NomesDias { get; } = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];
    public ObservableCollection<DiaCalendarioViewModel> Dias { get; } = [];
    public ObservableCollection<SemanaResumoViewModel> Semanas { get; } = [];

    [ObservableProperty] private double _progressoFracao;
    [ObservableProperty] private string _progressoTexto = "";
    [ObservableProperty] private string _progressoPercentual = "";

    public bool TemSemanas => Semanas.Count > 0;

    public override void Recarregar()
    {
        var mes = Plano.Mes(Referencia.Year, Referencia.Month);

        Semanas.Clear();
        if (mes is not null)
            foreach (var s in mes.Filhos.OrderBy(s => s.Inicio))
                Semanas.Add(new SemanaResumoViewModel(s, Shell));

        Dias.Clear();
        var inicioGrade = Datas.SegundaDaSemana(Referencia);
        var fimMes = Referencia.AddMonths(1).AddDays(-1);
        var totalDias = ((fimMes.DayNumber - inicioGrade.DayNumber) / 7 + 1) * 7;
        for (var i = 0; i < totalDias; i++)
        {
            var d = inicioGrade.AddDays(i);
            Dias.Add(new DiaCalendarioViewModel(d, d.Month == Referencia.Month, Plano.SemanaQueContem(d),
                Plano.TarefasDoDia(d).ToList(), Shell));
        }

        var p = mes?.Progresso() ?? new Progresso(0, 0);
        ProgressoFracao = p.Fracao;
        ProgressoTexto = p.Total == 0 ? "Nenhuma tarefa neste mês" : $"{p} subtarefas concluídas";
        ProgressoPercentual = p.Total == 0 ? "—" : $"{p.Percentual}%";
        OnPropertyChanged(nameof(TemSemanas));
    }

    [RelayCommand]
    private void MesAnterior()
    {
        Referencia = Referencia.AddMonths(-1);
        Recarregar();
    }

    [RelayCommand]
    private void ProximoMes()
    {
        Referencia = Referencia.AddMonths(1);
        Recarregar();
    }

    [RelayCommand]
    private void MesAtual()
    {
        Referencia = new DateOnly(Datas.Hoje.Year, Datas.Hoje.Month, 1);
        Recarregar();
    }
}
