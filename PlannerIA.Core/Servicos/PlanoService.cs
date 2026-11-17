using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;

namespace PlannerIA.Core.Servicos;

/// <summary>
/// Regras de aplicação sobre o plano (meses, semanas e tarefas). Mantém a árvore em memória
/// e grava cada alteração no repositório.
/// </summary>
public class PlanoService(IPlanoRepository repositorio)
{
    private readonly List<Periodo> _meses = [];

    /// <summary>Disparado depois de qualquer alteração salva.</summary>
    public event EventHandler? Alterado;

    public IReadOnlyList<Periodo> Meses => _meses.OrderBy(m => m.Inicio).ToList();

    public IEnumerable<Periodo> Semanas =>
        _meses.SelectMany(m => m.Filhos).Where(p => p.Tipo == TipoPeriodo.Semana).OrderBy(s => s.Inicio);

    public bool Vazio => _meses.Count == 0;

    public async Task InicializarAsync(CancellationToken ct = default)
    {
        _meses.Clear();
        _meses.AddRange(await repositorio.CarregarMesesAsync(ct));
        foreach (var mes in _meses)
            foreach (var semana in mes.Filhos)
                foreach (var t in semana.TarefasPrincipais)
                    t.RecalcularConclusao();
    }

    // ---------- consultas ----------

    public Periodo? SemanaQueContem(DateOnly data) => Semanas.FirstOrDefault(s => s.Contem(data));

    public Periodo? Mes(int ano, int mes) =>
        _meses.FirstOrDefault(m => m.Inicio.Year == ano && m.Inicio.Month == mes);

    /// <summary>A semana atual, ou a próxima, ou a última que existir.</summary>
    public Periodo? SemanaMaisRelevante(DateOnly hoje) =>
        SemanaQueContem(hoje)
        ?? Semanas.FirstOrDefault(s => s.Inicio > hoje)
        ?? Semanas.LastOrDefault();

    /// <summary>Tarefas folha e principais com data no dia informado.</summary>
    public IEnumerable<Tarefa> TarefasDoDia(DateOnly data) =>
        Semanas.SelectMany(s => s.TarefasPrincipais)
            .SelectMany(t => t.TemSubtarefas ? t.SubtarefasOrdenadas : [t])
            .Where(t => t.Data == data);

    /// <summary>Tarefas folha ainda abertas com data anterior a hoje.</summary>
    public IEnumerable<Tarefa> Atrasadas(DateOnly hoje) =>
        Semanas.SelectMany(s => s.TarefasPrincipais)
            .SelectMany(t => t.TemSubtarefas ? t.SubtarefasOrdenadas : [t])
            .Where(t => !t.Concluida && t.Data is { } d && d < hoje)
            .OrderBy(t => t.Data);

    // ---------- períodos ----------

    public Periodo ObterOuCriarMes(int ano, int mes)
    {
        var existente = Mes(ano, mes);
        if (existente is not null) return existente;

        var inicio = new DateOnly(ano, mes, 1);
        var novo = new Periodo
        {
            Tipo = TipoPeriodo.Mes,
            Titulo = Datas.NomeDoMes(ano, mes),
            Inicio = inicio,
            Fim = inicio.AddMonths(1).AddDays(-1)
        };
        _meses.Add(novo);
        repositorio.Adicionar(novo);
        return novo;
    }

    public async Task<Periodo> CriarSemanaAsync(string? titulo, string? objetivo, DateOnly inicio, DateOnly fim)
    {
        ValidarIntervalo(inicio, fim);
        if (Semanas.Any(s => s.Inicio == inicio && s.Fim == fim))
            throw new RegraDeDominioException("Já existe uma semana com esse mesmo intervalo de datas.");

        var mes = ObterOuCriarMes(inicio.Year, inicio.Month);
        var semana = new Periodo
        {
            Tipo = TipoPeriodo.Semana,
            Titulo = string.IsNullOrWhiteSpace(titulo) ? TituloPadraoSemana(mes, inicio, fim) : titulo.Trim(),
            Objetivo = Limpar(objetivo),
            Inicio = inicio,
            Fim = fim,
            PeriodoPaiId = mes.Id,
            PeriodoPai = mes
        };
        mes.Filhos.Add(semana);
        repositorio.Adicionar(semana);
        await SalvarAsync();
        return semana;
    }

