using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;

namespace PlannerIA.Tests;

/// <summary>Cliente de IA falso: devolve um JSON fixo e guarda o pedido. Nenhum teste chama a API real.</summary>
internal sealed class ClienteIaFalso(string resposta, EstadoIa estado = EstadoIa.Disponivel) : IClienteIa
{
    public string Provedor => "falso";
    public string? UltimoPedido { get; private set; }
    public string? UltimasInstrucoes { get; private set; }

    public Task<EstadoIa> VerificarAsync(CancellationToken ct = default) => Task.FromResult(estado);

    public Task<string> GerarPlanoJsonAsync(string instrucoes, string pedido, CancellationToken ct = default)
    {
        UltimasInstrucoes = instrucoes;
        UltimoPedido = pedido;
        return Task.FromResult(resposta);
    }
}

public class LeitorPlanoIaTests
{
    private const string Valido = """
        {
          "resumo": "Duas frentes na semana.",
          "semanas": [
            {
              "titulo": "Semana 1 — 16 a 22/11", "objetivo": "Terminar o jogo",
              "inicio": "2026-11-16", "fim": "2026-11-22",
              "tarefas": [
                { "titulo": "Finalizar o jogo", "data": null, "notas": null,
                  "subtarefas": [
                    { "titulo": "Corrigir bugs", "data": "2026-11-17", "notas": null },
                    { "titulo": "Publicar", "data": "2026-11-19", "notas": "itch.io" }
                  ] },
                { "titulo": "Inglês", "data": null, "notas": "ter a qui", "subtarefas": [] }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Le_semanas_tarefas_e_subtarefas()
    {
        var p = LeitorPlanoIa.Ler(Valido);

        Assert.Equal("Duas frentes na semana.", p.Resumo);
        var s = Assert.Single(p.Semanas);
        Assert.Equal(new DateOnly(2026, 11, 16), s.Inicio);
        Assert.Equal("Terminar o jogo", s.Objetivo);
        var jogo = s.TarefasPrincipais.First();
        Assert.Equal(2, jogo.Subtarefas.Count);
        Assert.Equal(new DateOnly(2026, 11, 17), jogo.Subtarefas[0].Data);
        Assert.Equal("itch.io", jogo.Subtarefas[1].Notas);
        Assert.Equal("ter a qui", s.TarefasPrincipais.Last().Notas);
        Assert.Empty(p.Avisos);
    }

    [Fact]
    public void Data_fora_da_semana_e_removida_com_aviso()
    {
        var json = Valido.Replace("2026-11-19", "2026-11-25");
        var p = LeitorPlanoIa.Ler(json);

        Assert.Null(p.Semanas[0].TarefasPrincipais.First().Subtarefas[1].Data);
        Assert.Contains(p.Avisos, a => a.Contains("fora da semana"));
    }

    [Fact]
    public void Tarefa_sem_titulo_e_ignorada()
    {
        var json = Valido.Replace("\"titulo\": \"Inglês\"", "\"titulo\": \"  \"");
        var p = LeitorPlanoIa.Ler(json);

        Assert.Single(p.Semanas[0].TarefasPrincipais);
        Assert.Contains(p.Avisos, a => a.Contains("sem título"));
    }

    [Fact]
    public void Subtarefa_aninhada_demais_e_ignorada()
    {
        var json = Valido.Replace(
            "{ \"titulo\": \"Corrigir bugs\", \"data\": \"2026-11-17\", \"notas\": null }",
            "{ \"titulo\": \"Corrigir bugs\", \"data\": \"2026-11-17\", \"notas\": null, \"subtarefas\": [ { \"titulo\": \"neta\" } ] }");
        var p = LeitorPlanoIa.Ler(json);

        var sub = p.Semanas[0].TarefasPrincipais.First().Subtarefas[0];
        Assert.Empty(sub.Subtarefas);
        Assert.DoesNotContain(p.Semanas[0].Tarefas, t => t.Titulo == "neta");
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{ \"resumo\": \"x\" }")]
    [InlineData("{ \"semanas\": [ { \"titulo\": \"x\", \"inicio\": \"2026-11-22\", \"fim\": \"2026-11-16\", \"tarefas\": [] } ] }")]
    [InlineData("{ \"semanas\": [ { \"titulo\": \"x\", \"inicio\": \"16/11\", \"fim\": \"22/11\", \"tarefas\": [] } ] }")]
    public void Resposta_invalida_vira_erro_amigavel(string json) =>
        Assert.Throws<RegraDeDominioException>(() => LeitorPlanoIa.Ler(json));
}

public class GeradorDePlanoTests
{
    private static async Task<PlanoService> PlanoComUmaSemana()
    {
        var plano = new PlanoService(new RepositorioFalso());
        await plano.InicializarAsync();
        var s = await plano.CriarSemanaAsync(null, "Jogo", new(2026, 11, 16), new(2026, 11, 22));
        var t = await plano.CriarTarefaAsync(s, "Finalizar o jogo", null, null);
        await plano.CriarSubtarefaAsync(t, "Corrigir bugs", new DateOnly(2026, 11, 16), null);
        return plano;
    }

    [Fact]
    public async Task Pedido_leva_data_de_hoje_semanas_existentes_e_o_texto_do_usuario()
    {
        var plano = await PlanoComUmaSemana();
        var cliente = new ClienteIaFalso("""{ "resumo": "", "semanas": [ { "titulo": "S", "objetivo": "", "inicio": "2026-11-23", "fim": "2026-11-29", "tarefas": [] } ] }""");

        await new GeradorDePlano(cliente, plano).GerarAsync("ler .NET 3x por semana", new DateOnly(2026, 11, 16));

        Assert.Contains("2026-11-16", cliente.UltimoPedido);
        Assert.Contains("America/Sao_Paulo", cliente.UltimoPedido);
        Assert.Contains("2026-11-16 a 2026-11-22", cliente.UltimoPedido);
        Assert.Contains("Corrigir bugs", cliente.UltimoPedido);
        Assert.Contains("ler .NET 3x por semana", cliente.UltimoPedido);
        Assert.Equal(GeradorDePlano.Instrucoes, cliente.UltimasInstrucoes);
    }

    [Fact]
    public async Task Gerar_nao_salva_nada()
    {
        var plano = await PlanoComUmaSemana();
        var cliente = new ClienteIaFalso("""{ "resumo": "", "semanas": [ { "titulo": "S2", "objetivo": "", "inicio": "2026-11-23", "fim": "2026-11-29", "tarefas": [ { "titulo": "A", "data": null, "notas": null, "subtarefas": [] } ] } ] }""");

        var proposta = await new GeradorDePlano(cliente, plano).GerarAsync("algo", new DateOnly(2026, 11, 16));

        Assert.Single(proposta.Semanas);
        Assert.Single(plano.Semanas); // só a semana original
    }

    [Fact]
    public async Task Avisa_quando_a_semana_proposta_se_sobrepoe_a_uma_existente()
    {
        var plano = await PlanoComUmaSemana();
        var cliente = new ClienteIaFalso("""{ "resumo": "", "semanas": [ { "titulo": "S", "objetivo": "", "inicio": "2026-11-20", "fim": "2026-11-26", "tarefas": [] } ] }""");

        var proposta = await new GeradorDePlano(cliente, plano).GerarAsync("algo", new DateOnly(2026, 11, 16));

        Assert.Contains(proposta.Avisos, a => a.Contains("sobrepõe"));
    }

    [Fact]
    public async Task Pedido_vazio_nao_chama_a_api()
    {
        var plano = await PlanoComUmaSemana();
        var cliente = new ClienteIaFalso("{}");

        await Assert.ThrowsAsync<RegraDeDominioException>(() =>
            new GeradorDePlano(cliente, plano).GerarAsync("   ", new DateOnly(2026, 11, 16)));
        Assert.Null(cliente.UltimoPedido);
    }

    [Fact]
    public async Task Aplicar_cria_semanas_novas_e_acrescenta_nas_existentes()
    {
        var plano = await PlanoComUmaSemana();
        var cliente = new ClienteIaFalso("""
            { "resumo": "", "semanas": [
              { "titulo": "Semana 1", "objetivo": "", "inicio": "2026-11-16", "fim": "2026-11-22", "tarefas": [
                  { "titulo": "finalizar o jogo", "data": null, "notas": null, "subtarefas": [ { "titulo": "Publicar", "data": "2026-11-19", "notas": null } ] },
                  { "titulo": "Inglês", "data": null, "notas": null, "subtarefas": [] } ] },
              { "titulo": "Semana 2 — 23 a 29/11", "objetivo": "Jam", "inicio": "2026-11-23", "fim": "2026-11-29", "tarefas": [
                  { "titulo": "Game jam", "data": null, "notas": null, "subtarefas": [ { "titulo": "Protótipo", "data": "2026-11-24", "notas": null } ] } ] }
            ] }
            """);
        var proposta = await new GeradorDePlano(cliente, plano).GerarAsync("algo", new DateOnly(2026, 11, 16));

        var (novas, atualizadas, tarefas) = await plano.AplicarPropostaAsync(proposta.Semanas);

        Assert.Equal((1, 1), (novas, atualizadas));
        Assert.Equal(4, tarefas); // Publicar + Inglês (semana 1), Game jam + Protótipo (semana 2)
        var s1 = plano.Semanas.First();
        var jogo = s1.TarefasPrincipais.First();
        Assert.Equal(["Corrigir bugs", "Publicar"], jogo.SubtarefasOrdenadas.Select(t => t.Titulo));
        Assert.Equal(["Finalizar o jogo", "Inglês"], s1.TarefasPrincipais.Select(t => t.Titulo));
        Assert.Equal("Jam", plano.Semanas.Last().Objetivo);
        Assert.Equal(new Progresso(0, 3), s1.Progresso()); // Corrigir bugs, Publicar e Inglês
    }
}
