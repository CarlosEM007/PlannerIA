using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Servicos;
using PlannerIA.Servicos;

namespace PlannerIA.ViewModels;

/// <summary>Uma tarefa (principal ou subtarefa) exibida em tela.</summary>
public partial class TarefaItemViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    public TarefaItemViewModel(Tarefa tarefa, MainViewModel shell, TarefaItemViewModel? pai = null, bool incluirSubtarefas = true)
    {
        Tarefa = tarefa;
        _shell = shell;
        Pai = pai;
        if (incluirSubtarefas)
            foreach (var s in tarefa.SubtarefasOrdenadas)
                Subtarefas.Add(new TarefaItemViewModel(s, shell, this));
    }

    public Tarefa Tarefa { get; }
    public TarefaItemViewModel? Pai { get; }
    public ObservableCollection<TarefaItemViewModel> Subtarefas { get; } = [];

    public string Titulo => Tarefa.Titulo;
    public string? Notas => Tarefa.Notas;
    public bool TemNotas => !string.IsNullOrWhiteSpace(Tarefa.Notas);
    public bool TemData => Tarefa.Data is not null;
    public string DataTexto => Tarefa.Data is { } d ? Datas.DiaCurto(d) : "";
    public bool EhHoje => Tarefa.Data == Datas.Hoje;
    public bool Atrasada => !Tarefa.Concluida && Tarefa.Data is { } d && d < Datas.Hoje;

    public bool EhSubtarefa => Tarefa.EhSubtarefa;
    public bool TemSubtarefas => Tarefa.TemSubtarefas;
    public bool PodeMarcar => Tarefa.PodeMarcarManualmente;

    /// <summary>Título da tarefa principal (para mostrar a categoria em listas de subtarefas).</summary>
    public string? TituloPrincipal => Tarefa.TarefaPai?.Titulo ?? (Tarefa.EhSubtarefa ? null : Tarefa.Titulo);
    public string? TituloSemana => Tarefa.Periodo?.Titulo;
    public SolidColorBrush Cor => Paleta.CorPara(TituloPrincipal);

    public Progresso Progresso => Tarefa.Progresso();
    public double ProgressoFracao => Progresso.Fracao;
    public string ProgressoTexto => Progresso.ToString();

    [ObservableProperty]
    private bool _expandido = true;

    public bool Concluida
    {
        get => Tarefa.Concluida;
        set
        {
            if (value == Tarefa.Concluida || !PodeMarcar) return;
            _ = _shell.DefinirConclusaoAsync(this, value);
        }
    }

    public void Atualizar() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private void AlternarExpandido() => Expandido = !Expandido;

    [RelayCommand]
    private Task AdicionarSubtarefa() => _shell.NovaSubtarefaAsync(this);

    [RelayCommand]
    private Task Editar() => _shell.EditarTarefaAsync(this);

    [RelayCommand]
    private Task Remover() => _shell.RemoverTarefaAsync(this);

    [RelayCommand]
    private Task MoverAcima() => _shell.MoverTarefaAsync(this, -1);

    [RelayCommand]
    private Task MoverAbaixo() => _shell.MoverTarefaAsync(this, +1);
}
