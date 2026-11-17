using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Servicos;

namespace PlannerIA.ViewModels;

public partial class DiaResumoViewModel(DateOnly data, int feitas, int total, MainViewModel shell) : ObservableObject
{
    public DateOnly Data => data;
    public string Semana => Datas.DiaCurto(data).Split(' ')[0];
    public string Numero => data.Day.ToString("00");
    public int Feitas => feitas;
    public int Total => total;
    public bool TemTarefas => total > 0;
    public bool Completo => total > 0 && feitas == total;
    public double Fracao => total == 0 ? 0 : (double)feitas / total;
    public string Texto => total == 0 ? "livre" : $"{feitas}/{total}";
    public bool EhHoje => data == Datas.Hoje;

    [RelayCommand]
    private void Abrir() => shell.AbrirDia(data);
}

/// <summary>Visão Semana: objetivo, progresso, faixa de dias e cartões de tarefas com subtarefas.</summary>
public partial class SemanaViewModel(MainViewModel shell) : PaginaViewModel(shell)
{
    private Periodo? _semanaEscolhida;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemSemana), nameof(Titulo), nameof(Intervalo), nameof(Objetivo), nameof(TemObjetivo), nameof(Selo))]
    [NotifyCanExecuteChangedFor(nameof(AnteriorCommand), nameof(ProximaCommand))]
    private Periodo? _semana;

    public bool TemSemana => Semana is not null;
    public string Titulo => Semana?.Titulo ?? "";
    public string Intervalo => Semana is null ? "" : $"{Datas.DiaCurto(Semana.Inicio)} → {Datas.DiaCurto(Semana.Fim)} · {Semana.PeriodoPai?.Titulo}";
    public string? Objetivo => Semana?.Objetivo;
    public bool TemObjetivo => !string.IsNullOrWhiteSpace(Semana?.Objetivo);

    public string Selo => Semana is null ? "" :
        Semana.Contem(Datas.Hoje) ? "SEMANA ATUAL" :
        Semana.Fim < Datas.Hoje ? "SEMANA PASSADA" : "PRÓXIMA SEMANA";

    public ObservableCollection<TarefaItemViewModel> Tarefas { get; } = [];
    public ObservableCollection<DiaResumoViewModel> Dias { get; } = [];

    [ObservableProperty] private double _progressoFracao;
    [ObservableProperty] private string _progressoTexto = "";
    [ObservableProperty] private string _progressoPercentual = "";

    public bool SemPlano => Plano.Vazio;

    public void Exibir(Periodo? semana)
    {
        _semanaEscolhida = semana;
        Recarregar();
    }

    public override void Recarregar()
    {
        // Se a semana escolhida foi removida, volta para a mais relevante.
        if (_semanaEscolhida is not null && !Plano.Semanas.Contains(_semanaEscolhida))
            _semanaEscolhida = null;
        Semana = _semanaEscolhida ?? Plano.SemanaMaisRelevante(Datas.Hoje);
        _semanaEscolhida = Semana;

        Tarefas.Clear();
        if (Semana is not null)
            foreach (var t in Semana.TarefasPrincipais)
                Tarefas.Add(new TarefaItemViewModel(t, Shell));

        AtualizarProgresso();
        OnPropertyChanged(nameof(SemPlano));
    }

    public override void AoAlterarProgresso() => AtualizarProgresso();

    private void AtualizarProgresso()
    {
        Dias.Clear();
        if (Semana is null) return;

        var p = Semana.Progresso();
        ProgressoFracao = p.Fracao;
        ProgressoTexto = $"{p} subtarefas concluídas";
        ProgressoPercentual = $"{p.Percentual}%";

        var folhas = Semana.TarefasPrincipais.SelectMany(t => t.TemSubtarefas ? t.SubtarefasOrdenadas : [t]).ToList();
        for (var d = Semana.Inicio; d <= Semana.Fim; d = d.AddDays(1))
        {
            var doDia = folhas.Where(t => t.Data == d).ToList();
            Dias.Add(new DiaResumoViewModel(d, doDia.Count(t => t.Concluida), doDia.Count, Shell));
        }
    }

    private Periodo? Vizinha(int direcao)
    {
        if (Semana is null) return null;
        var semanas = Plano.Semanas.ToList();
        var i = semanas.IndexOf(Semana) + direcao;
        return i >= 0 && i < semanas.Count ? semanas[i] : null;
    }

    private bool TemAnterior() => Vizinha(-1) is not null;
    private bool TemProxima() => Vizinha(+1) is not null;

    [RelayCommand(CanExecute = nameof(TemAnterior))]
    private void Anterior() => Exibir(Vizinha(-1));

    [RelayCommand(CanExecute = nameof(TemProxima))]
    private void Proxima() => Exibir(Vizinha(+1));

    [RelayCommand]
    private void Atual() => Exibir(Plano.SemanaMaisRelevante(Datas.Hoje));

    [RelayCommand]
    private Task NovaTarefa() => Semana is null ? Task.CompletedTask : Shell.NovaTarefaAsync(Semana);

    [RelayCommand]
    private Task Editar() => Semana is null ? Task.CompletedTask : Shell.EditarSemanaAsync(Semana);

    [RelayCommand]
    private Task Remover() => Semana is null ? Task.CompletedTask : Shell.RemoverSemanaAsync(Semana);
}
