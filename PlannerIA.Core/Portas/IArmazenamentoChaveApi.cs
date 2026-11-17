namespace PlannerIA.Core.Portas;

/// <summary>Onde a chave atual foi encontrada.</summary>
public enum OrigemChave
{
    Nenhuma,
    Usuario,   // variável de ambiente do usuário (gravada pelo app)
    Outra      // definida no processo ou no sistema (fora do alcance do app)
}

/// <summary>
/// Grava e remove a chave da API como variável de ambiente do usuário do Windows.
/// O app não guarda a chave em nenhum arquivo próprio.
/// </summary>
public interface IArmazenamentoChaveApi
{
    string NomeVariavel { get; }

    (string? Chave, OrigemChave Origem) Ler();

    /// <summary>Grava a chave. Lança <see cref="FalhaIaException"/> com mensagem amigável se falhar.</summary>
    Task SalvarAsync(string chave, CancellationToken ct = default);

    Task RemoverAsync(CancellationToken ct = default);
}
