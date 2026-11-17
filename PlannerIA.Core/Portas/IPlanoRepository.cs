using PlannerIA.Core.Dominio;

namespace PlannerIA.Core.Portas;

/// <summary>
/// Armazenamento local do plano. O app é de um único usuário, então o repositório
/// carrega a árvore inteira uma vez e acompanha as alterações até <see cref="SalvarAsync"/>.
/// </summary>
public interface IPlanoRepository
{
    /// <summary>Meses com semanas, tarefas e subtarefas.</summary>
    Task<List<Periodo>> CarregarMesesAsync(CancellationToken ct = default);

    void Adicionar(Periodo periodo);
    void Adicionar(Tarefa tarefa);
    void Remover(Periodo periodo);
    void Remover(Tarefa tarefa);

    Task SalvarAsync(CancellationToken ct = default);
}

/// <summary>Lê uma planilha de objetivos (uma aba por semana) e devolve meses com semanas e tarefas.</summary>
public interface IImportadorPlanilha
{
    IReadOnlyList<Periodo> Importar(string caminho);
}
