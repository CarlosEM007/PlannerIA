using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;
using PlannerIA.Infrastructure.Ia;

namespace PlannerIA.Tests;

internal sealed class ProvedorFalso(string nome, EstadoIa estado) : IClienteIa
{
    public int Geracoes { get; private set; }
    public string Provedor => nome;
    public Task<EstadoIa> VerificarAsync(CancellationToken ct = default) => Task.FromResult(estado);

    public Task<string> GerarPlanoJsonAsync(string instrucoes, string pedido, CancellationToken ct = default)
    {
        Geracoes++;
        return Task.FromResult("{}");
    }
}

public class ClienteIaRoteadorTests
{
    [Fact]
    public async Task Automatico_prefere_o_cli()
    {
        var cli = new ProvedorFalso("Claude CLI", EstadoIa.Disponivel);
        var api = new ProvedorFalso("API", EstadoIa.Disponivel);
        var r = new ClienteIaRoteador(cli, api, () => ModoIa.Automatico);

        Assert.Equal(EstadoIa.Disponivel, await r.VerificarAsync());
        await r.GerarPlanoJsonAsync("i", "p");

        Assert.Equal("Claude CLI", r.Provedor);
        Assert.Equal((1, 0), (cli.Geracoes, api.Geracoes));
    }

    [Fact]
    public async Task Automatico_cai_para_a_api_quando_o_cli_nao_existe()
    {
        var cli = new ProvedorFalso("Claude CLI", EstadoIa.CliAusente);
        var api = new ProvedorFalso("API", EstadoIa.Disponivel);
        var r = new ClienteIaRoteador(cli, api, () => ModoIa.Automatico);

        Assert.Equal(EstadoIa.Disponivel, await r.VerificarAsync());
        await r.GerarPlanoJsonAsync("i", "p");

        Assert.Equal("API", r.Provedor);
        Assert.Equal((0, 1), (cli.Geracoes, api.Geracoes));
    }

    [Fact]
    public async Task Automatico_sem_nenhum_provedor_avisa_sem_provedor()
    {
        var r = new ClienteIaRoteador(new ProvedorFalso("Claude CLI", EstadoIa.CliAusente),
            new ProvedorFalso("API", EstadoIa.SemChave), () => ModoIa.Automatico);

        Assert.Equal(EstadoIa.SemProvedor, await r.VerificarAsync());
        await Assert.ThrowsAsync<FalhaIaException>(() => r.GerarPlanoJsonAsync("i", "p"));
    }

    [Fact]
    public async Task Cli_sem_login_e_sem_chave_avisa_para_fazer_login()
    {
        var r = new ClienteIaRoteador(new ProvedorFalso("Claude CLI", EstadoIa.CliSemLogin),
            new ProvedorFalso("API", EstadoIa.SemChave), () => ModoIa.Automatico);

        Assert.Equal(EstadoIa.CliSemLogin, await r.VerificarAsync());
    }

    [Fact]
    public async Task Modo_cli_nao_usa_a_api_mesmo_com_chave()
    {
        var api = new ProvedorFalso("API", EstadoIa.Disponivel);
        var r = new ClienteIaRoteador(new ProvedorFalso("Claude CLI", EstadoIa.CliAusente), api, () => ModoIa.ClaudeCli);

        Assert.Equal(EstadoIa.CliAusente, await r.VerificarAsync());
        Assert.Equal(0, api.Geracoes);
    }
}

public class ClienteClaudeCliTests
{
    [Fact]
    public void Argumentos_usam_a_assinatura_sem_ferramentas_e_sem_historico()
    {
        var args = ClienteClaudeCli.MontarArgumentos("instrucoes.txt", "{\"type\":\"object\"}", "opus");

        Assert.DoesNotContain("--bare", args); // --bare ignora o login da assinatura
        Assert.Equal("", args[args.ToList().IndexOf("--tools") + 1]);
        Assert.Equal("json", args[args.ToList().IndexOf("--output-format") + 1]);
        Assert.Contains("--json-schema", args);
        Assert.Contains("--no-session-persistence", args);
        Assert.Equal("instrucoes.txt", args[args.ToList().IndexOf("--system-prompt-file") + 1]);
    }

    [Theory]
    [InlineData("""{ "loggedIn": false, "authMethod": "none" }""", false)]
    [InlineData("""{ "loggedIn": true, "authMethod": "claude.ai" }""", true)]
    [InlineData("comando desconhecido", null)]
    [InlineData("""{ "outra": 1 }""", null)]
    public void Le_o_login_do_auth_status(string saida, bool? esperado) =>
        Assert.Equal(esperado, ClienteClaudeCli.LerLogin(saida));

