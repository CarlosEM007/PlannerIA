using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Servicos;

namespace PlannerIA.ViewModels;

/// <summary>Base dos diálogos exibidos sobre a janela (overlay).</summary>
public abstract partial class DialogoViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _resultado = new();

    public Task<bool> Resultado => _resultado.Task;

    public abstract string Titulo { get; }
    public virtual string TextoConfirmar => "Salvar";
    public virtual string TextoCancelar => "Cancelar";
    public virtual bool Perigo => false;
    public virtual bool PodeConfirmar => true;
    public virtual double Largura => 540;

    [ObservableProperty]
    private string? _erro;

    /// <summary>Validação antes de fechar. Devolve a mensagem de erro, ou null.</summary>
    protected virtual string? Validar() => null;

    /// <summary>Diálogos em etapas devolvem false para continuar abertos.</summary>
    protected virtual Task<bool> AoConfirmarAsync() => Task.FromResult(true);

    /// <summary>Devolve false para não fechar (ex.: só interromper uma operação em andamento).</summary>
    protected virtual bool AoCancelar() => true;

    protected void AtualizarBotoes()
    {
        OnPropertyChanged(nameof(TextoConfirmar));
        OnPropertyChanged(nameof(TextoCancelar));
        OnPropertyChanged(nameof(PodeConfirmar));
        ConfirmarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(PodeConfirmar))]
    private async Task Confirmar()
    {
        Erro = Validar();
        if (Erro is null && await AoConfirmarAsync()) _resultado.TrySetResult(true);
    }

    [RelayCommand]
    private void Cancelar()
    {
        if (AoCancelar()) _resultado.TrySetResult(false);
    }
}

public partial class DialogoConfirmacaoViewModel(string titulo, string mensagem, string textoConfirmar = "Excluir", bool perigo = true)
    : DialogoViewModel
{
    public override string Titulo => titulo;
    public string Mensagem => mensagem;
    public override string TextoConfirmar => textoConfirmar;
    public override bool Perigo => perigo;
}

public record OpcaoDia(string Rotulo, DateOnly? Data);

public partial class DialogoTarefaViewModel : DialogoViewModel
{
    private readonly string _titulo;

    public DialogoTarefaViewModel(string titulo, string? contexto, Periodo semana, string nome = "", DateOnly? data = null, string? notas = null)
    {
        _titulo = titulo;
        Contexto = contexto;
        Nome = nome;
        Notas = notas ?? "";

        Dias.Add(new OpcaoDia("Sem data", null));
        for (var d = semana.Inicio; d <= semana.Fim; d = d.AddDays(1))
            Dias.Add(new OpcaoDia(Datas.DiaCurto(d), d));
        DiaSelecionado = Dias.FirstOrDefault(o => o.Data == data) ?? Dias[0];
    }

    public override string Titulo => _titulo;
    public string? Contexto { get; }
    public ObservableCollection<OpcaoDia> Dias { get; } = [];

    [ObservableProperty] private string _nome;
    [ObservableProperty] private string _notas;
    [ObservableProperty] private OpcaoDia? _diaSelecionado;

    protected override string? Validar() =>
        string.IsNullOrWhiteSpace(Nome) ? "Dê um nome para a tarefa." : null;
}

public partial class DialogoSemanaViewModel : DialogoViewModel
{
    private readonly string _titulo;
    private const string Formato = "dd/MM/yyyy";

    public DialogoSemanaViewModel(string titulo, DateOnly inicio, DateOnly fim, string nome = "", string? objetivo = null, string textoConfirmar = "Criar semana")
    {
        _titulo = titulo;
        _textoConfirmar = textoConfirmar;
        Nome = nome;
        Objetivo = objetivo ?? "";
        InicioTexto = inicio.ToString(Formato);
        FimTexto = fim.ToString(Formato);
    }

    private readonly string _textoConfirmar;
    public override string Titulo => _titulo;
    public override string TextoConfirmar => _textoConfirmar;

    [ObservableProperty] private string _nome;
    [ObservableProperty] private string _objetivo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resumo))]
    private string _inicioTexto = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resumo))]
    private string _fimTexto = "";

    public DateOnly? Inicio => Ler(InicioTexto);
    public DateOnly? Fim => Ler(FimTexto);

    public string Resumo => Inicio is { } i && Fim is { } f && f >= i
        ? $"{Datas.DiaCurto(i)} → {Datas.DiaCurto(f)} · {f.DayNumber - i.DayNumber + 1} dias"
        : "Datas no formato dd/mm/aaaa";

    [RelayCommand] private void InicioMenos() => Mover(true, -1);
    [RelayCommand] private void InicioMais() => Mover(true, +1);
    [RelayCommand] private void FimMenos() => Mover(false, -1);
    [RelayCommand] private void FimMais() => Mover(false, +1);

    private void Mover(bool inicio, int dias)
    {
        if (inicio && Inicio is { } i)
        {
            var duracao = Fim is { } f ? f.DayNumber - i.DayNumber : 6;
            InicioTexto = i.AddDays(dias).ToString(Formato);
            FimTexto = i.AddDays(dias + duracao).ToString(Formato); // mantém a duração
        }
        else if (!inicio && Fim is { } f)
        {
            FimTexto = f.AddDays(dias).ToString(Formato);
        }
    }

    protected override string? Validar()
    {
        if (Inicio is null || Fim is null) return "Use datas no formato dd/mm/aaaa.";
        if (Fim < Inicio) return "A data final precisa ser depois da inicial.";
        return null;
    }

    private static DateOnly? Ler(string texto) =>
        DateOnly.TryParseExact(texto?.Trim(), [Formato, "d/M/yyyy", "dd/MM/yy", "d/M/yy"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
}

public partial class ToastViewModel(string mensagem, string icone, bool erro) : ObservableObject
{
    public string Mensagem => mensagem;
    public string Icone => icone;
    public bool Erro => erro;
}
