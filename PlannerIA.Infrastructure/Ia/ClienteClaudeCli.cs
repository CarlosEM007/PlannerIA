using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using PlannerIA.Core.Portas;

namespace PlannerIA.Infrastructure.Ia;

/// <summary>
/// Gera o plano pelo Claude Code CLI (<c>claude -p</c>), usando o login da assinatura do Claude
/// (Pro/Max) em vez de uma chave da API. Não usa <c>--bare</c>, porque esse modo ignora o login
/// da assinatura e exige ANTHROPIC_API_KEY.
/// </summary>
public sealed class ClienteClaudeCli(string pastaTrabalho) : IClienteIa
{
    public const string VariavelCaminho = "PLANNERIA_CLAUDE_CLI";
    public const string VariavelModelo = "PLANNERIA_MODELO_CLI";
    public const string ModeloPadrao = "opus";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public string Provedor => "Claude CLI";

    /// <summary>Caminho do executável encontrado na última verificação (para mostrar na tela).</summary>
    public string? CaminhoEncontrado { get; private set; }
    public string? Versao { get; private set; }

    public string Modelo { get; } =
        Environment.GetEnvironmentVariable(VariavelModelo) is { Length: > 0 } m ? m.Trim() : ModeloPadrao;

    // ---------- localizar o executável ----------