    public async Task AtualizarSemanaAsync(Periodo semana, string titulo, string? objetivo, DateOnly inicio, DateOnly fim)
    {
        ValidarIntervalo(inicio, fim);
        if (string.IsNullOrWhiteSpace(titulo))
            throw new RegraDeDominioException("O título da semana não pode ser vazio.");

        var foraDoIntervalo = semana.Tarefas.Where(t => t.Data is { } d && (d < inicio || d > fim)).ToList();
        if (foraDoIntervalo.Count > 0)
            throw new RegraDeDominioException(
                $"{foraDoIntervalo.Count} tarefa(s) ficariam com data fora do novo intervalo. Ajuste as datas antes.");

        semana.Titulo = titulo.Trim();
        semana.Objetivo = Limpar(objetivo);
        semana.Inicio = inicio;
        semana.Fim = fim;

        // A semana pertence ao mês em que começa.
        var mesCorreto = ObterOuCriarMes(inicio.Year, inicio.Month);
        if (semana.PeriodoPai != mesCorreto)
        {
            semana.PeriodoPai?.Filhos.Remove(semana);
            mesCorreto.Filhos.Add(semana);
            semana.PeriodoPai = mesCorreto;
            semana.PeriodoPaiId = mesCorreto.Id;
        }

        RemoverMesesVazios();
        await SalvarAsync();
    }

    public async Task RemoverSemanaAsync(Periodo semana)
    {
        semana.PeriodoPai?.Filhos.Remove(semana);
        repositorio.Remover(semana);
        RemoverMesesVazios();
        await SalvarAsync();
    }

    public string TituloPadraoSemana(Periodo mes, DateOnly inicio, DateOnly fim)
    {
        var numero = mes.Filhos.Count(s => s.Inicio < inicio) + 1;
        return $"Semana {numero} — {Datas.Intervalo(inicio, fim)}";
    }

    // ---------- tarefas ----------

    public async Task<Tarefa> CriarTarefaAsync(Periodo semana, string titulo, DateOnly? data, string? notas)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            throw new RegraDeDominioException("O título da tarefa não pode ser vazio.");
        ValidarData(semana, data);

