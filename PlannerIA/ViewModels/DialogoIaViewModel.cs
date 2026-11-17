using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;
using PlannerIA.Servicos;

namespace PlannerIA.ViewModels;

public enum EtapaIa { Pedido, Gerando, Previa }

/// <summary>Item editável da prévia (tarefa principal ou subtarefa).</summary>
public partial class TarefaPropostaViewModel : ObservableObject
{
    public TarefaPropostaViewModel(Tarefa t, string? tituloPrincipal = null)
    {
        _titulo = t.Titulo;
        Data = t.Data;
        Notas = t.Notas;
        Cor = Paleta.CorPara(tituloPrincipal ?? t.Titulo);
        foreach (var s in t.SubtarefasOrdenadas)
            Subtarefas.Add(new TarefaPropostaViewModel(s, t.Titulo));
    }

    [ObservableProperty] private bool _incluir = true;
    [ObservableProperty] private string _titulo;

    public DateOnly? Data { get; }
    public string? Notas { get; }
    public bool TemData => Data is not null;
    public bool TemNotas => !string.IsNullOrWhiteSpace(Notas);
    public string DataTexto => Data is { } d ? Datas.DiaCurto(d) : "";
    public System.Windows.Media.SolidColorBrush Cor { get; }
    public ObservableCollection<TarefaPropostaViewModel> Subtarefas { get; } = [];
}

public partial class SemanaPropostaViewModel : ObservableObject
{
    public SemanaPropostaViewModel(Periodo semana, Periodo? existente)
    {
        Inicio = semana.Inicio;
        Fim = semana.Fim;
        Existente = existente;
        _titulo = existente?.Titulo ?? semana.Titulo;
        _objetivo = semana.Objetivo ?? "";
        foreach (var t in semana.TarefasPrincipais)
            Tarefas.Add(new TarefaPropostaViewModel(t));
    }

    public DateOnly Inicio { get; }
    public DateOnly Fim { get; }
    public Periodo? Existente { get; }
    public bool EhExistente => Existente is not null;
    public string Intervalo => $"{Datas.DiaCurto(Inicio)} → {Datas.DiaCurto(Fim)}";
    public string Destino => EhExistente ? "ACRESCENTA À SEMANA EXISTENTE" : "NOVA SEMANA";

    [ObservableProperty] private bool _incluir = true;
    [ObservableProperty] private string _titulo;
    [ObservableProperty] private string _objetivo;

    public ObservableCollection<TarefaPropostaViewModel> Tarefas { get; } = [];
}

/// <summary>
/// Gerar plano com IA: pedido em texto livre → prévia editável → aplicar ou descartar.
/// Nada é salvo sem a confirmação do usuário.
/// </summary>
public partial class DialogoIaViewModel(GeradorDePlano gerador, PlanoService plano) : DialogoViewModel
{
    private CancellationTokenSource? _cts;

    public override string Titulo => "Gerar plano com IA";
    public override double Largura => 720;

    public override string TextoConfirmar => Etapa switch
    {
        EtapaIa.Gerando => "Gerando…",
        EtapaIa.Previa => "Aplicar plano",
        _ => "Gerar plano"
    };

    public override string TextoCancelar => Etapa switch
    {
        EtapaIa.Gerando => "Interromper",
        EtapaIa.Previa => "Descartar",
        _ => "Cancelar"
    };