    /// <summary>
    /// Procura o <c>claude</c>: variável PLANNERIA_CLAUDE_CLI, PATH do processo e do usuário,
    /// e as pastas dos instaladores (nativo e npm).
    /// </summary>
    public static string? Localizar()
    {
        if (Environment.GetEnvironmentVariable(VariavelCaminho) is { Length: > 0 } explicito && File.Exists(explicito))
            return explicito;

        var pastas = new List<string>();
        foreach (var path in new[]
                 {
                     Environment.GetEnvironmentVariable("PATH"),
                     OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) : null
                 })
            if (path is not null) pastas.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        pastas.Add(Path.Combine(perfil, ".local", "bin"));
        pastas.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));

        string[] nomes = OperatingSystem.IsWindows() ? ["claude.exe", "claude.cmd"] : ["claude"];
        foreach (var pasta in pastas.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var nome in nomes)
            {
                try
                {
                    var caminho = Path.Combine(pasta.Trim().Trim('"'), nome);
                    if (File.Exists(caminho)) return caminho;
                }
                catch (ArgumentException)
                {
                    // entrada inválida no PATH
                }
            }
        return null;
    }

    /// <summary>
    /// Monta como iniciar o CLI. Uma instalação via npm (claude.cmd) é executada pelo node
    /// diretamente, para evitar o cmd.exe reinterpretar aspas do JSON Schema.
    /// </summary>
    private static ProcessStartInfo IniciarCom(string executavel)
    {
        if (executavel.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var pasta = Path.GetDirectoryName(executavel)!;
            var script = Path.Combine(pasta, "node_modules", "@anthropic-ai", "claude-code", "cli.js");
            var node = Path.Combine(pasta, "node.exe");
            if (File.Exists(script))
            {
                var psiNode = new ProcessStartInfo(File.Exists(node) ? node : "node");
                psiNode.ArgumentList.Add(script);
                return psiNode;
            }
        }
        return new ProcessStartInfo(executavel);
    }

    // ---------- verificação ----------

    public async Task<EstadoIa> VerificarAsync(CancellationToken ct = default)
    {
        var caminho = Localizar();
        CaminhoEncontrado = caminho;
        if (caminho is null)
        {
            Versao = null;
            return EstadoIa.CliAusente;
        }

        // `claude --version` é rápido e não consome uso da assinatura.
        var (codigo, saida, _) = await ExecutarAsync(caminho, ["--version"], entrada: null, TimeSpan.FromSeconds(20), ct, apagarChave: false);
        if (codigo != 0)
        {
            Versao = null;
            return EstadoIa.CliAusente;
        }
        Versao = saida.Trim().Split('\n')[0].Trim();

        // `claude auth status` diz se há login, também sem consumir uso.
        var (_, status, _) = await ExecutarAsync(caminho, ["auth", "status"], entrada: null, TimeSpan.FromSeconds(20), ct, apagarChave: true);
        Logado = LerLogin(status);
        if (Logado == false) return EstadoIa.CliSemLogin;

        return await TemInternetAsync(ct) ? EstadoIa.Disponivel : EstadoIa.SemInternet;
    }

    /// <summary>true/false conforme <c>claude auth status</c>; null se a versão não informar.</summary>
    public bool? Logado { get; private set; }

    public static bool? LerLogin(string saidaAuthStatus)
    {
        try
        {
            var texto = saidaAuthStatus.Trim();
            var inicio = texto.IndexOf('{');
            if (inicio < 0) return null;
            using var doc = JsonDocument.Parse(texto[inicio..]);
            return doc.RootElement.TryGetProperty("loggedIn", out var l) && l.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? l.GetBoolean()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> TemInternetAsync(CancellationToken ct)
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) return false;
        try
        {
            // Qualquer resposta HTTP serve: só confirma que a API é alcançável. Nada é enviado.
            using var resp = await Http.GetAsync("https://api.anthropic.com/", HttpCompletionOption.ResponseHeadersRead, ct);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ---------- geração ----------

    public static IReadOnlyList<string> MontarArgumentos(string arquivoInstrucoes, string esquemaJson, string modelo) =>
    [
        "-p",
        "--output-format", "json",
        "--json-schema", esquemaJson,
        "--system-prompt-file", arquivoInstrucoes,
        "--model", modelo,
        "--tools", "",                 // sem ferramentas: não lê nem altera arquivos, só responde
        "--strict-mcp-config",         // não conecta servidores MCP configurados
        "--permission-mode", "dontAsk",
        "--no-session-persistence",    // não guarda a conversa no histórico do Claude Code
        "--max-turns", "5",
    ];

    public async Task<string> GerarPlanoJsonAsync(string instrucoes, string pedido, CancellationToken ct = default)
    {
        var caminho = Localizar() ?? throw new FalhaIaException(
            "Claude CLI não encontrado. Instale o Claude Code e faça login (veja Configurações).", false);

        // Pasta própria e vazia: sem --bare, o CLI carregaria CLAUDE.md, hooks e .mcp.json da pasta atual.
        Directory.CreateDirectory(pastaTrabalho);
        var arquivo = Path.Combine(pastaTrabalho, "instrucoes.txt");
        await File.WriteAllTextAsync(arquivo, instrucoes, new UTF8Encoding(false), ct);

        var esquema = JsonSerializer.Serialize(EsquemaPlano.Criar());
        var (codigo, saida, erro) = await ExecutarAsync(caminho, MontarArgumentos(arquivo, esquema, Modelo), pedido,
            TimeSpan.FromMinutes(5), ct, apagarChave: true);

        return InterpretarSaida(codigo, saida, erro);
    }

    /// <summary>Lê o JSON de <c>--output-format json</c> e devolve o <c>structured_output</c>.</summary>
    public static string InterpretarSaida(int codigo, string saida, string erro)
    {
        JsonDocument? doc = null;
        try
        {
            var texto = saida.Trim();
            var inicio = texto.IndexOf('{');
            if (inicio >= 0) doc = JsonDocument.Parse(texto[inicio..]);
        }
        catch (JsonException)
        {
            doc = null;
        }

        using (doc)
        {
            if (doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var raiz = doc.RootElement;
                if (raiz.TryGetProperty("structured_output", out var plano) && plano.ValueKind == JsonValueKind.Object)
                    return plano.GetRawText();

                var resultado = raiz.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                throw Traduzir(resultado.Length > 0 ? resultado : erro);
            }
        }

        throw Traduzir(string.IsNullOrWhiteSpace(erro) ? saida : erro, codigo);
    }

    private static FalhaIaException Traduzir(string mensagem, int? codigo = null)
    {
        var m = mensagem.Trim();
        var minusculo = m.ToLowerInvariant();
        if (minusculo.Contains("login") || minusculo.Contains("not logged") || minusculo.Contains("authenticat")
            || minusculo.Contains("oauth") || minusculo.Contains("credential"))
            return new FalhaIaException(
                "O Claude CLI não está logado. Abra um terminal, rode “claude” e faça login com a sua conta.", false);
        if (minusculo.Contains("usage limit") || minusculo.Contains("rate limit") || minusculo.Contains("limit reached"))
            return new FalhaIaException("O limite de uso da sua assinatura foi atingido. Tente mais tarde.", true);
        if (minusculo.Contains("max turns") || minusculo.Contains("max_turns"))
            return new FalhaIaException("O Claude não terminou o plano a tempo. Tente um pedido mais curto.", true);

        if (m.Length > 300) m = m[..300] + "…";
        return new FalhaIaException(
            m.Length > 0 ? $"O Claude CLI não devolveu um plano: {m}" : $"O Claude CLI falhou (código {codigo}).", true);
    }

    // ---------- processo ----------

    private async Task<(int Codigo, string Saida, string Erro)> ExecutarAsync(
        string executavel, IReadOnlyList<string> argumentos, string? entrada, TimeSpan limite, CancellationToken ct, bool apagarChave)
    {
        var psi = IniciarCom(executavel);
        foreach (var a in argumentos) psi.ArgumentList.Add(a);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.StandardInputEncoding = new UTF8Encoding(false);
        Directory.CreateDirectory(pastaTrabalho);
        psi.WorkingDirectory = pastaTrabalho;

        if (apagarChave)
        {
            // Com ANTHROPIC_API_KEY no ambiente, o CLI usaria a chave (cobrança da API) em vez da assinatura.
            psi.Environment.Remove("ANTHROPIC_API_KEY");
            psi.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
        }

        Process? processo;
        try
        {
            processo = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new FalhaIaException("Não foi possível iniciar o Claude CLI.", true, ex);
        }
        if (processo is null) throw new FalhaIaException("Não foi possível iniciar o Claude CLI.", true);

        using (processo)
        {
            using var tempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tempo.CancelAfter(limite);
            try
            {
                var lerSaida = processo.StandardOutput.ReadToEndAsync(tempo.Token);
                var lerErro = processo.StandardError.ReadToEndAsync(tempo.Token);

                if (entrada is not null) await processo.StandardInput.WriteAsync(entrada.AsMemory(), tempo.Token);
                processo.StandardInput.Close();

                await processo.WaitForExitAsync(tempo.Token);
                return (processo.ExitCode, await lerSaida, await lerErro);
            }
            catch (OperationCanceledException)
            {
                try { processo.Kill(entireProcessTree: true); } catch { /* já terminou */ }
                if (ct.IsCancellationRequested) throw;
                throw new FalhaIaException("O Claude CLI demorou demais para responder.", true);
            }
        }
    }
}
