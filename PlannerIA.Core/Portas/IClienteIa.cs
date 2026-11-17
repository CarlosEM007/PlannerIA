namespace PlannerIA.Core.Portas;

/// <summary>Por que a IA está (ou não) disponível. O app funciona normalmente em qualquer estado.</summary>
public enum EstadoIa
{
    Verificando,
    Disponivel,
    SemChave,
    ChaveInvalida,
    SemInternet,
    CliAusente,     // Claude CLI não encontrado
    CliSemLogin,    // Claude CLI instalado, mas sem login na conta
    SemProvedor     // modo automático: nem CLI nem chave da API
}

/// <summary>Qual caminho a IA usa.</summary>
public enum ModoIa
{
    Automatico,     // Claude CLI se estiver instalado; senão, API com chave
    ClaudeCli,      // assinatura do Claude (Pro/Max) via Claude Code CLI
    Api             // API da Anthropic com ANTHROPIC_API_KEY
}

/// <summary>
/// Cliente opcional de IA. Só é usado quando o provedor está disponível (CLI instalado ou chave
/// configurada) e há internet; o resto do app nunca depende dele.
/// </summary>
public interface IClienteIa
{
    /// <summary>Nome do provedor em uso, para mostrar na tela (ex.: "Claude CLI").</summary>
    string Provedor { get; }

    /// <summary>Checa se dá para usar. Não lança exceção.</summary>
    Task<EstadoIa> VerificarAsync(CancellationToken ct = default);

    /// <summary>
    /// Envia o pedido e devolve o JSON do plano (formato definido em <see cref="Servicos.LeitorPlanoIa"/>).
    /// Lança <see cref="FalhaIaException"/> com mensagem amigável em caso de erro.
    /// </summary>
    Task<string> GerarPlanoJsonAsync(string instrucoes, string pedido, CancellationToken ct = default);
}

public class FalhaIaException(string mensagem, bool podeTentarDeNovo = true, Exception? interna = null)
    : Exception(mensagem, interna)
{
    public bool PodeTentarDeNovo { get; } = podeTentarDeNovo;
}
