using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;

namespace PlannerIA.Tests;

/// <summary>Repositório em memória para testar o serviço sem banco.</summary>
internal sealed class RepositorioFalso : IPlanoRepository
{
    public List<Periodo> Meses { get; } = [];
    public int Salvamentos { get; private set; }

    public Task<List<Periodo>> CarregarMesesAsync(CancellationToken ct = default) => Task.FromResult(Meses.ToList());
    public void Adicionar(Periodo periodo) { if (periodo.Tipo == TipoPeriodo.Mes) Meses.Add(periodo); }
    public void Adicionar(Tarefa tarefa) { }
    public void Remover(Periodo periodo) => Meses.Remove(periodo);
    public void Remover(Tarefa tarefa) { }
    public Task SalvarAsync(CancellationToken ct = default) { Salvamentos++; return Task.CompletedTask; }
}

public class PlanoServiceTests
{
    private static async Task<PlanoService> Novo()
    {
        var s = new PlanoService(new RepositorioFalso());
        await s.InicializarAsync();
        return s;
    }

    [Fact]
    public async Task Semana_que_atravessa_dois_meses_fica_no_mes_em_que_comeca()
    {
        var s = await Novo();

        var semana = await s.CriarSemanaAsync(null, "Portfólio", new(2026, 11, 30), new(2026, 12, 6));

        Assert.Equal(11, semana.PeriodoPai!.Inicio.Month);
        Assert.Equal("Novembro 2026", semana.PeriodoPai.Titulo);
        Assert.Null(s.Mes(2026, 12));
    }

    [Fact]
    public async Task Titulo_padrao_numera_as_semanas_do_mes()
    {
        var s = await Novo();
        await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));
        var segunda = await s.CriarSemanaAsync(null, null, new(2026, 11, 23), new(2026, 11, 29));

        Assert.Equal("Semana 2 — 23 a 29/11", segunda.Titulo);
    }

    [Fact]
    public async Task Nao_permite_semana_duplicada()
    {
        var s = await Novo();
        await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));

        await Assert.ThrowsAsync<RegraDeDominioException>(() =>
            s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22)));
    }

    [Fact]
    public async Task Data_da_tarefa_precisa_estar_dentro_da_semana()
    {
        var s = await Novo();
        var semana = await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));

        await Assert.ThrowsAsync<RegraDeDominioException>(() =>
            s.CriarTarefaAsync(semana, "Fora", new DateOnly(2026, 11, 23), null));
    }

    [Fact]
    public async Task Tarefas_do_dia_traz_subtarefas_com_aquela_data()
    {
        var s = await Novo();
        var semana = await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));
        var principal = await s.CriarTarefaAsync(semana, "Finalizar o jogo", null, null);
        await s.CriarSubtarefaAsync(principal, "Corrigir bugs", new DateOnly(2026, 11, 16), null);
        await s.CriarSubtarefaAsync(principal, "Polir", new DateOnly(2026, 11, 17), null);

        var dia = s.TarefasDoDia(new DateOnly(2026, 11, 16)).ToList();

        Assert.Single(dia);
        Assert.Equal("Corrigir bugs", dia[0].Titulo);
    }

    [Fact]
    public async Task Marcar_subtarefa_salva_e_propaga()
    {
        var repo = new RepositorioFalso();
        var s = new PlanoService(repo);
        await s.InicializarAsync();
        var semana = await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));
        var principal = await s.CriarTarefaAsync(semana, "Inglês", null, null);
        var sub = await s.CriarSubtarefaAsync(principal, "Listening", null, null);
        var antes = repo.Salvamentos;

        await s.DefinirConclusaoAsync(sub, true);

        Assert.True(principal.Concluida);
        Assert.Equal(antes + 1, repo.Salvamentos);
    }

    [Fact]
    public async Task Remover_ultima_semana_remove_o_mes_vazio()
    {
        var s = await Novo();
        var semana = await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));

        await s.RemoverSemanaAsync(semana);

        Assert.True(s.Vazio);
    }

    [Fact]
    public async Task Atualizar_semana_nao_deixa_tarefas_fora_do_intervalo()
    {
        var s = await Novo();
        var semana = await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));
        await s.CriarTarefaAsync(semana, "Sexta", new DateOnly(2026, 11, 20), null);

        await Assert.ThrowsAsync<RegraDeDominioException>(() =>
            s.AtualizarSemanaAsync(semana, "Curta", null, new(2026, 11, 16), new(2026, 11, 18)));
    }

    [Fact]
    public async Task Mover_tarefa_troca_a_ordem()
    {
        var s = await Novo();
        var semana = await s.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));
        var a = await s.CriarTarefaAsync(semana, "A", null, null);
        var b = await s.CriarTarefaAsync(semana, "B", null, null);

        await s.MoverTarefaAsync(b, -1);

        Assert.Equal(["B", "A"], semana.TarefasPrincipais.Select(t => t.Titulo));
    }
}
