using PlannerIA.Core.Dominio;

namespace PlannerIA.Tests;

public class DominioTests
{
    private static (Periodo Semana, Tarefa Principal) Montar(int subtarefas)
    {
        var semana = new Periodo { Tipo = TipoPeriodo.Semana, Inicio = new(2026, 11, 16), Fim = new(2026, 11, 22) };
        var principal = new Tarefa { Titulo = "Finalizar o jogo", PeriodoId = semana.Id, Periodo = semana };
        semana.Tarefas.Add(principal);
        for (var i = 0; i < subtarefas; i++)
            semana.Tarefas.Add(principal.AdicionarSubtarefa($"Sub {i}"));
        return (semana, principal);
    }

    [Fact]
    public void Principal_conclui_sozinha_quando_todas_as_subtarefas_terminam()
    {
        var (_, principal) = Montar(3);

        principal.Subtarefas[0].DefinirConclusao(true);
        principal.Subtarefas[1].DefinirConclusao(true);
        Assert.False(principal.Concluida);

        principal.Subtarefas[2].DefinirConclusao(true);
        Assert.True(principal.Concluida);
    }

    [Fact]
    public void Desmarcar_subtarefa_reabre_a_principal()
    {
        var (_, principal) = Montar(2);
        foreach (var s in principal.Subtarefas) s.DefinirConclusao(true);

        principal.Subtarefas[0].DefinirConclusao(false);

        Assert.False(principal.Concluida);
    }

    [Fact]
    public void Principal_com_subtarefas_nao_pode_ser_marcada_a_mao()
    {
        var (_, principal) = Montar(1);
        Assert.Throws<RegraDeDominioException>(() => principal.DefinirConclusao(true));
    }

    [Fact]
    public void Principal_sem_subtarefas_e_marcada_a_mao()
    {
        var (_, principal) = Montar(0);
        principal.DefinirConclusao(true);
        Assert.True(principal.Concluida);
    }

    [Fact]
    public void Subtarefa_nao_pode_ter_subtarefas()
    {
        var (_, principal) = Montar(1);
        Assert.Throws<RegraDeDominioException>(() => principal.Subtarefas[0].AdicionarSubtarefa("neta"));
    }

    [Fact]
    public void Adicionar_subtarefa_a_principal_concluida_reabre_a_principal()
    {
        var (_, principal) = Montar(1);
        principal.Subtarefas[0].DefinirConclusao(true);
        Assert.True(principal.Concluida);

        principal.AdicionarSubtarefa("Mais uma");

        Assert.False(principal.Concluida);
    }

    [Fact]
    public void Progresso_conta_apenas_tarefas_folha()
    {
        var (semana, principal) = Montar(3);
        var avulsa = new Tarefa { Titulo = "Avulsa", PeriodoId = semana.Id, Periodo = semana, Ordem = 1 };
        semana.Tarefas.Add(avulsa);

        principal.Subtarefas[0].DefinirConclusao(true);
        avulsa.DefinirConclusao(true);

        var p = semana.Progresso();
        Assert.Equal(new Progresso(2, 4), p);
        Assert.Equal(50, p.Percentual);
        Assert.Equal("2 de 4", p.ToString());
    }

    [Fact]
    public void Progresso_do_mes_soma_as_semanas()
    {
        var mes = new Periodo { Tipo = TipoPeriodo.Mes, Inicio = new(2026, 11, 1), Fim = new(2026, 11, 30) };
        var (s1, p1) = Montar(2);
        var (s2, _) = Montar(3);
        mes.Filhos.AddRange([s1, s2]);
        p1.Subtarefas[0].DefinirConclusao(true);

        Assert.Equal(new Progresso(1, 5), mes.Progresso());
    }
}
