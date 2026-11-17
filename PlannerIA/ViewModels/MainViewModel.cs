using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;
using PlannerIA.Infrastructure.Ia;
using PlannerIA.Servicos;

namespace PlannerIA.ViewModels;

public abstract partial class PaginaViewModel(MainViewModel shell) : ObservableObject
{
    protected MainViewModel Shell { get; } = shell;
    protected PlanoService Plano => Shell.Plano;

    /// <summary>Reconstrói a página a partir do plano (após mudanças estruturais).</summary>
    public abstract void Recarregar();

    /// <summary>Chamado quando só o progresso mudou (checkbox). Por padrão, nada.</summary>
    public virtual void AoAlterarProgresso() { }
}

/// <summary>Shell do app: navegação, diálogos, toasts e operações compartilhadas entre páginas.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IImportadorPlanilha _importador;
    private readonly ConfiguracaoApp _config;

    private readonly IClienteIa _ia;
    private readonly GeradorDePlano _gerador;

    public MainViewModel(PlanoService plano, IImportadorPlanilha importador, ConfiguracaoApp config,
        IClienteIa ia, GeradorDePlano gerador, IArmazenamentoChaveApi chaves, ClienteClaudeCli cli)
    {
        Plano = plano;
        _importador = importador;
        _config = config;
        _ia = ia;
        _gerador = gerador;

        Hoje = new HojeViewModel(this);
        Semana = new SemanaViewModel(this);
        Mes = new MesViewModel(this);
        Geral = new VisaoGeralViewModel(this);
        Configuracoes = new ConfiguracoesViewModel(this, config, chaves, cli);

        _paginaAtual = Hoje;
    }

    public PlanoService Plano { get; }

    public HojeViewModel Hoje { get; }
    public SemanaViewModel Semana { get; }
    public MesViewModel Mes { get; }
    public VisaoGeralViewModel Geral { get; }
    public ConfiguracoesViewModel Configuracoes { get; }

    [ObservableProperty]
    private PaginaViewModel _paginaAtual;

    /// <summary>Chave da página para os botões da barra lateral.</summary>
    public string PaginaSelecionada
    {
        get => PaginaAtual switch
        {
            SemanaViewModel => "Semana",
            MesViewModel => "Mes",
            VisaoGeralViewModel => "Geral",
            ConfiguracoesViewModel => "Config",
            _ => "Hoje"
        };
        set => Navegar(value switch
        {
            "Semana" => Semana,
            "Mes" => Mes,
            "Geral" => Geral,
            "Config" => Configuracoes,
            _ => Hoje
        });
    }

    partial void OnPaginaAtualChanged(PaginaViewModel value) => OnPropertyChanged(nameof(PaginaSelecionada));

    public void Navegar(PaginaViewModel pagina)
    {
        if (pagina == PaginaAtual) return;
        pagina.Recarregar();
        PaginaAtual = pagina;
    }

    // ---------- resumo da barra lateral ----------

    [ObservableProperty] private string _resumoTitulo = "Sem semana ativa";
    [ObservableProperty] private string _resumoTexto = "Importe a planilha ou crie uma semana";
    [ObservableProperty] private double _resumoFracao;
    [ObservableProperty] private string _resumoPercentual = "0%";

    private void AtualizarResumo()
    {
        Paleta.Definir(Plano.Semanas.SelectMany(s => s.TarefasPrincipais).Select(t => t.Titulo));

        var semana = Plano.SemanaMaisRelevante(Datas.Hoje);
        if (semana is null)
        {
            ResumoTitulo = "Sem semana ativa";
            ResumoTexto = "Importe a planilha ou crie uma semana";
            ResumoFracao = 0;
            ResumoPercentual = "—";
            return;
        }
        var p = semana.Progresso();
        ResumoTitulo = semana.Titulo.Split('—')[0].Trim();
        ResumoTexto = $"{p} subtarefas";
        ResumoFracao = p.Fracao;
        ResumoPercentual = $"{p.Percentual}%";
    }

    public async Task InicializarAsync()
    {
        await Plano.InicializarAsync();
        AtualizarResumo();
        PaginaAtual.Recarregar();
    }

    private void RecarregarTudo()
    {
        AtualizarResumo();
        PaginaAtual.Recarregar();
    }

    // ---------- navegação contextual ----------

    public void AbrirSemana(Periodo semana)
    {
        Semana.Exibir(semana);
        if (PaginaAtual != Semana) PaginaAtual = Semana;
    }

    public void AbrirDia(DateOnly dia)
    {
        Hoje.Exibir(dia);
        if (PaginaAtual != Hoje) PaginaAtual = Hoje;
    }

    // ---------- diálogos ----------

    [ObservableProperty]
    private DialogoViewModel? _dialogo;

    public async Task<bool> AbrirDialogoAsync(DialogoViewModel dialogo)
    {
        Dialogo = dialogo;
        try
        {
            return await dialogo.Resultado;
        }
        finally
        {
            Dialogo = null;
        }
    }

    // ---------- toasts ----------

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public void Avisar(string mensagem, string icone = "", bool erro = false)
    {
        var toast = new ToastViewModel(mensagem, icone, erro);
        Toasts.Add(toast);
        while (Toasts.Count > 3) Toasts.RemoveAt(0);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(erro ? 5 : 3.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();
    }

    /// <summary>Executa uma operação e mostra as regras de domínio violadas como aviso.</summary>
    private async Task<bool> Tentar(Func<Task> acao)
    {
        try
        {
            await acao();
            return true;
        }
        catch (RegraDeDominioException ex)
        {
            Avisar(ex.Message, "", erro: true);
        }
        catch (Exception ex)
        {
            Avisar($"Algo deu errado: {ex.Message}", "", erro: true);
        }
        return false;
    }

    // ---------- tarefas ----------

    public async Task DefinirConclusaoAsync(TarefaItemViewModel item, bool concluida)
    {
        var principal = item.Tarefa.TarefaPai ?? item.Tarefa;
        var semana = item.Tarefa.Periodo;
        var principalAntes = principal.Concluida;
        var semanaAntes = semana?.Progresso().Completo ?? false;

        var ok = await Tentar(() => Plano.DefinirConclusaoAsync(item.Tarefa, concluida));

        item.Atualizar();
        item.Pai?.Atualizar();
        AtualizarResumo();
        PaginaAtual.AoAlterarProgresso();
        if (!ok) return;

        if (semana is not null && !semanaAntes && semana.Progresso().Completo)
            Avisar($"Semana concluída! {semana.Titulo.Split('—')[0].Trim()} está 100%.", "");
        else if (principal.TemSubtarefas && !principalAntes && principal.Concluida)
            Avisar($"“{principal.Titulo}” concluída", "");
    }

    public async Task NovaTarefaAsync(Periodo semana, DateOnly? dataSugerida = null)
    {
        var dlg = new DialogoTarefaViewModel("Nova tarefa", semana.Titulo, semana, data: dataSugerida);
        if (!await AbrirDialogoAsync(dlg)) return;
        if (await Tentar(() => Plano.CriarTarefaAsync(semana, dlg.Nome, dlg.DiaSelecionado?.Data, dlg.Notas)))
        {
            RecarregarTudo();
            Avisar("Tarefa criada");
        }
    }

    public async Task NovaSubtarefaAsync(TarefaItemViewModel principal)
    {
        var semana = principal.Tarefa.Periodo!;
        var dlg = new DialogoTarefaViewModel("Nova subtarefa", principal.Titulo, semana);
        if (!await AbrirDialogoAsync(dlg)) return;
        if (await Tentar(() => Plano.CriarSubtarefaAsync(principal.Tarefa, dlg.Nome, dlg.DiaSelecionado?.Data, dlg.Notas)))
            RecarregarTudo();
    }

    public async Task EditarTarefaAsync(TarefaItemViewModel item)
    {
        var t = item.Tarefa;
        var dlg = new DialogoTarefaViewModel(t.EhSubtarefa ? "Editar subtarefa" : "Editar tarefa",
            t.TarefaPai?.Titulo ?? t.Periodo?.Titulo, t.Periodo!, t.Titulo, t.Data, t.Notas);
        if (!await AbrirDialogoAsync(dlg)) return;
        if (await Tentar(() => Plano.AtualizarTarefaAsync(t, dlg.Nome, dlg.DiaSelecionado?.Data, dlg.Notas)))
            RecarregarTudo();
    }

    public async Task RemoverTarefaAsync(TarefaItemViewModel item)
    {
        var t = item.Tarefa;
        var extra = t.TemSubtarefas ? $" e suas {t.Subtarefas.Count} subtarefas" : "";
        var dlg = new DialogoConfirmacaoViewModel("Excluir tarefa", $"“{t.Titulo}”{extra} serão excluídas. Isso não pode ser desfeito.");
        if (!await AbrirDialogoAsync(dlg)) return;
        if (await Tentar(() => Plano.RemoverTarefaAsync(t)))
        {
            RecarregarTudo();
            Avisar("Tarefa excluída", "");
        }
    }

    public async Task MoverTarefaAsync(TarefaItemViewModel item, int deslocamento)
    {
        if (await Tentar(() => Plano.MoverTarefaAsync(item.Tarefa, deslocamento)))
            RecarregarTudo();
    }

    // ---------- semanas ----------

    [RelayCommand]
    public async Task NovaSemanaAsync()
    {
        // Sugere a semana seguinte à última cadastrada (segunda a domingo), ou a semana atual.
        var ultima = Plano.Semanas.LastOrDefault();
        var inicio = ultima is null ? Datas.SegundaDaSemana(Datas.Hoje) : ultima.Fim.AddDays(1);
        var fim = inicio.AddDays(6 - ((int)inicio.DayOfWeek + 6) % 7);
        if (fim < inicio.AddDays(3)) fim = inicio.AddDays(6);

        var dlg = new DialogoSemanaViewModel("Nova semana", inicio, fim);
        if (!await AbrirDialogoAsync(dlg)) return;

        Periodo? criada = null;
        if (await Tentar(async () => criada = await Plano.CriarSemanaAsync(dlg.Nome, dlg.Objetivo, dlg.Inicio!.Value, dlg.Fim!.Value)))
        {
            AtualizarResumo();
            AbrirSemana(criada!);
            Avisar("Semana criada");
        }
    }

    public async Task EditarSemanaAsync(Periodo semana)
    {
        var dlg = new DialogoSemanaViewModel("Editar semana", semana.Inicio, semana.Fim, semana.Titulo, semana.Objetivo, "Salvar");
        if (!await AbrirDialogoAsync(dlg)) return;
        if (await Tentar(() => Plano.AtualizarSemanaAsync(semana, dlg.Nome, dlg.Objetivo, dlg.Inicio!.Value, dlg.Fim!.Value)))
            RecarregarTudo();
    }

    public async Task RemoverSemanaAsync(Periodo semana)
    {
        var dlg = new DialogoConfirmacaoViewModel("Excluir semana",
            $"“{semana.Titulo}” e todas as suas {semana.Tarefas.Count} tarefas serão excluídas. Isso não pode ser desfeito.");
        if (!await AbrirDialogoAsync(dlg)) return;
        if (await Tentar(() => Plano.RemoverSemanaAsync(semana)))
        {
            Semana.Exibir(Plano.SemanaMaisRelevante(Datas.Hoje));
            RecarregarTudo();
            Avisar("Semana excluída", "");
        }
    }

    // ---------- importação ----------

    [RelayCommand]
    public async Task ImportarPlanilhaAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importar planilha de objetivos",
            Filter = "Planilhas do Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(Application.Current.MainWindow) != true) return;
        await ImportarArquivoAsync(dlg.FileName);
    }

    public async Task ImportarArquivoAsync(string caminho)
    {
        await Tentar(async () =>
        {
            var meses = await Task.Run(() => _importador.Importar(caminho));
            if (meses.Count == 0)
            {
                Avisar("Nenhuma aba com semana encontrada (título como “Semana 1 — 16 a 22/11”).", "", erro: true);
                return;
            }
            var (importadas, ignoradas) = await Plano.ImportarAsync(meses);
            RecarregarTudo();
            Avisar(ignoradas > 0
                ? $"{importadas} semana(s) importada(s), {ignoradas} já existiam"
                : $"{importadas} semana(s) importada(s)", "");
        });
    }

    // ---------- IA (opcional) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IaDisponivel), nameof(IaStatusTexto), nameof(IaStatusDica), nameof(IaVerificando), nameof(IaProvedor))]
    private EstadoIa _estadoIa = EstadoIa.Verificando;

    public bool IaDisponivel => EstadoIa == EstadoIa.Disponivel;
    public bool IaVerificando => EstadoIa == EstadoIa.Verificando;

    /// <summary>Provedor em uso ("Claude CLI", "API"...).</summary>
    public string IaProvedor => _ia.Provedor;

    public string IaStatusTexto => EstadoIa switch
    {
        EstadoIa.Disponivel => $"IA disponível · {_ia.Provedor}",
        EstadoIa.Verificando => "Verificando IA…",
        EstadoIa.SemChave => "IA desligada",
        EstadoIa.ChaveInvalida => "Chave da API recusada",
        EstadoIa.CliAusente => "Claude CLI não encontrado",
        EstadoIa.CliSemLogin => "Claude CLI sem login",
        EstadoIa.SemProvedor => "IA desligada",
        _ => "IA sem internet"
    };

    public string IaStatusDica => EstadoIa switch
    {
        EstadoIa.Disponivel => $"Gere planos descrevendo o que você quer (Ctrl+G). Usando: {_ia.Provedor}.",
        EstadoIa.Verificando => "Checando o provedor de IA e a conexão.",
        EstadoIa.SemChave => "Opcional: salve a sua chave da API em Configurações para gerar planos com IA.",
        EstadoIa.ChaveInvalida => "A API recusou a chave salva. Confira o valor em Configurações.",
        EstadoIa.CliAusente => "Instale o Claude Code CLI e faça login com a sua assinatura (veja Configurações).",
        EstadoIa.CliSemLogin => "O Claude CLI está instalado, mas sem login. Em Configurações, clique em “Fazer login no Claude CLI”.",
        EstadoIa.SemProvedor => "Opcional: instale o Claude CLI (usa a sua assinatura) ou salve uma chave da API em Configurações.",
        _ => "Sem conexão. O app segue funcionando; a IA volta quando a internet voltar."
    };

    private bool _verificandoIa;
    private DispatcherTimer? _timerIa;

    /// <summary>
    /// Checa a IA ao abrir, quando a rede muda e periodicamente (a cada minuto enquanto indisponível,
    /// a cada 10 minutos quando disponível). Nenhuma checagem bloqueia o app.
    /// </summary>
    public void IniciarMonitorIa()
    {
        _ = VerificarIaAsync();

        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(() => _ = VerificarIaAsync());

        _timerIa = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        var minutos = 0;
        _timerIa.Tick += (_, _) =>
        {
            minutos++;
            if (!IaDisponivel || minutos % 10 == 0) _ = VerificarIaAsync();
        };
        _timerIa.Start();
    }

    [RelayCommand]
    public async Task VerificarIaAsync()
    {
        if (_verificandoIa) return;
        _verificandoIa = true;
        try
        {
            if (EstadoIa != EstadoIa.Disponivel) EstadoIa = EstadoIa.Verificando;
            EstadoIa = await _ia.VerificarAsync();
            // O provedor pode mudar com o mesmo estado (ex.: trocou de CLI para API).
            OnPropertyChanged(nameof(IaStatusTexto));
            OnPropertyChanged(nameof(IaStatusDica));
            OnPropertyChanged(nameof(IaProvedor));
            Configuracoes.AtualizarInfoCli();
        }
        finally
        {
            _verificandoIa = false;
        }
    }

    [RelayCommand]
    private async Task GerarComIa()
    {
        if (Dialogo is not null) return;
        if (!IaDisponivel) await VerificarIaAsync();
        if (!IaDisponivel)
        {
            Avisar(IaStatusDica, "", erro: true);
            return;
        }

        var dlg = new DialogoIaViewModel(_gerador, Plano);
        if (!await AbrirDialogoAsync(dlg)) return;

        var semanas = dlg.SemanasAceitas();
        if (await Tentar(async () =>
            {
                var (novas, atualizadas, tarefas) = await Plano.AplicarPropostaAsync(semanas);
                var partes = new List<string>();
                if (novas > 0) partes.Add($"{novas} semana(s) nova(s)");
                if (atualizadas > 0) partes.Add($"{atualizadas} semana(s) atualizada(s)");
                Avisar($"Plano aplicado: {string.Join(", ", partes)} · {tarefas} tarefa(s)", "");
            }))
        {
            AtualizarResumo();
            var primeira = Plano.Semanas.FirstOrDefault(s => s.Inicio == semanas.Min(x => x.Inicio));
            if (primeira is not null) AbrirSemana(primeira);
            else PaginaAtual.Recarregar();
        }
    }

    // ---------- atalhos ----------

    [RelayCommand]
    private void Navegar(string chave) => PaginaSelecionada = chave;

    [RelayCommand]
    private async Task NovaTarefaAtalho()
    {
        if (Dialogo is not null) return;
        var (semana, data) = PaginaAtual == Hoje
            ? (Plano.SemanaQueContem(Hoje.Data), (DateOnly?)Hoje.Data)
            : (Semana.Semana ?? Plano.SemanaMaisRelevante(Datas.Hoje), null);
        if (semana is null)
        {
            Avisar("Crie uma semana primeiro (Ctrl+Shift+N).", "", erro: true);
            return;
        }
        await NovaTarefaAsync(semana, data);
    }

    // ---------- tema ----------

    [RelayCommand]
    private void AlternarTema() => Configuracoes.Tema = GerenciadorTema.Atual == "Escuro" ? "Claro" : "Escuro";
}
