using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Servicos;

namespace PlannerIA.ViewModels;

/// <summary>Visão Dia: tarefas com data no dia, atrasadas e as da semana sem data.</summary>
public partial class HojeViewModel(MainViewModel shell) : PaginaViewModel(shell)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhHoje), nameof(TituloData), nameof(Saudacao), nameof(Sobretitulo))]
    private DateOnly _data = Datas.Hoje;

    public bool EhHoje => Data == Datas.Hoje;

    public string TituloData
    {
        get
        {
            var s = Data.ToDateTime(TimeOnly.MinValue).ToString("dddd, d 'de' MMMM", Datas.PtBr);
            return char.ToUpper(s[0], Datas.PtBr) + s[1..];
        }
    }

    public string Sobretitulo => EhHoje
        ? "HOJE"
        : Data == Datas.Hoje.AddDays(1) ? "AMANHÃ"
        : Data == Datas.Hoje.AddDays(-1) ? "ONTEM"
        : Data < Datas.Hoje ? "DIA PASSADO" : "PRÓXIMO DIA";

    public string Saudacao
    {
        get
        {
            if (!EhHoje) return "Planejamento do dia";
            var h = DateTime.Now.Hour;
            return h < 12 ? "Bom dia" : h < 18 ? "Boa tarde" : "Boa noite";
        }
    }

    public ObservableCollection<TarefaItemViewModel> Tarefas { get; } = [];
    public ObservableCollection<TarefaItemViewModel> Atrasadas { get; } = [];
    public ObservableCollection<TarefaItemViewModel> SemData { get; } = [];

    [ObservableProperty] private Periodo? _semana;
    [ObservableProperty] private double _progressoFracao;
    [ObservableProperty] private string _progressoTexto = "";
    [ObservableProperty] private string _progressoPercentual = "";
    [ObservableProperty] private string _resumoDia = "";
    [ObservableProperty] private double _semanaFracao;
    [ObservableProperty] private string _semanaTexto = "";

    public bool TemTarefas => Tarefas.Count > 0;
    public bool TemAtrasadas => Atrasadas.Count > 0;
    public bool TemSemData => SemData.Count > 0;
    public bool SemPlano => Plano.Vazio;
    public bool TemSemana => Semana is not null;

    public void Exibir(DateOnly dia)
    {
        Data = dia;
        Recarregar();
    }

    public override void Recarregar()
    {
        Semana = Plano.SemanaQueContem(Data);

        Tarefas.Clear();
        foreach (var t in Plano.TarefasDoDia(Data).OrderBy(t => t.Concluida).ThenBy(t => t.TarefaPai?.Ordem ?? t.Ordem))
            Tarefas.Add(new TarefaItemViewModel(t, Shell, incluirSubtarefas: false));

        Atrasadas.Clear();
        if (EhHoje)
            foreach (var t in Plano.Atrasadas(Data))
                Atrasadas.Add(new TarefaItemViewModel(t, Shell, incluirSubtarefas: false));

        SemData.Clear();
        if (Semana is not null)
            foreach (var t in Semana.TarefasPrincipais
                         .SelectMany(p => p.TemSubtarefas ? p.SubtarefasOrdenadas : [p])
                         .Where(t => t.Data is null))
                SemData.Add(new TarefaItemViewModel(t, Shell, incluirSubtarefas: false));

        AtualizarProgresso();
        OnPropertyChanged(nameof(TemTarefas));
        OnPropertyChanged(nameof(TemAtrasadas));
        OnPropertyChanged(nameof(TemSemData));
        OnPropertyChanged(nameof(SemPlano));
        OnPropertyChanged(nameof(TemSemana));
    }

    public override void AoAlterarProgresso() => AtualizarProgresso();

    private void AtualizarProgresso()
    {
        var feitas = Tarefas.Count(t => t.Concluida);
        var p = new Progresso(feitas, Tarefas.Count);
        ProgressoFracao = p.Fracao;
        ProgressoTexto = p.Total == 0 ? "Nada marcado para este dia" : $"{p} feitas";
        ProgressoPercentual = p.Total == 0 ? "—" : $"{p.Percentual}%";
        ResumoDia = p.Total switch
        {
            0 => "Dia livre. Que tal adiantar algo da semana?",
            _ when p.Completo => "Tudo feito por hoje. Excelente!",
            _ => $"{p.Total - p.Feitas} tarefa(s) para concluir"
        };

        if (Semana is not null)
        {
            var ps = Semana.Progresso();
            SemanaFracao = ps.Fracao;
            SemanaTexto = $"{ps} · {ps.Percentual}%";
        }
    }

    [RelayCommand] private void DiaAnterior() => Exibir(Data.AddDays(-1));
    [RelayCommand] private void ProximoDia() => Exibir(Data.AddDays(1));
    [RelayCommand] private void IrHoje() => Exibir(Datas.Hoje);

    [RelayCommand]
    private void AbrirSemana()
    {
        if (Semana is not null) Shell.AbrirSemana(Semana);
    }

    [RelayCommand]
    private async Task NovaTarefa()
    {
        if (Semana is null)
        {
            Shell.Avisar("Nenhuma semana contém este dia. Crie uma semana primeiro.", "", erro: true);
            return;
        }
        await Shell.NovaTarefaAsync(Semana, Data);
    }
}
