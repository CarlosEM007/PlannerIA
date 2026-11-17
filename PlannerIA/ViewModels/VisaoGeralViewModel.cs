using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;
using PlannerIA.Servicos;

namespace PlannerIA.ViewModels;

public record TarefaNo(string Titulo, double Fracao, string Texto, bool Concluida, SolidColorBrush Cor);

public partial class SemanaNo(Periodo semana, MainViewModel shell) : ObservableObject
{
    public string Titulo => semana.Titulo;
    public string Intervalo => Datas.Intervalo(semana.Inicio, semana.Fim);
    public string? Objetivo => semana.Objetivo;
    public double Fracao => semana.Progresso().Fracao;
    public string Texto => semana.Progresso().ToString();
    public string Percentual => $"{semana.Progresso().Percentual}%";
    public bool EhAtual => semana.Contem(Datas.Hoje);
    public bool Completa => semana.Progresso().Completo;

    public IEnumerable<TarefaNo> Tarefas => semana.TarefasPrincipais.Select(t =>
    {
        var p = t.Progresso();
        return new TarefaNo(t.Titulo, p.Fracao, p.ToString(), t.Concluida, Paleta.CorPara(t.Titulo));
    });

    [ObservableProperty] private bool _expandido = semana.Contem(Datas.Hoje);

    [RelayCommand]
    private void Abrir() => shell.AbrirSemana(semana);
}

public partial class MesNo(Periodo mes, MainViewModel shell) : ObservableObject
{
    public string Titulo => mes.Titulo;
    public double Fracao => mes.Progresso().Fracao;
    public string Texto => $"{mes.Progresso()} subtarefas";
    public string Percentual => $"{mes.Progresso().Percentual}%";
    public IEnumerable<SemanaNo> Semanas { get; } = mes.Filhos.OrderBy(s => s.Inicio).Select(s => new SemanaNo(s, shell)).ToList();

    [ObservableProperty]
    private bool _expandido = mes.Inicio.Year == Datas.Hoje.Year && mes.Inicio.Month == Datas.Hoje.Month;
}

/// <summary>Visão geral: árvore Mês → Semanas → Tarefas com progresso em cada nível.</summary>
public partial class VisaoGeralViewModel(MainViewModel shell) : PaginaViewModel(shell)
{
    public ObservableCollection<MesNo> Meses { get; } = [];

    [ObservableProperty] private double _geralFracao;
    [ObservableProperty] private string _geralPercentual = "";
    [ObservableProperty] private string _subtarefasTexto = "";
    [ObservableProperty] private string _semanasTexto = "";
    [ObservableProperty] private string _atrasadasTexto = "";
    [ObservableProperty] private string _diasRestantesTexto = "";

    public bool SemPlano => Plano.Vazio;

    public override void Recarregar()
    {
        Meses.Clear();
        foreach (var m in Plano.Meses)
            Meses.Add(new MesNo(m, Shell));

        if (Meses.Count > 0 && !Meses.Any(m => m.Expandido))
            Meses[0].Expandido = true;

        var semanas = Plano.Semanas.ToList();
        var total = Plano.Meses.Aggregate(new Progresso(0, 0), (acc, m) => acc + m.Progresso());
        GeralFracao = total.Fracao;
        GeralPercentual = $"{total.Percentual}%";
        SubtarefasTexto = total.ToString();
        SemanasTexto = $"{semanas.Count(s => s.Progresso().Completo)} de {semanas.Count}";
        AtrasadasTexto = Plano.Atrasadas(Datas.Hoje).Count().ToString();

        var fim = semanas.Count == 0 ? (DateOnly?)null : semanas.Max(s => s.Fim);
        DiasRestantesTexto = fim is { } f && f >= Datas.Hoje ? (f.DayNumber - Datas.Hoje.DayNumber).ToString() : "0";

        OnPropertyChanged(nameof(SemPlano));
    }
}

