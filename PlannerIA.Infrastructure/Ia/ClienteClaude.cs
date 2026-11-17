using System.Net.NetworkInformation;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using PlannerIA.Core.Portas;

namespace PlannerIA.Infrastructure.Ia;

/// <summary>
/// Cliente da API do Claude (SDK oficial da Anthropic). Só fica ativo quando a variável de ambiente
/// ANTHROPIC_API_KEY existe e há internet. Sem isso, o app segue funcionando sem a IA.
/// </summary>
public sealed class ClienteClaude : IClienteIa, IDisposable
{
    public const string VariavelChave = "ANTHROPIC_API_KEY";
    public const string VariavelModelo = "PLANNERIA_MODELO";
    public const string ModeloPadrao = "claude-opus-5-5";

    private AnthropicClient? _cliente;
    private string? _chaveDoCliente;

    public string Provedor => "API";

    public string Modelo { get; } = LerVariavel(VariavelModelo) ?? ModeloPadrao;

    /// <summary>
    /// Lê a variável do processo e, se não houver, a do usuário no Windows
    /// (assim uma chave criada depois de logar no Windows também é encontrada).
    /// </summary>
    private static string? LerVariavel(string nome)
    {
        var valor = Environment.GetEnvironmentVariable(nome);
        if (string.IsNullOrWhiteSpace(valor) && OperatingSystem.IsWindows())
            valor = Environment.GetEnvironmentVariable(nome, EnvironmentVariableTarget.User);
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    private AnthropicClient? Cliente()
    {
        var chave = LerVariavel(VariavelChave);
        if (chave is null) return null;
        if (_cliente is null || chave != _chaveDoCliente)
        {
            _cliente?.Dispose();
            _cliente = new AnthropicClient { ApiKey = chave };
            _chaveDoCliente = chave;
        }
        return _cliente;
    }

    public async Task<EstadoIa> VerificarAsync(CancellationToken ct = default)
    {
        var cliente = Cliente();
        if (cliente is null) return EstadoIa.SemChave;
        if (!NetworkInterface.GetIsNetworkAvailable()) return EstadoIa.SemInternet;

        // Listar modelos não gera custo e confirma ao mesmo tempo a conexão e a chave.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await cliente.Models.Retrieve(Modelo, cancellationToken: limite.Token);
            return EstadoIa.Disponivel;
        }
        catch (AnthropicUnauthorizedException)
        {
            return EstadoIa.ChaveInvalida;
        }
        catch (AnthropicForbiddenException)
        {
            return EstadoIa.ChaveInvalida;
        }
        catch (AnthropicApiException)
        {
            // A API respondeu (ex.: 429/5xx): há internet e a chave não foi recusada.
            return EstadoIa.Disponivel;
        }
        catch (Exception)
        {
            // AnthropicIOException, HttpRequestException, timeout...
            return EstadoIa.SemInternet;
        }
    }

    public async Task<string> GerarPlanoJsonAsync(string instrucoes, string pedido, CancellationToken ct = default)
    {
        var cliente = Cliente() ?? throw new FalhaIaException(
            $"Defina a variável de ambiente {VariavelChave} para usar a IA.", podeTentarDeNovo: false);

        var parametros = new MessageCreateParams
        {
            Model = Modelo,
            MaxTokens = 16000,
            System = instrucoes,
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Medium,
                Format = new BetaJsonOutputFormat { Schema = EsquemaPlano.Criar() },
            },
            // Se o modelo recusar por política, o servidor reencaminha para um modelo alternativo.
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            Messages = [new BetaUserMessageParam { Content = pedido }],
        };

        BetaMessage resposta;
        try
        {
            resposta = await cliente.Beta.Messages.Create(parametros, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new FalhaIaException("A chave da API foi recusada. Confira a variável ANTHROPIC_API_KEY.", false, ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new FalhaIaException("Limite de uso da API atingido. Aguarde um pouco e tente de novo.", true, ex);
        }
        catch (AnthropicBadRequestException ex)
        {
            throw new FalhaIaException($"A API recusou o pedido: {ex.Message}", false, ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new FalhaIaException("A API está instável no momento. Tente de novo em instantes.", true, ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new FalhaIaException($"Erro da API: {ex.Message}", true, ex);
        }
        catch (Exception ex)
        {
            throw new FalhaIaException("Sem conexão com a API. Verifique a internet.", true, ex);
        }

        var motivo = resposta.StopReason?.ToString();
        if (motivo is "refusal")
            throw new FalhaIaException("O modelo não quis gerar esse plano. Tente reformular o pedido.", true);
        if (motivo is "max_tokens")
            throw new FalhaIaException("O plano ficou grande demais. Peça menos semanas por vez.", true);

        var texto = string.Concat(resposta.Content
            .Select(b => b.TryPickText(out var t) ? t.Text : null)
            .Where(t => t is not null));
        if (string.IsNullOrWhiteSpace(texto))
            throw new FalhaIaException("A IA não devolveu conteúdo. Tente de novo.", true);
        return texto;
    }

    public void Dispose() => _cliente?.Dispose();
}

/// <summary>JSON Schema (saída estruturada) do plano; espelha o formato lido por LeitorPlanoIa.</summary>
internal static class EsquemaPlano
{
    public static Dictionary<string, JsonElement> Criar()
    {
        object TextoOuNulo(string descricao) => new { type = new[] { "string", "null" }, description = descricao };

        var subtarefa = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "titulo", "data", "notas" },
            properties = new
            {
                titulo = new { type = "string" },
                data = TextoOuNulo("yyyy-MM-dd dentro da semana, ou null"),
                notas = TextoOuNulo("detalhes opcionais"),
            },
        };
        var tarefa = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "titulo", "data", "notas", "subtarefas" },
            properties = new
            {
                titulo = new { type = "string" },
                data = TextoOuNulo("yyyy-MM-dd dentro da semana, ou null"),
                notas = TextoOuNulo("detalhes opcionais"),
                subtarefas = new { type = "array", items = subtarefa },
            },
        };
        var semana = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "titulo", "objetivo", "inicio", "fim", "tarefas" },
            properties = new
            {
                titulo = new { type = "string" },
                objetivo = new { type = "string" },
                inicio = new { type = "string", description = "yyyy-MM-dd" },
                fim = new { type = "string", description = "yyyy-MM-dd" },
                tarefas = new { type = "array", items = tarefa },
            },
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "resumo", "semanas" }),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                resumo = new { type = "string" },
                semanas = new { type = "array", items = semana },
            }),
        };
    }
}