        var tarefa = new Tarefa
        {
            Titulo = titulo.Trim(),
            Data = data,
            Notas = Limpar(notas),
            Ordem = semana.Tarefas.Where(t => !t.EhSubtarefa).Select(t => t.Ordem).DefaultIfEmpty(-1).Max() + 1,
            PeriodoId = semana.Id,
            Periodo = semana
        };
        semana.Tarefas.Add(tarefa);
        repositorio.Adicionar(tarefa);
        await SalvarAsync();
        return tarefa;
    }

    public async Task<Tarefa> CriarSubtarefaAsync(Tarefa principal, string titulo, DateOnly? data, string? notas)
    {
        var semana = principal.Periodo ?? throw new InvalidOperationException("Tarefa sem período.");
        ValidarData(semana, data);

        var sub = principal.AdicionarSubtarefa(titulo, data, Limpar(notas));
        semana.Tarefas.Add(sub);
        repositorio.Adicionar(sub);
        await SalvarAsync();
        return sub;
    }

    public async Task AtualizarTarefaAsync(Tarefa tarefa, string titulo, DateOnly? data, string? notas)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            throw new RegraDeDominioException("O título da tarefa não pode ser vazio.");
        if (tarefa.Periodo is { } semana) ValidarData(semana, data);

        tarefa.Titulo = titulo.Trim();
        tarefa.Data = data;
        tarefa.Notas = Limpar(notas);
        await SalvarAsync();
    }

    public async Task DefinirConclusaoAsync(Tarefa tarefa, bool concluida)
    {
        tarefa.DefinirConclusao(concluida);
        await SalvarAsync();
    }

    public async Task RemoverTarefaAsync(Tarefa tarefa)
    {
        foreach (var sub in tarefa.Subtarefas.ToList())
        {
            tarefa.Periodo?.Tarefas.Remove(sub);
            repositorio.Remover(sub);
        }
        tarefa.Periodo?.Tarefas.Remove(tarefa);
        tarefa.TarefaPai?.RemoverSubtarefa(tarefa);
        repositorio.Remover(tarefa);
        await SalvarAsync();
    }

    public async Task MoverTarefaAsync(Tarefa tarefa, int deslocamento)
    {
        var irmas = (tarefa.TarefaPai is { } pai
            ? pai.SubtarefasOrdenadas
            : tarefa.Periodo!.TarefasPrincipais).ToList();
        var i = irmas.IndexOf(tarefa);
        var j = i + deslocamento;
        if (i < 0 || j < 0 || j >= irmas.Count) return;

        (irmas[i], irmas[j]) = (irmas[j], irmas[i]);
        for (var k = 0; k < irmas.Count; k++) irmas[k].Ordem = k;
        await SalvarAsync();
    }

    // ---------- importação ----------

    /// <summary>
    /// Junta meses importados ao plano. Semanas com o mesmo intervalo de datas de uma já existente são ignoradas.
    /// </summary>
    public async Task<(int Importadas, int Ignoradas)> ImportarAsync(IEnumerable<Periodo> mesesImportados)
    {
        int importadas = 0, ignoradas = 0;
        foreach (var semana in mesesImportados.SelectMany(m => m.Filhos).OrderBy(s => s.Inicio))
        {
            if (Semanas.Any(s => s.Inicio == semana.Inicio && s.Fim == semana.Fim))
            {
                ignoradas++;
                continue;
            }

            var mes = ObterOuCriarMes(semana.Inicio.Year, semana.Inicio.Month);
            semana.PeriodoPai = mes;
            semana.PeriodoPaiId = mes.Id;
            mes.Filhos.Add(semana);
            repositorio.Adicionar(semana);
            foreach (var t in semana.Tarefas)
            {
                t.PeriodoId = semana.Id;
                t.Periodo = semana;
                repositorio.Adicionar(t);
            }
            foreach (var t in semana.TarefasPrincipais) t.RecalcularConclusao();
            importadas++;
        }

        await SalvarAsync();
        return (importadas, ignoradas);
    }

    /// <summary>
    /// Aplica semanas propostas (pela IA). Semana com as mesmas datas de uma existente recebe as tarefas
    /// novas; tarefa principal com o mesmo título de uma existente recebe as subtarefas.
    /// </summary>
    public async Task<(int SemanasNovas, int SemanasAtualizadas, int Tarefas)> AplicarPropostaAsync(IEnumerable<Periodo> semanasPropostas)
    {
        int novas = 0, atualizadas = 0, tarefas = 0;
        foreach (var proposta in semanasPropostas.OrderBy(s => s.Inicio))
        {
            var existente = Semanas.FirstOrDefault(s => s.Inicio == proposta.Inicio && s.Fim == proposta.Fim);
            if (existente is null)
            {
                var mes = ObterOuCriarMes(proposta.Inicio.Year, proposta.Inicio.Month);
                proposta.PeriodoPai = mes;
                proposta.PeriodoPaiId = mes.Id;
                mes.Filhos.Add(proposta);
                repositorio.Adicionar(proposta);
                foreach (var t in proposta.Tarefas)
                {
                    t.PeriodoId = proposta.Id;
                    t.Periodo = proposta;
                    repositorio.Adicionar(t);
                }
                tarefas += proposta.Tarefas.Count;
                novas++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(existente.Objetivo) && !string.IsNullOrWhiteSpace(proposta.Objetivo))
                existente.Objetivo = proposta.Objetivo;

            var proximaOrdem = existente.TarefasPrincipais.Select(t => t.Ordem).DefaultIfEmpty(-1).Max() + 1;
            foreach (var principal in proposta.TarefasPrincipais.ToList())
            {
                var mesma = existente.TarefasPrincipais.FirstOrDefault(t =>
                    string.Equals(t.Titulo.Trim(), principal.Titulo.Trim(), StringComparison.OrdinalIgnoreCase));

                if (mesma is null)
                {
                    var nova = new Tarefa
                    {
                        Titulo = principal.Titulo, Data = principal.Data, Notas = principal.Notas,
                        Ordem = proximaOrdem++, PeriodoId = existente.Id, Periodo = existente
                    };
                    existente.Tarefas.Add(nova);
                    repositorio.Adicionar(nova);
                    tarefas++;
                    mesma = nova;
                }

                foreach (var sub in principal.SubtarefasOrdenadas)
                {
                    var s = mesma.AdicionarSubtarefa(sub.Titulo, sub.Data, sub.Notas);
                    existente.Tarefas.Add(s);
                    repositorio.Adicionar(s);
                    tarefas++;
                }
                mesma.RecalcularConclusao();
            }
            atualizadas++;
        }

        await SalvarAsync();
        return (novas, atualizadas, tarefas);
    }

    // ---------- auxiliares ----------

    private async Task SalvarAsync()
    {
        await repositorio.SalvarAsync();
        Alterado?.Invoke(this, EventArgs.Empty);
    }

    private void RemoverMesesVazios()
    {
        foreach (var mes in _meses.Where(m => m.Filhos.Count == 0 && m.Tarefas.Count == 0).ToList())
        {
            _meses.Remove(mes);
            repositorio.Remover(mes);
        }
    }

    private static void ValidarIntervalo(DateOnly inicio, DateOnly fim)
    {
        if (fim < inicio)
            throw new RegraDeDominioException("A data final precisa ser igual ou posterior à data inicial.");
        if (fim.DayNumber - inicio.DayNumber > 30)
            throw new RegraDeDominioException("Um período pode ter no máximo 31 dias.");
    }

    private static void ValidarData(Periodo semana, DateOnly? data)
    {
        if (data is { } d && !semana.Contem(d))
            throw new RegraDeDominioException(
                $"A data {d:dd/MM} está fora da semana ({Datas.Intervalo(semana.Inicio, semana.Fim)}).");
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