    [Fact]
    public void Le_o_structured_output()
    {
        const string saida = """{"type":"result","subtype":"success","is_error":false,"result":"","structured_output":{"resumo":"ok","semanas":[]}}""";

        var json = ClienteClaudeCli.InterpretarSaida(0, saida, "");

        Assert.Contains("\"resumo\":\"ok\"", json);
    }

    [Fact]
    public void Sem_login_vira_mensagem_para_fazer_login()
    {
        const string saida = """{"type":"result","is_error":true,"result":"Not logged in · Please run /login"}""";

        var ex = Assert.Throws<FalhaIaException>(() => ClienteClaudeCli.InterpretarSaida(1, saida, ""));

        Assert.Contains("login", ex.Message);
        Assert.False(ex.PodeTentarDeNovo);
    }

    [Fact]
    public void Resposta_sem_plano_vira_erro_amigavel()
    {
        Assert.Throws<FalhaIaException>(() =>
            ClienteClaudeCli.InterpretarSaida(0, """{"is_error":false,"result":"Não posso ajudar com isso."}""", ""));
        Assert.Throws<FalhaIaException>(() => ClienteClaudeCli.InterpretarSaida(1, "", "erro qualquer"));
    }

    /// <summary>
    /// Teste opcional contra o Claude CLI de verdade (consome uso da assinatura).
    /// Só roda com PLANNERIA_TESTE_CLI_REAL=1 e o CLI instalado e logado.
    /// </summary>
    [Fact]
    public async Task Cli_real_gera_um_plano_valido()
    {
        if (Environment.GetEnvironmentVariable("PLANNERIA_TESTE_CLI_REAL") != "1") return;

        var plano = new PlanoService(new RepositorioFalso());
        await plano.InicializarAsync();
        var cli = new ClienteClaudeCli(Path.Combine(Path.GetTempPath(), "PlannerIA.Tests", "cli-real"));
        Assert.Equal(EstadoIa.Disponivel, await cli.VerificarAsync());

        var proposta = await new GeradorDePlano(cli, plano).GerarAsync(
            "Crie só a semana de 23 a 29/11/2026, com 1 tarefa principal \"Inglês\" e 2 subtarefas curtas.",
            new DateOnly(2026, 11, 16));

        var semana = Assert.Single(proposta.Semanas);
        Assert.Equal(new DateOnly(2026, 11, 23), semana.Inicio);
        Assert.NotEmpty(semana.TarefasPrincipais);
    }

    /// <summary>
    /// Roda um "claude" falso (.cmd) para testar o processo de ponta a ponta: versão, pedido pela
    /// entrada padrão, leitura do JSON e que a ANTHROPIC_API_KEY não é repassada ao CLI.
    /// </summary>
    [Fact]
    public async Task Processo_de_ponta_a_ponta_com_cli_falso()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pasta = Path.Combine(Path.GetTempPath(), "PlannerIA.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        var falso = Path.Combine(pasta, "claude.cmd");
        File.WriteAllText(Path.Combine(pasta, "saida.json"),
            """{"type":"result","is_error":false,"structured_output":{"resumo":"do cli falso","semanas":[]}}""");
        File.WriteAllText(falso, """
            @echo off
            if "%~1"=="--version" (
              echo 9.9.9 ^(Claude Code^)
              exit /b 0
            )
            if "%~1"=="auth" (
              echo {"loggedIn": true, "authMethod": "claude.ai"}
              exit /b 0
            )
            more > "%~dp0entrada.txt"
            if defined ANTHROPIC_API_KEY (
              echo {"is_error":true,"result":"a chave da API vazou para o CLI"}
              exit /b 1
            )
            type "%~dp0saida.json"
            """);

        var antesCaminho = Environment.GetEnvironmentVariable(ClienteClaudeCli.VariavelCaminho);
        var antesChave = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable(ClienteClaudeCli.VariavelCaminho, falso);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-nao-deve-chegar");
            var cliente = new ClienteClaudeCli(Path.Combine(pasta, "trabalho"));

            await cliente.VerificarAsync();
            Assert.Equal(falso, cliente.CaminhoEncontrado);
            Assert.StartsWith("9.9.9", cliente.Versao);
            Assert.True(cliente.Logado);

            var json = await cliente.GerarPlanoJsonAsync("regras", "planeje a semana com acentuação");

            Assert.Contains("do cli falso", json);
            Assert.Contains("planeje a semana", File.ReadAllText(Path.Combine(pasta, "entrada.txt")));
            Assert.Equal("regras", File.ReadAllText(Path.Combine(pasta, "trabalho", "instrucoes.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClienteClaudeCli.VariavelCaminho, antesCaminho);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", antesChave);
            try { Directory.Delete(pasta, true); } catch { /* em uso */ }
        }
    }
}
