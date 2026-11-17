using PlannerIA.Core.Portas;

namespace PlannerIA.Core.Servicos;

/// <summary>
/// Escolhe entre o Claude CLI (assinatura) e a API (chave) conforme o modo configurado.
/// No modo automático, o CLI tem preferência porque não gera cobrança à parte.
/// </summary>
public class ClienteIaRoteador(IClienteIa cli, IClienteIa api, Func<ModoIa> modo) : IClienteIa
{
    private IClienteIa? _escolhido;

    public string Provedor => (_escolhido ?? Padrao()).Provedor;

    private IClienteIa Padrao() => modo() == ModoIa.Api ? api : cli;

    public async Task<EstadoIa> VerificarAsync(CancellationToken ct = default)
    {
        switch (modo())
        {
            case ModoIa.ClaudeCli:
                _escolhido = cli;
                return await cli.VerificarAsync(ct);

            case ModoIa.Api:
                _escolhido = api;
                return await api.VerificarAsync(ct);

            default:
                var estadoCli = await cli.VerificarAsync(ct);
                if (estadoCli == EstadoIa.Disponivel)
                {
                    _escolhido = cli;
                    return estadoCli;
                }

                var estadoApi = await api.VerificarAsync(ct);
                if (estadoApi == EstadoIa.Disponivel)
                {
                    _escolhido = api;
                    return estadoApi;
                }

                // Nenhum disponível: a falta de internet é o motivo mais útil de mostrar.
                _escolhido = null;
                if (estadoCli == EstadoIa.SemInternet || estadoApi == EstadoIa.SemInternet) return EstadoIa.SemInternet;
                if (estadoApi == EstadoIa.ChaveInvalida) return EstadoIa.ChaveInvalida;
                if (estadoCli == EstadoIa.CliSemLogin) return EstadoIa.CliSemLogin;
                return EstadoIa.SemProvedor;
        }
    }

    public async Task<string> GerarPlanoJsonAsync(string instrucoes, string pedido, CancellationToken ct = default)
    {
        if (_escolhido is null && await VerificarAsync(ct) != EstadoIa.Disponivel)
            throw new FalhaIaException("Nenhum provedor de IA disponível. Instale o Claude CLI ou salve uma chave da API.", false);
        return await (_escolhido ?? Padrao()).GerarPlanoJsonAsync(instrucoes, pedido, ct);
    }
}