public partial class ConfiguracoesViewModel(MainViewModel shell, ConfiguracaoApp config, IArmazenamentoChaveApi chaves,
    PlannerIA.Infrastructure.Ia.ClienteClaudeCli cli)
    : PaginaViewModel(shell)
{
    // ---------- provedor da IA ----------

    public const string ComandoInstalarCli = "irm https://claude.ai/install.ps1 | iex";

    /// <summary>"Automatico", "ClaudeCli" ou "Api" (RadioButtons ligados com o conversor IgualA).</summary>
    public string ModoIa
    {
        get => config.ModoIa;
        set
        {
            if (config.ModoIa == value) return;
            config.ModoIa = value;
            config.Salvar();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MostrarChave));
            OnPropertyChanged(nameof(MostrarCli));
            _ = Shell.VerificarIaAsync();
        }
    }

    public bool MostrarChave => config.LerModoIa() != PlannerIA.Core.Portas.ModoIa.ClaudeCli;
    public bool MostrarCli => config.LerModoIa() != PlannerIA.Core.Portas.ModoIa.Api;

    [ObservableProperty] private string _cliTexto = "Procurando o Claude CLI…";
    [ObservableProperty] private bool _cliInstalado;

    public void AtualizarInfoCli()
    {
        var caminho = cli.CaminhoEncontrado ?? PlannerIA.Infrastructure.Ia.ClienteClaudeCli.Localizar();
        CliInstalado = caminho is not null;
        CliTexto = caminho is null
            ? "Claude CLI não encontrado neste computador."
            : $"Encontrado: {caminho}" + (cli.Versao is { Length: > 0 } v ? $" · versão {v}" : "")
              + (cli.Logado switch { true => " · logado", false => " · sem login", _ => "" });
    }

    [RelayCommand]
    private void CopiarComandoInstalacao()
    {
        try
        {
            System.Windows.Clipboard.SetText(ComandoInstalarCli);
            Shell.Avisar("Comando copiado. Cole no PowerShell para instalar o Claude CLI.", "");
        }
        catch
        {
            Shell.Avisar("Não foi possível copiar. Digite o comando mostrado na tela.", "", erro: true);
        }
    }

    /// <summary>Abre um terminal com o Claude CLI para o usuário fazer login com a assinatura.</summary>
    [RelayCommand]
    private void AbrirLoginCli()
    {
        var caminho = PlannerIA.Infrastructure.Ia.ClienteClaudeCli.Localizar();
        if (caminho is null)
        {
            Shell.Avisar("Instale o Claude CLI primeiro.", "", erro: true);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{caminho}\"\"") { UseShellExecute = true });
            Shell.Avisar("Faça login no terminal que abriu (comando /login, se pedir). Depois clique em Verificar de novo.", "");
        }
        catch (Exception ex)
        {
            Shell.Avisar($"Não foi possível abrir o terminal: {ex.Message}", "", erro: true);
        }
    }

    // ---------- chave da API ----------

    /// <summary>Texto digitado no campo de senha (preenchido pela view).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SalvarChaveCommand))]
    private string _novaChave = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SalvarChaveCommand), nameof(RemoverChaveCommand))]
    private bool _salvandoChave;

    [ObservableProperty] private string _chaveAtualTexto = "";
    [ObservableProperty] private bool _temChaveDoUsuario;
    [ObservableProperty] private string? _avisoChave;

    /// <summary>Pedido para a view limpar o campo de senha.</summary>
    public event EventHandler? LimparCampoChave;

    public string NomeVariavelChave => chaves.NomeVariavel;

    private void AtualizarChaveAtual()
    {
        var (chave, origem) = chaves.Ler();
        TemChaveDoUsuario = origem == OrigemChave.Usuario;
        ChaveAtualTexto = origem switch
        {
            OrigemChave.Nenhuma => "Nenhuma chave salva",
            OrigemChave.Usuario => $"Chave salva: {Mascarar(chave!)}",
            _ => $"Chave definida fora do app (processo ou sistema): {Mascarar(chave!)}"
        };
    }

    private static string Mascarar(string chave) =>
        chave.Length <= 12 ? new string('•', chave.Length) : $"{chave[..7]}…{chave[^4..]}";

    private bool PodeSalvarChave() => !SalvandoChave && !string.IsNullOrWhiteSpace(NovaChave);
    private bool PodeRemoverChave() => !SalvandoChave;

    partial void OnNovaChaveChanged(string value) =>
        AvisoChave = value.Trim().Length > 0 && !value.Trim().StartsWith("sk-ant-", StringComparison.Ordinal)
            ? "As chaves da Anthropic costumam começar com “sk-ant-”. Confira se colou a chave certa."
            : null;

    [RelayCommand(CanExecute = nameof(PodeSalvarChave))]
    private async Task SalvarChave()
    {
        SalvandoChave = true;
        try
        {
            await chaves.SalvarAsync(NovaChave);
            NovaChave = "";
            LimparCampoChave?.Invoke(this, EventArgs.Empty);
            AtualizarChaveAtual();
        AtualizarInfoCli();
            await Shell.VerificarIaAsync();
            if (Shell.IaDisponivel)
                Shell.Avisar("Chave salva. A IA está disponível.", "");
            else
                Shell.Avisar($"Chave salva, mas a IA ainda não está disponível: {Shell.IaStatusTexto.ToLowerInvariant()}.", "", erro: true);
        }
        catch (FalhaIaException ex)
        {
            Shell.Avisar(ex.Message, "", erro: true);
        }
        finally
        {
            SalvandoChave = false;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeRemoverChave))]
    private async Task RemoverChave()
    {
        var dlg = new DialogoConfirmacaoViewModel("Remover a chave da API",
            $"A variável {chaves.NomeVariavel} será apagada das variáveis do seu usuário no Windows. A IA fica desligada até você salvar uma chave de novo.",
            "Remover");
        if (!await Shell.AbrirDialogoAsync(dlg)) return;

        SalvandoChave = true;
        try
        {
            await chaves.RemoverAsync();
            AtualizarChaveAtual();
        AtualizarInfoCli();
            await Shell.VerificarIaAsync();
            Shell.Avisar("Chave removida. A IA foi desligada.", "");
        }
        catch (FalhaIaException ex)
        {
            Shell.Avisar(ex.Message, "", erro: true);
        }
        finally
        {
            SalvandoChave = false;
        }
    }

    // ---------- tema ----------

    public string Tema
    {
        get => config.Tema;
        set
        {
            if (config.Tema == value) return;
            config.Tema = value;
            GerenciadorTema.Aplicar(value);
            config.Salvar();
            OnPropertyChanged();
        }
    }

    public string PastaDados => Caminhos.PastaDados;
    public string ArquivoBanco => Caminhos.Banco;

    [ObservableProperty] private string _estatisticas = "";

    public override void Recarregar()
    {
        var semanas = Plano.Semanas.ToList();
        var tarefas = semanas.Sum(s => s.Tarefas.Count);
        Estatisticas = $"{Plano.Meses.Count} mês(es) · {semanas.Count} semana(s) · {tarefas} tarefa(s)";
        AtualizarChaveAtual();
        AtualizarInfoCli();
    }

    [RelayCommand]
    private void AbrirPasta()
    {
        Directory.CreateDirectory(Caminhos.PastaDados);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Caminhos.PastaDados}\"") { UseShellExecute = true });
    }
}