    public override bool PodeConfirmar => Etapa switch
    {
        EtapaIa.Pedido => !string.IsNullOrWhiteSpace(Pedido),
        EtapaIa.Previa => Semanas.Any(s => s.Incluir),
        _ => false
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmPedido), nameof(EmGeracao), nameof(EmPrevia))]
    private EtapaIa _etapa = EtapaIa.Pedido;

    public bool EmPedido => Etapa == EtapaIa.Pedido;
    public bool EmGeracao => Etapa == EtapaIa.Gerando;
    public bool EmPrevia => Etapa == EtapaIa.Previa;

    partial void OnEtapaChanged(EtapaIa value) => AtualizarBotoes();

    [ObservableProperty] private string _pedido = "";
    partial void OnPedidoChanged(string value) => AtualizarBotoes();

    public string[] Exemplos { get; } =
    [
        "Planeje as próximas 2 semanas: terminar o jogo, ler .NET 3x por semana e inglês no ônibus de terça a quinta",
        "Acrescente à semana atual 20 minutos de inglês por dia",
        "Crie a semana que vem focada em portfólio e currículo",
    ];

    [RelayCommand]
    private void UsarExemplo(string exemplo) => Pedido = exemplo;

    // ---------- prévia ----------

    [ObservableProperty] private string? _resumo;
    public ObservableCollection<string> Avisos { get; } = [];
    public ObservableCollection<SemanaPropostaViewModel> Semanas { get; } = [];
    public bool TemAvisos => Avisos.Count > 0;

    [RelayCommand]
    private void RefazerPedido()
    {
        Erro = null;
        Etapa = EtapaIa.Pedido;
    }

    protected override async Task<bool> AoConfirmarAsync()
    {
        if (Etapa == EtapaIa.Previa) return true;
        if (Etapa == EtapaIa.Pedido) await GerarAsync();
        return false;
    }

    protected override bool AoCancelar()
    {
        if (Etapa != EtapaIa.Gerando) return true;
        _cts?.Cancel();
        return false;
    }

    private async Task GerarAsync()
    {
        Erro = null;
        Etapa = EtapaIa.Gerando;
        _cts = new CancellationTokenSource();
        try
        {
            var proposta = await gerador.GerarAsync(Pedido, Datas.Hoje, _cts.Token);

            Resumo = proposta.Resumo;
            Avisos.Clear();
            foreach (var a in proposta.Avisos) Avisos.Add(a);
            OnPropertyChanged(nameof(TemAvisos));

            foreach (var s in Semanas) s.PropertyChanged -= AoMudarSemana;
            Semanas.Clear();
            foreach (var s in proposta.Semanas)
            {
                var existente = plano.Semanas.FirstOrDefault(e => e.Inicio == s.Inicio && e.Fim == s.Fim);
                var vm = new SemanaPropostaViewModel(s, existente);
                vm.PropertyChanged += AoMudarSemana;
                Semanas.Add(vm);
            }
            Etapa = EtapaIa.Previa;
        }
        catch (OperationCanceledException)
        {
            Etapa = EtapaIa.Pedido;
        }
        catch (FalhaIaException ex)
        {
            Erro = ex.Message;
            Etapa = EtapaIa.Pedido;
        }
        catch (RegraDeDominioException ex)
        {
            Erro = ex.Message;
            Etapa = EtapaIa.Pedido;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    private void AoMudarSemana(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemanaPropostaViewModel.Incluir)) AtualizarBotoes();
    }

    /// <summary>Monta as semanas aceitas, já com as edições e só os itens marcados.</summary>
    public List<Periodo> SemanasAceitas()
    {
        var resultado = new List<Periodo>();
        foreach (var s in Semanas.Where(s => s.Incluir))
        {
            var semana = new Periodo
            {
                Tipo = TipoPeriodo.Semana,
                Titulo = string.IsNullOrWhiteSpace(s.Titulo) ? $"Semana — {Datas.Intervalo(s.Inicio, s.Fim)}" : s.Titulo.Trim(),
                Objetivo = string.IsNullOrWhiteSpace(s.Objetivo) ? null : s.Objetivo.Trim(),
                Inicio = s.Inicio,
                Fim = s.Fim
            };
            var ordem = 0;
            foreach (var t in s.Tarefas.Where(t => t.Incluir && !string.IsNullOrWhiteSpace(t.Titulo)))
            {
                var principal = new Tarefa
                {
                    Titulo = t.Titulo.Trim(), Data = t.Data, Notas = t.Notas, Ordem = ordem++,
                    PeriodoId = semana.Id, Periodo = semana
                };
                semana.Tarefas.Add(principal);
                foreach (var st in t.Subtarefas.Where(x => x.Incluir && !string.IsNullOrWhiteSpace(x.Titulo)))
                    semana.Tarefas.Add(principal.AdicionarSubtarefa(st.Titulo.Trim(), st.Data, st.Notas));
            }
            resultado.Add(semana);
        }
        return resultado;
    }
}
